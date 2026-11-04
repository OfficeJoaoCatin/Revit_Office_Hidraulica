using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
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
    public class CreateConfiguredPipeSegmentsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            Logger.Info("=== Iniciando CreateConfiguredPipeSegmentsCommand ===");

            try
            {
                BoxPipeSettings settings = BoxPipeSettingsService.Load();
                List<PipeType> pipeTypes = GetPipeTypes(doc);
                List<PipingSystemType> systemTypes = GetSystemTypes(doc);

                if (pipeTypes.Count == 0 || systemTypes.Count == 0)
                {
                    Logger.ShowError(
                        "Configuracao incompleta",
                        "Configure o tipo de tubo e verifique se existe pelo menos um sistema de tubulacao no projeto.");
                    return Result.Failed;
                }

                PipeType pipeType = pipeTypes
                    .FirstOrDefault(type => type.Name.Equals(settings.PipeTypeName, StringComparison.OrdinalIgnoreCase)) ??
                    pipeTypes[0];

                settings.PipeTypeName = pipeType.Name;

                bool undoGroupOpen = false;
                using (var undoGroup = new TransactionGroup(doc, "Criar tubos por trecho"))
                {
                    undoGroup.Start();
                    undoGroupOpen = true;

                    try
                    {
                        using (var selectionOverlay = new SelectionNumberOverlayService(doc, doc.ActiveView))
                        {
                            List<FamilyInstance> boxes = PickBoxSequence(uiDoc, settings, selectionOverlay);
                            if (boxes.Count < 2)
                            {
                                return Result.Cancelled;
                            }

                            List<CreatePipeBetweenBoxesCommand.BoxInfo> boxInfos = boxes
                                .Select((box, index) => CreatePipeBetweenBoxesCommand.ReadBoxInfo(
                                    box,
                                    settings,
                                    requireWritableHeight: index > 0))
                                .ToList();

                            List<ConfiguredPipeSegmentInput> segmentInputs = BuildSegmentInputs(settings, boxInfos);
                            var configWindow = new ConfiguredPipeSegmentsWindow(
                                systemTypes,
                                settings.SystemTypeName,
                                pipeTypes,
                                settings.PipeTypeName,
                                segmentInputs);

                            bool? dialogResult = configWindow.ShowDialog();
                            if (dialogResult != true ||
                                !configWindow.IsConfirmed ||
                                configWindow.SelectedSystemType == null)
                            {
                                return Result.Cancelled;
                            }

                            PipingSystemType systemType = configWindow.SelectedSystemType;
                            settings.SystemTypeName = systemType.Name;

                            List<ConfiguredSegmentPlan> plans = BuildSegmentPlans(
                                doc,
                                settings,
                                pipeTypes,
                                boxInfos,
                                configWindow.Segments);

                            List<string> blockingMessages = GetBlockingMessages(settings, plans);
                            if (blockingMessages.Count > 0)
                            {
                                Logger.ShowError(
                                    "Limite de altura",
                                    "A sequencia foi bloqueada pelos limites configurados.\n\n" +
                                    string.Join("\n", blockingMessages));
                                return Result.Failed;
                            }

                            selectionOverlay.Clear();

                            using (Transaction tx = new Transaction(doc, "Criar tubos por trecho"))
                            {
                                tx.Start();

                                foreach (ConfiguredSegmentPlan plan in plans)
                                {
                                    plan.Destination.HeightParameter.Set(plan.Calculation.NewDestinationHeightFeet);
                                }

                                doc.Regenerate();

                                foreach (ConfiguredSegmentPlan plan in plans)
                                {
                                    ElementId levelId = CreatePipeBetweenBoxesCommand.ResolveLevelId(
                                        doc,
                                        plan.Origin.Element,
                                        plan.Destination.Element,
                                        plan.Calculation.StartPoint.Z);

                                    foreach (CreatePipeBetweenBoxesCommand.PipeRun pipeRun in
                                        CreatePipeBetweenBoxesCommand.BuildPipeRuns(plan.Settings, plan.Calculation))
                                    {
                                        Pipe pipe = Pipe.Create(
                                            doc,
                                            systemType.Id,
                                            plan.PipeType.Id,
                                            levelId,
                                            pipeRun.StartPoint,
                                            pipeRun.EndPoint);

                                        CreatePipeBetweenBoxesCommand.SetPipeDiameter(
                                            pipe,
                                            UnitConversion.MmToFt(plan.Settings.DiameterMm));
                                    }
                                }

                                doc.Regenerate();
                                tx.Commit();
                            }

                            undoGroup.Assimilate();
                            undoGroupOpen = false;

                            string summary = BuildSummary(settings, plans);
                            if (plans.Any(plan => plan.Warnings.Count > 0))
                            {
                                Logger.ShowWarning("Tubos por trecho criados com aviso", summary);
                            }
                            else
                            {
                                Logger.ShowInfo("Tubos por trecho criados", summary);
                            }

                            Logger.Info(summary);
                            return Result.Succeeded;
                        }
                    }
                    finally
                    {
                        if (undoGroupOpen)
                        {
                            RollBackUndoGroupQuietly(undoGroup);
                        }
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("Criacao de tubos por trecho cancelada pelo usuario.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao criar tubos por trecho", ex);
                message = ex.Message;
                Logger.ShowError(
                    "Tubos por trecho",
                    $"Ocorreu um erro ao criar os tubos:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static List<PipingSystemType> GetSystemTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .OrderBy(type => type.Name)
                .ToList();
        }

        private static List<PipeType> GetPipeTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .OrderBy(type => type.Name)
                .ToList();
        }

        private static List<FamilyInstance> PickBoxSequence(
            UIDocument uiDoc,
            BoxPipeSettings settings,
            SelectionNumberOverlayService selectionOverlay)
        {
            var boxes = new List<FamilyInstance>();

            while (true)
            {
                string prompt = boxes.Count < 2
                    ? $"Selecione a caixa {boxes.Count + 1} da sequencia"
                    : $"Selecione a caixa {boxes.Count + 1} da sequencia ou pressione ESC para finalizar";

                try
                {
                    FamilyInstance? box = CreatePipeBetweenBoxesCommand.PickBox(uiDoc, settings, prompt);
                    if (box == null)
                    {
                        break;
                    }

                    if (boxes.Any(existing => existing.Id == box.Id))
                    {
                        Logger.ShowWarning("Caixa repetida", "Essa caixa ja foi selecionada na sequencia.");
                        continue;
                    }

                    boxes.Add(box);
                    selectionOverlay.AddNumber(box, boxes.Count);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
            }

            if (boxes.Count < 2)
            {
                Logger.ShowWarning("Sequencia incompleta", "Selecione pelo menos duas caixas.");
            }

            return boxes;
        }

        private static List<ConfiguredPipeSegmentInput> BuildSegmentInputs(
            BoxPipeSettings settings,
            List<CreatePipeBetweenBoxesCommand.BoxInfo> boxInfos)
        {
            var inputs = new List<ConfiguredPipeSegmentInput>();

            for (int index = 0; index < boxInfos.Count - 1; index++)
            {
                double horizontalDistance = CreatePipeBetweenBoxesCommand.CalculateHorizontalDistance(
                    boxInfos[index].Center,
                    boxInfos[index + 1].Center);

                inputs.Add(new ConfiguredPipeSegmentInput
                {
                    Index = index + 1,
                    OriginLabel = $"Caixa {index + 1}",
                    DestinationLabel = $"Caixa {index + 2}",
                    DistanceText = $"{UnitConversion.FtToM(horizontalDistance).ToString("F2", CultureInfo.InvariantCulture)} m",
                    PipeTypeName = settings.PipeTypeName,
                    SlopePercentText = "2.0",
                    PipeQuantityText = Math.Max(1, settings.PipeQuantity).ToString(CultureInfo.InvariantCulture),
                    DiameterMmText = settings.DiameterMm.ToString("0.##", CultureInfo.InvariantCulture)
                });
            }

            return inputs;
        }

        private static List<ConfiguredSegmentPlan> BuildSegmentPlans(
            Document doc,
            BoxPipeSettings settings,
            List<PipeType> pipeTypes,
            List<CreatePipeBetweenBoxesCommand.BoxInfo> boxInfos,
            List<ConfiguredPipeSegmentInput> inputs)
        {
            var plans = new List<ConfiguredSegmentPlan>();
            var plannedHeights = new Dictionary<ElementId, double>();

            for (int index = 0; index < inputs.Count; index++)
            {
                ConfiguredPipeSegmentInput input = inputs[index];
                CreatePipeBetweenBoxesCommand.BoxInfo origin = CloneBoxInfo(boxInfos[index]);
                CreatePipeBetweenBoxesCommand.BoxInfo destination = CloneBoxInfo(boxInfos[index + 1]);

                if (plannedHeights.TryGetValue(origin.Element.Id, out double plannedOriginHeight))
                {
                    origin.HeightFeet = plannedOriginHeight;
                }

                double horizontalDistance = CreatePipeBetweenBoxesCommand.CalculateHorizontalDistance(
                    origin.Center,
                    destination.Center);

                if (horizontalDistance <= CreatePipeBetweenBoxesCommand.MinimumHorizontalDistanceFeet)
                {
                    throw new InvalidOperationException(
                        $"Caixas {index + 1} e {index + 2} estao sobrepostas em planta.");
                }

                BoxPipeSettings segmentSettings = BuildSegmentSettings(settings, input);
                PipeType pipeType = pipeTypes.FirstOrDefault(type =>
                    type.Name.Equals(input.PipeTypeName, StringComparison.OrdinalIgnoreCase)) ??
                    throw new InvalidOperationException($"Tipo de tubo '{input.PipeTypeName}' nao encontrado para o trecho {input.Index}.");
                CreatePipeBetweenBoxesCommand.CalculationResult calculation =
                    CreatePipeBetweenBoxesCommand.Calculate(
                        segmentSettings,
                        origin,
                        destination,
                        horizontalDistance,
                        input.SlopePercent);

                plannedHeights[destination.Element.Id] = calculation.NewDestinationHeightFeet;

                plans.Add(new ConfiguredSegmentPlan
                {
                    Input = input,
                    Settings = segmentSettings,
                    PipeType = pipeType,
                    Origin = origin,
                    Destination = destination,
                    HorizontalDistanceFeet = horizontalDistance,
                    Calculation = calculation,
                    Warnings = CreatePipeBetweenBoxesCommand.BuildWarnings(
                        doc,
                        segmentSettings,
                        destination,
                        calculation)
                });
            }

            return plans;
        }

        private static BoxPipeSettings BuildSegmentSettings(
            BoxPipeSettings baseSettings,
            ConfiguredPipeSegmentInput input)
        {
            return new BoxPipeSettings
            {
                PipeTypeName = baseSettings.PipeTypeName,
                SystemTypeName = baseSettings.SystemTypeName,
                DiameterMm = input.DiameterMm,
                PipeQuantity = input.PipeQuantity,
                BirthClearanceCm = baseSettings.BirthClearanceCm,
                ArrivalClearanceCm = baseSettings.ArrivalClearanceCm,
                MinBoxHeightCm = baseSettings.MinBoxHeightCm,
                MaxBoxHeightCm = baseSettings.MaxBoxHeightCm,
                LimitBehavior = baseSettings.LimitBehavior,
                HeightParameterName = baseSettings.HeightParameterName,
                FlowDirection = baseSettings.FlowDirection,
                SpreadsheetExtensionHeader = baseSettings.SpreadsheetExtensionHeader,
                SpreadsheetMappings = baseSettings.SpreadsheetMappings
            };
        }

        private static List<string> GetBlockingMessages(
            BoxPipeSettings settings,
            List<ConfiguredSegmentPlan> plans)
        {
            var messages = new List<string>();
            if (!settings.ShouldBlockOnLimits)
            {
                return messages;
            }

            foreach (ConfiguredSegmentPlan plan in plans)
            {
                double newHeightCm = UnitConversion.FtToCm(plan.Calculation.NewDestinationHeightFeet);
                if (newHeightCm < settings.MinBoxHeightCm || newHeightCm > settings.MaxBoxHeightCm)
                {
                    messages.Add(
                        $"Trecho {plan.Input.Index}: caixa destino Id {plan.Destination.Element.Id.IntegerValue} exige Altura {newHeightCm:F1} cm.");
                }
            }

            return messages;
        }

        private static string BuildSummary(
            BoxPipeSettings settings,
            List<ConfiguredSegmentPlan> plans)
        {
            string header =
                $"Trechos: {plans.Count}\n" +
                $"Tipo de tubo: por trecho\n" +
                $"Sistema: {settings.SystemTypeName}\n" +
                $"Sentido: {settings.FlowDirection}\n\n";

            string detail = string.Join(
                "\n\n",
                plans.Select(plan =>
                {
                    string warningText = plan.Warnings.Count == 0
                        ? "Nenhum."
                        : string.Join("\n", plan.Warnings.Select(warning => $"- {warning}"));

                    return
                        $"Trecho {plan.Input.Index}: Id {plan.Origin.Element.Id.IntegerValue} -> Id {plan.Destination.Element.Id.IntegerValue}\n" +
                        $"Tipo: {plan.PipeType.Name}\n" +
                        $"Tubos: {CreatePipeBetweenBoxesCommand.FormatPipeBundle(plan.Settings)} | Inclinacao: {plan.Input.SlopePercent:F2}%\n" +
                        $"Distancia: {FormatFeetAsMeters(plan.HorizontalDistanceFeet)} | Desnivel: {FormatFeetAsCentimeters(plan.Calculation.PipeFallFeet)}\n" +
                        $"Cotas: {FormatElevation(plan.Calculation.StartPoint.Z)} -> {FormatElevation(plan.Calculation.EndPoint.Z)}\n" +
                        $"Altura destino: {FormatFeetAsCentimeters(plan.Destination.HeightFeet)} -> {FormatFeetAsCentimeters(plan.Calculation.NewDestinationHeightFeet)}\n" +
                        $"Avisos: {warningText}";
                }));

            return header + detail;
        }

        private static void RollBackUndoGroupQuietly(TransactionGroup undoGroup)
        {
            try
            {
                undoGroup.RollBack();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel reverter o grupo temporario de tubos por trecho: {ex.Message}");
            }
        }

        private static CreatePipeBetweenBoxesCommand.BoxInfo CloneBoxInfo(
            CreatePipeBetweenBoxesCommand.BoxInfo source)
        {
            return new CreatePipeBetweenBoxesCommand.BoxInfo
            {
                Element = source.Element,
                Center = source.Center,
                TopZ = source.TopZ,
                HeightFeet = source.HeightFeet,
                HeightParameter = source.HeightParameter,
                BoundingBox = source.BoundingBox
            };
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

        private sealed class ConfiguredSegmentPlan
        {
            public ConfiguredPipeSegmentInput Input { get; set; } = null!;
            public BoxPipeSettings Settings { get; set; } = null!;
            public PipeType PipeType { get; set; } = null!;
            public CreatePipeBetweenBoxesCommand.BoxInfo Origin { get; set; } = null!;
            public CreatePipeBetweenBoxesCommand.BoxInfo Destination { get; set; } = null!;
            public double HorizontalDistanceFeet { get; set; }
            public CreatePipeBetweenBoxesCommand.CalculationResult Calculation { get; set; } = null!;
            public List<string> Warnings { get; set; } = new List<string>();
        }
    }
}
