using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Collections.Generic;

namespace RevitSlopePlugin.Models
{
    /// <summary>
    /// Representa um elemento da rede hidráulica durante o percurso do grafo.
    /// Armazena informações de conectividade, distância acumulada e offset calculado.
    /// </summary>
    public class NetworkElement
    {
        /// <summary>
        /// Id do elemento no modelo Revit.
        /// </summary>
        public ElementId ElementId { get; set; }

        /// <summary>
        /// Referência direta ao elemento Revit.
        /// </summary>
        public Element RevitElement { get; set; }

        /// <summary>
        /// Lista de conectores MEP do elemento.
        /// </summary>
        public List<Connector> Connectors { get; set; } = new List<Connector>();

        /// <summary>
        /// Distancia acumulada ate cada conector fisico do elemento.
        /// Para pipes, permite calcular uma cota diferente em cada extremidade.
        /// </summary>
        public List<(XYZ Origin, double Distance)> ConnectorDistances { get; } = new List<(XYZ Origin, double Distance)>();

        /// <summary>
        /// Distância acumulada (em pés) do centro deste elemento até o ponto base/fixo.
        /// Usada para calcular o offset vertical.
        /// </summary>
        public double DistanceFromBase { get; set; }

        /// <summary>
        /// Offset vertical calculado (em pés) = DistanceFromBase × (Inclinação / 100).
        /// Positivo significa que o elemento deve subir em relação ao ponto base.
        /// </summary>
        public double CalculatedOffset { get; set; }

        /// <summary>
        /// Coordenada Z alvo calculada para o ponto de referencia do elemento.
        /// </summary>
        public double TargetZ { get; set; }

        /// <summary>
        /// Distancia do ponto fixo usado como origem local do trecho.
        /// </summary>
        public double SlopeReferenceDistance { get; set; }

        /// <summary>
        /// Cota Z do ponto fixo usado como origem local do trecho.
        /// </summary>
        public double SlopeReferenceZ { get; set; }

        /// <summary>
        /// Cota Z de conector usada quando uma conexao vira ancora por tocar elemento fixo.
        /// </summary>
        public double? FixedReferenceZ { get; set; }

        /// <summary>
        /// Indica se o elemento deve manter a altura original.
        /// </summary>
        public bool IsFixed { get; set; }

        /// <summary>
        /// Indica se o elemento é um Pipe (tubulação).
        /// </summary>
        public bool IsPipe => RevitElement is Pipe;

        /// <summary>
        /// Indica se o trecho é predominantemente vertical (ângulo > 80° com horizontal).
        /// Trechos verticais não recebem inclinação.
        /// </summary>
        public bool IsVertical { get; set; }

        /// <summary>
        /// Coordenada Z original do ponto médio/referência do elemento antes da movimentação.
        /// </summary>
        public double OriginalZ { get; set; }

        /// <summary>
        /// Elementos vizinhos conectados (adjacência no grafo da rede).
        /// </summary>
        public List<ElementId> ConnectedElementIds { get; set; } = new List<ElementId>();

        /// <summary>
        /// Profundidade no grafo BFS (nível a partir do elemento base).
        /// </summary>
        public int BfsDepth { get; set; }

        /// <summary>
        /// ElementId do elemento pai na árvore BFS (de onde veio a navegação).
        /// </summary>
        public ElementId? ParentElementId { get; set; }

        public NetworkElement(Element element)
        {
            RevitElement = element;
            ElementId = element.Id;
        }

        public void AddConnectorDistance(XYZ origin, double distance)
        {
            ConnectorDistances.Add((origin, distance));
        }

        public bool TryGetConnectorDistance(XYZ origin, out double distance, double toleranceFeet = 0.01)
        {
            foreach (var connectorDistance in ConnectorDistances)
            {
                if (HorizontalDistance(connectorDistance.Origin, origin) <= toleranceFeet)
                {
                    distance = connectorDistance.Distance;
                    return true;
                }
            }

            distance = DistanceFromBase;
            return false;
        }

        private static double HorizontalDistance(XYZ point1, XYZ point2)
        {
            double dx = point2.X - point1.X;
            double dy = point2.Y - point1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString()
        {
            string tipo = IsPipe ? "Pipe" : RevitElement.Category?.Name ?? "Desconhecido";
            return $"[{tipo}] Id={ElementId.IntegerValue} Dist={DistanceFromBase:F2}ft Offset={CalculatedOffset:F4}ft Vertical={IsVertical}";
        }
    }
}
