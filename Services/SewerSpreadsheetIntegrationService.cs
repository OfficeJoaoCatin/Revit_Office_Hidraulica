using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    internal sealed class SewerSpreadsheetIntegrationService
    {
        private readonly Document _doc;
        private readonly BoxPipeSettings _settings;
        private readonly SewerSpreadsheetMapping _mapping;

        public SewerSpreadsheetIntegrationService(
            Document doc,
            BoxPipeSettings settings,
            SewerSpreadsheetMapping mapping)
        {
            _doc = doc;
            _settings = settings;
            _mapping = mapping;
        }

        public SewerSpreadsheetProcessResult ProcessSegments(List<SewerSpreadsheetSegment> segments)
        {
            List<BoxNumberRecord> boxRecords = IndexBoxesByNumber();
            var result = new SewerSpreadsheetProcessResult();

            AddDuplicateNumberIssues(boxRecords, result.Items);

            foreach (SewerSpreadsheetSegment segment in segments)
            {
                ProcessSegment(segment, boxRecords, result);
            }

            return result;
        }

        private void ProcessSegment(
            SewerSpreadsheetSegment segment,
            List<BoxNumberRecord> boxRecords,
            SewerSpreadsheetProcessResult result)
        {
            FamilyInstance? firstBox = ResolveSingleBox(
                segment.FirstNumber,
                segment.FirstCode,
                segment,
                boxRecords,
                result.Items);

            FamilyInstance? secondBox = ResolveSingleBox(
                segment.SecondNumber,
                segment.SecondCode,
                segment,
                boxRecords,
                result.Items);
            if (firstBox == null || secondBox == null)
            {
                return;
            }

            if (!TryReadBoxMetrics(firstBox, out BoxMetrics firstMetrics, out string firstReason))
            {
                AddIssue(result.Items, segment, "Pendencia", string.Empty, string.Empty, firstReason, firstBox.Id);
                return;
            }

            if (!TryReadBoxMetrics(secondBox, out BoxMetrics secondMetrics, out string secondReason))
            {
                AddIssue(result.Items, segment, "Pendencia", string.Empty, string.Empty, secondReason, secondBox.Id);
                return;
            }

            BoxMetrics upstream = firstMetrics.CollectorElevationFeet >= secondMetrics.CollectorElevationFeet
                ? firstMetrics
                : secondMetrics;
            BoxMetrics downstream = upstream.Element.Id == firstMetrics.Element.Id ? secondMetrics : firstMetrics;

            double extensionFeet = HorizontalDistance(upstream.Center, downstream.Center);
            double extensionMeters = UnitConversion.FtToM(extensionFeet);

            double slopeFraction = extensionFeet <= 0.0001
                ? 0.0
                : (upstream.CollectorElevationFeet - downstream.CollectorElevationFeet) / extensionFeet;
            double slopePercent = slopeFraction * 100.0;

            double upstreamDepth = upstream.TopElevationFeet - upstream.CollectorElevationFeet;
            double downstreamDepth = downstream.TopElevationFeet - downstream.CollectorElevationFeet;
            double? modeledDiameterMm = TryReadModeledPipeDiameterMm(upstream, downstream, out ElementId pipeId);

            CompareMinimumSlope(segment, result.Items, slopePercent, upstream.Element.Id, downstream.Element.Id);
            CompareDiameter(segment, result.Items, modeledDiameterMm, upstream.Element.Id, downstream.Element.Id, pipeId);

            if (NeedsExtensionWrite(segment.ExistingExtensionMeters, extensionMeters))
            {
                result.ExtensionsToWrite[(segment.Row, segment.ExtensionColumn)] = extensionMeters;
                AddIssue(
                    result.Items,
                    segment,
                    "Conferencia",
                    FormatOptionalMeters(segment.ExistingExtensionMeters, "Pendente aprovacao"),
                    $"{extensionMeters.ToString("F2", CultureInfo.InvariantCulture)} m",
                    $"Extensao calculada, ainda nao gravada | declividade {slopePercent:F2}% | prof. montante {UnitConversion.FtToM(upstreamDepth):F2} m | prof. jusante {UnitConversion.FtToM(downstreamDepth):F2} m",
                    upstream.Element.Id,
                    downstream.Element.Id);
            }
            else
            {
                AddIssue(
                    result.Items,
                    segment,
                    "Conferencia",
                    FormatOptionalMeters(segment.ExistingExtensionMeters, "Atualizada"),
                    $"{extensionMeters.ToString("F2", CultureInfo.InvariantCulture)} m",
                    $"Extensao da planilha ja confere com o modelo | declividade {slopePercent:F2}% | prof. montante {UnitConversion.FtToM(upstreamDepth):F2} m | prof. jusante {UnitConversion.FtToM(downstreamDepth):F2} m",
                    upstream.Element.Id,
                    downstream.Element.Id);
            }
        }

        private List<BoxNumberRecord> IndexBoxesByNumber()
        {
            HashSet<string> familyNames = SplitFamilyNames(_mapping.FamilyNames);

            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(instance => familyNames.Count == 0 || IsMappedFamily(instance, familyNames))
                .Select(instance => CreateBoxNumberRecord(
                    instance,
                    ReadParameterText(instance, _mapping.NumberParameterName)))
                .Where(record => !string.IsNullOrWhiteSpace(record.Number))
                .ToList();
        }

        private BoxNumberRecord CreateBoxNumberRecord(FamilyInstance box, string rawValue)
        {
            string number = XlsxWorkbookService.NormalizeNumber(rawValue);
            bool hasExplicitPrefix = HasLetters(rawValue);
            return new BoxNumberRecord
            {
                Box = box,
                RawValue = rawValue ?? string.Empty,
                Number = number,
                NormalizedCode = hasExplicitPrefix ? NormalizeCode(rawValue) : number,
                HasExplicitPrefix = hasExplicitPrefix
            };
        }

        private void AddDuplicateNumberIssues(
            List<BoxNumberRecord> boxRecords,
            List<SewerSpreadsheetCheckItem> items)
        {
            foreach (var pair in boxRecords
                .Where(record => !string.IsNullOrWhiteSpace(record.NormalizedCode))
                .GroupBy(record => record.NormalizedCode)
                .Where(group => group.Count() > 1))
            {
                var item = new SewerSpreadsheetCheckItem
                {
                    SegmentCode = pair.Key,
                    Severity = "Aviso",
                    Type = "Numero duplicado",
                    SheetValue = pair.Key,
                    ModelValue = $"{pair.Count()} caixas",
                    Message = "Mais de uma caixa com o mesmo codigo no parametro configurado."
                };

                item.ElementIds.AddRange(pair.Select(record => record.Box.Id));
                items.Add(item);
            }
        }

        private FamilyInstance? ResolveSingleBox(
            string number,
            string segmentCode,
            SewerSpreadsheetSegment segment,
            List<BoxNumberRecord> boxRecords,
            List<SewerSpreadsheetCheckItem> items)
        {
            List<BoxNumberRecord> candidates = boxRecords
                .Where(record => record.Number == number)
                .ToList();

            if (candidates.Count == 0)
            {
                AddIssue(
                    items,
                    segment,
                    "Pendencia",
                    number,
                    "Nao encontrada",
                    $"Caixa numero {number} nao encontrada no modelo pelo mapeamento '{_mapping.DisplayName}'.");
                return null;
            }

            string normalizedSegmentCode = NormalizeCode(segmentCode);
            List<BoxNumberRecord> exactCodeMatches = candidates
                .Where(record => record.NormalizedCode == normalizedSegmentCode)
                .ToList();

            if (exactCodeMatches.Count == 1)
            {
                return exactCodeMatches[0].Box;
            }

            List<BoxNumberRecord> prefixedCandidates = candidates
                .Where(record => record.HasExplicitPrefix)
                .ToList();

            List<BoxNumberRecord> numberOnlyMatches = candidates
                .Where(record => !record.HasExplicitPrefix)
                .ToList();

            if (numberOnlyMatches.Count == 1 && exactCodeMatches.Count == 0)
            {
                return numberOnlyMatches[0].Box;
            }

            if (numberOnlyMatches.Count > 1 && exactCodeMatches.Count == 0)
            {
                AddIssue(
                    items,
                    segment,
                    "Pendencia",
                    segmentCode,
                    $"{numberOnlyMatches.Count} caixas sem prefixo",
                    $"Codigo '{segmentCode}' encontrou mais de uma caixa com apenas o numero {number} no parametro configurado. Informe o codigo completo em uma delas ou ajuste o mapeamento de familias.",
                    numberOnlyMatches.Select(record => record.Box.Id).ToArray());
                return null;
            }

            if (exactCodeMatches.Count > 1)
            {
                AddIssue(
                    items,
                    segment,
                    "Pendencia",
                    number,
                    $"{exactCodeMatches.Count} caixas",
                    $"Codigo '{segmentCode}' possui duplicidade exata no modelo.",
                    exactCodeMatches.Select(record => record.Box.Id).ToArray());
                return null;
            }

            if (prefixedCandidates.Count > 0)
            {
                AddIssue(
                    items,
                    segment,
                    "Pendencia",
                    segmentCode,
                    string.Join(", ", prefixedCandidates.Select(record => record.RawValue).Distinct()),
                    $"So existem caixas com prefixo explicito diferente para o numero {number}. Para '{segmentCode}', use uma caixa com codigo completo igual ou apenas '{number}' no parametro configurado.",
                    prefixedCandidates.Concat(numberOnlyMatches).Select(record => record.Box.Id).ToArray());
                return null;
            }

            AddIssue(
                items,
                segment,
                "Pendencia",
                number,
                $"{candidates.Count} caixas",
                $"Caixa numero {number} e ambigua. Use o codigo completo no parametro, por exemplo '{segmentCode}', ou ajuste o mapeamento de familias.",
                candidates.Select(record => record.Box.Id).ToArray());
            return null;
        }

        private bool TryReadBoxMetrics(
            FamilyInstance box,
            out BoxMetrics metrics,
            out string reason)
        {
            metrics = new BoxMetrics();

            if (box.Location is not LocationPoint locationPoint)
            {
                reason = $"Caixa Id {box.Id.IntegerValue} sem ponto de insercao.";
                return false;
            }

            BoundingBoxXYZ? boundingBox = box.get_BoundingBox(null);
            if (boundingBox == null)
            {
                reason = $"Caixa Id {box.Id.IntegerValue} sem bounding box.";
                return false;
            }

            Parameter? heightParameter = box.LookupParameter(_settings.HeightParameterName);
            if (heightParameter == null || heightParameter.StorageType != StorageType.Double)
            {
                reason = $"Caixa Id {box.Id.IntegerValue} sem parametro numerico '{_settings.HeightParameterName}'.";
                return false;
            }

            double clearanceFeet = UnitConversion.CmToFt(_settings.BirthClearanceCm);
            metrics = new BoxMetrics
            {
                Element = box,
                Center = locationPoint.Point,
                TopElevationFeet = boundingBox.Max.Z,
                HeightFeet = heightParameter.AsDouble()
            };

            metrics.CollectorElevationFeet = metrics.TopElevationFeet - metrics.HeightFeet + clearanceFeet;
            reason = string.Empty;
            return true;
        }

        private void CompareMinimumSlope(
            SewerSpreadsheetSegment segment,
            List<SewerSpreadsheetCheckItem> items,
            double modelSlopePercent,
            params ElementId[] ids)
        {
            if (!segment.MinimumSlope.HasValue)
            {
                return;
            }

            double minimumSlopePercent = NormalizeSlopeToPercent(segment.MinimumSlope.Value);
            if (modelSlopePercent + 0.0001 >= minimumSlopePercent)
            {
                return;
            }

            AddIssue(
                items,
                segment,
                "Aviso",
                $"{minimumSlopePercent:F2}%",
                $"{modelSlopePercent:F2}%",
                "Declividade adotada no modelo abaixo da minima da planilha.",
                ids);
        }

        private void CompareDiameter(
            SewerSpreadsheetSegment segment,
            List<SewerSpreadsheetCheckItem> items,
            double? modeledDiameterMm,
            ElementId upstreamId,
            ElementId downstreamId,
            ElementId pipeId)
        {
            if (!segment.CalculatedDiameterMm.HasValue)
            {
                return;
            }

            if (!modeledDiameterMm.HasValue)
            {
                AddIssue(
                    items,
                    segment,
                    "Aviso",
                    $"{segment.CalculatedDiameterMm.Value:F0} mm",
                    "Nao encontrado",
                    "Nao foi encontrado tubo modelado diretamente entre as caixas.",
                    upstreamId,
                    downstreamId);
                return;
            }

            if (modeledDiameterMm.Value + 0.001 >= segment.CalculatedDiameterMm.Value)
            {
                return;
            }

            var ids = new List<ElementId> { upstreamId, downstreamId };
            if (pipeId != ElementId.InvalidElementId)
            {
                ids.Add(pipeId);
            }

            AddIssue(
                items,
                segment,
                "Aviso",
                $"{segment.CalculatedDiameterMm.Value:F0} mm",
                $"{modeledDiameterMm.Value:F0} mm",
                "Diametro adotado no modelo abaixo do calculado na planilha.",
                ids.ToArray());
        }

        private double? TryReadModeledPipeDiameterMm(
            BoxMetrics upstream,
            BoxMetrics downstream,
            out ElementId pipeId)
        {
            pipeId = ElementId.InvalidElementId;
            BoundingBoxXYZ? upstreamBox = upstream.Element.get_BoundingBox(null);
            BoundingBoxXYZ? downstreamBox = downstream.Element.get_BoundingBox(null);
            if (upstreamBox == null || downstreamBox == null)
            {
                return null;
            }

            foreach (Pipe pipe in new FilteredElementCollector(_doc).OfClass(typeof(Pipe)).Cast<Pipe>())
            {
                if (pipe.Location is not LocationCurve locationCurve)
                {
                    continue;
                }

                Curve curve = locationCurve.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);

                bool forward = IsPointInsideBoxXY(start, upstreamBox) && IsPointInsideBoxXY(end, downstreamBox);
                bool reverse = IsPointInsideBoxXY(start, downstreamBox) && IsPointInsideBoxXY(end, upstreamBox);
                if (!forward && !reverse)
                {
                    continue;
                }

                Parameter? diameterParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diameterParameter == null)
                {
                    continue;
                }

                pipeId = pipe.Id;
                return UnitConversion.FtToMm(diameterParameter.AsDouble());
            }

            return null;
        }

        private static void AddIssue(
            List<SewerSpreadsheetCheckItem> items,
            SewerSpreadsheetSegment segment,
            string type,
            string sheetValue,
            string modelValue,
            string message,
            params ElementId[] ids)
        {
            var item = new SewerSpreadsheetCheckItem
            {
                SegmentCode = segment.SegmentCode,
                Severity = type == "Conferencia" ? "Info" : "Aviso",
                Type = type,
                SheetValue = sheetValue,
                ModelValue = modelValue,
                Message = message
            };

            item.ElementIds.AddRange(ids.Where(id => id != ElementId.InvalidElementId));
            items.Add(item);
        }

        private static HashSet<string> SplitFamilyNames(string familyNames)
        {
            return new HashSet<string>(
                (familyNames ?? string.Empty)
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsMappedFamily(FamilyInstance instance, HashSet<string> familyNames)
        {
            string familyName = instance.Symbol?.Family?.Name ?? string.Empty;
            string typeName = instance.Symbol?.Name ?? string.Empty;
            return familyNames.Contains(familyName) || familyNames.Contains(typeName);
        }

        private static string NormalizeCode(string value)
        {
            string text = XlsxWorkbookService.RemoveDiacritics(value ?? string.Empty).ToUpperInvariant();
            string cleaned = new string(text
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '-')
                .ToArray());

            string number = XlsxWorkbookService.NormalizeNumber(cleaned);
            string prefix = new string(cleaned.TakeWhile(ch => !char.IsDigit(ch)).ToArray()).Trim('-');

            return string.IsNullOrWhiteSpace(prefix)
                ? number
                : $"{prefix}-{number}";
        }

        private static bool HasLetters(string value)
        {
            return (value ?? string.Empty).Any(char.IsLetter);
        }

        private static string ReadParameterText(Element element, string parameterName)
        {
            Parameter? parameter = element.LookupParameter(parameterName);
            if (parameter == null && parameterName.Equals("Comments", StringComparison.OrdinalIgnoreCase))
            {
                parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            }

            if (parameter == null)
            {
                return string.Empty;
            }

            return parameter.AsString() ??
                parameter.AsValueString() ??
                string.Empty;
        }

        private static double HorizontalDistance(XYZ first, XYZ second)
        {
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double NormalizeSlopeToPercent(double value)
        {
            return Math.Abs(value) <= 1.0 ? value * 100.0 : value;
        }

        private static bool NeedsExtensionWrite(double? existingMeters, double calculatedMeters)
        {
            const double toleranceMeters = 0.005;
            return !existingMeters.HasValue ||
                Math.Abs(existingMeters.Value - calculatedMeters) > toleranceMeters;
        }

        private static string FormatOptionalMeters(double? meters, string fallback)
        {
            return meters.HasValue
                ? $"{meters.Value.ToString("F2", CultureInfo.InvariantCulture)} m"
                : fallback;
        }

        private static bool IsPointInsideBoxXY(XYZ point, BoundingBoxXYZ box)
        {
            double margin = UnitConversion.CmToFt(50.0);
            return point.X >= box.Min.X - margin &&
                point.X <= box.Max.X + margin &&
                point.Y >= box.Min.Y - margin &&
                point.Y <= box.Max.Y + margin;
        }

        private sealed class BoxMetrics
        {
            public FamilyInstance Element { get; set; } = null!;
            public XYZ Center { get; set; } = XYZ.Zero;
            public double TopElevationFeet { get; set; }
            public double HeightFeet { get; set; }
            public double CollectorElevationFeet { get; set; }
        }

        private sealed class BoxNumberRecord
        {
            public FamilyInstance Box { get; set; } = null!;
            public string RawValue { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string NormalizedCode { get; set; } = string.Empty;
            public bool HasExplicitPrefix { get; set; }
        }
    }

    internal sealed class SewerSpreadsheetProcessResult
    {
        public List<SewerSpreadsheetCheckItem> Items { get; } = new List<SewerSpreadsheetCheckItem>();
        public Dictionary<(int Row, int Column), double> ExtensionsToWrite { get; } =
            new Dictionary<(int Row, int Column), double>();
    }
}
