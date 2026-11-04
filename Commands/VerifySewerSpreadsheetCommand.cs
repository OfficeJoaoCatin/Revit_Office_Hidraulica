using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitSlopePlugin.Models;
using RevitSlopePlugin.Services;
using RevitSlopePlugin.UI;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitSlopePlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class VerifySewerSpreadsheetCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                BoxPipeSettings settings = BoxPipeSettingsService.Load();
                SewerSpreadsheetMapping[] mappings = settings.SpreadsheetMappings
                    .Where(mapping => !string.IsNullOrWhiteSpace(mapping.CodePrefix))
                    .ToArray();

                if (mappings.Length == 0)
                {
                    Logger.ShowWarning(
                        "Verificar Planilha",
                        "Configure pelo menos um mapeamento de planilha em Configuracoes.");
                    return Result.Cancelled;
                }

                string? pickedWorkbookPath = PickWorkbookPath();
                if (string.IsNullOrWhiteSpace(pickedWorkbookPath))
                {
                    return Result.Cancelled;
                }

                string workbookPath = pickedWorkbookPath;

                var workbookService = new XlsxWorkbookService();
                List<string> sheetNames = workbookService.GetSheetNames(workbookPath);
                if (sheetNames.Count == 0)
                {
                    Logger.ShowError("Verificar Planilha", "A planilha nao possui abas legiveis.");
                    return Result.Failed;
                }

                var optionsWindow = new SewerSpreadsheetOptionsWindow(sheetNames, mappings);
                bool? dialogResult = optionsWindow.ShowDialog();
                if (dialogResult != true || !optionsWindow.IsConfirmed || optionsWindow.SelectedMapping == null)
                {
                    return Result.Cancelled;
                }

                XlsxWorksheet worksheet = workbookService.ReadWorksheet(workbookPath, optionsWindow.SelectedSheetName);
                List<SewerSpreadsheetSegment> segments = workbookService.FindSegments(
                    worksheet,
                    optionsWindow.SelectedMapping.CodePrefix,
                    settings.SpreadsheetExtensionHeader);

                if (segments.Count == 0)
                {
                    Logger.ShowWarning(
                        "Verificar Planilha",
                        $"Nenhum trecho com prefixo '{optionsWindow.SelectedMapping.CodePrefix}' foi encontrado na aba selecionada.");
                    return Result.Cancelled;
                }

                var integrationService = new SewerSpreadsheetIntegrationService(
                    doc,
                    settings,
                    optionsWindow.SelectedMapping);

                SewerSpreadsheetProcessResult result = integrationService.ProcessSegments(segments);

                var resultsWindow = new SewerSpreadsheetResultsWindow(
                    uiApp,
                    result.Items,
                    segments.Count,
                    result.ExtensionsToWrite.Count,
                    workbookPath,
                    optionsWindow.SelectedSheetName,
                    result.ExtensionsToWrite);
                resultsWindow.Show();

                Logger.Info(
                    $"Planilha verificada: {Path.GetFileName(workbookPath)} | aba {optionsWindow.SelectedSheetName} | trechos {segments.Count} | extensoes pendentes {result.ExtensionsToWrite.Count}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao verificar planilha de esgoto", ex);
                message = ex.Message;
                Logger.ShowError("Verificar Planilha", $"Nao foi possivel verificar a planilha:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static string? PickWorkbookPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecionar planilha de calculo",
                Filter = "Planilhas Excel (*.xlsx)|*.xlsx",
                Multiselect = false,
                CheckFileExists = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
