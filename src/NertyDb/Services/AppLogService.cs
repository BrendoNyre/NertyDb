using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace NertyDb.Services
{
    public class AppLogEntry
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; } = DateTime.Now;
        public ToastType Level { get; set; } = ToastType.Info;
        public string Source { get; set; } = "Sistema";
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");

        public string Icon => Level switch
        {
            ToastType.Success => "✅",
            ToastType.Warning => "⚠️",
            ToastType.Error => "❌",
            ToastType.Info => "💬",
            _ => "💬"
        };

        public string LevelBadgeColor => Level switch
        {
            ToastType.Success => "#10B981",
            ToastType.Warning => "#F59E0B",
            ToastType.Error => "#EF4444",
            ToastType.Info => "#38BDF8",
            _ => "#94A3B8"
        };

        public string LevelName => Level switch
        {
            ToastType.Success => "SUCESSO",
            ToastType.Warning => "AVISO",
            ToastType.Error => "ERRO",
            ToastType.Info => "INFO",
            _ => "LOG"
        };
    }

    public class AppLogService
    {
        private static readonly Lazy<AppLogService> _instance = new(() => new AppLogService());
        public static AppLogService Instance => _instance.Value;

        public ObservableCollection<AppLogEntry> Entries { get; } = new();

        public void Log(ToastType level, string source, string message, string? details = null)
        {
            var entry = new AppLogEntry
            {
                Level = level,
                Source = source,
                Message = message,
                Details = details
            };

            ExecuteOnUi(() =>
            {
                Entries.Insert(0, entry);
                // Keep max 500 entries in memory for performance
                while (Entries.Count > 500)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                }
            });
        }

        public void LogSuccess(string source, string message, string? details = null) => Log(ToastType.Success, source, message, details);
        public void LogWarning(string source, string message, string? details = null) => Log(ToastType.Warning, source, message, details);
        public void LogError(string source, string message, string? details = null) => Log(ToastType.Error, source, message, details);
        public void LogInfo(string source, string message, string? details = null) => Log(ToastType.Info, source, message, details);

        public void Clear()
        {
            ExecuteOnUi(() => Entries.Clear());
        }

        private static void ExecuteOnUi(Action action)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
