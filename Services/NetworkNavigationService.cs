using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitSlopePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    /// <summary>
    /// Serviço responsável por navegar pela rede hidráulica MEP usando conectores.
    /// Utiliza BFS (Breadth-First Search) para percorrer o grafo de elementos conectados,
    /// calculando a distância acumulada de cada elemento até o ponto base.
    /// </summary>
    public class NetworkNavigationService
    {
        private readonly Document _doc;

        public int SkippedVerticalPipeCount { get; private set; }

        public NetworkNavigationService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Percorre toda a rede conectada a partir do elemento base usando BFS.
        /// Retorna um dicionário com todos os elementos encontrados e suas distâncias.
        /// </summary>
        /// <param name="baseElement">Elemento selecionado como ponto fixo.</param>
        /// <returns>Dicionário ElementId → NetworkElement com a rede completa.</returns>
        public Dictionary<ElementId, NetworkElement> TraverseNetwork(Element baseElement, XYZ? basePickPoint = null)
        {
            SkippedVerticalPipeCount = 0;

            var network = new Dictionary<ElementId, NetworkElement>();
            var visited = new HashSet<ElementId>();
            var skippedVerticalPipes = new HashSet<ElementId>();
            var queue = new Queue<Element>();

            XYZ? baseConnectorOrigin = GetBaseConnectorOrigin(baseElement, basePickPoint);

            // Criar elemento base com distancia 0 no conector mais baixo/jusante.
            var baseNetworkElement = CreateNetworkElement(baseElement, 0.0, 0, null, baseConnectorOrigin);
            network[baseElement.Id] = baseNetworkElement;
            visited.Add(baseElement.Id);

            queue.Enqueue(baseElement);

            while (queue.Count > 0)
            {
                Element currentElement = queue.Dequeue();
                NetworkElement currentNetworkElement = network[currentElement.Id];

                // Obter ConnectorManager do elemento atual
                var connectorManager = ValidationService.GetConnectorManager(currentElement);
                if (connectorManager == null) continue;

                // Percorrer todos os conectores do elemento
                foreach (Connector connector in connectorManager.Connectors)
                {
                    // Ignorar conectores lógicos ou não-físicos
                    if (connector.ConnectorType != ConnectorType.End &&
                        connector.ConnectorType != ConnectorType.Curve)
                        continue;

                    if (!connector.IsConnected) continue;
                    if (!currentNetworkElement.TryGetConnectorDistance(
                        connector.Origin,
                        out double currentConnectorDistance))
                    {
                        currentConnectorDistance = currentNetworkElement.DistanceFromBase;
                    }

                    // Percorrer todos os elementos conectados a este conector
                    foreach (Connector otherConnector in connector.AllRefs)
                    {
                        Element connectedElement = otherConnector.Owner;

                        if (connectedElement.Id == currentElement.Id) continue;

                        // Evitar loops: não revisitar elementos já processados
                        if (visited.Contains(connectedElement.Id))
                        {
                            AddAdjacency(network, currentElement.Id, connectedElement.Id);
                            continue;
                        }

                        // Verificar se é um elemento MEP aceito
                        if (!ValidationService.IsAcceptedMepElement(connectedElement)) continue;
                        if (!ValidationService.IsSameMepSystem(baseElement, connectedElement)) continue;
                        if (PipeOrientationService.IsPipeVerticalOrSteep(connectedElement))
                        {
                            if (skippedVerticalPipes.Add(connectedElement.Id))
                            {
                                SkippedVerticalPipeCount++;
                            }

                            continue;
                        }

                        // Calcular distancia entre conectores; em conexoes reais costuma ser zero.
                        double connectorGap = CalculateHorizontalDistance(
                            connector.Origin, otherConnector.Origin);

                        double newDistance = currentConnectorDistance + connectorGap;
                        int newDepth = currentNetworkElement.BfsDepth + 1;

                        // Criar e registrar o elemento da rede
                        var netElement = CreateNetworkElement(
                            connectedElement, newDistance, newDepth, currentElement.Id, otherConnector.Origin);

                        network[connectedElement.Id] = netElement;
                        visited.Add(connectedElement.Id);

                        // Registrar adjacência
                        AddAdjacency(network, currentElement.Id, connectedElement.Id);

                        queue.Enqueue(connectedElement);
                    }
                }
            }

            return network;
        }

        /// <summary>
        /// Obtém a coordenada Z de referência do elemento base (ponto fixo).
        /// Para pipes, usa o ponto médio da LocationCurve.
        /// Para fittings/accessories, usa a LocationPoint.
        /// </summary>
        public double GetBaseElevation(Element baseElement, XYZ? basePickPoint = null)
        {
            Connector? baseConnector = GetBaseConnector(baseElement, basePickPoint);
            if (baseConnector != null)
            {
                return baseConnector.Origin.Z;
            }

            if (baseElement.Location is LocationCurve locationCurve)
            {
                var curve = locationCurve.Curve;
                XYZ midPoint = curve.Evaluate(0.5, true);
                return midPoint.Z;
            }

            if (baseElement.Location is LocationPoint locationPoint)
            {
                return locationPoint.Point.Z;
            }

            // Fallback: tentar BoundingBox
            var bb = baseElement.get_BoundingBox(null);
            if (bb != null)
            {
                return (bb.Min.Z + bb.Max.Z) / 2.0;
            }

            return 0.0;
        }

        /// <summary>
        /// Cria um NetworkElement a partir de um Element do Revit.
        /// </summary>
        private NetworkElement CreateNetworkElement(
            Element element,
            double entryDistance,
            int depth,
            ElementId? parentId,
            XYZ? entryConnectorOrigin)
        {
            var netElement = new NetworkElement(element)
            {
                DistanceFromBase = entryDistance,
                BfsDepth = depth,
                ParentElementId = parentId,
                OriginalZ = GetElementReferenceZ(element),
                IsVertical = PipeOrientationService.IsPipeVerticalOrSteep(element)
            };

            // Popular lista de conectores
            var cm = ValidationService.GetConnectorManager(element);
            if (cm != null)
            {
                foreach (Connector c in cm.Connectors)
                {
                    if (!IsPhysicalConnector(c)) continue;
                    netElement.Connectors.Add(c);
                }
            }

            PopulateConnectorDistances(netElement, entryDistance, entryConnectorOrigin);

            return netElement;
        }

        private void PopulateConnectorDistances(
            NetworkElement netElement,
            double entryDistance,
            XYZ? entryConnectorOrigin)
        {
            if (netElement.Connectors.Count == 0)
            {
                return;
            }

            XYZ referenceOrigin = entryConnectorOrigin ?? GetBaseConnectorOrigin(netElement.RevitElement) ?? netElement.Connectors[0].Origin;

            foreach (Connector connector in netElement.Connectors)
            {
                double connectorDistance = entryDistance;

                if (netElement.IsPipe)
                {
                    connectorDistance += CalculateHorizontalDistance(referenceOrigin, connector.Origin);
                }

                netElement.AddConnectorDistance(connector.Origin, connectorDistance);
            }

            if (netElement.ConnectorDistances.Count > 0)
            {
                netElement.DistanceFromBase = netElement.IsPipe
                    ? netElement.ConnectorDistances.Max(cd => cd.Distance)
                    : entryDistance;
            }
        }

        /// <summary>
        /// Obtém a coordenada Z de referência de um elemento.
        /// </summary>
        private double GetElementReferenceZ(Element element)
        {
            if (element.Location is LocationCurve lc)
            {
                XYZ midPoint = lc.Curve.Evaluate(0.5, true);
                return midPoint.Z;
            }

            if (element.Location is LocationPoint lp)
            {
                return lp.Point.Z;
            }

            var bb = element.get_BoundingBox(null);
            return bb != null ? (bb.Min.Z + bb.Max.Z) / 2.0 : 0.0;
        }

        /// <summary>
        /// Calcula a distância horizontal (projeção XY) entre dois pontos.
        /// Ignora a componente Z para medir distância no plano.
        /// </summary>
        private double CalculateHorizontalDistance(XYZ point1, XYZ point2)
        {
            double dx = point2.X - point1.X;
            double dy = point2.Y - point1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsPhysicalConnector(Connector connector)
        {
            return connector.ConnectorType == ConnectorType.End ||
                connector.ConnectorType == ConnectorType.Curve;
        }

        private Connector? GetBaseConnector(Element element, XYZ? referencePoint = null)
        {
            var cm = ValidationService.GetConnectorManager(element);
            if (cm == null) return null;

            IEnumerable<Connector> connectors = cm.Connectors
                .Cast<Connector>()
                .Where(IsPhysicalConnector);

            if (referencePoint != null)
            {
                return connectors
                    .OrderBy(connector => connector.Origin.DistanceTo(referencePoint))
                    .FirstOrDefault();
            }

            return connectors
                .OrderBy(connector => connector.Origin.Z)
                .FirstOrDefault();
        }

        private XYZ? GetBaseConnectorOrigin(Element element, XYZ? referencePoint = null)
        {
            return GetBaseConnector(element, referencePoint)?.Origin;
        }

        private static void AddAdjacency(
            Dictionary<ElementId, NetworkElement> network,
            ElementId firstId,
            ElementId secondId)
        {
            if (network.TryGetValue(firstId, out NetworkElement? first) &&
                !first.ConnectedElementIds.Contains(secondId))
            {
                first.ConnectedElementIds.Add(secondId);
            }

            if (network.TryGetValue(secondId, out NetworkElement? second) &&
                !second.ConnectedElementIds.Contains(firstId))
            {
                second.ConnectedElementIds.Add(firstId);
            }
        }

    }
}
