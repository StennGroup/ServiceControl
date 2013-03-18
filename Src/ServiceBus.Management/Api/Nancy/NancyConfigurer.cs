﻿namespace ServiceBus.Management.Api.Nancy
{
    using System;
    using NServiceBus;
    using NServiceBus.Logging;
    using global::Nancy.Hosting.Self;

    public class NancyConfigurer : INeedInitialization
    {
        public void Init()
        {
            //we need to use a func here to delay the nancy modules to load since we need to configure its dependencies first
            Configure.Instance.Configurer.ConfigureComponent(() => new NancyHost(new Uri(Settings.ApiUrl)), DependencyLifecycle.SingleInstance);

            Logger.InfoFormat("The service bus management api is configured to accept requests on: {0}",Settings.ApiUrl);
        }

        static ILog Logger = LogManager.GetLogger(typeof(NancyConfigurer));
    }
}