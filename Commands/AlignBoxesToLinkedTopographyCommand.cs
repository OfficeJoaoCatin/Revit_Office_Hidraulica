using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitSlopePlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignBoxesToLinkedTopographyCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            Logger.Info("=== Iniciando AlignBoxesToLinkedTopographyCommand ===");

            try
            {
                List<FamilyInstance> boxes = PickBoxes(uiDoc);
                if (boxes.Count == 0)
                {
                    Logger.ShowWarning("Alinhar caixas", "Nenhuma caixa valida foi selecionada.");
                    return Result.Cancelled;
                }

                var topographyService = new LinkedTopographyService();
                RevitLinkInstance linkInstance = topographyService.PickRevitLink(uiDoc);
                Document? linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null)
                {
                    Logger.ShowError(
                        "Revit Link nao carregado",
                        "O Revit Link selecionado nao esta carregado ou nao pode ser acessado.");
                    return Result.Failed;
                }

                var boxElevationService = new InspectionBoxElevationService(doc);
                List<XYZ> linkPoints = ReadValidBoxLinkPoints(
                    boxes,
                    linkInstance,
                    topographyService,
                    boxElevationService);

                List<LinkedTopographyCandidate> referenceSurfaces =
                    topographyService.FindReferenceSurfaces(linkDoc, linkPoints);

                if (referenceSurfaces.Count == 0)
                {
                    Logger.ShowError(
                        "Superficie nao encontrada",
                        "Nao foi encontrada uma face superior valida nos elementos do Revit Link selecionado.");
                    return Result.Failed;
                }

                List<BoxTopographyAlignmentResult> results = AlignBoxes(
                    doc,
                    linkInstance,
                    referenceSurfaces,
                    topographyService,
                    boxElevationService,
                    boxes);

                string summary = BuildSummary(results, boxes.Count, linkInstance, referenceSurfaces);
                ShowSummary(results, summary);
                return results.Any(result => result.Success) ? Result.Succeeded : Result.Failed;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("Alinhamento de caixas cancelado pelo usuario.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao alinhar caixas a topografia vinculada", ex);
                message = ex.Message;
                Logger.ShowError(
                    "Alinhar caixas a topografia",
                    $"Ocorreu um erro ao alinhar as caixas:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static List<FamilyInstance> PickBoxes(UIDocument uiDoc)
        {
            IList<Reference> references = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new InspectionBoxSelectionFilter(),
                "Selecione as caixas de inspecao e clique em Concluir");

            return references
                .Select(reference => uiDoc.Document.GetElement(reference) as FamilyInstance)
                .Where(box => box != null)
                .Cast<FamilyInstance>()
                .GroupBy(box => box.Id)
                .Select(group => group.First())
                .ToList();
        }

        private static List<XYZ> ReadValidBoxLinkPoints(
            List<FamilyInstance> boxes,
            RevitLinkInstance linkInstance,
            LinkedTopographyService topographyService,
            InspectionBoxElevationService boxElevationService)
        {
            var points = new List<XYZ>();

            foreach (FamilyInstance box in boxes)
            {
                if (!boxElevationService.TryReadBoxPositionAndTop(
                    box,
                    out XYZ hostInsertionPoint,
                    out double currentTopFeet,
                    out _))
                {
                    continue;
                }

                points.Add(topographyService.ConvertHostPointToLink(linkInstance, hostInsertionPoint));
            }

            return points;
        }

        private static List<BoxTopographyAlignmentResult> AlignBoxes(
            Document doc,
            RevitLinkInstance linkInstance,
            List<LinkedTopographyCandidate> referenceSurfaces,
            LinkedTopographyService topographyService,
            InspectionBoxElevationService boxElevationService,
            List<FamilyInstance> boxes)
        {
            var results = new List<BoxTopographyAlignmentResult>();

            using (var group = new TransactionGroup(doc, "Alinhar caixas a superficie do link"))
            {
                group.Start();

                foreach (FamilyInstance box in boxes)
                {
                    BoxTopographyAlignmentResult result = PrepareAlignment(
                        box,
                        linkInstance,
                        referenceSurfaces,
                        topographyService,
                        boxElevationService);

                    if (!string.IsNullOrWhiteSpace(result.Message) && !result.Success)
                    {
                        results.Add(result);
                        continue;
                    }

                    using (var transaction = new Transaction(
                        doc,
                        $"Alinhar caixa {box.Id.IntegerValue} a superficie do link"))
                    {
                        try
                        {
                            transaction.Start();
                            SetTransactionFailureHandling(transaction);

                            result = boxElevationService.AlignTopToElevation(
                                box,
                                result.CurrentTopFeet,
                                result.TargetTopFeet);

                            if (!result.Success)
                            {
                                transaction.RollBack();
                                results.Add(result);
                                continue;
                            }

                            TransactionStatus status = transaction.Commit();
                            if (status != TransactionStatus.Committed)
                            {
                                result.Success = false;
                                result.Message = $"transacao nao confirmada: {status}";
                            }
                        }
                        catch (Exception ex)
                        {
                            RollBackQuietly(transaction);

                            result.Success = false;
                            result.Message = ex.Message;
                        }
                    }

                    results.Add(result);
                }

                if (results.Any(result => result.Success))
                {
                    group.Assimilate();
                }
                else
                {
                    group.RollBack();
                }
            }

            return results;
        }

        private static void RollBackQuietly(Transaction transaction)
        {
            try
            {
                if (transaction.HasStarted())
                {
                    transaction.RollBack();
                }
            }
            catch
            {
                // A transacao pode ja ter sido finalizada pelo Revit durante o
                // processamento de falhas; nesse caso apenas preserva o erro original.
            }
        }

        private static void SetTransactionFailureHandling(Transaction transaction)
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new DeleteWarningsPreprocessor());
            options.SetClearAfterRollback(true);
            transaction.SetFailureHandlingOptions(options);
        }

        private static BoxTopographyAlignmentResult PrepareAlignment(
            FamilyInstance box,
            RevitLinkInstance linkInstance,
            List<LinkedTopographyCandidate> referenceSurfaces,
            LinkedTopographyService topographyService,
            InspectionBoxElevationService boxElevationService)
        {
            var result = new BoxTopographyAlignmentResult
            {
                BoxId = box.Id
            };

            if (!boxElevationService.TryReadBoxPositionAndTop(
                box,
                out XYZ hostInsertionPoint,
                out double currentTopFeet,
                out string boxReason))
            {
                result.Message = boxReason;
                return result;
            }

            // O X/Y da caixa no host e convertido para o sistema local do link antes
            // da consulta, pois a topografia pertence ao documento vinculado.
            XYZ linkPoint = topographyService.ConvertHostPointToLink(linkInstance, hostInsertionPoint);
            double currentTopLinkElevation = topographyService
                .ConvertHostPointToLink(linkInstance, new XYZ(
                    hostInsertionPoint.X,
                    hostInsertionPoint.Y,
                    currentTopFeet))
                .Z;

            if (!TryFindBestReferenceElevation(
                referenceSurfaces,
                topographyService,
                linkPoint,
                currentTopLinkElevation,
                out double linkElevationFeet,
                out string referenceSurfaceName,
                out string topographyReason))
            {
                result.Message = topographyReason;
                return result;
            }

            result.CurrentTopFeet = currentTopFeet;
            result.ReferenceSurfaceName = referenceSurfaceName;
            result.TargetTopFeet = topographyService.ConvertLinkElevationToHost(
                linkInstance,
                linkPoint,
                linkElevationFeet);
            result.DeltaFeet = result.TargetTopFeet - result.CurrentTopFeet;
            return result;
        }

        private static bool TryFindBestReferenceElevation(
            List<LinkedTopographyCandidate> referenceSurfaces,
            LinkedTopographyService topographyService,
            XYZ linkPoint,
            double currentTopLinkElevation,
            out double linkElevationFeet,
            out string referenceSurfaceName,
            out string reason)
        {
            linkElevationFeet = 0.0;
            referenceSurfaceName = string.Empty;
            double bestDistance = double.MaxValue;
            string lastReason = "ponto fora das faces superiores do link";

            foreach (LinkedTopographyCandidate referenceSurface in referenceSurfaces)
            {
                if (!topographyService.TryGetElevationAtPoint(
                    referenceSurface,
                    linkPoint,
                    currentTopLinkElevation,
                    out double candidateElevation,
                    out string candidateReason))
                {
                    lastReason = candidateReason;
                    continue;
                }

                double distance = Math.Abs(currentTopLinkElevation - candidateElevation);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                linkElevationFeet = candidateElevation;
                referenceSurfaceName = referenceSurface.DisplayName;
            }

            if (bestDistance < double.MaxValue)
            {
                reason = string.Empty;
                return true;
            }

            reason = lastReason;
            return false;
        }

        private static string BuildSummary(
            List<BoxTopographyAlignmentResult> results,
            int totalCount,
            RevitLinkInstance linkInstance,
            List<LinkedTopographyCandidate> referenceSurfaces)
        {
            int successCount = results.Count(result => result.Success);
            int skippedCount = results.Count - successCount;

            var lines = new List<string>
            {
                $"Revit Link: {linkInstance.Name}",
                $"Superficies analisadas: {referenceSurfaces.Count}",
                $"Total selecionado: {totalCount}",
                $"Alinhadas: {successCount}",
                $"Nao processadas: {skippedCount}"
            };

            foreach (BoxTopographyAlignmentResult result in results.Where(result => result.Success).Take(8))
            {
                string surface = string.IsNullOrWhiteSpace(result.ReferenceSurfaceName)
                    ? string.Empty
                    : $" - {TrimSurfaceName(result.ReferenceSurfaceName)}";

                lines.Add(
                    $"Caixa {result.BoxId.IntegerValue}: delta {FormatFeetAsCentimeters(result.DeltaFeet)} ({result.Message}){surface}");
            }

            foreach (BoxTopographyAlignmentResult result in results.Where(result => !result.Success).Take(12))
            {
                lines.Add($"Caixa {result.BoxId.IntegerValue}: {result.Message}");
            }

            int omitted = results.Count - Math.Min(successCount, 8) - Math.Min(skippedCount, 12);
            if (omitted > 0)
            {
                lines.Add($"Mais {omitted} resultados omitidos no resumo.");
            }

            return string.Join("\n", lines);
        }

        private static void ShowSummary(
            List<BoxTopographyAlignmentResult> results,
            string summary)
        {
            try
            {
                if (results.Any(result => !result.Success))
                {
                    Logger.ShowWarning("Alinhamento concluido com avisos", summary);
                }
                else
                {
                    Logger.ShowInfo("Alinhamento concluido", summary);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Nao foi possivel exibir o resumo do alinhamento", ex);
            }

            Logger.Info(summary);
        }

        private static string FormatFeetAsCentimeters(double feet)
        {
            return $"{UnitConversion.FtToCm(feet).ToString("F1", CultureInfo.InvariantCulture)} cm";
        }

        private static string TrimSurfaceName(string surfaceName)
        {
            const int maxLength = 80;
            return surfaceName.Length <= maxLength
                ? surfaceName
                : surfaceName.Substring(0, maxLength - 3) + "...";
        }

        private sealed class InspectionBoxSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is FamilyInstance familyInstance &&
                    familyInstance.Location is LocationPoint;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }

        private sealed class DeleteWarningsPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failure);
                    }
                }

                return FailureProcessingResult.Continue;
            }
        }
    }
}
