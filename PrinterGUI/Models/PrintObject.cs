using System.ComponentModel;

namespace PrinterGUI.Models
{
    public class PrintObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty; // source STL path or resource identifier
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double HeightMm { get; set; }

        public override string ToString() => Name;
    }
}