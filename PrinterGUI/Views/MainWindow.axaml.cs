using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using PrinterGUI.ViewModels;
using System.Threading.Tasks;
using System.Linq;

namespace PrinterGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (NumericKeyboard != null)
            {
                NumericKeyboard.EnterPressed += (s, e) =>
                {
                    if (KeyboardPopup != null) KeyboardPopup.IsOpen = false;
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                };
            }

            // Use Bubble (not Tunnel) to avoid pre-emptive close on touch interactions
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Bubble);

            var textboxesToBind = new[] {
                this.FindControl<TextBox>("LayerHeightTextBox"),
                this.FindControl<TextBox>("DryingTempTextBox"),
                this.FindControl<TextBox>("InfillTextBox"),
                this.FindControl<TextBox>("DryingTimeTextBox"),
                this.FindControl<TextBox>("PrintSpeedTextBox"),
                this.FindControl<TextBox>("DryingTimeRTTextBox")
            };

            foreach (var tb in textboxesToBind.Where(t => t != null))
            {
                tb.AddHandler(PointerPressedEvent, TextBox_PointerPressed, RoutingStrategies.Bubble);
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (KeyboardPopup == null || !KeyboardPopup.IsOpen)
                return;

            if (e.Source is TextBox)
                return;

            KeyboardPopup.IsOpen = false;
            this.Focus();
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void TextBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is TextBox textBox && KeyboardPopup != null && NumericKeyboard != null)
            {
                textBox.Focus();

                KeyboardPopup.PlacementTarget = textBox;
                KeyboardPopup.Placement = PlacementMode.Right;
                KeyboardPopup.HorizontalOffset = 10;

                double textBoxHeight = textBox.Bounds.Height;
                double keyboardHeight = NumericKeyboard.Bounds.Height;
                if (keyboardHeight == 0) keyboardHeight = 232;

                KeyboardPopup.VerticalOffset = (keyboardHeight - textBoxHeight) / 2;
                NumericKeyboard.OverwriteNextInput = true;
                KeyboardPopup.IsOpen = true;

                // Prevent this same press from bubbling to window-close logic
                e.Handled = true;
            }
        }

        private async void SendToPrinter_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var dlg = new ConfirmationDialog("ARE YOU SURE??");
            var result = await dlg.ShowDialog<bool>(this);

            if (result && vm.SendToPrinterCommand.CanExecute(null))
                vm.SendToPrinterCommand.Execute(null);
        }

        private void AxisControl_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var axisWindow = new AxisControlWindow(vm.SerialPortPath);
                axisWindow.Show();
            }
        }

        private void SetProbeOffset_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var probeWindow = new ProbeOffsetWindow(vm.SerialPortPath);
                probeWindow.Show();
            }
        }

        private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is TextBox textBox && KeyboardPopup != null && NumericKeyboard != null)
            {
                if (!KeyboardPopup.IsOpen)
                {
                    KeyboardPopup.PlacementTarget = textBox;
                    KeyboardPopup.Placement = PlacementMode.Right;
                    KeyboardPopup.HorizontalOffset = 10;

                    double textBoxHeight = textBox.Bounds.Height;
                    double keyboardHeight = NumericKeyboard.Bounds.Height;
                    if (keyboardHeight == 0) keyboardHeight = 232;

                    KeyboardPopup.VerticalOffset = (keyboardHeight - textBoxHeight) / 2;
                    NumericKeyboard.OverwriteNextInput = true;
                    KeyboardPopup.IsOpen = true;
                }
            }
        }

        private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
        {
        }
    }
}