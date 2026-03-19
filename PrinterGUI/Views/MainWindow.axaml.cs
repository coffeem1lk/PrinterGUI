using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using PrinterGUI.ViewModels;
using System.Threading.Tasks;

namespace PrinterGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Listen for the Enter pressed event so we can close the keyboard
            if (NumericKeyboard != null)
            {
                NumericKeyboard.EnterPressed += (s, e) => 
                {
                    if (KeyboardPopup != null) KeyboardPopup.IsOpen = false;
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                };
            }
        }

        private async void SendToPrinter_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var dlg = new ConfirmationDialog("ARE YOU SURE??");

            var result = await dlg.ShowDialog<bool>(this);
            if (result)
            {
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

        private void SetProbeOffset_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var probeWindow = new ProbeOffsetWindow(vm.SerialPortPath);
                probeWindow.Show();
            }
        }

        private void TextBox_GotFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
        {
            if (sender is TextBox textBox && KeyboardPopup != null && NumericKeyboard != null)
            {
                // Set the placement target to the focused textbox
                KeyboardPopup.PlacementTarget = textBox;
                KeyboardPopup.Placement = PlacementMode.Right;
                KeyboardPopup.HorizontalOffset = 10;
                
                // Calculate vertical offset to align top borders
                // By default, centers are aligned. To align tops, shift DOWN.
                double textBoxHeight = textBox.Bounds.Height;
                double keyboardHeight = NumericKeyboard.Bounds.Height;
                
                // If keyboard hasn't been measured yet, use estimated height
                if (keyboardHeight == 0)
                {
                    keyboardHeight = 232;
                }
                
                KeyboardPopup.VerticalOffset = (keyboardHeight - textBoxHeight) / 2;
                
                // Signal the keyboard to overwrite the textbox contents on the next key press
                NumericKeyboard.OverwriteNextInput = true;
                
                // Show keyboard
                KeyboardPopup.IsOpen = true;
            }
        }

        private void TextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Intentionally left blank.
        }
    }
}