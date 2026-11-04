using Autodesk.Revit.DB;
using System;

namespace RevitSlopePlugin.Services
{
    internal static class PipeOrientationService
    {
        private const double MinHorizontalProjectionFeet = 0.03; // cerca de 9 mm
        private const double MaxHorizontalInfraAngleDegrees = 30.0;

        public static bool IsPipeVerticalOrSteep(Element element)
        {
            if (!IsPipeCurve(element))
            {
                return false;
            }

            if (element.Location is not LocationCurve locationCurve)
            {
                return false;
            }

            Curve curve = locationCurve.Curve;
            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double dz = end.Z - start.Z;

            double horizontalLength = Math.Sqrt(dx * dx + dy * dy);
            double verticalLength = Math.Abs(dz);

            if (horizontalLength < MinHorizontalProjectionFeet)
            {
                return verticalLength >= MinHorizontalProjectionFeet;
            }

            double angleDeg = Math.Atan2(verticalLength, horizontalLength) * 180.0 / Math.PI;
            return angleDeg >= MaxHorizontalInfraAngleDegrees;
        }

        public static bool IsPipeHorizontalInfrastructure(Element element)
        {
            return IsPipeCurve(element) && !IsPipeVerticalOrSteep(element);
        }

        private static bool IsPipeCurve(Element element)
        {
            if (element?.Category == null)
            {
                return false;
            }

            return (BuiltInCategory)element.Category.Id.IntegerValue == BuiltInCategory.OST_PipeCurves;
        }
    }
}
