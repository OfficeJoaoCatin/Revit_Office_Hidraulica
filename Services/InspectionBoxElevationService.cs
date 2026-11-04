using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitSlopePlugin.Models;
using System;

namespace RevitSlopePlugin.Services
{
    internal sealed class InspectionBoxElevationService
    {
        private const double MovementToleranceFeet = 0.0001;
        private readonly Document _doc;

        public InspectionBoxElevationService(Document doc)
        {
            _doc = doc;
        }

        public bool TryReadBoxPositionAndTop(
            FamilyInstance box,
            out XYZ insertionPoint,
            out double topElevationFeet,
            out string reason)
        {
            insertionPoint = XYZ.Zero;
            topElevationFeet = 0.0;

            if (box.Location is not LocationPoint locationPoint)
            {
                reason = "a familia nao possui ponto de insercao";
                return false;
            }

            BoundingBoxXYZ? boundingBox = box.get_BoundingBox(null);
            if (boundingBox == null)
            {
                reason = "nao foi possivel ler a geometria/bounding box";
                return false;
            }

            // O ponto de insercao e usado apenas para X/Y. O topo vem do BoundingBox,
            // evitando assumir que a origem da familia coincide com a tampa da caixa.
            insertionPoint = locationPoint.Point;
            topElevationFeet = boundingBox.Max.Z;
            reason = string.Empty;
            return true;
        }

        public BoxTopographyAlignmentResult AlignTopToElevation(
            FamilyInstance box,
            double currentTopFeet,
            double targetTopFeet)
        {
            var result = new BoxTopographyAlignmentResult
            {
                BoxId = box.Id,
                CurrentTopFeet = currentTopFeet,
                TargetTopFeet = targetTopFeet,
                DeltaFeet = targetTopFeet - currentTopFeet
            };

            if (box.Pinned)
            {
                result.Message = "elemento fixado (Pinned)";
                return result;
            }

            if (Math.Abs(result.DeltaFeet) <= MovementToleranceFeet)
            {
                result.Success = true;
                result.Message = "ja estava alinhada";
                return result;
            }

            try
            {
                // O deslocamento vertical e a diferenca entre a cota da topografia
                // e o topo atual da caixa: topo final = topo atual + delta.
                ElementTransformUtils.MoveElement(_doc, box.Id, new XYZ(0.0, 0.0, result.DeltaFeet));

                _doc.Regenerate();
                if (!TryReadBoxPositionAndTop(box, out _, out double movedTopFeet, out string reason))
                {
                    result.Message = $"movida, mas nao foi possivel conferir o topo: {reason}";
                    return result;
                }

                double residual = targetTopFeet - movedTopFeet;
                if (Math.Abs(residual) > MovementToleranceFeet)
                {
                    result.Success = false;
                    result.Message = $"movimento nao alterou o topo como esperado; residual {residual:F4} ft";
                    return result;
                }

                result.Success = true;
                result.Message = "alinhada";
            }
            catch (Exception ex)
            {
                result.Message = $"nao foi possivel mover: {ex.Message}";
            }

            return result;
        }
    }
}
