using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using PrinterGUI.ViewModels;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.VisualTree; 
using Avalonia.LogicalTree; // Required for logical hierarchy checks

namespace PrinterGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Close keyboard when clicking outside
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);

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

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Close keyboard if click is outside the keyboard and not on a textbox
            if (KeyboardPopup != null && KeyboardPopup.IsOpen)
            {
                bool isInsideKeyboard = false;

                // Reliably check if the clicked UI element is part of the keyboard layout
                if (e.Source is Avalonia.Visual sourceVisual && KeyboardPopup.Child is Avalonia.Visual targetVisual)
                {
                    var mainLevel = TopLevel.GetTopLevel(this);
                    var srcLevel = TopLevel.GetTopLevel(sourceVisual);
                    var tgtLevel = TopLevel.GetTopLevel(targetVisual);

                    // If the popup is drawn as a separate OS window/top-level, ensure we don't close it when clicked
                    if (srcLevel != null && srcLevel != mainLevel && srcLevel == tgtLevel)
                    {
                        isInsideKeyboard = true;
                    }
                    else
                    {
                        // Fallback: Exhaustive tree traversal (combining Visual and Logical chains)
                        Avalonia.Visual? current = sourceVisual;
                        while (current != null)
                        {
                            if (current == targetVisual || current == KeyboardPopup)
                            {
                                isInsideKeyboard = true;
                                break;
                            }
                            current = current.GetVisualParent() ?? (current as ILogical)?.LogicalParent as Avalonia.Visual;
                        }
                    }
                }
                
                // Check if click is outside keyboard and not on a textbox
                if (!isInsideKeyboard && e.Source is not TextBox)
                {
                    KeyboardPopup.IsOpen = false;
                    
                    // Explicitly take focus to the Window itself to un-focus the TextBox visually
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
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
                double textBoxHeight = textBox.Bounds.Height;
                double keyboardHeight = NumericKeyboard.Bounds.Height;
                
                // If keyboard hasn't been measured yet, use estimated height
                if (keyboardHeight == 0)
                {
                    // 4 rows of 48px buttons + 3 gaps of 8px + 16px padding = 232px
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
            // On touch devices (like Linux/Raspberry Pi), Popups are separate windows and 
            // can temporarily steal focus during a touch gesture, which would cause the keyboard 
            // to spontaneously disappear if we hid it here.
            // Hiding the keyboard is fully handled by Window_PointerPressed when clicking off the keyboard.
        }
    }
}