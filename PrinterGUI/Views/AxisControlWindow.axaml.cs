using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PrinterGUI.ViewModels;

namespace PrinterGUI.Views
{
    public partial class AxisControlWindow : Window
    {
        private TextBox? _activeTextBox;

        public AxisControlWindow()
        {
            InitializeComponent();
        }

        public AxisControlWindow(string serialPort) : this()
        {
            DataContext = new AxisControlViewModel(serialPort);

            if (GcodeKeyboard != null)
            {
                GcodeKeyboard.EnterPressed += (s, e) => HideKeyboard();
            }

            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Bubble);

            if (CustomGcodeTextBox != null)
            {
                CustomGcodeTextBox.AddHandler(PointerPressedEvent, TextBox_PointerPressed, RoutingStrategies.Bubble);
            }
        }

        private bool IsPointerInsideGcodeKeyboard(object? source)
        {
            if (GcodeKeyboard == null || source is not Control sourceControl)
                return false;

            return sourceControl == GcodeKeyboard || sourceControl.GetVisualAncestors().Contains(GcodeKeyboard);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (GcodeKeyboard == null || !GcodeKeyboard.IsVisible)
                return;

            if (IsPointerInsideGcodeKeyboard(e.Source))
                return;

            if (e.Source is TextBox)
                return;

            HideKeyboard();
        }

        private void ShowKeyboard(TextBox textBox)
        {
            _activeTextBox = textBox;
            textBox.Focus();

            if (GcodeKeyboard != null)
            {
                GcodeKeyboard.OverwriteNextInput = true;
                GcodeKeyboard.IsVisible = true;
            }
        }

        private void HideKeyboard()
        {
            if (GcodeKeyboard != null)
            {
                GcodeKeyboard.IsVisible = false;
            }

            _activeTextBox = null;
            this.Focus();
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void TextBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                ShowKeyboard(textBox);
                e.Handled = true;
            }
        }

        private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                ShowKeyboard(textBox);
            }
        }

        private void TextBox_LostFocus(object? sender, RoutedEventArgs e)
        {
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
