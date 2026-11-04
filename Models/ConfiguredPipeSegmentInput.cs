namespace RevitSlopePlugin.Models
{
    public sealed class ConfiguredPipeSegmentInput
    {
        public int Index { get; set; }
        public string OriginLabel { get; set; } = string.Empty;
        public string DestinationLabel { get; set; } = string.Empty;
        public string DistanceText { get; set; } = string.Empty;
        public string PipeTypeName { get; set; } = string.Empty;
        public string SlopePercentText { get; set; } = "2.0";
        public string PipeQuantityText { get; set; } = "1";
        public string DiameterMmText { get; set; } = "100";
        public double SlopePercent { get; set; } = 2.0;
        public int PipeQuantity { get; set; } = 1;
        public double DiameterMm { get; set; } = 100.0;
    }
}
