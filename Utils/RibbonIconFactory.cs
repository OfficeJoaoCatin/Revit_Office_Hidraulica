using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RevitSlopePlugin.Utils
{
    public enum RibbonIconKind
    {
        ApplySlope,
        BoxPipe,
        BoxPipeSequence,
        MainBranchSequence,
        SecondaryBranchSequence,
        ConfiguredSegments,
        AlignTopography,
        VerifySpreadsheet,
        Settings
    }

    public static class RibbonIconFactory
    {
        public static ImageSource Create(RibbonIconKind kind, int size)
        {
            var visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                DrawBackground(dc, size);

                switch (kind)
                {
                    case RibbonIconKind.ApplySlope:
                        DrawApplySlope(dc, size);
                        break;
                    case RibbonIconKind.BoxPipe:
                        DrawBoxPipe(dc, size);
                        break;
                    case RibbonIconKind.BoxPipeSequence:
                        DrawBoxPipeSequence(dc, size);
                        break;
                    case RibbonIconKind.MainBranchSequence:
                        DrawMainBranchSequence(dc, size);
                        break;
                    case RibbonIconKind.SecondaryBranchSequence:
                        DrawSecondaryBranchSequence(dc, size);
                        break;
                    case RibbonIconKind.ConfiguredSegments:
                        DrawConfiguredSegments(dc, size);
                        break;
                    case RibbonIconKind.AlignTopography:
                        DrawAlignTopography(dc, size);
                        break;
                    case RibbonIconKind.VerifySpreadsheet:
                        DrawVerifySpreadsheet(dc, size);
                        break;
                    case RibbonIconKind.Settings:
                        DrawSettings(dc, size);
                        break;
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void DrawBackground(DrawingContext dc, int size)
        {
            double radius = size * 0.16;
            var rect = new Rect(0.5, 0.5, size - 1.0, size - 1.0);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(242, 247, 249)),
                new Pen(new SolidColorBrush(Color.FromRgb(106, 130, 142)), Math.Max(1.0, size / 32.0)),
                rect,
                radius,
                radius);
        }

        private static void DrawApplySlope(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var pipePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 108, 149)), 4.0 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var guidePen = new Pen(new SolidColorBrush(Color.FromRgb(118, 137, 145)), 1.6 * s);
            guidePen.DashStyle = DashStyles.Dash;

            dc.DrawLine(guidePen, new Point(6 * s, 23 * s), new Point(26 * s, 23 * s));
            dc.DrawLine(pipePen, new Point(6 * s, 11 * s), new Point(26 * s, 21 * s));
            DrawArrowHead(dc, new Point(26 * s, 21 * s), new Vector(-1, -0.5), new SolidColorBrush(Color.FromRgb(0, 108, 149)), s);

            var dropBrush = new SolidColorBrush(Color.FromRgb(0, 150, 190));
            dc.DrawEllipse(dropBrush, null, new Point(10 * s, 23 * s), 2.3 * s, 2.3 * s);
        }

        private static void DrawBoxPipe(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var boxBrush = new SolidColorBrush(Color.FromRgb(221, 233, 238));
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(75, 98, 110)), 1.7 * s);
            var pipePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 3.6 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            dc.DrawLine(pipePen, new Point(10 * s, 17 * s), new Point(22 * s, 20 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(4 * s, 8 * s, 8 * s, 14 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(20 * s, 11 * s, 8 * s, 14 * s));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(128, 154, 164)), 1.2 * s), new Point(4 * s, 12 * s), new Point(12 * s, 12 * s));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(128, 154, 164)), 1.2 * s), new Point(20 * s, 15 * s), new Point(28 * s, 15 * s));
        }

        private static void DrawBoxPipeSequence(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var boxBrush = new SolidColorBrush(Color.FromRgb(221, 233, 238));
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(75, 98, 110)), 1.5 * s);
            var pipePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 3.0 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            dc.DrawLine(pipePen, new Point(8 * s, 13 * s), new Point(16 * s, 17 * s));
            dc.DrawLine(pipePen, new Point(16 * s, 17 * s), new Point(24 * s, 21 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(4 * s, 7 * s, 7 * s, 10 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(13 * s, 11 * s, 7 * s, 10 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(22 * s, 15 * s, 7 * s, 10 * s));
        }

        private static void DrawMainBranchSequence(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var boxBrush = new SolidColorBrush(Color.FromRgb(221, 233, 238));
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(75, 98, 110)), 1.5 * s);
            var pipePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 3.2 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            dc.DrawLine(pipePen, new Point(7 * s, 21 * s), new Point(16 * s, 16 * s));
            dc.DrawLine(pipePen, new Point(16 * s, 16 * s), new Point(25 * s, 11 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(4 * s, 17 * s, 7 * s, 9 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(12.5 * s, 12 * s, 7 * s, 9 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(21 * s, 7 * s, 7 * s, 9 * s));
        }

        private static void DrawSecondaryBranchSequence(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var boxBrush = new SolidColorBrush(Color.FromRgb(221, 233, 238));
            var fixedBoxBrush = new SolidColorBrush(Color.FromRgb(255, 238, 226));
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(75, 98, 110)), 1.5 * s);
            var mainPipePen = new Pen(new SolidColorBrush(Color.FromRgb(112, 126, 136)), 2.4 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var branchPipePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 3.0 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            dc.DrawLine(mainPipePen, new Point(5 * s, 23 * s), new Point(25 * s, 23 * s));
            dc.DrawLine(branchPipePen, new Point(15 * s, 23 * s), new Point(24 * s, 11 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(4 * s, 19 * s, 7 * s, 8 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(12 * s, 19 * s, 7 * s, 8 * s));
            dc.DrawRectangle(fixedBoxBrush, boxPen, new Rect(21 * s, 7 * s, 7 * s, 8 * s));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(198, 55, 55)), 1.8 * s), new Point(22.5 * s, 5.5 * s), new Point(26.5 * s, 5.5 * s));
        }

        private static void DrawConfiguredSegments(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var boxBrush = new SolidColorBrush(Color.FromRgb(221, 233, 238));
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(75, 98, 110)), 1.4 * s);
            var pipePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 2.8 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var editPen = new Pen(new SolidColorBrush(Color.FromRgb(198, 55, 55)), 1.4 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            dc.DrawLine(pipePen, new Point(7 * s, 21 * s), new Point(15 * s, 16 * s));
            dc.DrawLine(pipePen, new Point(15 * s, 16 * s), new Point(23 * s, 12 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(4 * s, 18 * s, 6 * s, 7 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(12 * s, 13 * s, 6 * s, 7 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(20 * s, 9 * s, 6 * s, 7 * s));

            dc.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                new Pen(new SolidColorBrush(Color.FromRgb(85, 107, 117)), 1.0 * s),
                new Rect(18 * s, 19 * s, 9 * s, 7 * s));
            dc.DrawLine(editPen, new Point(20 * s, 21 * s), new Point(25 * s, 21 * s));
            dc.DrawLine(editPen, new Point(20 * s, 24 * s), new Point(23 * s, 24 * s));
        }

        private static void DrawSettings(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(67, 84, 96)), 2.0 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var knobBrush = new SolidColorBrush(Color.FromRgb(0, 117, 153));

            DrawSlider(dc, pen, knobBrush, 8 * s, 12 * s, 21 * s, 14 * s, s);
            DrawSlider(dc, pen, knobBrush, 8 * s, 20 * s, 24 * s, 18 * s, s);

            var gearPen = new Pen(new SolidColorBrush(Color.FromRgb(67, 84, 96)), 1.6 * s);
            dc.DrawEllipse(null, gearPen, new Point(23 * s, 9 * s), 4.4 * s, 4.4 * s);
            dc.DrawEllipse(knobBrush, null, new Point(23 * s, 9 * s), 1.5 * s, 1.5 * s);

            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                var inner = new Point(23 * s + Math.Cos(angle) * 5.2 * s, 9 * s + Math.Sin(angle) * 5.2 * s);
                var outer = new Point(23 * s + Math.Cos(angle) * 6.7 * s, 9 * s + Math.Sin(angle) * 6.7 * s);
                dc.DrawLine(gearPen, inner, outer);
            }
        }

        private static void DrawAlignTopography(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var topoPen = new Pen(new SolidColorBrush(Color.FromRgb(57, 122, 78)), 2.0 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var guidePen = new Pen(new SolidColorBrush(Color.FromRgb(198, 55, 55)), 1.5 * s);
            guidePen.DashStyle = DashStyles.Dash;
            var boxBrush = new SolidColorBrush(Color.FromRgb(221, 233, 238));
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(75, 98, 110)), 1.6 * s);

            var terrain = new StreamGeometry();
            using (StreamGeometryContext context = terrain.Open())
            {
                context.BeginFigure(new Point(4 * s, 12 * s), false, false);
                context.LineTo(new Point(10 * s, 10 * s), true, false);
                context.LineTo(new Point(16 * s, 13 * s), true, false);
                context.LineTo(new Point(22 * s, 9 * s), true, false);
                context.LineTo(new Point(28 * s, 11 * s), true, false);
            }

            terrain.Freeze();
            dc.DrawGeometry(null, topoPen, terrain);
            dc.DrawLine(guidePen, new Point(8 * s, 12 * s), new Point(24 * s, 12 * s));
            dc.DrawRectangle(boxBrush, boxPen, new Rect(11 * s, 15 * s, 10 * s, 10 * s));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 1.8 * s), new Point(16 * s, 15 * s), new Point(16 * s, 25 * s));
            DrawArrowHead(dc, new Point(16 * s, 13 * s), new Vector(0, 1), new SolidColorBrush(Color.FromRgb(198, 55, 55)), s);
        }

        private static void DrawVerifySpreadsheet(DrawingContext dc, int size)
        {
            double s = size / 32.0;
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(85, 107, 117)), 1.2 * s);
            var accentPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 117, 153)), 2.0 * s)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var fill = new SolidColorBrush(Color.FromRgb(255, 255, 255));

            dc.DrawRectangle(fill, gridPen, new Rect(6 * s, 5 * s, 16 * s, 20 * s));
            for (int i = 1; i < 4; i++)
            {
                dc.DrawLine(gridPen, new Point((6 + i * 4) * s, 5 * s), new Point((6 + i * 4) * s, 25 * s));
            }

            for (int i = 1; i < 5; i++)
            {
                dc.DrawLine(gridPen, new Point(6 * s, (5 + i * 4) * s), new Point(22 * s, (5 + i * 4) * s));
            }

            dc.DrawLine(accentPen, new Point(9 * s, 20 * s), new Point(13 * s, 23 * s));
            dc.DrawLine(accentPen, new Point(13 * s, 23 * s), new Point(24 * s, 11 * s));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(57, 122, 78)), null, new Point(24 * s, 11 * s), 3.0 * s, 3.0 * s);
        }

        private static void DrawSlider(
            DrawingContext dc,
            Pen linePen,
            Brush knobBrush,
            double x1,
            double y,
            double x2,
            double knobX,
            double scale)
        {
            dc.DrawLine(linePen, new Point(x1, y), new Point(x2, y));
            dc.DrawEllipse(knobBrush, null, new Point(knobX, y), 2.3 * scale, 2.3 * scale);
        }

        private static void DrawArrowHead(DrawingContext dc, Point tip, Vector direction, Brush brush, double scale)
        {
            direction.Normalize();
            Vector normal = new Vector(-direction.Y, direction.X);

            Point basePoint = tip + direction * 6.0 * scale;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(tip, true, true);
                context.LineTo(basePoint + normal * 3.5 * scale, true, false);
                context.LineTo(basePoint - normal * 3.5 * scale, true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }
    }
}
