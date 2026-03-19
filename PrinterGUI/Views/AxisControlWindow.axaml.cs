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
        }
    }
}
