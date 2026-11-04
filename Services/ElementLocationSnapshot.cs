using Autodesk.Revit.DB;
using System;

namespace RevitSlopePlugin.Services
{
    public sealed class ElementLocationSnapshot
    {
        private const double ToleranceFeet = 0.0001;

        private readonly ElementId _elementId;
        private readonly Curve? _curve;
        private readonly XYZ? _point;

        private ElementLocationSnapshot(ElementId elementId, Curve? curve, XYZ? point)
        {
            _elementId = elementId;
            _curve = curve;
            _point = point;
        }

        public static ElementLocationSnapshot Capture(Element element)
        {
            if (element.Location is LocationCurve locationCurve)
            {
                return new ElementLocationSnapshot(element.Id, locationCurve.Curve.Clone(), null);
            }

            if (element.Location is LocationPoint locationPoint)
            {
                return new ElementLocationSnapshot(element.Id, null, locationPoint.Point);
            }

            return new ElementLocationSnapshot(element.Id, null, null);
        }

        public bool RestoreIfChanged(Document doc)
        {
            Element? element = doc.GetElement(_elementId);
            if (element == null)
            {
                return false;
            }

            if (_curve != null && element.Location is LocationCurve locationCurve)
            {
                Curve current = locationCurve.Curve;
                if (current.GetEndPoint(0).DistanceTo(_curve.GetEndPoint(0)) <= ToleranceFeet &&
                    current.GetEndPoint(1).DistanceTo(_curve.GetEndPoint(1)) <= ToleranceFeet)
                {
                    return false;
                }

                locationCurve.Curve = _curve.Clone();
                return true;
            }

            if (_point != null && element.Location is LocationPoint locationPoint)
            {
                XYZ delta = _point - locationPoint.Point;
                if (delta.GetLength() <= ToleranceFeet)
                {
                    return false;
                }

                ElementTransformUtils.MoveElement(doc, _elementId, delta);
                return true;
            }

            return false;
        }
    }
}
