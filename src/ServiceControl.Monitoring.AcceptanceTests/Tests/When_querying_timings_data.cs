﻿namespace NServiceBus.Metrics.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using global::Newtonsoft.Json.Linq;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using Conventions = AcceptanceTesting.Customization;

    public class When_querying_timings_data : ApiIntegrationTest
    {
        static string ReceiverEndpointName => Conventions.Conventions.EndpointNamingConvention(typeof(MonitoringEndpoint));

        [Test]
        public async Task Should_report_via_http()
        {
            JToken processingTime = null;

            await Scenario.Define<Context>()
                .WithEndpoint<MonitoredEndpoint>(c => c.When(s => s.SendLocal(new SampleMessage())))
                .WithEndpoint<MonitoringEndpoint>(c =>
                {
                    c.CustomConfig(conf =>
                    {
                        Bootstrapper.CreateReceiver(conf, ConnectionString);
                        Bootstrapper.StartWebApi();

                        conf.LimitMessageProcessingConcurrencyTo(1);
                    });
                })
                .Done(c =>
                {
                    return MetricReported("processingTime", out processingTime, c);
                })
                .Run();

            Assert.IsTrue(processingTime["average"].Value<int>() > 0);
            Assert.AreEqual(60, processingTime["points"].Value<JArray>().Count);
        }

        class MonitoredEndpoint : EndpointConfigurationBuilder
        {
            public MonitoredEndpoint()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.EnableMetrics().SendMetricDataToServiceControl(ReceiverEndpointName, TimeSpan.FromSeconds(5));
                });
            }

            class Handler : IHandleMessages<SampleMessage>
            {
                public Task Handle(SampleMessage message, IMessageHandlerContext context)
                {
                    return Task.Delay(TimeSpan.FromMilliseconds(10));
                }
            }
        }

        class MonitoringEndpoint : EndpointConfigurationBuilder
        {
            public MonitoringEndpoint()
            {
                EndpointSetup<DefaultServer>();
            }
        }
    }
}