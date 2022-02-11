using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NServiceBus.Extensibility;
using NServiceBus.Features;
using NServiceBus.Routing;
using NServiceBus.Transport;
using ServiceControl.Infrastructure;

namespace ServiceControl.LoadTests.AuditGenerator
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Support;
    using Transports;

    class Program
    {
        static readonly string QueueLengthProviderMsmqTransportCustomizationName = typeof(MsmqTransportCustomizationWithQueueLengthProvider).AssemblyQualifiedName;

        const string DefaultDestination = "audit";
        static readonly string HostId = Guid.NewGuid().ToString();
        static readonly ConcurrentDictionary<string, int> QueueLengths = new ConcurrentDictionary<string, int>();
        static int bodySize;
        static Counter counter;
        static MetricsReporter reporter;
        static int numberOfLayouts = 100;
        static int numberOfEndpoints = 40;

        static readonly string MessageBaseLayout =
            @"<__MESSAGETYPENAME__ xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns=""http://tempuri.net/NServiceBus.Serializers.XML.Test"">__CONTENT__</__MESSAGETYPENAME__>";

        static readonly List<(string layout, int numberOfProperties)> MessageBaseLayouts = new List<(string layout, int numberOfProperties)>();

        public static IDispatchMessages Dispatcher { get; set; }
        
        private 

        static async Task Main(string[] commandLineArgs)
        {
            bodySize = int.Parse(commandLineArgs[0]);
            var transportCustomizationName = commandLineArgs.Length > 1
                ? commandLineArgs[1]
                : QueueLengthProviderMsmqTransportCustomizationName;

            var connectionString = commandLineArgs.Length > 2 ? commandLineArgs[2] : null;

            var transportCustomization = (TransportCustomization) Activator.CreateInstance(Type.GetType(transportCustomizationName, true));
            var queueLengthProvider = transportCustomization.CreateQueueLengthProvider();
            queueLengthProvider.Initialize(connectionString, CacheQueueLength);

            var configuration = new EndpointConfiguration("AuditGenerator");
            configuration.EnableFeature<MyDispatchHackFeature>();
            configuration.SendOnly();
            configuration.UseTransport<MsmqTransport>();
            configuration.UsePersistence<InMemoryPersistence>();
            configuration.EnableInstallers();

            GenerateLayouts();

            var metrics = new Metrics();
            reporter = new MetricsReporter(metrics, Console.WriteLine, TimeSpan.FromSeconds(2));
            counter = metrics.GetCounter("Messages sent");

            var endpoint = await Endpoint.Start(configuration);

            var commands = new (string, Func<CancellationToken, string[], Task>)[]
            {
                ("f|Fill the sender queue. Syntax: f <number of messages> <number of tasks> <destination>",
                    (ct, args) => Fill(args, Dispatcher)),
                ("s|Start sending messages to the queue. Syntax: s <number of tasks> <destination>",
                    (ct, args) => FullSpeedSend(args, ct, Dispatcher)),
                ("t|Throttled sending that keeps the receiver queue size at n. Syntax: t <number of msgs> <destination>",
                    (ct, args) => ConstantQueueLengthSend(args, ct, Dispatcher, queueLengthProvider)),
                ("c|Constant-throughput sending. Syntax: c <number of msgs per second> <destination>",
                    (ct, args) => ConstantThroughputSend(args, ct, Dispatcher))
            };

            await queueLengthProvider.Start();

            await Run(commands);

            await queueLengthProvider.Stop();
        }
        
        class MyDispatchHackFeature : Feature
        {
            protected override void Setup(FeatureConfigurationContext context)
            {
                context.Container.ConfigureComponent<DispatcherHackStartupTask>(DependencyLifecycle.SingleInstance);
                context.RegisterStartupTask(b => b.Build<DispatcherHackStartupTask>());
            }
            
            class DispatcherHackStartupTask : FeatureStartupTask
            {
                public DispatcherHackStartupTask(IDispatchMessages dispatchMessages)
                {
                    Dispatcher = dispatchMessages;
                }
                
                protected override Task OnStart(IMessageSession session)
                {
                    return Task.CompletedTask;
                }

                protected override Task OnStop(IMessageSession session)
                {
                    return Task.CompletedTask;
                }
            }
        }

        private static void GenerateLayouts()
        {
            var random = new Random();
            for (var i = 0; i < numberOfLayouts; i++)
            {
                var messageTypeName = RandomString(20, random).FirstCharToUpper();
                var layoutBuilder = new StringBuilder();
                layoutBuilder.AppendLine();
                var propertyPositionPlaceHolder = 0;
                for (var j = 1; j < bodySize; j++)
                {
                    var propertyName = RandomString(random.Next(10, 30), random).FirstCharToUpper();
                    layoutBuilder.AppendLine(
                        $"   <{propertyName}>{{{propertyPositionPlaceHolder}}}</{propertyName}>");
                    propertyPositionPlaceHolder++;

                    var intermediateLayout = MessageBaseLayout.Replace("__MESSAGETYPENAME__", messageTypeName)
                        .Replace("__CONTENT__", layoutBuilder.ToString());
                    // saving 20 chars per property
                    if ((Encoding.UTF8.GetBytes(intermediateLayout).Length + (i * 20 * sizeof(char))) >= bodySize)
                    {
                        MessageBaseLayouts.Add((intermediateLayout, propertyPositionPlaceHolder));
                        break;
                    }
                }
            }
        }
        
        public static string RandomString(int length, Random random)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(Enumerable.Range(1, length).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }

        static void CacheQueueLength(QueueLengthEntry[] values, EndpointToQueueMapping queueAndEndpointName)
        {
            var newValue = (int) values.OrderBy(x => x.DateTicks).Last().Value;
            QueueLengths.AddOrUpdate(queueAndEndpointName.InputQueue, newValue, (queue, oldValue) => newValue);
        }

        static async Task Fill(string[] args, IDispatchMessages dispatchMessages)
        {
            var totalMessages = args.Length > 0 ? int.Parse(args[0]) : 1000;
            var numberOfTasks = args.Length > 1 ? int.Parse(args[1]) : 5;
            var destination = args.Length > 2 ? args[2] : DefaultDestination;

            var tasks = Enumerable.Range(1, numberOfTasks).Select(async taskId =>
            {
                var random = new Random(Environment.TickCount + taskId);
                for (var i = 0; i < totalMessages / numberOfTasks; i++)
                {
                    await SendAuditMessage(dispatchMessages, destination, random).ConfigureAwait(false);
                }
            }).ToArray();

            await Task.WhenAll(tasks);
        }

        static Task SendAuditMessage(IDispatchMessages dispatcher, string destination, Random random)
        {
            // because MSMQ is essentially synchronous
            return Task.Run(() =>
            {
                var now = DateTime.UtcNow;

                var headers = new Dictionary<string, string>
                {
                    [Headers.ContentType] = "application/xml",
                    [Headers.HostId] = HostId,
                    [Headers.HostDisplayName] = "Load Generator",
                    [Headers.ProcessingMachine] = RuntimeEnvironment.MachineName,
                    [Headers.ProcessingEndpoint] = $"LoadGenerator{random.Next(1, numberOfEndpoints)}",
                    [Headers.ProcessingStarted] = DateTimeExtensions.ToWireFormattedString(now),
                    [Headers.ProcessingEnded] = DateTimeExtensions.ToWireFormattedString(now.AddMilliseconds(random.Next(10, 100))),
                };

                var layoutIndex = random.Next(0, numberOfLayouts);
                var (layout, numberOfProperties) = MessageBaseLayouts[layoutIndex];
                var propertyValues = new List<object>(numberOfProperties);
                for (var i = 0; i < numberOfProperties; i++)
                {
                    propertyValues.Add(RandomString(20, random));
                }

                var transportOperation = new TransportOperation(
                    new OutgoingMessage(Guid.NewGuid().ToString(), headers,
                        Encoding.UTF8.GetBytes(string.Format(layout, propertyValues.ToArray()))),
                    new UnicastAddressTag(destination), DispatchConsistency.Isolated);

                counter.Mark();
                return dispatcher.Dispatch(new TransportOperations(transportOperation), new TransportTransaction(),
                    new ContextBag());
            });
        }

        static async Task FullSpeedSend(string[] args, CancellationToken ct, IDispatchMessages dispatcher)
        {
            var numberOfTasks = args.Length > 0 ? int.Parse(args[0]) : 5;
            var destination = args.Length > 1 ? args[1] : DefaultDestination;

            var tasks = Enumerable.Range(1, numberOfTasks).Select(async taskId =>
            {
                var random = new Random(Environment.TickCount + taskId);
                while (ct.IsCancellationRequested == false)
                {
                    await SendAuditMessage(dispatcher, destination, random).ConfigureAwait(false);
                }
            }).ToArray();

            await Task.WhenAll(tasks);
        }

        static async Task ConstantQueueLengthSend(string[] args, CancellationToken ct, IDispatchMessages dispatcher, IProvideQueueLength queueLengthProvider)
        {
            var maxSenderCount = 20;
            var taskBarriers = new int[maxSenderCount];

            var numberOfMessages = int.Parse(args[0]);
            var destination = args.Length > 1 ? args[1] : DefaultDestination;

            queueLengthProvider.TrackEndpointInputQueue(new EndpointToQueueMapping(destination, destination));

            var monitor = Task.Run(async () =>
            {
                var nextTask = 0;

                while (ct.IsCancellationRequested == false)
                {
                    try
                    {
                        if (!QueueLengths.TryGetValue(destination, out var queueLength))
                        {
                            queueLength = 0;
                        }

                        Console.WriteLine($"Current queue length: {queueLength}");

                        var delta = numberOfMessages - queueLength;

                        if (delta > 0)
                        {
                            Interlocked.Exchange(ref taskBarriers[nextTask], 1);

                            nextTask = Math.Min(maxSenderCount - 1, nextTask + 1);
                        }
                        else
                        {
                            nextTask = Math.Max(0, nextTask - 1);

                            Interlocked.Exchange(ref taskBarriers[nextTask], 0);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    }
                    catch
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }
            }, ct);


            var senders = Enumerable.Range(0, maxSenderCount).Select(async taskNo =>
            {
                var random = new Random(Environment.TickCount + taskNo);
                while (ct.IsCancellationRequested == false)
                {
                    try
                    {
                        var allowed = Interlocked.CompareExchange(ref taskBarriers[taskNo], 1, 1);

                        if (allowed == 1)
                        {
                            await SendAuditMessage(dispatcher, destination, random).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), ct);
                        }
                    }
                    catch
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }
            }).ToArray();

            await Task.WhenAll(new List<Task>(senders) { monitor });
        }

        static async Task ConstantThroughputSend(string[] args, CancellationToken ct, IDispatchMessages dispatcher)
        {
            var maxSenderCount = 20;

            var messagesPerSecond = int.Parse(args[0]);
            var destination = args.Length > 1 ? args[1] : DefaultDestination;

            var semaphore = new SemaphoreSlim(0);

            var delaySeconds = (double)1 / messagesPerSecond;
            var delaySpan = TimeSpan.FromSeconds(delaySeconds);

            var startTime = DateTime.UtcNow;
            var generatedMessages = 0;

            var monitor = Task.Run(async () =>
            {
                while (ct.IsCancellationRequested == false)
                {
                    var spin = new SpinWait();
                    try
                    {
                        spin.SpinOnce();
                        await Task.Delay(delaySpan, ct).ConfigureAwait(false);
                        var elapsedTime = DateTime.UtcNow - startTime;
                        var totalMessagesToBeGenerated = (int)(elapsedTime.TotalSeconds * messagesPerSecond);
                        var deltaMessages = totalMessagesToBeGenerated - generatedMessages;
                        if (deltaMessages > 0)
                        {
                            semaphore.Release(deltaMessages);
                            generatedMessages += deltaMessages;
                        }
                    }
                    catch
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        throw;
                    }
                }
            }, ct);


            var senders = Enumerable.Range(0, maxSenderCount).Select(async taskNo =>
            {
                var random = new Random(Environment.TickCount + taskNo);
                while (ct.IsCancellationRequested == false)
                {
                    try
                    {
                        await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        await SendAuditMessage(dispatcher, destination, random).ConfigureAwait(false);
                    }
                    catch
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        throw;
                    }
                }
            }).ToArray();

            await Task.WhenAll(new List<Task>(senders) { monitor });
        }

        static async Task Run((string, Func<CancellationToken, string[], Task>)[] commands)
        {
            Console.WriteLine("Select command:");
            commands.Select(i => i.Item1).ToList().ForEach(Console.WriteLine);

            while (true)
            {
                var commandLine = Console.ReadLine();
                if (commandLine == null)
                {
                    continue;
                }

                var parts = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var key = parts.First().ToLowerInvariant();
                var arguments = parts.Skip(1).ToArray();

                var match = commands.Where(c => c.Item1.StartsWith(key)).ToArray();

                if (match.Any())
                {
                    var command = match.First();

                    Console.WriteLine($"\nExecuting: {command.Item1.Split('|')[1]}");

                    reporter.Start();

                    using (var ctSource = new CancellationTokenSource())
                    {
                        var task = command.Item2(ctSource.Token, arguments);

                        while (ctSource.IsCancellationRequested == false && task.IsCompleted == false)
                        {
                            if (Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Enter)
                            {
                                ctSource.Cancel();
                                break;
                            }

                            await Task.Delay(TimeSpan.FromMilliseconds(500), ctSource.Token);
                        }

                        await task;
                    }

                    await reporter.Stop();

                    Console.WriteLine("Done");
                }
            }
        }
    }
}