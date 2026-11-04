using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitSlopePlugin.Services
{
    internal sealed class LinkedTopographyService
    {
        private const double TriangleTolerance = 1e-8;
        private const double UpFacingNormalTolerance = 0.12;

        public RevitLinkInstance PickRevitLink(UIDocument uiDoc)
        {
            Reference reference = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new RevitLinkSelectionFilter(),
                "Selecione o Revit Link com a topografia");

            return (RevitLinkInstance)uiDoc.Document.GetElement(reference.ElementId);
        }

        public List<LinkedTopographyCandidate> FindReferenceSurfaces(
            Document linkDoc,
            IReadOnlyCollection<XYZ> linkPoints)
        {
            var options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            return new FilteredElementCollector(linkDoc)
                .WhereElementIsNotElementType()
                .Where(IsModelSurfaceElement)
                .Where(element => MightCoverAnyPoint(element, linkPoints))
                .Select(element => CreateCandidate(element, options))
                .Where(candidate => candidate != null)
                .Cast<LinkedTopographyCandidate>()
                .OrderByDescending(candidate => candidate.EstimatedArea)
                .ThenBy(candidate => candidate.DisplayName)
                .ToList();
        }

        public XYZ ConvertHostPointToLink(RevitLinkInstance linkInstance, XYZ hostPoint)
        {
            // A transformacao inversa leva o ponto do documento host para o
            // sistema local do arquivo vinculado, respeitando deslocamento e rotacao.
            Transform hostToLink = linkInstance.GetTransform().Inverse;
            return hostToLink.OfPoint(hostPoint);
        }

        public double ConvertLinkElevationToHost(
            RevitLinkInstance linkInstance,
            XYZ linkPoint,
            double linkElevationFeet)
        {
            // A cota encontrada na topografia esta no sistema do link. Ao reconstruir
            // o ponto com o mesmo X/Y do link e aplicar a transformacao direta, obtemos
            // a elevacao equivalente no documento host.
            XYZ hostSurfacePoint = linkInstance.GetTransform().OfPoint(
                new XYZ(linkPoint.X, linkPoint.Y, linkElevationFeet));

            return hostSurfacePoint.Z;
        }

        public bool TryGetElevationAtPoint(
            LinkedTopographyCandidate candidate,
            XYZ linkPoint,
            double currentTopLinkElevationFeet,
            out double elevationFeet,
            out string reason)
        {
            elevationFeet = 0.0;

            if (candidate.BoundingBox != null && !ContainsXY(candidate.BoundingBox, linkPoint))
            {
                reason = "fora da area da topografia";
                return false;
            }

            if (candidate.Triangles.Count == 0)
            {
                reason = "topografia sem malha triangulada acessivel";
                return false;
            }

            bool found = false;
            double bestElevation = 0.0;
            double bestDistance = double.MaxValue;

            foreach (TerrainTriangle triangle in candidate.Triangles)
            {
                if (!TryInterpolateTriangleZ(linkPoint, triangle.A, triangle.B, triangle.C, out double z))
                {
                    continue;
                }

                // A ferramenta usa faces superiores do link, e nao apenas elementos
                // de topografia. Se houver mais de um piso/laje no mesmo X/Y, a face
                // valida mais proxima do topo atual da caixa funciona tanto com caixas
                // acima quanto abaixo do link.
                double distance = Math.Abs(currentTopLinkElevationFeet - z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestElevation = z;
                }

                found = true;
            }

            if (!found)
            {
                reason = "ponto fora das faces superiores do link";
                return false;
            }

            elevationFeet = bestElevation;
            reason = string.Empty;
            return true;
        }

        private static LinkedTopographyCandidate? CreateCandidate(Element element, Options options)
        {
            var triangles = new List<TerrainTriangle>();
            GeometryElement? geometry = element.get_Geometry(options);
            if (geometry != null)
            {
                AppendTriangles(geometry, Transform.Identity, triangles);
            }

            if (triangles.Count == 0)
            {
                return null;
            }

            BoundingBoxXYZ? boundingBox = element.get_BoundingBox(null);
            return new LinkedTopographyCandidate(
                element.Id,
                BuildDisplayName(element, boundingBox, triangles.Count),
                boundingBox,
                triangles);
        }

        private static bool IsModelSurfaceElement(Element element)
        {
            if (element is TopographySurface || element is Toposolid)
            {
                return true;
            }

            Category? category = element.Category;
            if (category == null || category.CategoryType != CategoryType.Model)
            {
                return false;
            }

            if (element is View || element is RevitLinkInstance)
            {
                return false;
            }

            return true;
        }

        private static bool MightCoverAnyPoint(Element element, IReadOnlyCollection<XYZ> linkPoints)
        {
            if (linkPoints.Count == 0)
            {
                return true;
            }

            BoundingBoxXYZ? boundingBox = element.get_BoundingBox(null);
            if (boundingBox == null)
            {
                return true;
            }

            return linkPoints.Any(point => ContainsXY(boundingBox, point));
        }

        private static string BuildDisplayName(
            Element element,
            BoundingBoxXYZ? boundingBox,
            int triangleCount)
        {
            string name = string.IsNullOrWhiteSpace(element.Name)
                ? element.GetType().Name
                : element.Name;

            string category = element.Category?.Name ?? element.GetType().Name;
            string area = boundingBox == null
                ? string.Empty
                : $" - area aprox. {CalculateArea(boundingBox).ToString("F1", CultureInfo.InvariantCulture)} m2";

            return $"{name} ({category}, Id {element.Id.IntegerValue}, {triangleCount} faces superiores{area})";
        }

        private static void AppendTriangles(
            GeometryElement geometry,
            Transform transform,
            List<TerrainTriangle> triangles)
        {
            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject is Solid solid)
                {
                    AppendSolidTriangles(solid, transform, triangles);
                    continue;
                }

                if (geometryObject is Mesh mesh)
                {
                    AppendMeshTriangles(mesh, transform, triangles);
                    continue;
                }

                if (geometryObject is GeometryInstance instance)
                {
                    Transform nestedTransform = transform.Multiply(instance.Transform);
                    GeometryElement instanceGeometry = instance.GetSymbolGeometry();
                    AppendTriangles(instanceGeometry, nestedTransform, triangles);
                }
            }
        }

        private static void AppendSolidTriangles(
            Solid solid,
            Transform transform,
            List<TerrainTriangle> triangles)
        {
            if (solid.Faces == null || solid.Faces.Size == 0)
            {
                return;
            }

            foreach (Face face in solid.Faces)
            {
                Mesh mesh = face.Triangulate();
                AppendMeshTriangles(mesh, transform, triangles, onlyUpFacing: true);
            }
        }

        private static void AppendMeshTriangles(
            Mesh mesh,
            Transform transform,
            List<TerrainTriangle> triangles,
            bool onlyUpFacing = true)
        {
            for (int index = 0; index < mesh.NumTriangles; index++)
            {
                MeshTriangle triangle = mesh.get_Triangle(index);
                XYZ a = transform.OfPoint(triangle.get_Vertex(0));
                XYZ b = transform.OfPoint(triangle.get_Vertex(1));
                XYZ c = transform.OfPoint(triangle.get_Vertex(2));

                if (IsTriangleDegeneratedInXY(a, b, c))
                {
                    continue;
                }

                if (onlyUpFacing && GetTriangleNormalZ(a, b, c) <= UpFacingNormalTolerance)
                {
                    continue;
                }

                triangles.Add(new TerrainTriangle(a, b, c));
            }
        }

        private static double GetTriangleNormalZ(XYZ a, XYZ b, XYZ c)
        {
            XYZ ab = b - a;
            XYZ ac = c - a;
            XYZ normal = ab.CrossProduct(ac);
            double length = normal.GetLength();

            return length <= TriangleTolerance ? 0.0 : normal.Z / length;
        }

        private static bool TryInterpolateTriangleZ(
            XYZ point,
            XYZ a,
            XYZ b,
            XYZ c,
            out double z)
        {
            z = 0.0;

            double denominator =
                ((b.Y - c.Y) * (a.X - c.X)) +
                ((c.X - b.X) * (a.Y - c.Y));

            if (Math.Abs(denominator) <= TriangleTolerance)
            {
                return false;
            }

            double alpha =
                (((b.Y - c.Y) * (point.X - c.X)) +
                ((c.X - b.X) * (point.Y - c.Y))) / denominator;

            double beta =
                (((c.Y - a.Y) * (point.X - c.X)) +
                ((a.X - c.X) * (point.Y - c.Y))) / denominator;

            double gamma = 1.0 - alpha - beta;

            if (alpha < -TriangleTolerance ||
                beta < -TriangleTolerance ||
                gamma < -TriangleTolerance)
            {
                return false;
            }

            z = (alpha * a.Z) + (beta * b.Z) + (gamma * c.Z);
            return !double.IsNaN(z) && !double.IsInfinity(z);
        }

        private static bool IsTriangleDegeneratedInXY(XYZ a, XYZ b, XYZ c)
        {
            double area2 = Math.Abs(
                ((b.X - a.X) * (c.Y - a.Y)) -
                ((c.X - a.X) * (b.Y - a.Y)));

            return area2 <= TriangleTolerance;
        }

        private static bool ContainsXY(BoundingBoxXYZ boundingBox, XYZ point)
        {
            const double tolerance = 0.01;
            return point.X >= boundingBox.Min.X - tolerance &&
                point.X <= boundingBox.Max.X + tolerance &&
                point.Y >= boundingBox.Min.Y - tolerance &&
                point.Y <= boundingBox.Max.Y + tolerance;
        }

        private static double CalculateArea(BoundingBoxXYZ boundingBox)
        {
            double widthFeet = Math.Abs(boundingBox.Max.X - boundingBox.Min.X);
            double depthFeet = Math.Abs(boundingBox.Max.Y - boundingBox.Min.Y);
            return widthFeet * depthFeet * 0.09290304;
        }

        private sealed class RevitLinkSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is RevitLinkInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }

    internal sealed class LinkedTopographyCandidate
    {
        public LinkedTopographyCandidate(
            ElementId elementId,
            string displayName,
            BoundingBoxXYZ? boundingBox,
            List<TerrainTriangle> triangles)
        {
            ElementId = elementId;
            DisplayName = displayName;
            BoundingBox = boundingBox;
            Triangles = triangles;
            EstimatedArea = boundingBox == null ? 0.0 : Math.Abs(
                (boundingBox.Max.X - boundingBox.Min.X) *
                (boundingBox.Max.Y - boundingBox.Min.Y));
        }

        public ElementId ElementId { get; }
        public string DisplayName { get; }
        public BoundingBoxXYZ? BoundingBox { get; }
        public List<TerrainTriangle> Triangles { get; }
        public double EstimatedArea { get; }
    }

    internal sealed class TerrainTriangle
    {
        public TerrainTriangle(XYZ a, XYZ b, XYZ c)
        {
            A = a;
            B = b;
            C = c;
        }

        public XYZ A { get; }
        public XYZ B { get; }
        public XYZ C { get; }
    }
}
