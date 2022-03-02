namespace ServiceControl.Config.UI.MessageBox
{
    using System.Windows.Input;
    using Caliburn.Micro;
    using Framework;
    using Framework.Rx;

    class YesNoCancelViewModel : RxScreen
    {
        public YesNoCancelViewModel(string title, string message, string question, string yesText, string noText)
        {
            Title = title;
            Message = message;
            YesText = yesText;
            NoText = noText;
            Question = question;
            Cancel = Command.Create(async () =>
            {
                Result = null;
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                await ((IDeactivate)this).DeactivateAsync(true);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            });
            No = Command.Create(async () =>
            {
                Result = false;
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                await ((IDeactivate)this).DeactivateAsync(true);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            });
            Yes = Command.Create(async () =>
            {
                Result = true;
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                await ((IDeactivate)this).DeactivateAsync(true);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            });
            ShowCancelButton = true;
        }

        public string Title { get; set; }
        public string Message { get; set; }
        public string Question { get; set; }
        public ICommand Cancel { get; }
        public ICommand No { get; }
        public string NoText { get; }
        public ICommand Yes { get; }
        public string YesText { get; }
        public bool ShowCancelButton { get; set; }
    }
}