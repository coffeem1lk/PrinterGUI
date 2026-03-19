using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
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

                    // Force the window to take focus so the textbox truly "loses" it visually and logically
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                };
            }

            // Re-route normal window clicks to close keyboard
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);

            // Bind pointer pressed to the textbox to ALWAYS open keyboard even if already focused
            if (CustomGcodeTextBox != null)
            {
                CustomGcodeTextBox.AddHandler(PointerPressedEvent, TextBox_PointerPressed, RoutingStrategies.Tunnel);
            }
        }

        private bool IsPointerInsideKeyboard(object? source)
        {
            if (GcodeKeyboardPopup?.Child is not Control popupRoot || source is not Control sourceControl)
                return false;

            return sourceControl == popupRoot || sourceControl.GetVisualAncestors().Contains(popupRoot);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (GcodeKeyboardPopup == null || !GcodeKeyboardPopup.IsOpen)
                return;

            // Ignore clicks/touches inside the popup keyboard itself
            if (IsPointerInsideKeyboard(e.Source))
                return;

            // Keep open when interacting with textboxes; they manage popup open/reposition
            if (e.Source is TextBox)
                return;

            GcodeKeyboardPopup.IsOpen = false;
            this.Focus();
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void TextBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is TextBox textBox && GcodeKeyboardPopup != null && GcodeKeyboard != null)
            {
                // Force focus programmatically
                textBox.Focus();

                GcodeKeyboardPopup.PlacementTarget = textBox;
                GcodeKeyboardPopup.Placement = PlacementMode.Bottom;
                GcodeKeyboardPopup.HorizontalOffset = 0;
                GcodeKeyboardPopup.VerticalOffset = 5;

                GcodeKeyboard.OverwriteNextInput = true;
                GcodeKeyboardPopup.IsOpen = true;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
        {
            // Fallback for standard focus (e.g., Tab key traversal)
            if (sender is TextBox textBox && GcodeKeyboardPopup != null && GcodeKeyboard != null)
            {
                // Only open if not already open to avoid resetting state unexpectedly
                if (!GcodeKeyboardPopup.IsOpen)
                {
                    GcodeKeyboardPopup.PlacementTarget = textBox;
                    GcodeKeyboardPopup.Placement = PlacementMode.Bottom;
                    GcodeKeyboardPopup.HorizontalOffset = 0;
                    GcodeKeyboardPopup.VerticalOffset = 5;

                    GcodeKeyboard.OverwriteNextInput = true;
                    GcodeKeyboardPopup.IsOpen = true;
                }
            }
        }

        private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
        {
        }
    }
}
