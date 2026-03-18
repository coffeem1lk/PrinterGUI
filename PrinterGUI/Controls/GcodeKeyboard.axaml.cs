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
        // Tracks if the next input should overwrite the existing textbox content
        public bool OverwriteNextInput { get; set; }

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

        private void Space_Click(object? sender, RoutedEventArgs e)
        {
            InsertText(" ");
        }

        private void Del_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var focused = topLevel?.FocusManager?.GetFocusedElement();
            
            if (focused is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
            {
                if (OverwriteNextInput)
                {
                    // Clear the whole box if it's the first deletion after clicking
                    textBox.Text = string.Empty;
                    textBox.CaretIndex = 0;
                    OverwriteNextInput = false;
                }
                else
                {
                    int caretIndex = textBox.CaretIndex;
                    if (caretIndex > 0)
                    {
                        textBox.Text = textBox.Text.Remove(caretIndex - 1, 1);
                        textBox.CaretIndex = caretIndex - 1;
                    }
                }
            }
        }

        private void Enter_Click(object? sender, RoutedEventArgs e)
        {
            // Clear focus to make the keyboard disappear
            var topLevel = TopLevel.GetTopLevel(this);
            topLevel?.FocusManager?.ClearFocus();
        }

        private void InsertText(string text)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var focused = topLevel?.FocusManager?.GetFocusedElement();
            
            if (focused is TextBox textBox)
            {
                if (OverwriteNextInput)
                {
                    // Replace all text with the new input
                    textBox.Text = text;
                    textBox.CaretIndex = text.Length;
                    OverwriteNextInput = false;
                }
                else
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
}