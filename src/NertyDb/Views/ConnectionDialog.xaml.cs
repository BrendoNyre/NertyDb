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

            // Set initial passwords into passwordboxes
            if (viewModel.SelectedProfile != null)
            {
                _isSyncing = true;
                if (TxtPassword != null) TxtPassword.Password = viewModel.SelectedProfile.Password ?? string.Empty;
                if (TxtSguPassword != null) TxtSguPassword.Password = viewModel.SelectedProfile.SguPassword ?? string.Empty;
                _isSyncing = false;
            }

            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionViewModel.SelectedProfile) && viewModel.SelectedProfile != null)
                {
                    _isSyncing = true;
                    if (TxtPassword != null) TxtPassword.Password = viewModel.SelectedProfile.Password ?? string.Empty;
                    if (TxtSguPassword != null) TxtSguPassword.Password = viewModel.SelectedProfile.SguPassword ?? string.Empty;
                    _isSyncing = false;
                }
                else if (e.PropertyName == nameof(ConnectionViewModel.CurrentPassword) && !_isSyncing)
                {
                    _isSyncing = true;
                    if (TxtPassword != null) TxtPassword.Password = viewModel.CurrentPassword ?? string.Empty;
                    _isSyncing = false;
                }
                else if (e.PropertyName == nameof(ConnectionViewModel.CurrentSguPassword) && !_isSyncing)
                {
                    _isSyncing = true;
                    if (TxtSguPassword != null) TxtSguPassword.Password = viewModel.CurrentSguPassword ?? string.Empty;
                    _isSyncing = false;
                }
            };
        }

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSyncing && DataContext is ConnectionViewModel vm && vm.SelectedProfile != null)
            {
                _isSyncing = true;
                vm.CurrentPassword = TxtPassword.Password;
                _isSyncing = false;
            }
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
