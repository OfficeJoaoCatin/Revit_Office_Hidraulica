using Autodesk.Revit.DB;
using RevitSlopePlugin.Models;
using System;
using System.Collections.Generic;

namespace RevitSlopePlugin.Services
{
    internal sealed class FixedConnectionGuard
    {
        private readonly List<(Connector FixedConnector, Connector OtherConnector)> _connections =
            new List<(Connector FixedConnector, Connector OtherConnector)>();

        public int DisconnectedCount { get; private set; }

        public static FixedConnectionGuard Capture(
            Dictionary<ElementId, NetworkElement> network,
            ISet<ElementId> fixedElementIds)
        {
            var guard = new FixedConnectionGuard();
            var seen = new HashSet<string>();

            foreach (ElementId fixedElementId in fixedElementIds)
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
                        if (otherId == fixedElementId || !network.ContainsKey(otherId))
                        {
                            continue;
                        }

                        string key = BuildKey(fixedElementId, otherId, connector.Origin, otherConnector.Origin);
                        if (seen.Add(key))
                        {
                            guard._connections.Add((connector, otherConnector));
                        }
                    }
                }
            }

            return guard;
        }

        public void Disconnect()
        {
            foreach (var pair in _connections)
            {
                try
                {
                    if (pair.FixedConnector.IsConnectedTo(pair.OtherConnector))
                    {
                        pair.FixedConnector.DisconnectFrom(pair.OtherConnector);
                        DisconnectedCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FixedConnectionGuard] Falha ao desconectar: {ex.Message}");
                }
            }
        }

        public int Reconnect()
        {
            int reconnected = 0;

            foreach (var pair in _connections)
            {
                try
                {
                    if (!pair.FixedConnector.IsConnectedTo(pair.OtherConnector))
                    {
                        pair.FixedConnector.ConnectTo(pair.OtherConnector);
                        reconnected++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FixedConnectionGuard] Falha ao reconectar: {ex.Message}");
                }
            }

            return reconnected;
        }

        private static string BuildKey(ElementId firstId, ElementId secondId, XYZ first, XYZ second)
        {
            return $"{firstId.IntegerValue}:{secondId.IntegerValue}:" +
                $"{Math.Round(first.X, 4)}:{Math.Round(first.Y, 4)}:{Math.Round(first.Z, 4)}:" +
                $"{Math.Round(second.X, 4)}:{Math.Round(second.Y, 4)}:{Math.Round(second.Z, 4)}";
        }
    }
}
