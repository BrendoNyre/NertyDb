using System.Windows;
using System.Windows.Controls;
using NertyDb.Services;

namespace NertyDb.Views.Controls
{
    public partial class ToastContainer : UserControl
    {
        public ToastContainer()
        {
            InitializeComponent();
        }

        private void CloseToast_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                ToastService.Instance.Dismiss(id);
            }
        }
    }
}
