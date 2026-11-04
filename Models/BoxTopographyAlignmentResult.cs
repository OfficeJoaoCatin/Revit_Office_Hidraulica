using Autodesk.Revit.DB;

namespace RevitSlopePlugin.Models
{
    internal sealed class BoxTopographyAlignmentResult
    {
        public ElementId BoxId { get; set; } = ElementId.InvalidElementId;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ReferenceSurfaceName { get; set; } = string.Empty;
        public double CurrentTopFeet { get; set; }
        public double TargetTopFeet { get; set; }
        public double DeltaFeet { get; set; }
    }
}
