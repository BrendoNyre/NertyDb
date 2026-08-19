using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NertyDb.Services;
using NertyDb.ViewModels;

namespace NertyDb.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            var storageService = new StorageService();
            var exportService = new ExportService();

            ViewModel = new MainViewModel(
                storageService,
                exportService,
                openPendingChangesDialog: (vm) =>
                {
                    var dlg = new PendingChangesDialog(vm) { Owner = this };
                    dlg.ShowDialog();
                },
                openExportDialog: (vm) =>
                {
                    var dlg = new ExportDialog(vm) { Owner = this };
                    dlg.ShowDialog();
                },
                openConnectionDialog: (vm) =>
                {
                    var dlg = new ConnectionDialog(vm) { Owner = this };
                    dlg.ShowDialog();
                },
                openAboutDialog: () =>
                {
                    var dlg = new AboutDialog { Owner = this };
                    dlg.ShowDialog();
                },
                openShortcutsHelpDialog: () =>
                {
                    var dlg = new ShortcutsHelpDialog { Owner = this };
                    dlg.ShowDialog();
                },
                applyTheme: (theme) =>
                {
                    App.ApplyTheme(theme);
                });

            DataContext = ViewModel;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Prompt connection dialog if no active connection on first launch
            if (!ViewModel.HasActiveConnection)
            {
                ViewModel.OpenConnectionsDialogCommand.Execute(null);
            }
        }

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem tvi && tvi.DataContext is SchemaNode sn)
            {
                if (sn.NodeType == SchemaNodeType.Table || sn.NodeType == SchemaNodeType.View)
                {
                    e.Handled = true;
                    ViewModel.SchemaTree.OpenTableCommand.Execute(sn);
                }
            }
        }

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem tvi)
            {
                tvi.IsSelected = true;
                tvi.Focus();
            }
        }

        private void TreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeView tv && tv.SelectedItem is SchemaNode sn)
            {
                if (sn.NodeType == SchemaNodeType.Table || sn.NodeType == SchemaNodeType.View)
                {
                    ViewModel.SchemaTree.OpenTableCommand.Execute(sn);
                }
            }
        }
    }
}
