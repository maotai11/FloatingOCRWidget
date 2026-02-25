using System.Windows;
using System.Windows.Threading;

namespace FloatingOCRWidget
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handling
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An error occurred: {e.Exception.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}