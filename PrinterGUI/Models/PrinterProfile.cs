namespace PrinterGUI.Models
{
    public sealed class PrinterProfile
    {
        public string Name { get; set; } = "Current printer";
        public double DoorTravelMm { get; set; } = 17;
        public bool RequiresDoorOpenBeforeHoming { get; set; } = true;
        public bool SafeHomingEnabled { get; set; } = true;
        public double BedSizeXMm { get; set; } = 261;
        public double BedSizeYMm { get; set; } = 132;
        public double ZHomeXMm { get; set; } = 130;
        public double ZHomeYMm { get; set; } = 58;
    }
}
