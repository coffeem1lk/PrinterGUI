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
            
            // Close keyboard when clicking outside
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Close keyboard if click is outside the keyboard and not on a textbox
            if (KeyboardPopup != null && KeyboardPopup.IsOpen)
            {
                var point = e.GetPosition(KeyboardPopup.Child);
                var keyboardBounds = KeyboardPopup.Child?.Bounds;
                
                // Check if click is outside keyboard
                if (keyboardBounds.HasValue && !keyboardBounds.Value.Contains(point))
                {
                    // Check if click is not on a textbox
                    if (e.Source is not TextBox)
                    {
                        KeyboardPopup.IsOpen = false;
                    }
                }
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
                // Offset = (keyboardHeight - textboxHeight) / 2
                double textBoxHeight = textBox.Bounds.Height;
                double keyboardHeight = NumericKeyboard.Bounds.Height;
                
                // If keyboard hasn't been measured yet, use estimated height
                if (keyboardHeight == 0)
                {
                    // 4 rows of 48px buttons + 3 gaps of 8px + 16px padding = 232px
                    keyboardHeight = 232;
                }
                
                KeyboardPopup.VerticalOffset = (keyboardHeight - textBoxHeight) / 2;
                
                // Show keyboard
                KeyboardPopup.IsOpen = true;
            }
        }

        private void TextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Hide keyboard after a short delay to allow clicking keyboard buttons
            Task.Delay(200).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Only hide if no textbox has focus
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.FocusManager?.GetFocusedElement() is not TextBox)
                    {
                        if (KeyboardPopup != null)
                        {
                            KeyboardPopup.IsOpen = false;
                        }
                    }
                });
            });
        }
    }
}