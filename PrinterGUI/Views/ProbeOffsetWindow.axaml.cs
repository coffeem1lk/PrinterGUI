using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PrinterGUI.ViewModels;

namespace PrinterGUI.Views
{
    public partial class ProbeOffsetWindow : Window
    {
        public ProbeOffsetWindow()
        {
            InitializeComponent();
        }

        public ProbeOffsetWindow(string serialPort) : this()
        {
            DataContext = new ProbeOffsetViewModel(serialPort);
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
