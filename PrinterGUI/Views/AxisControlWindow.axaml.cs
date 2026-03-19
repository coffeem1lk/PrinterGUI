using System;
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

            if (GcodeKeyboard != null)
            {
                GcodeKeyboard.EnterPressed += (s, e) =>
                {
                    if (GcodeKeyboardPopup != null) GcodeKeyboardPopup.IsOpen = false;
                    this.Focus();
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                };
            }

            // Use Bubble (not Tunnel) for touch-stable behavior
            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Bubble);

            if (CustomGcodeTextBox != null)
            {
                CustomGcodeTextBox.AddHandler(PointerPressedEvent, TextBox_PointerPressed, RoutingStrategies.Bubble);
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (GcodeKeyboardPopup == null || !GcodeKeyboardPopup.IsOpen)
                return;

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
                textBox.Focus();

                GcodeKeyboardPopup.PlacementTarget = textBox;
                GcodeKeyboardPopup.Placement = PlacementMode.Bottom;
                GcodeKeyboardPopup.HorizontalOffset = 0;
                GcodeKeyboardPopup.VerticalOffset = 5;

                GcodeKeyboard.OverwriteNextInput = true;
                GcodeKeyboardPopup.IsOpen = true;

                // Prevent this same press from bubbling to window-close logic
                e.Handled = true;
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
