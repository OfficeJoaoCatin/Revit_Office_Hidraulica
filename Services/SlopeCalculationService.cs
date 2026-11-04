using Autodesk.Revit.DB;
using RevitSlopePlugin.Models;
using System;
using System.Collections.Generic;

namespace RevitSlopePlugin.Services
{
    /// <summary>
    /// Serviço responsável por calcular o offset vertical de cada elemento da rede
    /// com base na inclinação desejada e na distância acumulada até o ponto base.
    /// 
    /// Lógica: o elemento inicial é a cota travada; a rede conectada sobe
    /// para tras a partir dele conforme a inclinação informada.
    /// 
    /// offset = distância_horizontal × (inclinação% / 100)
    /// </summary>
    public class SlopeCalculationService
    {
        /// <summary>
        /// Calcula o offset vertical para todos os elementos da rede.
        /// O elemento base (distância = 0) não se move.
        /// Elementos conectados sobem proporcionalmente à distância.
        /// </summary>
        /// <param name="network">Dicionário com todos os elementos da rede.</param>
        /// <param name="slopePercent">Inclinação desejada em porcentagem (ex: 2.0 = 2%).</param>
        /// <param name="baseElementId">ElementId do ponto fixo (não será movido).</param>
        public void CalculateOffsets(
            Dictionary<ElementId, NetworkElement> network,
            double slopePercent,
            ElementId baseElementId,
            double baseElevation,
            ISet<ElementId>? fixedElementIds = null)
        {
            // Converter porcentagem para fração decimal
            // Exemplo: 2% → 0.02 (para cada 1 pé horizontal, sobe 0.02 pé)
            double slopeFraction = slopePercent / 100.0;

            foreach (var kvp in network)
            {
                var netElement = kvp.Value;
                netElement.IsFixed = kvp.Key == baseElementId ||
                    (fixedElementIds != null && fixedElementIds.Contains(kvp.Key));
                netElement.FixedReferenceZ = null;
                netElement.SlopeReferenceDistance = 0.0;
                netElement.SlopeReferenceZ = baseElevation;
            }

            ExpandImmediateFixedConnectorAnchors(network, fixedElementIds ?? new HashSet<ElementId> { baseElementId });

            foreach (var kvp in network)
            {
                var netElement = kvp.Value;

                // Elemento base: offset = 0 (ponto fixo)
                if (netElement.IsFixed)
                {
                    netElement.CalculatedOffset = 0.0;
                    netElement.TargetZ = GetFixedReferenceZ(netElement);
                    netElement.SlopeReferenceDistance = netElement.DistanceFromBase;
                    netElement.SlopeReferenceZ = GetFixedReferenceZ(netElement);
                    continue;
                }

                // Trechos verticais não recebem inclinação
                if (netElement.IsVertical)
                {
                    netElement.CalculatedOffset = 0.0;
                    netElement.TargetZ = netElement.OriginalZ;
                    continue;
                }

                NetworkElement? anchor = FindNearestFixedAncestor(network, netElement);
                double anchorDistance = anchor?.DistanceFromBase ?? 0.0;
                double anchorZ = anchor != null ? GetFixedReferenceZ(anchor) : baseElevation;
                double relativeDistance = Math.Max(0.0, netElement.DistanceFromBase - anchorDistance);

                netElement.SlopeReferenceDistance = anchorDistance;
                netElement.SlopeReferenceZ = anchorZ;

                // Calcular offset: distância horizontal × fração da inclinação.
                // Positivo = a rede sobe para tras a partir do ponto fixo local.
                netElement.CalculatedOffset = relativeDistance * slopeFraction;
                netElement.TargetZ = anchorZ + netElement.CalculatedOffset;
            }
        }

        private static void ExpandImmediateFixedConnectorAnchors(
            Dictionary<ElementId, NetworkElement> network,
            ISet<ElementId> explicitFixedElementIds)
        {
            foreach (ElementId fixedElementId in explicitFixedElementIds)
            {
                if (!network.TryGetValue(fixedElementId, out NetworkElement? fixedElement))
                {
                    continue;
                }

                foreach (Connector connector in fixedElement.Connectors)
                {
                    if (!connector.IsConnected)
                    {
                        continue;
                    }

                    foreach (Connector otherConnector in connector.AllRefs)
                    {
                        ElementId otherId = otherConnector.Owner.Id;
                        if (otherId == fixedElement.ElementId)
                        {
                            continue;
                        }

                        if (!network.TryGetValue(otherId, out NetworkElement? neighbor))
                        {
                            continue;
                        }

                        if (neighbor.IsPipe || neighbor.IsFixed)
                        {
                            continue;
                        }

                        neighbor.IsFixed = true;
                        neighbor.FixedReferenceZ = otherConnector.Origin.Z;
                    }
                }
            }
        }

        private static NetworkElement? FindNearestFixedAncestor(
            Dictionary<ElementId, NetworkElement> network,
            NetworkElement netElement)
        {
            NetworkElement current = netElement;
            var visited = new HashSet<ElementId>();

            while (current.ParentElementId != null &&
                visited.Add(current.ElementId) &&
                network.TryGetValue(current.ParentElementId, out NetworkElement? parent))
            {
                if (parent.IsFixed)
                {
                    return parent;
                }

                current = parent;
            }

            return null;
        }

        private static double GetFixedReferenceZ(NetworkElement netElement)
        {
            return netElement.FixedReferenceZ ?? netElement.OriginalZ;
        }

        /// <summary>
        /// Gera um resumo textual dos cálculos para log/debug.
        /// </summary>
        public string GenerateSummary(
            Dictionary<ElementId, NetworkElement> network,
            double slopePercent,
            ElementId baseElementId)
        {
            int totalElements = network.Count;
            int verticalSkipped = 0;
            int fixedSkipped = 0;
            double maxDistance = 0;
            double maxOffset = 0;

            foreach (var kvp in network)
            {
                var ne = kvp.Value;
                if (ne.IsVertical) verticalSkipped++;
                if (ne.IsFixed) fixedSkipped++;
                if (ne.DistanceFromBase > maxDistance) maxDistance = ne.DistanceFromBase;
                if (Math.Abs(ne.CalculatedOffset) > maxOffset) maxOffset = Math.Abs(ne.CalculatedOffset);
            }

            // Converter pés para metros para exibição
            double maxDistanceM = maxDistance * 0.3048;
            double maxOffsetM = maxOffset * 0.3048;

            return $"Resumo do Cálculo de Inclinação:\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                   $"  Inclinação aplicada: {slopePercent:F2}%\n" +
                   $"  Total de elementos na rede: {totalElements}\n" +
                   $"  Pontos travados: {fixedSkipped}\n" +
                   $"  Trechos verticais ignorados: {verticalSkipped}\n" +
                   $"  Maior distância horizontal: {maxDistance:F2} ft ({maxDistanceM:F2} m)\n" +
                   $"  Maior offset vertical: {maxOffset:F4} ft ({maxOffsetM:F4} m)\n" +
                   $"  Elemento inicial: Id {baseElementId.IntegerValue}";
        }
    }
}
