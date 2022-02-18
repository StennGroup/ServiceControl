﻿using System.Diagnostics;
using ServiceControl.Infrastructure;

namespace ServiceControl.Audit.Auditing
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using BodyStorage;
    using Infrastructure;
    using Infrastructure.Settings;
    using NServiceBus;
    using NServiceBus.Logging;
    using NServiceBus.Transport;
    using Raven.Client.Documents;

    class AuditIngestionComponent
    {
        static ILog log = LogManager.GetLogger<AuditIngestionComponent>();

        ImportFailedAudits failedImporter;
        Watchdog watchdog;
        AuditPersister auditPersister;
        Channel<MessageContext> channel;
        AuditIngestor ingestor;
        Task ingestionWorker;
        Settings settings;
        Counter receivedMeter;
        Meter batchSizeMeter;
        Meter batchDurationMeter;
        static readonly long frequencyInMilliseconds = Stopwatch.Frequency / 1000;

        public AuditIngestionComponent(
            Metrics metrics,
            Settings settings,
            IDocumentStore documentStore,
            RawEndpointFactory rawEndpointFactory,
            LoggingSettings loggingSettings,
            BodyStorageFeature.BodyStorageEnricher bodyStorageEnricher,
            IEnrichImportedAuditMessages[] enrichers,
            AuditIngestionCustomCheck.State ingestionState
        )
        {
            receivedMeter = metrics.GetCounter("Audit ingestion - received");
            batchSizeMeter = metrics.GetMeter("Audit ingestion - batch size");
            var ingestedAuditMeter = metrics.GetCounter("Audit ingestion - ingested audit");
            var ingestedSagaAuditMeter = metrics.GetCounter("Audit ingestion - ingested saga audit");
            var auditBulkInsertDurationMeter = metrics.GetMeter("Audit ingestion - audit bulk insert duration", frequencyInMilliseconds);
            var sagaAuditBulkInsertDurationMeter = metrics.GetMeter("Audit ingestion - saga audit bulk insert duration", frequencyInMilliseconds);
            var bulkInsertCommitDurationMeter = metrics.GetMeter("Audit ingestion - bulk insert commit duration", frequencyInMilliseconds);
            batchDurationMeter = metrics.GetMeter("Audit ingestion - batch processing duration", frequencyInMilliseconds);

            this.settings = settings;
            var errorHandlingPolicy = new AuditIngestionFaultPolicy(documentStore, loggingSettings, OnCriticalError);
            auditPersister = new AuditPersister(documentStore, bodyStorageEnricher, enrichers, settings.AuditRetentionPeriod, 
                ingestedAuditMeter, ingestedSagaAuditMeter, auditBulkInsertDurationMeter, sagaAuditBulkInsertDurationMeter, bulkInsertCommitDurationMeter,settings);

            ingestor = new AuditIngestor(auditPersister, settings);

            var ingestion = new AuditIngestion(
                async (messageContext, dispatcher) =>
                {
                    var taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    messageContext.SetTaskCompletionSource(taskCompletionSource);

                    receivedMeter.Mark();

                    await channel.Writer.WriteAsync(messageContext).ConfigureAwait(false);
                    await taskCompletionSource.Task.ConfigureAwait(false);
                },
                dispatcher => ingestor.Initialize(dispatcher),
                settings.AuditQueue, rawEndpointFactory, errorHandlingPolicy, OnCriticalError);

            failedImporter = new ImportFailedAudits(documentStore, ingestor, rawEndpointFactory);

            watchdog = new Watchdog(ingestion.EnsureStarted, ingestion.EnsureStopped, ingestionState.ReportError,
                ingestionState.Clear, settings.TimeToRestartAuditIngestionAfterFailure, log, "audit message ingestion");


            channel = Channel.CreateBounded<MessageContext>(new BoundedChannelOptions(settings.MaximumConcurrencyLevel)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            ingestionWorker = Task.Run(() => Loop(), CancellationToken.None);
        }

        Task OnCriticalError(string failure, Exception arg2)
        {
            log.Warn($"OnCriticalError. '{failure}'", arg2);
            return watchdog.OnFailure(failure);
        }

        public Task Start(IMessageSession session)
        {
            auditPersister.Initialize(session);
            return watchdog.Start();
        }

        public async Task Stop()
        {
            await watchdog.Stop().ConfigureAwait(false);
            channel.Writer.Complete();
            await ingestionWorker.ConfigureAwait(false);
        }

        async Task Loop()
        {
            var contexts = new List<MessageContext>(settings.MaximumConcurrencyLevel);
            var tasks = new List<Task>(settings.MaximumConcurrencyLevel);

            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // will only enter here if there is something to read.
                try
                {

                    
                    while (channel.Reader.TryRead(out var context))
                    {
                        contexts.Add(context);
                        if (settings.NoBulkInserts)
                        {
                            tasks.Add(ingestor.Ingest(new List<MessageContext> { context }));
                        }
                    }

                    if (!settings.NoBulkInserts)
                    {
                        batchSizeMeter.Mark(contexts.Count);
                        using (batchDurationMeter.Measure())
                        {
                            await ingestor.Ingest(contexts).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        batchSizeMeter.Mark(tasks.Count);
                        using (batchDurationMeter.Measure())
                        {
                            await Task.WhenAll(tasks).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception e) // show must go on
                {
                    if (log.IsInfoEnabled)
                    {
                        log.Info("Ingesting messages failed", e);
                    }

                    // signal all message handling tasks to terminate
                    foreach (var context in contexts)
                    {
                        context.GetTaskCompletionSource().TrySetException(e);
                    }
                }
                finally
                {
                    contexts.Clear();
                    tasks.Clear();
                }
            }
            // will fall out here when writer is completed
        }

        public Task ImportFailedAudits(CancellationToken cancellationToken)
        {
            return failedImporter.Run(cancellationToken);
        }
    }
}
