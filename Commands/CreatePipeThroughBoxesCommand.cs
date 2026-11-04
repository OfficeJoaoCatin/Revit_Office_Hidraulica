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
    internal enum BoxPipeSequenceMode
    {
        MainBranch,
        SecondaryBranch
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePipeThroughBoxesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return ExecuteSequence(
                commandData,
                ref message,
                BoxPipeSequenceMode.MainBranch,
                "Ramal principal");
        }

        internal static Result ExecuteSequence(
            ExternalCommandData commandData,
            ref string message,
            BoxPipeSequenceMode mode,
            string commandTitle)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            Logger.Info($"=== Iniciando {commandTitle} ===");

            try
            {
                BoxPipeSettings settings = BoxPipeSettingsService.Load();
                PipeType? pipeType = CreatePipeBetweenBoxesCommand.ResolvePipeType(doc, settings);
                PipingSystemType? systemType = CreatePipeBetweenBoxesCommand.ResolveSystemType(doc, settings);

                if (pipeType == null || systemType == null)
                {
                    Logger.ShowError(
                        "Configuracao incompleta",
                        "Configure o tipo de tubo e o sistema de tubulacao antes de criar a sequencia.");
                    return Result.Failed;
                }

                settings.PipeTypeName = pipeType.Name;
                settings.SystemTypeName = systemType.Name;

                bool undoGroupOpen = false;
                using (var undoGroup = new TransactionGroup(doc, $"Criar tubos - {commandTitle}"))
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

                            var inputWindow = new SlopeInputWindow($"{boxes.Count} caixas selecionadas - {commandTitle}");
                            bool? dialogResult = inputWindow.ShowDialog();
                            if (dialogResult != true || !inputWindow.IsConfirmed)
                            {
                                return Result.Cancelled;
                            }

                            double slopePercent = inputWindow.SlopePercent;
                            List<CreatePipeBetweenBoxesCommand.BoxInfo> boxInfos = boxes
                                .Select((box, index) => CreatePipeBetweenBoxesCommand.ReadBoxInfo(
                                    box,
                                    settings,
                                    requireWritableHeight: ShouldRequireWritableHeight(mode, index, boxes.Count)))
                                .ToList();

                            List<SegmentPlan> plans = BuildSegmentPlans(doc, settings, boxInfos, slopePercent, mode);
                            List<string> blockingMessages = GetBlockingMessages(settings, plans);
                            if (blockingMessages.Count > 0)
                            {
                                Logger.ShowError(
                                    "Limite de altura",
                                    "A sequencia foi bloqueada pelos limites configurados.\n\n" +
                                    string.Join("\n", blockingMessages));
                                return Result.Failed;
                            }

                            if (!ConfirmSequencePreview(settings, plans, slopePercent, mode, commandTitle))
                            {
                                return Result.Cancelled;
                            }

                            selectionOverlay.Clear();

                            using (Transaction tx = new Transaction(doc, $"Criar tubos - {commandTitle}"))
                            {
                                tx.Start();

                                Dictionary<ElementId, double> heightsToSet = BuildHeightUpdates(plans);
                                foreach (KeyValuePair<ElementId, double> update in heightsToSet)
                                {
                                    Element element = doc.GetElement(update.Key);
                                    if (element == null)
                                    {
                                        continue;
                                    }

                                    SegmentPlan? plan = plans.FirstOrDefault(candidate =>
                                        candidate.Destination.Element.Id == update.Key ||
                                        candidate.Origin.Element.Id == update.Key);
                                    Parameter? heightParameter = plan?.Destination.Element.Id == update.Key
                                        ? plan.Destination.HeightParameter
                                        : plan?.Origin.HeightParameter;

                                    if (heightParameter != null && !heightParameter.IsReadOnly)
                                    {
                                        heightParameter.Set(update.Value);
                                    }
                                }

                                doc.Regenerate();

                                foreach (SegmentPlan plan in plans)
                                {
                                    ElementId levelId = CreatePipeBetweenBoxesCommand.ResolveLevelId(
                                        doc,
                                        plan.Origin.Element,
                                        plan.Destination.Element,
                                        plan.Calculation.StartPoint.Z);

                                    foreach (CreatePipeBetweenBoxesCommand.PipeRun pipeRun in
                                        CreatePipeBetweenBoxesCommand.BuildPipeRuns(settings, plan.Calculation))
                                    {
                                        Pipe pipe = Pipe.Create(
                                            doc,
                                            systemType.Id,
                                            pipeType.Id,
                                            levelId,
                                            pipeRun.StartPoint,
                                            pipeRun.EndPoint);

                                        CreatePipeBetweenBoxesCommand.SetPipeDiameter(
                                            pipe,
                                            UnitConversion.MmToFt(settings.DiameterMm));
                                    }
                                }

                                doc.Regenerate();
                                tx.Commit();
                            }

                            undoGroup.Assimilate();
                            undoGroupOpen = false;

                            string successMessage = BuildSequenceSummary(settings, plans, slopePercent, mode);
                            if (plans.Any(plan => plan.Warnings.Count > 0))
                            {
                                Logger.ShowWarning($"{commandTitle} criado com aviso", successMessage);
                            }
                            else
                            {
                                Logger.ShowInfo($"{commandTitle} criado", successMessage);
                            }

                            Logger.Info(successMessage);
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
                Logger.Info($"Criacao de {commandTitle} cancelada pelo usuario.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error($"Erro ao criar {commandTitle}", ex);
                message = ex.Message;
                Logger.ShowError(
                    commandTitle,
                    $"Ocorreu um erro ao criar a sequencia:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static void RollBackUndoGroupQuietly(TransactionGroup undoGroup)
        {
            try
            {
                undoGroup.RollBack();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel reverter o grupo temporario da sequencia: {ex.Message}");
            }
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

        private static Dictionary<ElementId, double> BuildHeightUpdates(List<SegmentPlan> plans)
        {
            var heightsToSet = new Dictionary<ElementId, double>();

            foreach (SegmentPlan plan in plans)
            {
                if (!plan.KeepsDestinationHeight)
                {
                    heightsToSet[plan.Destination.Element.Id] = plan.Calculation.NewDestinationHeightFeet;
                }

                if (plan.Calculation.HasNewOriginHeight)
                {
                    heightsToSet[plan.Origin.Element.Id] = plan.Calculation.NewOriginHeightFeet;
                }
            }

            return heightsToSet;
        }

        private static bool ShouldRequireWritableHeight(BoxPipeSequenceMode mode, int boxIndex, int boxCount)
        {
            if (boxIndex == 0)
            {
                return false;
            }

            return mode != BoxPipeSequenceMode.SecondaryBranch || boxIndex < boxCount - 1;
        }

        private static List<SegmentPlan> BuildSegmentPlans(
            Document doc,
            BoxPipeSettings settings,
            List<CreatePipeBetweenBoxesCommand.BoxInfo> boxInfos,
            double slopePercent,
            BoxPipeSequenceMode mode)
        {
            var plans = new List<SegmentPlan>();
            var plannedHeights = new Dictionary<ElementId, double>();

            for (int index = 0; index < boxInfos.Count - 1; index++)
            {
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

                bool keepsDestinationHeight =
                    mode == BoxPipeSequenceMode.SecondaryBranch &&
                    index == boxInfos.Count - 2;

                CreatePipeBetweenBoxesCommand.CalculationResult calculation = keepsDestinationHeight
                    ? CreatePipeBetweenBoxesCommand.CalculateWithFixedDestination(
                        settings,
                        origin,
                        destination,
                        horizontalDistance,
                        slopePercent)
                    : CreatePipeBetweenBoxesCommand.Calculate(
                        settings,
                        origin,
                        destination,
                        horizontalDistance,
                        slopePercent);

                if (!keepsDestinationHeight)
                {
                    plannedHeights[destination.Element.Id] = calculation.NewDestinationHeightFeet;
                }
                else if (calculation.HasNewOriginHeight)
                {
                    if (origin.HeightParameter.IsReadOnly)
                    {
                        throw new InvalidOperationException(
                            $"A caixa de nascimento do ultimo trecho Id {origin.Element.Id.IntegerValue} precisa alterar Altura para a tubulacao chegar na ultima caixa, mas o parametro esta bloqueado.");
                    }

                    plannedHeights[origin.Element.Id] = calculation.NewOriginHeightFeet;
                }

                List<string> warnings = CreatePipeBetweenBoxesCommand.BuildWarnings(
                    doc,
                    settings,
                    destination,
                    calculation,
                    includeDestinationHeightWarnings: !keepsDestinationHeight);

                if (calculation.HasNewOriginHeight)
                {
                    warnings.Add("Altura da caixa de nascimento ajustada automaticamente para manter a queda e chegar na ultima caixa.");
                }

                plans.Add(new SegmentPlan
                {
                    Index = index + 1,
                    Origin = origin,
                    Destination = destination,
                    HorizontalDistanceFeet = horizontalDistance,
                    Calculation = calculation,
                    Warnings = warnings,
                    KeepsDestinationHeight = keepsDestinationHeight
                });
            }

            if (mode == BoxPipeSequenceMode.SecondaryBranch &&
                plans.Count > 0 &&
                plans[plans.Count - 1].Calculation.HasNewOriginHeight)
            {
                return BuildSecondaryBranchBackCalculatedPlans(
                    doc,
                    settings,
                    boxInfos,
                    slopePercent,
                    plans[plans.Count - 1].Calculation.EndPoint.Z);
            }

            return plans;
        }

        private static List<SegmentPlan> BuildSecondaryBranchBackCalculatedPlans(
            Document doc,
            BoxPipeSettings settings,
            List<CreatePipeBetweenBoxesCommand.BoxInfo> boxInfos,
            double slopePercent,
            double fixedDestinationEndZ)
        {
            var plans = new List<SegmentPlan>();
            var requiredHeights = new double?[boxInfos.Count];
            int lastBoxIndex = boxInfos.Count - 1;
            int lastSegmentIndex = boxInfos.Count - 2;
            double birthClearance = UnitConversion.CmToFt(settings.BirthClearanceCm);
            double arrivalClearance = UnitConversion.CmToFt(settings.ArrivalClearanceCm);

            requiredHeights[lastBoxIndex] = boxInfos[lastBoxIndex].HeightFeet;

            for (int index = lastSegmentIndex; index >= 0; index--)
            {
                CreatePipeBetweenBoxesCommand.BoxInfo origin = CloneBoxInfo(boxInfos[index]);
                CreatePipeBetweenBoxesCommand.BoxInfo destination = CloneBoxInfo(boxInfos[index + 1]);
                double destinationHeight = requiredHeights[index + 1] ?? destination.HeightFeet;
                double horizontalDistance = CreatePipeBetweenBoxesCommand.CalculateHorizontalDistance(
                    origin.Center,
                    destination.Center);

                if (horizontalDistance <= CreatePipeBetweenBoxesCommand.MinimumHorizontalDistanceFeet)
                {
                    throw new InvalidOperationException(
                        $"Caixas {index + 1} e {index + 2} estao sobrepostas em planta.");
                }

                bool keepsDestinationHeight = index == lastSegmentIndex;
                double endZ = keepsDestinationHeight
                    ? fixedDestinationEndZ
                    : destination.TopZ - destinationHeight + arrivalClearance;
                CreatePipeBetweenBoxesCommand.BuildPipeEndpointPoints(
                    origin,
                    destination,
                    endZ,
                    endZ,
                    out XYZ preliminaryStartPoint,
                    out XYZ preliminaryEndPoint);
                double pipeHorizontalDistance = CreatePipeBetweenBoxesCommand.CalculateHorizontalDistance(
                    preliminaryStartPoint,
                    preliminaryEndPoint);
                double fall = pipeHorizontalDistance * (slopePercent / 100.0);
                double startZ = settings.IsReverseFlow ? endZ - fall : endZ + fall;
                double newOriginHeight = origin.TopZ - (startZ - birthClearance);
                bool hasNewOriginHeight = HeightDiffers(origin.HeightFeet, newOriginHeight);
                requiredHeights[index] = newOriginHeight;
                CreatePipeBetweenBoxesCommand.BuildPipeEndpointPoints(
                    origin,
                    destination,
                    startZ,
                    endZ,
                    out XYZ startPoint,
                    out XYZ endPoint);

                if (hasNewOriginHeight && origin.HeightParameter.IsReadOnly)
                {
                    throw new InvalidOperationException(
                        $"A caixa Id {origin.Element.Id.IntegerValue} precisa alterar Altura para manter a continuidade do ramal secundario, mas o parametro esta bloqueado.");
                }

                var calculation = new CreatePipeBetweenBoxesCommand.CalculationResult
                {
                    StartPoint = startPoint,
                    EndPoint = endPoint,
                    PipeFallFeet = fall,
                    NewDestinationHeightFeet = keepsDestinationHeight
                        ? destination.HeightFeet
                        : destinationHeight,
                    NewOriginHeightFeet = newOriginHeight,
                    HasNewOriginHeight = hasNewOriginHeight,
                    PipeHorizontalDistanceFeet = pipeHorizontalDistance,
                    SlopePercent = slopePercent,
                    IsReverseFlow = settings.IsReverseFlow,
                    OriginBox = origin,
                    DestinationBox = destination
                };

                List<string> warnings = CreatePipeBetweenBoxesCommand.BuildWarnings(
                    doc,
                    settings,
                    destination,
                    calculation,
                    includeDestinationHeightWarnings: !keepsDestinationHeight);

                if (hasNewOriginHeight)
                {
                    warnings.Add(keepsDestinationHeight
                        ? "Altura da caixa de nascimento ajustada automaticamente para manter a queda e chegar na ultima caixa."
                        : "Altura da caixa de nascimento ajustada automaticamente para manter a continuidade com o trecho seguinte.");
                }

                plans.Insert(
                    0,
                    new SegmentPlan
                    {
                        Index = index + 1,
                        Origin = origin,
                        Destination = destination,
                        HorizontalDistanceFeet = horizontalDistance,
                        Calculation = calculation,
                        Warnings = warnings,
                        KeepsDestinationHeight = keepsDestinationHeight
                    });
            }

            return plans;
        }

        private static List<string> GetBlockingMessages(BoxPipeSettings settings, List<SegmentPlan> plans)
        {
            var messages = new List<string>();
            if (!settings.ShouldBlockOnLimits)
            {
                return messages;
            }

            foreach (SegmentPlan plan in plans)
            {
                if (plan.Calculation.HasNewOriginHeight)
                {
                    double newOriginHeightCm = UnitConversion.FtToCm(plan.Calculation.NewOriginHeightFeet);
                    if (newOriginHeightCm < settings.MinBoxHeightCm || newOriginHeightCm > settings.MaxBoxHeightCm)
                    {
                        messages.Add(
                            $"Trecho {plan.Index}: caixa de nascimento Id {plan.Origin.Element.Id.IntegerValue} exige Altura {newOriginHeightCm:F1} cm.");
                    }
                }

                if (plan.KeepsDestinationHeight)
                {
                    continue;
                }

                double newHeightCm = UnitConversion.FtToCm(plan.Calculation.NewDestinationHeightFeet);
                if (newHeightCm < settings.MinBoxHeightCm || newHeightCm > settings.MaxBoxHeightCm)
                {
                    messages.Add(
                        $"Trecho {plan.Index}: caixa destino Id {plan.Destination.Element.Id.IntegerValue} exige Altura {newHeightCm:F1} cm.");
                }
            }

            return messages;
        }

        private static bool ConfirmSequencePreview(
            BoxPipeSettings settings,
            List<SegmentPlan> plans,
            double slopePercent,
            BoxPipeSequenceMode mode,
            string commandTitle)
        {
            string content = BuildSequenceSummary(settings, plans, slopePercent, mode);
            TaskDialog dialog = new TaskDialog($"Confirmar {commandTitle}")
            {
                MainInstruction = $"Confirmar criacao do {commandTitle.ToLowerInvariant()}?",
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Ok
            };

            return dialog.Show() == TaskDialogResult.Ok;
        }

        private static string BuildSequenceSummary(
            BoxPipeSettings settings,
            List<SegmentPlan> plans,
            double slopePercent,
            BoxPipeSequenceMode mode)
        {
            string modeDescription = mode == BoxPipeSequenceMode.SecondaryBranch
                ? "Ramal secundario: ultima caixa mantida na altura atual, sem aplicar folga de chegada nesse ultimo trecho.\n"
                : "Ramal principal: caixas de destino ajustadas pela folga de chegada em todos os trechos.\n";

            string header =
                modeDescription +
                $"Trechos: {plans.Count}\n" +
                $"Tipo de tubo: {settings.PipeTypeName}\n" +
                $"Sistema: {settings.SystemTypeName}\n" +
                $"Tubos por trecho: {CreatePipeBetweenBoxesCommand.FormatPipeBundle(settings)}\n" +
                $"Sentido: {settings.FlowDirection}\n" +
                $"Inclinacao: {slopePercent:F2}%\n\n";

            string detail = string.Join(
                "\n\n",
                plans.Select(plan =>
                {
                    string warningText = plan.Warnings.Count == 0
                        ? "Nenhum."
                        : string.Join("\n", plan.Warnings.Select(warning => $"- {warning}"));

                    string destinationHeightText = plan.KeepsDestinationHeight
                        ? $"{FormatFeetAsCentimeters(plan.Destination.HeightFeet)} mantida"
                        : $"{FormatFeetAsCentimeters(plan.Destination.HeightFeet)} -> {FormatFeetAsCentimeters(plan.Calculation.NewDestinationHeightFeet)}";

                    string arrivalClearanceText = plan.KeepsDestinationHeight
                        ? "\nUltima caixa: altura preservada; folga de chegada ignorada."
                        : string.Empty;

                    string originHeightText = plan.Calculation.HasNewOriginHeight
                        ? $"\nAltura nascimento: {FormatFeetAsCentimeters(plan.Origin.HeightFeet)} -> {FormatFeetAsCentimeters(plan.Calculation.NewOriginHeightFeet)}"
                        : string.Empty;

                    return
                        $"Trecho {plan.Index}: Id {plan.Origin.Element.Id.IntegerValue} -> Id {plan.Destination.Element.Id.IntegerValue}\n" +
                        $"Distancia: {FormatFeetAsMeters(plan.HorizontalDistanceFeet)} | Desnivel: {FormatFeetAsCentimeters(plan.Calculation.PipeFallFeet)}\n" +
                        $"Cotas: {FormatElevation(plan.Calculation.StartPoint.Z)} -> {FormatElevation(plan.Calculation.EndPoint.Z)}\n" +
                        $"Altura destino: {destinationHeightText}{arrivalClearanceText}{originHeightText}\n" +
                        $"Avisos: {warningText}";
                }));

            return header + detail;
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

        private static bool HeightDiffers(double first, double second)
        {
            return Math.Abs(first - second) > UnitConversion.CmToFt(0.05);
        }

        private sealed class SegmentPlan
        {
            public int Index { get; set; }
            public CreatePipeBetweenBoxesCommand.BoxInfo Origin { get; set; } = null!;
            public CreatePipeBetweenBoxesCommand.BoxInfo Destination { get; set; } = null!;
            public double HorizontalDistanceFeet { get; set; }
            public CreatePipeBetweenBoxesCommand.CalculationResult Calculation { get; set; } = null!;
            public List<string> Warnings { get; set; } = new List<string>();
            public bool KeepsDestinationHeight { get; set; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateSecondaryBranchPipeThroughBoxesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return CreatePipeThroughBoxesCommand.ExecuteSequence(
                commandData,
                ref message,
                BoxPipeSequenceMode.SecondaryBranch,
                "Ramal secundario");
        }
    }
}
