namespace ServiceControlInstaller.PowerShell
{
    using System;
    using System.Management.Automation;
    using Engine.Instances;
    using Engine.Unattended;

    [Cmdlet(VerbsLifecycle.Invoke, "MonitoringInstanceUpgrade")]
    public class InvokeMonitoringUpgrade : PSCmdlet
    {
        [Parameter(Mandatory = false, HelpMessage = "Do not automatically create new queues")]
        public SwitchParameter SkipQueueCreation { get; set; }


        protected override void BeginProcessing()
        {
            AppDomain.CurrentDomain.AssemblyResolve += BindingRedirectAssemblyLoader.CurrentDomain_BindingRedirect;

            Account.TestIfAdmin();
        }

        protected override void ProcessRecord()
        {
            var logger = new PSLogger(Host);

            var zipFolder = ZipPath.Get(this);
            var installer = new UnattendMonitoringInstaller(logger, zipFolder);

            foreach (var name in Name)
            {
                var instance = InstanceFinder.FindMonitoringInstance(name);
                if (instance == null)
                {
                    WriteWarning($"No action taken. An instance called {name} was not found");
                    break;
                }

                instance.SkipQueueCreation = SkipQueueCreation;

                if (!installer.Upgrade(instance))
                {
                    ThrowTerminatingError(new ErrorRecord(new Exception($"Upgrade of {instance.Name} failed"), "UpgradeFailure", ErrorCategory.InvalidResult, null));
                }
            }
        }

        [ValidateNotNullOrEmpty]
        [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0, HelpMessage = "Specify the name of the ServiceControl Instance to update")]
        public string[] Name;
    }
}