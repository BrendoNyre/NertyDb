using System.Windows;
using NertyDb.ViewModels;

namespace NertyDb.Views
{
    public partial class PendingChangesDialog : Window
    {
        public PendingChangesDialog(PendingChangesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.RequestClose += (s, e) =>
            {
                DialogResult = true;
                Close();
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
