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
            if (viewModel.SelectedProfile != null && !string.IsNullOrEmpty(viewModel.SelectedProfile.Password))
            {
                _isSyncing = true;
                TxtPassword.Password = viewModel.SelectedProfile.Password;
                _isSyncing = false;
            }

            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionViewModel.SelectedProfile) && viewModel.SelectedProfile != null)
                {
                    _isSyncing = true;
                    TxtPassword.Password = viewModel.SelectedProfile.Password ?? string.Empty;
                    _isSyncing = false;
                }
                else if (e.PropertyName == nameof(ConnectionViewModel.CurrentPassword) && !_isSyncing)
                {
                    _isSyncing = true;
                    TxtPassword.Password = viewModel.CurrentPassword ?? string.Empty;
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
