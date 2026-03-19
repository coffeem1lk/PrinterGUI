using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
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

            // Catch clicks that hit the window background
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);

            // Manually bind PointerPressed to our known Numeric TextBoxes
            // so tapping them ALWAYS opens the keyboard, even if they are already focused.
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
                tb.AddHandler(PointerPressedEvent, TextBox_PointerPressed, RoutingStrategies.Tunnel);
            }
        }

        private bool IsPointerInsideKeyboard(object? source)
        {
            if (KeyboardPopup?.Child is not Control popupRoot || source is not Control sourceControl)
                return false;

            return sourceControl == popupRoot || sourceControl.GetVisualAncestors().Contains(popupRoot);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (KeyboardPopup == null || !KeyboardPopup.IsOpen)
                return;

            // Ignore clicks/touches inside the popup keyboard itself
            if (IsPointerInsideKeyboard(e.Source))
                return;

            // Keep open when interacting with textboxes; they manage popup open/reposition
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
                // Force focus explicitly
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
            // Fallback for non-pointer focus traversal
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

        private void TextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
        }
    }
}