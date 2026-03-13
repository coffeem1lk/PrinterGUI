using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace PrinterGUI.Controls
{
    public partial class GcodeKeyboard : UserControl
    {
        public GcodeKeyboard()
        {
            InitializeComponent();
        }

        private void Number_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string digit)
            {
                InsertText(digit);
            }
        }

        private void Character_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string character)
            {
                InsertText(character);
            }
        }

        private void Enter_Click(object? sender, RoutedEventArgs e)
        {
            // Move focus to next control
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.FocusManager?.GetFocusedElement() is Control focused)
            {
                KeyboardNavigationHandler.GetNext(focused, NavigationDirection.Next)?.Focus();
            }
        }

        private void InsertText(string text)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var focused = topLevel?.FocusManager?.GetFocusedElement();
            
            if (focused is TextBox textBox)
            {
                int caretIndex = textBox.CaretIndex;
                string currentText = textBox.Text ?? string.Empty;
                
                // Insert text at caret position
                string newText = currentText.Insert(caretIndex, text);
                textBox.Text = newText;
                textBox.CaretIndex = caretIndex + text.Length;
            }
        }
    }
}