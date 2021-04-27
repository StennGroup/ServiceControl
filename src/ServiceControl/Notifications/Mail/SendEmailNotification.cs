﻿namespace ServiceControl.Notifications.Mail
{
    using NServiceBus;

    public class SendEmailNotification : ICommand
    {
        public string Subject { get; set; }

        public string Body { get; set; }

        public int? FailureNumber { get; set; }
    }
}