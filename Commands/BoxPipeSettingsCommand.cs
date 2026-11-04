using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitSlopePlugin.Services;
using RevitSlopePlugin.UI;
using RevitSlopePlugin.Utils;
using System;

namespace RevitSlopePlugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class BoxPipeSettingsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;
                var settings = BoxPipeSettingsService.Load();
                var window = new BoxPipeSettingsWindow(doc, settings);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao abrir configuracao de tubo entre caixas", ex);
                message = ex.Message;
                Logger.ShowError("Configuracao", $"Nao foi possivel abrir a configuracao:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
