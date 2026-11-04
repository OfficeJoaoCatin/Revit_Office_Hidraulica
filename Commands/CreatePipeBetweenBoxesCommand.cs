using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using RevitSlopePlugin.UI;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitSlopePlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePipeBetweenBoxesCommand : IExternalCommand
    {
        internal const double MinimumHorizontalDistanceFeet = 0.01;
        private static readonly double PipeEndpointInsetFeet = UnitConversion.CmToFt(5.0);
        private static readonly double MaximumParallelEndpointInsetFeet = UnitConversion.CmToFt(20.0);

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            Logger.Info("=== Iniciando CreatePipeBetweenBoxesCommand ===");

            try
            {
                BoxPipeSettings settings = BoxPipeSettingsService.Load();
                PipeType? pipeType = ResolvePipeType(doc, settings);
                PipingSystemType? systemType = ResolveSystemType(doc, settings);

                if (pipeType == null || systemType == null)
                {
                    Logger.ShowError(
                        "Configuracao incompleta",
                        "Configure o tipo de tubo e o sistema de tubulacao antes de criar o trecho.");
                    return Result.Failed;
                }

                settings.PipeTypeName = pipeType.Name;
                settings.SystemTypeName = systemType.Name;

                FamilyInstance? originBox = PickBox(uiDoc, settings, "Selecione a caixa de origem");
                if (originBox == null) return Result.Cancelled;

                FamilyInstance? destinationBox = PickBox(uiDoc, settings, "Selecione a caixa de destino");
                if (destinationBox == null) return Result.Cancelled;

                if (originBox.Id == destinationBox.Id)
                {
                    Logger.ShowWarning("Selecao invalida", "Selecione duas caixas diferentes.");
                    return Result.Cancelled;
                }

                BoxInfo originInfo = ReadBoxInfo(originBox, settings, requireWritableHeight: false);
                BoxInfo destinationInfo = ReadBoxInfo(destinationBox, settings, requireWritableHeight: true);

                var inputWindow = new SlopeInputWindow(
                    $"Origem Id {originBox.Id.IntegerValue} -> Destino Id {destinationBox.Id.IntegerValue}");

                bool? dialogResult = inputWindow.ShowDialog();
                if (dialogResult != true || !inputWindow.IsConfirmed)
                {
                    return Result.Cancelled;
                }

                double slopePercent = inputWindow.SlopePercent;
                double horizontalDistance = CalculateHorizontalDistance(originInfo.Center, destinationInfo.Center);
                if (horizontalDistance <= MinimumHorizontalDistanceFeet)
                {
                    Logger.ShowError(
                        "Distancia invalida",
                        "As caixas estao sobrepostas em planta. Nao e possivel calcular a inclinacao.");
                    return Result.Failed;
                }

                CalculationResult calculation = Calculate(settings, originInfo, destinationInfo, horizontalDistance, slopePercent);
                List<string> warnings = BuildWarnings(doc, settings, destinationInfo, calculation);

                bool outsideLimits = calculation.NewDestinationHeightFeet < UnitConversion.CmToFt(settings.MinBoxHeightCm) ||
                    calculation.NewDestinationHeightFeet > UnitConversion.CmToFt(settings.MaxBoxHeightCm);

                if (outsideLimits && settings.ShouldBlockOnLimits)
                {
                    string limitMessage =
                        "A profundidade necessaria na caixa de destino esta fora dos limites configurados.\n\n" +
                        BuildCalculationSummary(settings, originInfo, destinationInfo, calculation, horizontalDistance, slopePercent, warnings);
                    Logger.ShowError("Limite de altura", limitMessage);
                    return Result.Failed;
                }

                if (!ConfirmPreview(settings, originInfo, destinationInfo, calculation, horizontalDistance, slopePercent, warnings))
                {
                    return Result.Cancelled;
                }

                using (Transaction tx = new Transaction(doc, "Criar tubo entre caixas"))
                {
                    tx.Start();

                    destinationInfo.HeightParameter.Set(calculation.NewDestinationHeightFeet);
                    doc.Regenerate();

                    ElementId levelId = ResolveLevelId(doc, originBox, destinationBox, calculation.StartPoint.Z);
                    foreach (PipeRun pipeRun in BuildPipeRuns(settings, calculation))
                    {
                        Pipe pipe = Pipe.Create(
                            doc,
                            systemType.Id,
                            pipeType.Id,
                            levelId,
                            pipeRun.StartPoint,
                            pipeRun.EndPoint);

                        SetPipeDiameter(pipe, UnitConversion.MmToFt(settings.DiameterMm));
                    }
                    doc.Regenerate();

                    tx.Commit();
                }

                string successMessage =
                    "Tubo criado com sucesso.\n\n" +
                    BuildCalculationSummary(settings, originInfo, destinationInfo, calculation, horizontalDistance, slopePercent, warnings);

                if (warnings.Count > 0)
                {
                    Logger.ShowWarning("Tubo criado com aviso", successMessage);
                }
                else
                {
                    Logger.ShowInfo("Tubo criado", successMessage);
                }

                Logger.Info(successMessage);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("Criacao de tubo entre caixas cancelada pelo usuario.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao criar tubo entre caixas", ex);
                message = ex.Message;
                Logger.ShowError(
                    "Criar tubo entre caixas",
                    $"Ocorreu um erro ao criar o tubo:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        internal static CalculationResult Calculate(
            BoxPipeSettings settings,
            BoxInfo origin,
            BoxInfo destination,
            double horizontalDistance,
            double slopePercent)
        {
            double birthClearance = UnitConversion.CmToFt(settings.BirthClearanceCm);
            double arrivalClearance = UnitConversion.CmToFt(settings.ArrivalClearanceCm);
            double startZ = origin.TopZ - origin.HeightFeet + birthClearance;
            BuildPipeEndpointPoints(origin, destination, startZ, startZ, out XYZ preliminaryStartPoint, out XYZ preliminaryEndPoint);
            double pipeHorizontalDistance = CalculateHorizontalDistance(preliminaryStartPoint, preliminaryEndPoint);
            double fall = pipeHorizontalDistance * (slopePercent / 100.0);
            double endZ = settings.IsReverseFlow ? startZ + fall : startZ - fall;
            double newDestinationHeight = destination.TopZ - (endZ - arrivalClearance);
            BuildPipeEndpointPoints(origin, destination, startZ, endZ, out XYZ startPoint, out XYZ endPoint);

            return new CalculationResult
            {
                StartPoint = startPoint,
                EndPoint = endPoint,
                PipeFallFeet = fall,
                NewDestinationHeightFeet = newDestinationHeight,
                PipeHorizontalDistanceFeet = pipeHorizontalDistance,
                SlopePercent = slopePercent,
                IsReverseFlow = settings.IsReverseFlow,
                OriginBox = origin,
                DestinationBox = destination
            };
        }

        internal static CalculationResult CalculateWithFixedDestination(
            BoxPipeSettings settings,
            BoxInfo origin,
            BoxInfo destination,
            double horizontalDistance,
            double slopePercent)
        {
            double birthClearance = UnitConversion.CmToFt(settings.BirthClearanceCm);
            double startZ = origin.TopZ - origin.HeightFeet + birthClearance;
            BuildPipeEndpointPoints(origin, destination, startZ, startZ, out XYZ preliminaryStartPoint, out XYZ preliminaryEndPoint);
            double pipeHorizontalDistance = CalculateHorizontalDistance(preliminaryStartPoint, preliminaryEndPoint);
            double fall = pipeHorizontalDistance * (slopePercent / 100.0);
            double endZ = settings.IsReverseFlow ? startZ + fall : startZ - fall;
            double destinationBottomZ = destination.TopZ - destination.HeightFeet;
            bool adjustedOriginHeight = false;

            if (endZ < destinationBottomZ)
            {
                endZ = destinationBottomZ;
                startZ = settings.IsReverseFlow ? endZ - fall : endZ + fall;
                adjustedOriginHeight = true;
            }
            else if (endZ > destination.TopZ)
            {
                endZ = destination.TopZ;
                startZ = settings.IsReverseFlow ? endZ - fall : endZ + fall;
                adjustedOriginHeight = true;
            }

            double newOriginHeight = origin.TopZ - (startZ - birthClearance);
            BuildPipeEndpointPoints(origin, destination, startZ, endZ, out XYZ startPoint, out XYZ endPoint);

            return new CalculationResult
            {
                StartPoint = startPoint,
                EndPoint = endPoint,
                PipeFallFeet = fall,
                NewDestinationHeightFeet = destination.HeightFeet,
                NewOriginHeightFeet = newOriginHeight,
                HasNewOriginHeight = adjustedOriginHeight,
                PipeHorizontalDistanceFeet = pipeHorizontalDistance,
                SlopePercent = slopePercent,
                IsReverseFlow = settings.IsReverseFlow,
                OriginBox = origin,
                DestinationBox = destination
            };
        }

        internal static bool ConfirmPreview(
            BoxPipeSettings settings,
            BoxInfo origin,
            BoxInfo destination,
            CalculationResult calculation,
            double horizontalDistance,
            double slopePercent,
            List<string> warnings)
        {
            string content = BuildCalculationSummary(
                settings,
                origin,
                destination,
                calculation,
                horizontalDistance,
                slopePercent,
                warnings);

            TaskDialog dialog = new TaskDialog("Confirmar tubo entre caixas")
            {
                MainInstruction = "Confirmar criacao do tubo?",
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Ok
            };

            return dialog.Show() == TaskDialogResult.Ok;
        }

        internal static List<string> BuildWarnings(
            Document doc,
            BoxPipeSettings settings,
            BoxInfo destination,
            CalculationResult calculation,
            bool includeDestinationHeightWarnings = true)
        {
            var warnings = new List<string>();
            double minHeight = UnitConversion.CmToFt(settings.MinBoxHeightCm);
            double maxHeight = UnitConversion.CmToFt(settings.MaxBoxHeightCm);

            if (includeDestinationHeightWarnings && calculation.NewDestinationHeightFeet < minHeight)
            {
                warnings.Add("Nova Altura da caixa de destino ficou abaixo do minimo configurado.");
            }

            if (includeDestinationHeightWarnings && calculation.NewDestinationHeightFeet > maxHeight)
            {
                warnings.Add("Nova Altura da caixa de destino excedeu o maximo configurado.");
            }

            if (HasPipeEndpointInsideBox(doc, destination))
            {
                warnings.Add("A caixa de destino parece ja possuir tubo chegando ou saindo dela.");
            }

            return warnings;
        }

        internal static string BuildCalculationSummary(
            BoxPipeSettings settings,
            BoxInfo origin,
            BoxInfo destination,
            CalculationResult calculation,
            double horizontalDistance,
            double slopePercent,
            List<string> warnings)
        {
            string warningText = warnings.Count == 0
                ? "Nenhum."
                : string.Join("\n", warnings.Select(warning => $"- {warning}"));

            return
                $"Tipo de tubo: {settings.PipeTypeName}\n" +
                $"Sistema: {settings.SystemTypeName}\n" +
                $"Tubos: {FormatPipeBundle(settings)}\n" +
                $"Sentido: {settings.FlowDirection}\n" +
                $"Inclinacao: {slopePercent:F2}%\n" +
                $"Distancia horizontal: {FormatFeetAsMeters(horizontalDistance)}\n" +
                $"Desnivel calculado: {FormatFeetAsCentimeters(calculation.PipeFallFeet)}\n" +
                $"Cota nascimento: {FormatElevation(calculation.StartPoint.Z)}\n" +
                $"Cota chegada: {FormatElevation(calculation.EndPoint.Z)}\n" +
                $"Altura caixa destino: {FormatFeetAsCentimeters(destination.HeightFeet)} -> {FormatFeetAsCentimeters(calculation.NewDestinationHeightFeet)}\n" +
                $"Topos lidos individualmente: origem {FormatElevation(origin.TopZ)}, destino {FormatElevation(destination.TopZ)}\n" +
                $"Avisos: {warningText}";
        }

        internal static FamilyInstance? PickBox(UIDocument uiDoc, BoxPipeSettings settings, string prompt)
        {
            Reference reference = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new BoxSelectionFilter(settings.HeightParameterName),
                prompt);

            return uiDoc.Document.GetElement(reference) as FamilyInstance;
        }

        internal static BoxInfo ReadBoxInfo(
            FamilyInstance box,
            BoxPipeSettings settings,
            bool requireWritableHeight)
        {
            if (box.Location is not LocationPoint locationPoint)
            {
                throw new InvalidOperationException($"A caixa Id {box.Id.IntegerValue} nao possui ponto de insercao.");
            }

            BoundingBoxXYZ? boundingBox = box.get_BoundingBox(null);
            if (boundingBox == null)
            {
                throw new InvalidOperationException($"Nao foi possivel ler o topo da caixa Id {box.Id.IntegerValue}.");
            }

            Parameter? heightParameter = box.LookupParameter(settings.HeightParameterName);
            if (heightParameter == null)
            {
                throw new InvalidOperationException(
                    $"A caixa Id {box.Id.IntegerValue} nao possui o parametro '{settings.HeightParameterName}'.");
            }

            if (heightParameter.StorageType != StorageType.Double)
            {
                throw new InvalidOperationException(
                    $"O parametro '{settings.HeightParameterName}' da caixa Id {box.Id.IntegerValue} precisa ser numerico.");
            }

            if (requireWritableHeight && heightParameter.IsReadOnly)
            {
                throw new InvalidOperationException(
                    $"O parametro '{settings.HeightParameterName}' da caixa de destino esta bloqueado ou e de tipo.");
            }

            return new BoxInfo
            {
                Element = box,
                Center = locationPoint.Point,
                TopZ = boundingBox.Max.Z,
                HeightFeet = heightParameter.AsDouble(),
                HeightParameter = heightParameter,
                BoundingBox = boundingBox
            };
        }

        internal static PipeType? ResolvePipeType(Document doc, BoxPipeSettings settings)
        {
            List<PipeType> pipeTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .OrderBy(type => type.Name)
                .ToList();

            if (string.IsNullOrWhiteSpace(settings.PipeTypeName))
            {
                return pipeTypes.FirstOrDefault();
            }

            return pipeTypes.FirstOrDefault(type => type.Name == settings.PipeTypeName);
        }

        internal static PipingSystemType? ResolveSystemType(Document doc, BoxPipeSettings settings)
        {
            List<PipingSystemType> systemTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .OrderBy(type => type.Name)
                .ToList();

            if (string.IsNullOrWhiteSpace(settings.SystemTypeName))
            {
                return systemTypes.FirstOrDefault();
            }

            return systemTypes.FirstOrDefault(type => type.Name == settings.SystemTypeName);
        }

        internal static ElementId ResolveLevelId(
            Document doc,
            FamilyInstance originBox,
            FamilyInstance destinationBox,
            double elevation)
        {
            if (originBox.LevelId != ElementId.InvalidElementId)
            {
                return originBox.LevelId;
            }

            if (destinationBox.LevelId != ElementId.InvalidElementId)
            {
                return destinationBox.LevelId;
            }

            Level? nearestLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - elevation))
                .FirstOrDefault();

            if (nearestLevel == null)
            {
                throw new InvalidOperationException("Nao foi encontrado um nivel para criar o tubo.");
            }

            return nearestLevel.Id;
        }

        internal static void SetPipeDiameter(Pipe pipe, double diameterFeet)
        {
            Parameter diameterParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diameterParameter != null && !diameterParameter.IsReadOnly)
            {
                diameterParameter.Set(diameterFeet);
            }
        }

        internal static List<PipeRun> BuildPipeRuns(BoxPipeSettings settings, CalculationResult calculation)
        {
            int quantity = Math.Max(1, settings.PipeQuantity);
            var pipeRuns = new List<PipeRun>(quantity);

            XYZ startPoint = calculation.StartPoint;
            XYZ endPoint = calculation.EndPoint;
            double dx = endPoint.X - startPoint.X;
            double dy = endPoint.Y - startPoint.Y;
            double horizontalLength = Math.Sqrt(dx * dx + dy * dy);

            if (horizontalLength <= MinimumHorizontalDistanceFeet || quantity == 1)
            {
                pipeRuns.Add(new PipeRun { StartPoint = startPoint, EndPoint = endPoint });
                return pipeRuns;
            }

            double directionDx = calculation.DestinationBox != null && calculation.OriginBox != null
                ? calculation.DestinationBox.Center.X - calculation.OriginBox.Center.X
                : dx;
            double directionDy = calculation.DestinationBox != null && calculation.OriginBox != null
                ? calculation.DestinationBox.Center.Y - calculation.OriginBox.Center.Y
                : dy;
            double directionLength = Math.Sqrt((directionDx * directionDx) + (directionDy * directionDy));
            if (directionLength <= MinimumHorizontalDistanceFeet)
            {
                pipeRuns.Add(new PipeRun { StartPoint = startPoint, EndPoint = endPoint });
                return pipeRuns;
            }

            double spacing = UnitConversion.MmToFt(settings.DiameterMm) + UnitConversion.CmToFt(5.0);
            double center = (quantity - 1) / 2.0;
            double perpendicularX = -directionDy / directionLength;
            double perpendicularY = directionDx / directionLength;

            for (int index = 0; index < quantity; index++)
            {
                double offset = (index - center) * spacing;
                double offsetX = perpendicularX * offset;
                double offsetY = perpendicularY * offset;

                if (calculation.OriginBox != null && calculation.DestinationBox != null)
                {
                    double endpointInset = CalculateParallelEndpointInset(offset);
                    BuildPipeEndpointPoints(
                        calculation.OriginBox,
                        calculation.DestinationBox,
                        startPoint.Z,
                        endPoint.Z,
                        offsetX,
                        offsetY,
                        endpointInset,
                        out XYZ offsetStartPoint,
                        out XYZ offsetEndPoint);

                    double runHorizontalDistance = CalculateHorizontalDistance(offsetStartPoint, offsetEndPoint);
                    double runFall = runHorizontalDistance * (calculation.SlopePercent / 100.0);
                    double runEndZ = calculation.IsReverseFlow
                        ? startPoint.Z + runFall
                        : startPoint.Z - runFall;

                    BuildPipeEndpointPoints(
                        calculation.OriginBox,
                        calculation.DestinationBox,
                        startPoint.Z,
                        runEndZ,
                        offsetX,
                        offsetY,
                        endpointInset,
                        out offsetStartPoint,
                        out offsetEndPoint);

                    pipeRuns.Add(new PipeRun
                    {
                        StartPoint = offsetStartPoint,
                        EndPoint = offsetEndPoint
                    });

                    continue;
                }

                pipeRuns.Add(new PipeRun
                {
                    StartPoint = new XYZ(startPoint.X + offsetX, startPoint.Y + offsetY, startPoint.Z),
                    EndPoint = new XYZ(endPoint.X + offsetX, endPoint.Y + offsetY, endPoint.Z)
                });
            }

            return pipeRuns;
        }

        internal static string FormatPipeBundle(BoxPipeSettings settings)
        {
            int quantity = Math.Max(1, settings.PipeQuantity);
            return quantity == 1
                ? $"{settings.DiameterMm:F0} mm"
                : $"{quantity}x{settings.DiameterMm:F0} mm";
        }

        internal static void BuildPipeEndpointPoints(
            BoxInfo origin,
            BoxInfo destination,
            double startZ,
            double endZ,
            out XYZ startPoint,
            out XYZ endPoint)
        {
            BuildPipeEndpointPoints(
                origin,
                destination,
                startZ,
                endZ,
                0.0,
                0.0,
                PipeEndpointInsetFeet,
                out startPoint,
                out endPoint);
        }

        internal static void BuildPipeEndpointPoints(
            BoxInfo origin,
            BoxInfo destination,
            double startZ,
            double endZ,
            double offsetX,
            double offsetY,
            double endpointInsetFeet,
            out XYZ startPoint,
            out XYZ endPoint)
        {
            startPoint = ProjectPointNearBoxEdge(origin, destination.Center, startZ, offsetX, offsetY, endpointInsetFeet);
            endPoint = ProjectPointNearBoxEdge(destination, origin.Center, endZ, offsetX, offsetY, endpointInsetFeet);
        }

        internal static bool HasPipeEndpointInsideBox(Document doc, BoxInfo boxInfo)
        {
            BoundingBoxXYZ bb = boxInfo.BoundingBox;
            double margin = UnitConversion.CmToFt(5.0);

            foreach (Pipe pipe in new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>())
            {
                if (pipe.Location is not LocationCurve lc)
                {
                    continue;
                }

                Curve curve = lc.Curve;
                if (IsPointInsideBoxXY(curve.GetEndPoint(0), bb, margin) ||
                    IsPointInsideBoxXY(curve.GetEndPoint(1), bb, margin))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointInsideBoxXY(XYZ point, BoundingBoxXYZ bb, double margin)
        {
            return point.X >= bb.Min.X - margin &&
                point.X <= bb.Max.X + margin &&
                point.Y >= bb.Min.Y - margin &&
                point.Y <= bb.Max.Y + margin;
        }

        internal static double CalculateHorizontalDistance(XYZ first, XYZ second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static XYZ ProjectPointNearBoxEdge(BoxInfo box, XYZ targetCenter, double z)
        {
            return ProjectPointNearBoxEdge(box, targetCenter, z, 0.0, 0.0, PipeEndpointInsetFeet);
        }

        private static XYZ ProjectPointNearBoxEdge(
            BoxInfo box,
            XYZ targetCenter,
            double z,
            double offsetX,
            double offsetY,
            double endpointInsetFeet)
        {
            double dx = targetCenter.X - box.Center.X;
            double dy = targetCenter.Y - box.Center.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length <= MinimumHorizontalDistanceFeet)
            {
                return new XYZ(box.Center.X + offsetX, box.Center.Y + offsetY, z);
            }

            double ux = dx / length;
            double uy = dy / length;
            var shiftedCenter = new XYZ(box.Center.X + offsetX, box.Center.Y + offsetY, box.Center.Z);
            double boundaryDistance = IsRoundBox(box)
                ? FindEllipseBoundaryDistance(box.BoundingBox, shiftedCenter, ux, uy)
                : FindBoundaryDistance(box.BoundingBox, shiftedCenter, ux, uy);
            double endpointDistance = Math.Max(0.0, boundaryDistance - endpointInsetFeet);

            double x = shiftedCenter.X + ux * endpointDistance;
            double y = shiftedCenter.Y + uy * endpointDistance;
            return new XYZ(
                Clamp(x, box.BoundingBox.Min.X, box.BoundingBox.Max.X),
                Clamp(y, box.BoundingBox.Min.Y, box.BoundingBox.Max.Y),
                z);
        }

        private static double CalculateParallelEndpointInset(double offset)
        {
            double extraInset = Math.Abs(offset) * 0.35;
            return Math.Min(MaximumParallelEndpointInsetFeet, PipeEndpointInsetFeet + extraInset);
        }

        private static bool IsRoundBox(BoxInfo box)
        {
            string familyName = box.Element.Symbol?.Family?.Name ?? string.Empty;
            string typeName = box.Element.Symbol?.Name ?? string.Empty;
            string text = $"{familyName} {typeName}".ToUpperInvariant();
            return text.Contains("CIRC") || text.Contains("REDON") || text.Contains("ROUND");
        }

        private static double FindEllipseBoundaryDistance(BoundingBoxXYZ boundingBox, XYZ point, double ux, double uy)
        {
            double centerX = (boundingBox.Min.X + boundingBox.Max.X) / 2.0;
            double centerY = (boundingBox.Min.Y + boundingBox.Max.Y) / 2.0;
            double radiusX = Math.Abs(boundingBox.Max.X - boundingBox.Min.X) / 2.0;
            double radiusY = Math.Abs(boundingBox.Max.Y - boundingBox.Min.Y) / 2.0;

            if (radiusX <= MinimumHorizontalDistanceFeet || radiusY <= MinimumHorizontalDistanceFeet)
            {
                return FindBoundaryDistance(boundingBox, point, ux, uy);
            }

            double px = point.X - centerX;
            double py = point.Y - centerY;
            double a = ((ux * ux) / (radiusX * radiusX)) + ((uy * uy) / (radiusY * radiusY));
            double b = 2.0 * (((px * ux) / (radiusX * radiusX)) + ((py * uy) / (radiusY * radiusY)));
            double c = ((px * px) / (radiusX * radiusX)) + ((py * py) / (radiusY * radiusY)) - 1.0;
            double discriminant = (b * b) - (4.0 * a * c);

            if (a <= 0.0 || discriminant < 0.0)
            {
                return FindBoundaryDistance(boundingBox, point, ux, uy);
            }

            double sqrt = Math.Sqrt(discriminant);
            double first = (-b + sqrt) / (2.0 * a);
            double second = (-b - sqrt) / (2.0 * a);
            double boundaryDistance = new[] { first, second }
                .Where(distance => distance > 0.0 && !double.IsInfinity(distance) && !double.IsNaN(distance))
                .DefaultIfEmpty(0.0)
                .Min();

            return boundaryDistance > 0.0
                ? boundaryDistance
                : FindBoundaryDistance(boundingBox, point, ux, uy);
        }

        private static double FindBoundaryDistance(BoundingBoxXYZ boundingBox, XYZ center, double ux, double uy)
        {
            var distances = new List<double>();
            AddBoundaryDistance(distances, boundingBox.Min.X, boundingBox.Max.X, center.X, ux);
            AddBoundaryDistance(distances, boundingBox.Min.Y, boundingBox.Max.Y, center.Y, uy);

            double boundaryDistance = distances
                .Where(distance => distance > 0.0 && !double.IsInfinity(distance) && !double.IsNaN(distance))
                .DefaultIfEmpty(0.0)
                .Min();

            return boundaryDistance;
        }

        private static void AddBoundaryDistance(
            List<double> distances,
            double min,
            double max,
            double origin,
            double direction)
        {
            const double directionTolerance = 1.0e-9;
            if (Math.Abs(direction) <= directionTolerance)
            {
                return;
            }

            double boundary = direction > 0.0 ? max : min;
            distances.Add((boundary - origin) / direction);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static string FormatFeetAsMeters(double value)
        {
            return $"{UnitConversion.FtToM(value).ToString("F2", CultureInfo.InvariantCulture)} m";
        }

        private static string FormatFeetAsCentimeters(double value)
        {
            return $"{UnitConversion.FtToCm(value).ToString("F1", CultureInfo.InvariantCulture)} cm";
        }

        private static string FormatElevation(double value)
        {
            return $"{UnitConversion.FtToM(value).ToString("F3", CultureInfo.InvariantCulture)} m";
        }

        internal sealed class CalculationResult
        {
            public XYZ StartPoint { get; set; } = XYZ.Zero;
            public XYZ EndPoint { get; set; } = XYZ.Zero;
            public double PipeFallFeet { get; set; }
            public double NewDestinationHeightFeet { get; set; }
            public double NewOriginHeightFeet { get; set; }
            public bool HasNewOriginHeight { get; set; }
            public double PipeHorizontalDistanceFeet { get; set; }
            public double SlopePercent { get; set; }
            public bool IsReverseFlow { get; set; }
            public BoxInfo? OriginBox { get; set; }
            public BoxInfo? DestinationBox { get; set; }
        }

        internal sealed class PipeRun
        {
            public XYZ StartPoint { get; set; } = XYZ.Zero;
            public XYZ EndPoint { get; set; } = XYZ.Zero;
        }

        internal sealed class BoxInfo
        {
            public FamilyInstance Element { get; set; } = null!;
            public XYZ Center { get; set; } = XYZ.Zero;
            public double TopZ { get; set; }
            public double HeightFeet { get; set; }
            public Parameter HeightParameter { get; set; } = null!;
            public BoundingBoxXYZ BoundingBox { get; set; } = null!;
        }

        internal sealed class BoxSelectionFilter : ISelectionFilter
        {
            private readonly string _heightParameterName;

            public BoxSelectionFilter(string heightParameterName)
            {
                _heightParameterName = heightParameterName;
            }

            public bool AllowElement(Element elem)
            {
                if (elem is not FamilyInstance familyInstance)
                {
                    return false;
                }

                if (familyInstance.Location is not LocationPoint)
                {
                    return false;
                }

                Parameter? parameter = familyInstance.LookupParameter(_heightParameterName);
                return parameter != null && parameter.StorageType == StorageType.Double;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
