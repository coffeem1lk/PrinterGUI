using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PrinterGUI.Models;
using PrinterGUI.Services;
using PrinterGUI.ViewModels;

namespace PrinterGUI.Views
{
    public partial class ProbeOffsetWindow : Window
    {
        public ProbeOffsetWindow()
        {
            InitializeComponent();
        }

        public ProbeOffsetWindow(SharedSerialPortService sharedPort, PrinterProfile printerProfile) : this()
        {
            DataContext = new ProbeOffsetViewModel(sharedPort, printerProfile);
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
