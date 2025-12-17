using Avalonia.Controls;
using Avalonia.Interactivity;
using PrinterGUI.ViewModels;
using System.Threading.Tasks;

namespace PrinterGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // Existing Browse handlers (if present) can remain here.
        // New handler shows a confirmation dialog before invoking the ViewModel command.
        private async void SendToPrinter_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            // Build a concise confirmation message
            var message = $"Send '{vm.SelectedObject?.Name ?? "selected model"}' to the printer?\n\nThis will generate G-code, send it, and delete the generated file.";
            var dlg = new ConfirmationDialog(message);

            // Show modal and await result
            var result = await dlg.ShowDialog<bool>(this);
            if (result)
            {
                // Execute the VM command
                if (vm.SendToPrinterCommand.CanExecute(null))
                    vm.SendToPrinterCommand.Execute(null);
            }
        }

        private void AxisControl_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var axisWindow = new AxisControlWindow(vm.SerialPortPath);
                axisWindow.Show();
            }
        }
    }
}