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
using Avalonia.VisualTree; // Required to check the UI tree

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

            // Listen for the Enter pressed event so we can close the keyboard
            if (GcodeKeyboard != null)
            {
                GcodeKeyboard.EnterPressed += (s, e) => 
                {
                    if (GcodeKeyboardPopup != null) GcodeKeyboardPopup.IsOpen = false;
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                };
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Close keyboard if click is outside the keyboard and not on the textbox
            if (GcodeKeyboardPopup != null && GcodeKeyboardPopup.IsOpen)
            {
                bool isInsideKeyboard = false;
                
                // Reliably check if the clicked UI element is part of the keyboard layout
                if (e.Source is Avalonia.Visual sourceVisual && GcodeKeyboardPopup.Child is Avalonia.Visual targetVisual)
                {
                    isInsideKeyboard = sourceVisual == targetVisual || 
                                       sourceVisual.GetVisualAncestors().Any(v => v == targetVisual);
                }
                
                // Check if click is outside keyboard and not on a textbox
                if (!isInsideKeyboard && e.Source is not TextBox)
                {
                    GcodeKeyboardPopup.IsOpen = false;
                    
                    // Explicitly take focus to the Window itself to un-focus the TextBox visually
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
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
                
                // Signal the keyboard to overwrite the textbox contents on the next key press
                GcodeKeyboard.OverwriteNextInput = true;
                
                // Show keyboard
                GcodeKeyboardPopup.IsOpen = true;
            }
        }

        private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            // Intentionally left blank. 
            // On touch devices (like Linux/Raspberry Pi), Popups are separate windows and 
            // can temporarily steal focus during a touch gesture, which would cause the keyboard 
            // to spontaneously disappear if we hid it here.
            // Hiding the keyboard is fully handled by Window_PointerPressed when clicking off the keyboard.
        }
    }
}
