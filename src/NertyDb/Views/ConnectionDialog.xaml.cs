using System.Windows;
using NertyDb.ViewModels;

namespace NertyDb.Views
{
    public partial class ConnectionDialog : Window
    {
        private bool _isSyncing;

        public ConnectionDialog(ConnectionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.RequestClose += (s, e) =>
            {
                DialogResult = true;
                Close();
            };

            // Set initial password into passwordbox
            if (viewModel.SelectedProfile != null)
            {
                _isSyncing = true;
                TxtSguPassword.Password = viewModel.SelectedProfile.SguPassword ?? string.Empty;
                _isSyncing = false;
            }

            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionViewModel.SelectedProfile) && viewModel.SelectedProfile != null)
                {
                    _isSyncing = true;
                    TxtSguPassword.Password = viewModel.SelectedProfile.SguPassword ?? string.Empty;
                    _isSyncing = false;
                }
                else if (e.PropertyName == nameof(ConnectionViewModel.CurrentSguPassword) && !_isSyncing)
                {
                    _isSyncing = true;
                    TxtSguPassword.Password = viewModel.CurrentSguPassword ?? string.Empty;
                    _isSyncing = false;
                }
            };
        }

        private void TxtSguPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSyncing && DataContext is ConnectionViewModel vm && vm.SelectedProfile != null)
            {
                _isSyncing = true;
                vm.CurrentSguPassword = TxtSguPassword.Password;
                _isSyncing = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
