using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace NertyDb.Services
{
    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info
    }

    public class ToastItem
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public ToastType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; } = DateTime.Now;
        public int DurationMs { get; set; } = 4000;

        public string Icon => Type switch
        {
            ToastType.Success => "✅",
            ToastType.Warning => "⚠️",
            ToastType.Error => "❌",
            ToastType.Info => "ℹ️",
            _ => "ℹ️"
        };

        public string HeaderColor => Type switch
        {
            ToastType.Success => "#10B981", // Green
            ToastType.Warning => "#F59E0B", // Amber
            ToastType.Error => "#EF4444",   // Red
            ToastType.Info => "#38BDF8",    // Blue
            _ => "#38BDF8"
        };

        public string BorderBrushHex => Type switch
        {
            ToastType.Success => "#059669",
            ToastType.Warning => "#D97706",
            ToastType.Error => "#DC2626",
            ToastType.Info => "#0284C7",
            _ => "#0284C7"
        };
    }

    public class ToastService
    {
        private static readonly Lazy<ToastService> _instance = new(() => new ToastService());
        public static ToastService Instance => _instance.Value;

        public ObservableCollection<ToastItem> ActiveToasts { get; } = new();

        public void Show(ToastType type, string title, string message, int durationMs = 4000)
        {
            var item = new ToastItem
            {
                Type = type,
                Title = title,
                Message = message,
                DurationMs = durationMs
            };

            ExecuteOnUi(() =>
            {
                ActiveToasts.Add(item);

                if (durationMs > 0)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        Dismiss(item.Id);
                    };
                    timer.Start();
                }
            });
        }

        public void ShowSuccess(string message, string title = "Sucesso") => Show(ToastType.Success, title, message);
        public void ShowWarning(string message, string title = "Aviso") => Show(ToastType.Warning, title, message, 5000);
        public void ShowError(string message, string title = "Erro") => Show(ToastType.Error, title, message, 6000);
        public void ShowInfo(string message, string title = "Informação") => Show(ToastType.Info, title, message);

        public void Dismiss(string id)
        {
            ExecuteOnUi(() =>
            {
                for (int i = 0; i < ActiveToasts.Count; i++)
                {
                    if (ActiveToasts[i].Id == id)
                    {
                        ActiveToasts.RemoveAt(i);
                        break;
                    }
                }
            });
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
