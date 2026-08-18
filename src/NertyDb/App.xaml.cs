using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NertyDb
{
    public partial class App : Application
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NertyDb",
            "error.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Register Windows code page providers (enables Windows-1252/ISO-8859-1 for Senior export/accents)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Global Exception Handlers to prevent silent crashes and freeze
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("DispatcherUnhandledException", e.Exception);
            e.Handled = true; // Prevent app termination!

            var msg = e.Exception.Message;
            if (string.IsNullOrWhiteSpace(msg) || e.Exception.StackTrace?.Contains("RegisterDragDrop") == true)
            {
                return;
            }

            MessageBox.Show(
                $"Ocorreu um erro na interface:\r\n\r\n{msg}\r\n\r\nOs detalhes foram salvos em: {LogFilePath}",
                "NertyDb — Aviso do Sistema",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("UnhandledException", ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("UnobservedTaskException", e.Exception);
            e.SetObserved(); // Prevent app crash on background task failures
        }

        public static void LogException(string source, Exception ex)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}]");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"InnerException: {ex.InnerException.Message}");
                    sb.AppendLine($"InnerStackTrace: {ex.InnerException.StackTrace}");
                }
                sb.AppendLine(new string('-', 80));

                File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static void ApplyTheme(string themeName)
        {
            var app = Current;
            if (app == null) return;

            var themeUri = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase)
                ? new Uri("Resources/LightTheme.xaml", UriKind.Relative)
                : new Uri("Resources/DarkTheme.xaml", UriKind.Relative);

            var newDict = new ResourceDictionary { Source = themeUri };
            
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                app.Resources.MergedDictionaries[0] = newDict;
            }
            else
            {
                app.Resources.MergedDictionaries.Add(newDict);
            }
        }
    }
}
