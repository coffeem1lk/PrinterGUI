using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PrinterGUI.ViewModels;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

namespace PrinterGUI.Views
{
    public partial class MainWindow : Window
    {
        private TextBox? _activeTextBox;

        public MainWindow()
        {
            InitializeComponent();

            if (NumericKeyboard != null)
            {
                NumericKeyboard.EnterPressed += (s, e) => HideKeyboard();
            }

            this.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Bubble);

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
                tb.AddHandler(PointerPressedEvent, TextBox_PointerPressed, RoutingStrategies.Bubble);
            }
        }

        private bool IsPointerInsideNumericKeyboard(object? source)
        {
            if (NumericKeyboard == null || source is not Control sourceControl)
                return false;

            return sourceControl == NumericKeyboard || sourceControl.GetVisualAncestors().Contains(NumericKeyboard);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (NumericKeyboard == null || !NumericKeyboard.IsVisible)
                return;

            if (IsPointerInsideNumericKeyboard(e.Source))
                return;

            if (e.Source is TextBox)
                return;

            HideKeyboard();
        }

        private void ShowKeyboard(TextBox textBox)
        {
            _activeTextBox = textBox;
            textBox.Focus();

            if (NumericKeyboard != null)
            {
                NumericKeyboard.OverwriteNextInput = true;
                NumericKeyboard.IsVisible = true;
            }
        }

        private void HideKeyboard()
        {
            if (NumericKeyboard != null)
            {
                NumericKeyboard.IsVisible = false;
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

        private async void SendToPrinter_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var dlg = new ConfirmationDialog("ARE YOU SURE??");
            var result = await dlg.ShowDialog<bool>(this);

            if (result && vm.SendToPrinterCommand.CanExecute(null))
                vm.SendToPrinterCommand.Execute(null);
        }

        private void AxisControl_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var axisWindow = new AxisControlWindow(vm.SerialPortPath);
                axisWindow.Show();
            }
        }

        private void SetProbeOffset_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var probeWindow = new ProbeOffsetWindow(vm.SerialPortPath);
                probeWindow.Show();
            }
        }

        private async void PrintCustomGcode_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a G-code file",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("G-code")
                    {
                        Patterns = new[] { "*.gcode", "*.gc", "*.gco" }
                    }
                }
            });

            if (files == null || files.Count == 0)
                return;

            var selectedPath = files[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            var dlg = new ConfirmationDialog($"PRINT THIS FILE?\n{Path.GetFileName(selectedPath)}");
            var result = await dlg.ShowDialog<bool>(this);
            if (!result) return;

            await vm.SendCustomGcodeFileAsync(selectedPath);
        }
    }
}