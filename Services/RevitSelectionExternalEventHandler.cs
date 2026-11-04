using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitSlopePlugin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    internal sealed class RevitSelectionExternalEventHandler : IExternalEventHandler
    {
        private readonly object _syncRoot = new object();
        private List<ElementId> _pendingElementIds = new List<ElementId>();

        public void RequestSelection(IEnumerable<ElementId> elementIds)
        {
            lock (_syncRoot)
            {
                _pendingElementIds = elementIds
                    .Where(id => id != ElementId.InvalidElementId)
                    .Distinct()
                    .ToList();
            }
        }

        public void Execute(UIApplication app)
        {
            List<ElementId> elementIds;
            lock (_syncRoot)
            {
                elementIds = _pendingElementIds.ToList();
            }

            if (elementIds.Count == 0)
            {
                return;
            }

            try
            {
                UIDocument? uiDoc = app.ActiveUIDocument;
                if (uiDoc == null)
                {
                    return;
                }

                List<ElementId> validIds = elementIds
                    .Where(id => uiDoc.Document.GetElement(id) != null)
                    .ToList();

                if (validIds.Count == 0)
                {
                    return;
                }

                uiDoc.Selection.SetElementIds(validIds);
                uiDoc.ShowElements(validIds);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel navegar para o item da planilha: {ex.Message}");
            }
        }

        public string GetName()
        {
            return "Selecionar elementos da verificacao de planilha";
        }
    }
}
