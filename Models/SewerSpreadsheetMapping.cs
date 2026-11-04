namespace RevitSlopePlugin.Models
{
    public sealed class SewerSpreadsheetMapping
    {
        public string Name { get; set; } = "Esgoto";
        public string CodePrefix { get; set; } = "CI-ESG";
        public string FamilyNames { get; set; } = string.Empty;
        public string NumberParameterName { get; set; } = "Comments";

        public string DisplayName => string.IsNullOrWhiteSpace(CodePrefix)
            ? Name
            : $"{Name} ({CodePrefix})";
    }
}
