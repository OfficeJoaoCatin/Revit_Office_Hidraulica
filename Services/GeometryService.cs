using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitSlopePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    /// <summary>
    /// Serviço responsável por aplicar as movimentações geométricas nos elementos da rede.
    /// Move elementos no eixo Z e aplica slope em pipes, preservando conectividade MEP.
    /// </summary>
    public class GeometryService
    {
        private readonly Document _doc;

        public GeometryService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Aplica a inclinação em toda a rede, movendo cada elemento no eixo Z
        /// conforme o offset calculado.
        /// 
        /// Estratégia:
        /// 1. Move fittings e accessories primeiro (LocationPoint → MoveElement no Z)
        /// 2. Move pipes atualizando ambas as extremidades da LocationCurve
        /// 3. Aplica o parâmetro Slope nos pipes quando possível
        /// 
        /// Tudo ocorre dentro de uma Transaction já aberta pelo chamador.
        /// </summary>
        /// <param name="network">Rede com offsets já calculados.</param>
        /// <param name="baseElementId">Elemento fixo que não será movido.</param>
        /// <param name="baseElevation">Elevação Z original do ponto base.</param>
        /// <param name="slopePercent">Inclinação em % para aplicar ao parâmetro Slope dos pipes.</param>
        /// <returns>Número de elementos movidos com sucesso.</returns>
        public int ApplySlope(
            Dictionary<ElementId, NetworkElement> network,
            ElementId baseElementId,
            double baseElevation,
            double slopePercent,
            ISet<ElementId>? fixedElementIds = null,
            IReadOnlyList<ElementLocationSnapshot>? fixedSnapshots = null)
        {
            int movedCount = 0;
            var effectiveFixedIds = fixedElementIds ?? new HashSet<ElementId> { baseElementId };
            FixedConnectionGuard fixedConnectionGuard = FixedConnectionGuard.Capture(network, effectiveFixedIds);
            fixedConnectionGuard.Disconnect();

            var sortedElements = network.Values
                .Where(ne => !ne.IsFixed && !ne.IsVertical)
                .Where(ne => !ne.IsPipe || PipeOrientationService.IsPipeHorizontalInfrastructure(ne.RevitElement))
                .OrderBy(ne => ne.IsPipe ? 0 : 1)
                .ThenBy(ne => ne.BfsDepth)
                .ToList();

            foreach (var netElement in sortedElements)
            {
                try
                {
                    bool success = MoveElementVertically(netElement, baseElevation, slopePercent, network);
                    if (success) movedCount++;
                }
                catch (Exception ex)
                {
                    // Log do erro mas continua processando outros elementos
                    System.Diagnostics.Debug.WriteLine(
                        $"[GeometryService] Erro ao mover elemento {netElement.ElementId.IntegerValue}: {ex.Message}");
                }
            }

            // O LocationCurve ja define a declividade. Forcar RBS_PIPE_SLOPE aqui
            // pode inverter o sentido conforme a direcao interna do pipe no Revit.
            fixedSnapshots?.ToList().ForEach(snapshot => snapshot.RestoreIfChanged(_doc));
            fixedConnectionGuard.Reconnect();

            return movedCount;
        }

        /// <summary>
        /// Move um elemento individualmente no eixo Z.
        /// A nova posição Z segue o offset calculado a partir da ancora local.
        /// </summary>
        private bool MoveElementVertically(
            NetworkElement netElement,
            double baseElevation,
            double slopePercent,
            Dictionary<ElementId, NetworkElement> network)
        {
            Element element = netElement.RevitElement;
            double targetZ = netElement.TargetZ;

            if (element.Location is LocationCurve locationCurve)
            {
                if (!PipeOrientationService.IsPipeHorizontalInfrastructure(element))
                {
                    return false;
                }

                return MoveCurveElement(element, netElement, baseElevation, slopePercent, network);
            }

            // Para Fittings e Accessories (LocationPoint): mover no eixo Z
            if (element.Location is LocationPoint locationPoint)
            {
                double currentZ = locationPoint.Point.Z;
                double deltaZ = targetZ - currentZ;

                if (Math.Abs(deltaZ) < 0.0001) return false; // Sem movimento necessário

                XYZ translation = new XYZ(0, 0, deltaZ);
                ElementTransformUtils.MoveElement(_doc, element.Id, translation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Move um elemento com LocationCurve (Pipe) no eixo Z.
        /// Usa MoveElement para transladar todo o pipe verticalmente.
        /// </summary>
        private bool MoveCurveElement(
            Element element,
            NetworkElement netElement,
            double baseElevation,
            double slopePercent,
            Dictionary<ElementId, NetworkElement> network)
        {
            if (element.Location is not LocationCurve locationCurve) return false;

            Curve curve = locationCurve.Curve;
            XYZ originalStart = curve.GetEndPoint(0);
            XYZ originalEnd = curve.GetEndPoint(1);
            double horizontalLength = CalculateHorizontalDistance(originalStart, originalEnd);

            if (horizontalLength < 0.001) return false;

            double slopeFraction = slopePercent / 100.0;
            double fallbackNearDistance = Math.Max(0.0, netElement.DistanceFromBase - horizontalLength);

            if (!netElement.TryGetConnectorDistance(originalStart, out double startDistance))
            {
                startDistance = fallbackNearDistance;
            }

            if (!netElement.TryGetConnectorDistance(originalEnd, out double endDistance))
            {
                endDistance = netElement.DistanceFromBase;
            }

            double startZ = netElement.SlopeReferenceZ +
                Math.Max(0.0, startDistance - netElement.SlopeReferenceDistance) * slopeFraction;
            double endZ = netElement.SlopeReferenceZ +
                Math.Max(0.0, endDistance - netElement.SlopeReferenceDistance) * slopeFraction;

            bool startTouchesFixed = TryGetFixedConnectedZ(netElement, originalStart, network, out double fixedStartZ);
            bool endTouchesFixed = TryGetFixedConnectedZ(netElement, originalEnd, network, out double fixedEndZ);

            if (startTouchesFixed)
            {
                startZ = fixedStartZ;
            }

            if (endTouchesFixed)
            {
                endZ = fixedEndZ;
            }

            if (startTouchesFixed && !endTouchesFixed)
            {
                endZ = startZ + horizontalLength * slopeFraction;
            }
            else if (endTouchesFixed && !startTouchesFixed)
            {
                startZ = endZ + horizontalLength * slopeFraction;
            }

            XYZ newStart = new XYZ(originalStart.X, originalStart.Y, startZ);
            XYZ newEnd = new XYZ(originalEnd.X, originalEnd.Y, endZ);

            if (originalStart.DistanceTo(newStart) < 0.0001 &&
                originalEnd.DistanceTo(newEnd) < 0.0001)
            {
                return false;
            }

            locationCurve.Curve = Line.CreateBound(newStart, newEnd);
            return true;
        }

        private static bool TryGetFixedConnectedZ(
            NetworkElement netElement,
            XYZ endpoint,
            Dictionary<ElementId, NetworkElement> network,
            out double fixedZ)
        {
            foreach (Connector connector in netElement.Connectors)
            {
                if (CalculateHorizontalDistance(connector.Origin, endpoint) > 0.01)
                {
                    continue;
                }

                foreach (Connector otherConnector in connector.AllRefs)
                {
                    if (otherConnector.Owner.Id == netElement.ElementId)
                    {
                        continue;
                    }

                    if (network.TryGetValue(otherConnector.Owner.Id, out NetworkElement? connectedElement) &&
                        connectedElement.IsFixed)
                    {
                        fixedZ = otherConnector.Origin.Z;
                        return true;
                    }
                }
            }

            fixedZ = 0.0;
            return false;
        }

        private static double CalculateHorizontalDistance(XYZ point1, XYZ point2)
        {
            double dx = point2.X - point1.X;
            double dy = point2.Y - point1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Aplica o parâmetro built-in SLOPE nos pipes da rede.
        /// O slope no Revit é armazenado em fração (não porcentagem).
        /// Exemplo: 2% → 0.02
        /// </summary>
        private void ApplySlopeParameter(
            Dictionary<ElementId, NetworkElement> network,
            double slopePercent)
        {
            double slopeFraction = slopePercent / 100.0;

            foreach (var kvp in network)
            {
                var netElement = kvp.Value;

                // Somente pipes recebem o parâmetro Slope
                if (!netElement.IsPipe) continue;
                if (netElement.IsVertical) continue;
                if (!PipeOrientationService.IsPipeHorizontalInfrastructure(netElement.RevitElement)) continue;
                if (netElement.IsFixed) continue;

                try
                {
                    var pipe = netElement.RevitElement as Pipe;
                    if (pipe == null) continue;

                    // Tentar definir o parâmetro Slope (BuiltInParameter)
                    Parameter slopeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                    if (slopeParam != null && !slopeParam.IsReadOnly)
                    {
                        slopeParam.Set(slopeFraction);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[GeometryService] Erro ao definir slope no pipe {kvp.Key.IntegerValue}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Desconecta e reconecta elementos se necessário para preservar a topologia MEP.
        /// Chamado como fallback se a conectividade for quebrada após movimentação.
        /// </summary>
        public void RepairConnectivity(
            Dictionary<ElementId, NetworkElement> network,
            List<(ElementId, ElementId)> brokenConnections)
        {
            // Na maioria dos casos, o Revit preserva a conectividade ao mover elementos.
            // Este método serve como safety net para casos excepcionais.
            // A implementação completa de reparo de conectividade é complexa e
            // será adicionada em versões futuras.

            if (brokenConnections.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GeometryService] {brokenConnections.Count} conexões potencialmente afetadas. " +
                    "Verifique a rede manualmente.");
            }
        }
    }
}
