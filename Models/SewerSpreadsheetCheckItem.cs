using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitSlopePlugin.Models
{
    public sealed class SewerSpreadsheetCheckItem
    {
        public string SegmentCode { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string Type { get; set; } = string.Empty;
        public string SheetValue { get; set; } = string.Empty;
        public string ModelValue { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<ElementId> ElementIds { get; } = new List<ElementId>();
    }
}
