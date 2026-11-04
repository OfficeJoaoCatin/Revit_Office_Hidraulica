namespace RevitSlopePlugin.Models
{
    using System.Collections.Generic;

    public class BoxPipeSettings
    {
        public string PipeTypeName { get; set; } = string.Empty;
        public string SystemTypeName { get; set; } = string.Empty;
        public double DiameterMm { get; set; } = 100.0;
        public int PipeQuantity { get; set; } = 1;
        public double BirthClearanceCm { get; set; } = 1.0;
        public double ArrivalClearanceCm { get; set; } = 1.0;
        public double MinBoxHeightCm { get; set; } = 30.0;
        public double MaxBoxHeightCm { get; set; } = 300.0;
        public string LimitBehavior { get; set; } = LimitBehaviorBlock;
        public string HeightParameterName { get; set; } = "Altura";
        public string FlowDirection { get; set; } = FlowOriginToDestination;
        public string SpreadsheetExtensionHeader { get; set; } = "Extensao";
        public List<SewerSpreadsheetMapping> SpreadsheetMappings { get; set; } =
            new List<SewerSpreadsheetMapping>();

        public const string LimitBehaviorBlock = "Bloquear";
        public const string LimitBehaviorWarn = "Criar com aviso";
        public const string FlowOriginToDestination = "Origem para destino";
        public const string FlowDestinationToOrigin = "Destino para origem";

        public bool ShouldBlockOnLimits => LimitBehavior == LimitBehaviorBlock;
        public bool IsReverseFlow => FlowDirection == FlowDestinationToOrigin;
    }
}
