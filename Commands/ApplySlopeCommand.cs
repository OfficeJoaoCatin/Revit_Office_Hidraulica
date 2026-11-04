using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using RevitSlopePlugin.UI;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitSlopePlugin.Commands
{
    /// <summary>
    /// Comando principal do plugin. Registrado como ExternalCommand para ser
    /// chamado pelo Revit via .addin ou ribbon button.
    /// 
    /// Fluxo de execução:
    /// 1. Solicita ao usuário que selecione o elemento base (ponto fixo / jusante)
    /// 2. Valida o elemento selecionado
    /// 3. Abre janela WPF para input da inclinação desejada
    /// 4. Percorre a rede conectada (BFS via ConnectorManager)
    /// 5. Calcula offsets verticais para cada elemento
    /// 6. Aplica movimentação geométrica dentro de uma Transaction
    /// 7. Regenera o documento e valida conectividade
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplySlopeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            Logger.Info("=== Iniciando ApplySlopeCommand ===");

            try
            {
                // ─────────────────────────────────────────────────────────
                // ETAPA 1: Solicitar elemento inicial e pontos travados
                // ─────────────────────────────────────────────────────────
                BaseElementSelection? baseSelection = GetBaseElement(uiDoc);
                if (baseSelection == null)
                {
                    Logger.Info("Usuário cancelou a seleção.");
                    return Result.Cancelled;
                }

                Element baseElement = baseSelection.Element;

                // ─────────────────────────────────────────────────────────
                // ETAPA 2: Validar elemento base
                // ─────────────────────────────────────────────────────────
                var (isValid, errorMsg) = ValidationService.ValidateBaseElement(baseElement);
                if (!isValid)
                {
                    Logger.Warn($"Validação falhou: {errorMsg}");
                    Logger.ShowError("Elemento Inválido", errorMsg);
                    return Result.Failed;
                }

                HashSet<ElementId> fixedElementIds = GetFixedElements(uiDoc, baseElement);
                fixedElementIds.Add(baseElement.Id);
                List<ElementLocationSnapshot> fixedSnapshots = fixedElementIds
                    .Select(id => doc.GetElement(id))
                    .Where(element => element != null)
                    .Select(element => ElementLocationSnapshot.Capture(element!))
                    .ToList();
                Logger.Info($"Elemento inicial selecionado: {baseElement.Id.IntegerValue} ({baseElement.Category?.Name})");
                Logger.Info($"Pontos travados selecionados: {fixedElementIds.Count}");

                // ─────────────────────────────────────────────────────────
                // ETAPA 3: Abrir janela WPF para input da inclinação
                // ─────────────────────────────────────────────────────────
                string elementInfo = $"{FormatElementInfo(baseElement)} | Pontos travados: {fixedElementIds.Count}";
                var inputWindow = new SlopeInputWindow(elementInfo);

                bool? dialogResult = inputWindow.ShowDialog();
                if (dialogResult != true || !inputWindow.IsConfirmed)
                {
                    Logger.Info("Usuário cancelou o input de inclinação.");
                    return Result.Cancelled;
                }

                double slopePercent = inputWindow.SlopePercent;
                Logger.Info($"Inclinação definida: {slopePercent}%");

                // ─────────────────────────────────────────────────────────
                // ETAPA 4: Navegar pela rede conectada (BFS)
                // ─────────────────────────────────────────────────────────
                var navigationService = new NetworkNavigationService(doc);
                Dictionary<ElementId, NetworkElement> network = 
                    navigationService.TraverseNetwork(baseElement, baseSelection.PickPoint);

                Logger.Info($"Rede mapeada: {network.Count} elementos encontrados.");
                Logger.Info($"Tubos verticais/90 graus ignorados na navegacao: {navigationService.SkippedVerticalPipeCount}.");

                // Validar rede
                var (networkValid, networkError) = ValidationService.ValidateNetwork(network);
                if (!networkValid)
                {
                    Logger.Warn($"Rede inválida: {networkError}");
                    Logger.ShowWarning("Rede Inválida", networkError);
                    return Result.Failed;
                }

                // ─────────────────────────────────────────────────────────
                // ETAPA 5: Calcular offsets verticais
                // ─────────────────────────────────────────────────────────
                var calculationService = new SlopeCalculationService();
                calculationService.CalculateOffsets(
                    network,
                    slopePercent,
                    baseElement.Id,
                    baseElevation: navigationService.GetBaseElevation(baseElement, baseSelection.PickPoint),
                    fixedElementIds: fixedElementIds);

                string summary = calculationService.GenerateSummary(network, slopePercent, baseElement.Id);
                Logger.Info(summary);

                // ─────────────────────────────────────────────────────────
                // ETAPA 6: Aplicar movimentação dentro de Transaction
                // ─────────────────────────────────────────────────────────
                double baseElevation = navigationService.GetBaseElevation(baseElement, baseSelection.PickPoint);
                int movedCount = 0;
                string connectivityWarning = string.Empty;

                using (TransactionGroup tGroup = new TransactionGroup(doc, "Aplicar Inclinação — Esgoto"))
                {
                    tGroup.Start();

                    using (Transaction tx = new Transaction(doc, "Mover Elementos — Inclinação"))
                    {
                        tx.Start();

                        var geometryService = new GeometryService(doc);
                        movedCount = geometryService.ApplySlope(
                            network, baseElement.Id, baseElevation, slopePercent, fixedElementIds, fixedSnapshots);

                        // Regenerar para atualizar geometria
                        doc.Regenerate();

                        int restoredFixedCount = 0;
                        foreach (ElementLocationSnapshot snapshot in fixedSnapshots)
                        {
                            if (snapshot.RestoreIfChanged(doc))
                            {
                                restoredFixedCount++;
                            }
                        }

                        if (restoredFixedCount > 0)
                        {
                            Logger.Warn($"{restoredFixedCount} ponto(s) fixo(s) foram restaurados para a cota original.");
                            doc.Regenerate();
                        }

                        tx.Commit();
                    }

                    // ─────────────────────────────────────────────────────
                    // ETAPA 7: Validação pós-movimentação
                    // ─────────────────────────────────────────────────────
                    var brokenConnections = ValidationService.CheckConnectivityPostMove(doc, network);

                    bool hasBrokenConnections = brokenConnections.Count > 0;
                    if (hasBrokenConnections)
                    {
                        string brokenSummary = string.Join(
                            "\n",
                            brokenConnections
                                .Take(10)
                                .Select(pair => $"- {pair.Item1.IntegerValue} <-> {pair.Item2.IntegerValue}"));

                        connectivityWarning =
                            $"A operacao foi mantida, mas {brokenConnections.Count} conexao(oes) " +
                            "ficaram fora da tolerancia apos a movimentacao.\n\n" +
                            $"{brokenSummary}\n\n" +
                            "Revise as conexoes indicadas no modelo.";

                        Logger.Warn(connectivityWarning);
                    }

                    tGroup.Assimilate();
                }

                // ─────────────────────────────────────────────────────────
                // ETAPA 8: Feedback ao usuário
                // ─────────────────────────────────────────────────────────
                string resultMessage =
                    $"Inclinação aplicada com sucesso!\n\n" +
                    $"• Inclinação: {slopePercent:F2}%\n" +
                    $"• Elementos na rede: {network.Count}\n" +
                    $"• Pontos travados: {fixedElementIds.Count}\n" +
                    $"• Elementos movidos: {movedCount}\n" +
                    $"• Tubos verticais / 90° ignorados: {navigationService.SkippedVerticalPipeCount}\n" +
                    $"• Elemento inicial: Id {baseElement.Id.IntegerValue}\n" +
                    $"• Prumadas / tubos verticais: preservados\n\n" +
                    $"{(string.IsNullOrWhiteSpace(connectivityWarning) ? "" : connectivityWarning + "\n\n")}" +
                    $"Use Ctrl+Z para desfazer se necessário.";

                Logger.ShowInfo("Inclinação Aplicada", resultMessage);
                Logger.Info($"Concluído: {movedCount} elementos movidos.");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("Operação cancelada pelo usuário.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro durante execução do ApplySlopeCommand", ex);
                message = ex.Message;

                Logger.ShowError(
                    "Erro no Plugin",
                    $"Ocorreu um erro ao aplicar a inclinação:\n\n{ex.Message}\n\n" +
                    "Verifique o log para mais detalhes.");

                return Result.Failed;
            }
        }

        /// <summary>
        /// Solicita ao usuário que selecione o elemento base no modelo.
        /// Tenta primeiro usar a seleção atual; se vazia, solicita pick.
        /// </summary>
        private BaseElementSelection? GetBaseElement(UIDocument uiDoc)
        {
            // Verificar se já existe um elemento selecionado
            var selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds.Count == 1)
            {
                Element? selected = uiDoc.Document.GetElement(selectedIds.First());
                if (selected != null && ValidationService.IsAcceptedMepElement(selected))
                {
                    return new BaseElementSelection(selected, null);
                }
            }

            // Solicitar seleção do usuário
            try
            {
                Reference? reference = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new MepElementSelectionFilter(),
                    "Selecione o elemento inicial da rede de esgoto");

                if (reference != null)
                {
                    Element? element = uiDoc.Document.GetElement(reference);
                    return element == null ? null : new BaseElementSelection(element, reference.GlobalPoint);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Solicita a seleção dos elementos que devem manter a altura original.
        /// ESC sem selecionar nenhum elemento continua o comando sem pontos extras.
        /// </summary>
        private HashSet<ElementId> GetFixedElements(UIDocument uiDoc, Element baseElement)
        {
            var fixedIds = new HashSet<ElementId>();

            TaskDialog dialog = new TaskDialog("Pontos Fixos")
            {
                MainInstruction = "Deseja travar outros pontos de altura?",
                MainContent = $"O elemento inicial Id {baseElement.Id.IntegerValue} ja ficara travado. " +
                    "Use pontos extras apenas quando outra peca da rede tambem precisar manter a cota atual.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dialog.Show() != TaskDialogResult.Yes)
            {
                return fixedIds;
            }

            try
            {
                IList<Reference> references = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new MepElementSelectionFilter(),
                    "Selecione os pontos fixos de altura. Pressione ESC para finalizar.");

                foreach (Reference reference in references)
                {
                    Element? element = uiDoc.Document.GetElement(reference);
                    if (element != null && ValidationService.IsAcceptedMepElement(element))
                    {
                        if (element.Id == baseElement.Id)
                        {
                            continue;
                        }

                        fixedIds.Add(element.Id);
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                Logger.Info("Seleção de pontos fixos finalizada/cancelada.");
            }

            return fixedIds;
        }

        /// <summary>
        /// Formata informações do elemento para exibição na UI.
        /// </summary>
        private string FormatElementInfo(Element element)
        {
            string category = element.Category?.Name ?? "Desconhecido";
            string typeName = element.GetType().Name;
            string familyName = "";

            if (element is FamilyInstance fi)
            {
                familyName = $" — {fi.Symbol?.FamilyName ?? ""}";
            }

            return $"{category}{familyName} (Id: {element.Id.IntegerValue})";
        }
    }

    /// <summary>
    /// Filtro de seleção que aceita apenas elementos MEP de tubulação
    /// (Pipes, Pipe Fittings, Pipe Accessories).
    /// </summary>
    public class MepElementSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return ValidationService.IsAcceptedMepElement(elem);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    internal sealed class BaseElementSelection
    {
        public BaseElementSelection(Element element, XYZ? pickPoint)
        {
            Element = element;
            PickPoint = pickPoint;
        }

        public Element Element { get; }

        public XYZ? PickPoint { get; }
    }
}
