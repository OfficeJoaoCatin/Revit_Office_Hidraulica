using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    /// <summary>
    /// Serviço de validação para elementos MEP e rede hidráulica.
    /// Verifica pré-condições antes da execução do plugin.
    /// </summary>
    public static class ValidationService
    {
        // Categorias MEP aceitas pelo plugin (pipes, fittings, accessories)
        private static readonly HashSet<BuiltInCategory> AcceptedCategories = new()
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory
        };

        /// <summary>
        /// Verifica se o elemento selecionado é válido como ponto base.
        /// Deve ser um Pipe, Pipe Fitting ou Pipe Accessory com ConnectorManager.
        /// </summary>
        public static (bool IsValid, string ErrorMessage) ValidateBaseElement(Element? element)
        {
            if (element == null)
                return (false, "Nenhum elemento selecionado. Selecione um pipe, fitting ou acessório como ponto base.");

            // Verificar categoria
            if (element.Category == null)
                return (false, $"O elemento selecionado (Id: {element.Id.IntegerValue}) não possui categoria definida.");

            var category = (BuiltInCategory)element.Category.Id.IntegerValue;
            if (!AcceptedCategories.Contains(category))
            {
                return (false, $"O elemento selecionado é da categoria '{element.Category.Name}'. " +
                    "Selecione um Pipe, Pipe Fitting ou Pipe Accessory.");
            }

            // Verificar ConnectorManager
            var connectorManager = GetConnectorManager(element);
            if (connectorManager == null)
                return (false, $"O elemento selecionado (Id: {element.Id.IntegerValue}) não possui ConnectorManager.");

            // Verificar se tem pelo menos um conector
            int connectorCount = 0;
            foreach (Connector c in connectorManager.Connectors)
            {
                connectorCount++;
            }

            if (connectorCount == 0)
                return (false, $"O elemento selecionado (Id: {element.Id.IntegerValue}) não possui conectores.");

            if (PipeOrientationService.IsPipeVerticalOrSteep(element))
            {
                return (false, "O elemento inicial selecionado e um tubo vertical ou muito inclinado. " +
                    "Selecione um trecho horizontal da infraestrutura para aplicar inclinacao.");
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Verifica se um elemento MEP pertence às categorias aceitas pelo plugin.
        /// </summary>
        public static bool IsAcceptedMepElement(Element element)
        {
            if (element?.Category == null)
                return false;

            var category = (BuiltInCategory)element.Category.Id.IntegerValue;
            return AcceptedCategories.Contains(category);
        }

        /// <summary>
        /// Evita atravessar para outra rede MEP quando um fitting liga sistemas diferentes.
        /// Elementos sem parametro de sistema continuam permitidos para nao bloquear familias.
        /// </summary>
        public static bool IsSameMepSystem(Element baseElement, Element candidate)
        {
            string baseSystemName = GetStringParameter(baseElement, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            string candidateSystemName = GetStringParameter(candidate, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);

            if (!string.IsNullOrWhiteSpace(baseSystemName) &&
                !string.IsNullOrWhiteSpace(candidateSystemName) &&
                !baseSystemName.Equals(candidateSystemName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string baseClassification = GetStringParameter(baseElement, BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
            string candidateClassification = GetStringParameter(candidate, BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);

            if (!string.IsNullOrWhiteSpace(baseClassification) &&
                !string.IsNullOrWhiteSpace(candidateClassification) &&
                !baseClassification.Equals(candidateClassification, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verifica se a inclinação informada é válida.
        /// Aceita valores entre 0.1% e 100% (excluindo 0).
        /// </summary>
        public static (bool IsValid, string ErrorMessage) ValidateSlope(double slopePercent)
        {
            if (slopePercent <= 0)
                return (false, "A inclinação deve ser um valor positivo maior que zero.");

            if (slopePercent > 100)
                return (false, "A inclinação não pode ser superior a 100%. Valores típicos para esgoto: 1% a 5%.");

            return (true, string.Empty);
        }

        /// <summary>
        /// Verifica se a rede navegada possui pelo menos 2 elementos (base + 1 conectado).
        /// </summary>
        public static (bool IsValid, string ErrorMessage) ValidateNetwork(
            Dictionary<ElementId, Models.NetworkElement> network)
        {
            if (network == null || network.Count == 0)
                return (false, "Nenhum elemento foi encontrado na rede conectada.");

            if (network.Count == 1)
                return (false, "A rede possui apenas o elemento base. Não há elementos conectados para aplicar inclinação.");

            return (true, string.Empty);
        }

        /// <summary>
        /// Obtém o ConnectorManager de um elemento MEP (Pipe, FamilyInstance, etc.).
        /// </summary>
        public static ConnectorManager? GetConnectorManager(Element element)
        {
            if (element is MEPCurve mepCurve)
                return mepCurve.ConnectorManager;

            if (element is FamilyInstance familyInstance)
                return familyInstance.MEPModel?.ConnectorManager;

            return null;
        }

        private static string GetStringParameter(Element element, BuiltInParameter builtInParameter)
        {
            try
            {
                Parameter? parameter = element.get_Parameter(builtInParameter);
                if (parameter == null || !parameter.HasValue)
                {
                    return string.Empty;
                }

                return parameter.AsString() ??
                    parameter.AsValueString() ??
                    string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Verifica integridade pós-movimentação: se os conectores ainda estão próximos.
        /// Retorna lista de pares de elementos com conectividade possivelmente quebrada.
        /// </summary>
        public static List<(ElementId, ElementId)> CheckConnectivityPostMove(
            Document doc,
            Dictionary<ElementId, Models.NetworkElement> network,
            double toleranceFeet = 0.01)
        {
            var brokenConnections = new List<(ElementId, ElementId)>();

            foreach (var kvp in network)
            {
                var element = doc.GetElement(kvp.Key);
                if (element == null) continue;

                var cm = GetConnectorManager(element);
                if (cm == null) continue;

                foreach (Connector connector in cm.Connectors)
                {
                    if (!connector.IsConnected) continue;

                    foreach (Connector other in connector.AllRefs)
                    {
                        if (other.Owner.Id == element.Id) continue;
                        if (!network.ContainsKey(other.Owner.Id)) continue;

                        double distance = connector.Origin.DistanceTo(other.Origin);
                        if (distance > toleranceFeet)
                        {
                            brokenConnections.Add((element.Id, other.Owner.Id));
                        }
                    }
                }
            }

            return brokenConnections;
        }
    }
}
