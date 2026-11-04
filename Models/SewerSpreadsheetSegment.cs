namespace RevitSlopePlugin.Models
{
    public sealed class SewerSpreadsheetSegment
    {
        public int Row { get; set; }
        public int ExtensionColumn { get; set; }
        public string FirstCode { get; set; } = string.Empty;
        public string SecondCode { get; set; } = string.Empty;
        public string FirstNumber { get; set; } = string.Empty;
        public string SecondNumber { get; set; } = string.Empty;
        public double? ExistingExtensionMeters { get; set; }
        public double? MinimumSlope { get; set; }
        public double? CalculatedDiameterMm { get; set; }
        public string SegmentCode => $"{FirstCode} -> {SecondCode}";
    }
}
