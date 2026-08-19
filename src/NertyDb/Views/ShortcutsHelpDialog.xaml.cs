using System.Windows;

namespace NertyDb.Views
{
    public partial class ShortcutsHelpDialog : Window
    {
        public ShortcutsHelpDialog()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
