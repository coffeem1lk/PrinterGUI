using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using PrinterGUI.ViewModels;

namespace PrinterGUI.Views
{
    public partial class AxisControlWindow : Window
    {
        public AxisControlWindow()
        {
            InitializeComponent();
        }

        public AxisControlWindow(string serialPort) : this()
        {
            DataContext = new AxisControlViewModel(serialPort);
            
            // Close keyboard when clicking outside
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Close keyboard if click is outside the keyboard and not on the textbox
            if (GcodeKeyboardPopup != null && GcodeKeyboardPopup.IsOpen)
            {
                var point = e.GetPosition(GcodeKeyboardPopup.Child);
                var keyboardBounds = GcodeKeyboardPopup.Child?.Bounds;
                
                // Check if click is outside keyboard
                if (keyboardBounds.HasValue && !keyboardBounds.Value.Contains(point))
                {
                    // Check if click is not on the textbox
                    if (e.Source is not TextBox)
                    {
                        GcodeKeyboardPopup.IsOpen = false;
                        
                        // Explicitly take focus to the Window itself to un-focus the TextBox visually
                        this.Focus();
                        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                    }
                }
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is TextBox textBox && GcodeKeyboardPopup != null && GcodeKeyboard != null)
            {
                // Set the placement target to the focused textbox
                GcodeKeyboardPopup.PlacementTarget = textBox;
                GcodeKeyboardPopup.Placement = PlacementMode.Bottom;
                GcodeKeyboardPopup.HorizontalOffset = 0;
                GcodeKeyboardPopup.VerticalOffset = 5;
                
                // Show keyboard
                GcodeKeyboardPopup.IsOpen = true;
            }
        }

        private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            // Hide keyboard after a short delay to allow clicking keyboard buttons
            Task.Delay(200).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Only hide if the textbox no longer has focus
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.FocusManager?.GetFocusedElement() is not TextBox)
                    {
                        if (GcodeKeyboardPopup != null)
                        {
                            GcodeKeyboardPopup.IsOpen = false;
                        }
                    }
                });
            });
        }
    }
}
