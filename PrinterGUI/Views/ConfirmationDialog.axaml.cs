using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrinterGUI.Views
{
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog()
        {
            InitializeComponent();
        }

        public ConfirmationDialog(string message) : this()
        {
            MessageText.Text = message;
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void Send_Click(object? sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}