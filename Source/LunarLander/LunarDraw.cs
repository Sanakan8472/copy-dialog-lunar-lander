using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Box2DX.Common;
using Box2DX.Collision;
using Box2DX.Dynamics;
using System.Windows.Media;
using System.Windows;

namespace CopyDialogLunarLander
{
    /// <summary>
    /// Use when rendering the scene. Only renders solid polys.
    /// </summary>
    public class LunarSceneDraw : LunarDraw
    {
        public Box2DX.Dynamics.Color OverrideColor;

        public override void DrawPolygon(Vec2[] vertices, int vertexCount, Box2DX.Dynamics.Color color)
        {
        }

        public override void DrawSolidPolygon(Vec2[] vertices, int vertexCount, Box2DX.Dynamics.Color color)
        {
            base.DrawSolidPolygon(vertices, vertexCount, OverrideColor);
        }

        public override void DrawCircle(Vec2 center, float radius, Box2DX.Dynamics.Color color)
        {
        }

        public override void DrawSolidCircle(Vec2 center, float radius, Vec2 axis, Box2DX.Dynamics.Color color)
        {
            DrawingContext.DrawEllipse(ToBrush(OverrideColor), ToPen(OverrideColor), ToPoint(center), radius, radius);
        }

        public override void DrawSegment(Vec2 p1, Vec2 p2, Box2DX.Dynamics.Color color)
        {
        }

        public override void DrawXForm(XForm xf)
        {
        }
    }

    /// <summary>
    /// For debug visualization. Draws everything as red wireframe.
    /// </summary>
    public class LunarDebugSceneDraw : LunarDraw
    {
        private Box2DX.Dynamics.Color _debugColor = new Box2DX.Dynamics.Color(1, 0, 0);

        public override void DrawSolidPolygon(Box2DX.Common.Vec2[] vertices, int vertexCount, Box2DX.Dynamics.Color color)
        {
            DrawPolygon(vertices, vertexCount, _debugColor);
        }

        public override void DrawCircle(Box2DX.Common.Vec2 center, float radius, Box2DX.Dynamics.Color color)
        {
            DrawingContext.DrawEllipse(null, ToPen(_debugColor), ToPoint(center), radius, radius);
        }

        public override void DrawSolidCircle(Box2DX.Common.Vec2 center, float radius, Box2DX.Common.Vec2 axis, Box2DX.Dynamics.Color color)
        {
            DrawCircle(center, radius, _debugColor);
        }

        public override void DrawSegment(Box2DX.Common.Vec2 p1, Box2DX.Common.Vec2 p2, Box2DX.Dynamics.Color color)
        {
            DrawingContext.DrawLine(ToPen(_debugColor), ToPoint(p1), ToPoint(p2));
        }
    }

    /// <summary>
    /// Draws on the DrawingContext of the file copy dialog.
    /// </summary>
    public class LunarDraw : DebugDraw
    {
        public System.Windows.Media.DrawingContext DrawingContext { get; set; }

        public override void DrawPolygon(Box2DX.Common.Vec2[] vertices, int vertexCount, Box2DX.Dynamics.Color color)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                DrawSegment(vertices[i], vertices[(i + 1) % vertexCount], color);
            }
        }

        public override void DrawSolidPolygon(Box2DX.Common.Vec2[] vertices, int vertexCount, Box2DX.Dynamics.Color color)
        {
            StreamGeometry geo = new StreamGeometry();
            geo.FillRule = FillRule.EvenOdd;

            // Open the context to use for drawing.
            using (StreamGeometryContext context = geo.Open())
            {
                var vertices2 = vertices.Take(vertexCount);
                context.BeginFigure(ToPoint(vertices2.First()), true, true);
                context.PolyLineTo(vertices2.Skip(1).Select(x => ToPoint(x)).ToArray(), true, false);
            }
            DrawingContext.DrawGeometry(ToBrush(color), ToPen(color), geo);
        }

        public override void DrawCircle(Box2DX.Common.Vec2 center, float radius, Box2DX.Dynamics.Color color)
        {
            DrawingContext.DrawEllipse(null, ToPen(color), ToPoint(center), radius, radius);
        }

        public override void DrawSolidCircle(Box2DX.Common.Vec2 center, float radius, Box2DX.Common.Vec2 axis, Box2DX.Dynamics.Color color)
        {
            DrawCircle(center, radius, color);
        }

        public override void DrawSegment(Box2DX.Common.Vec2 p1, Box2DX.Common.Vec2 p2, Box2DX.Dynamics.Color color)
        {
            DrawingContext.DrawLine(ToPen(color), ToPoint(p1), ToPoint(p2));
        }

        public override void DrawXForm(Box2DX.Common.XForm xf)
        {
            Vec2 p1 = xf.Position, p2;
            float k_axisScale = 0.4f;
            p2 = p1 + k_axisScale * xf.R.Col1;
            DrawSegment(p1, p2, new Box2DX.Dynamics.Color(1.0f, 0.0f, 0.0f));
            p2 = p1 + k_axisScale * xf.R.Col2;
            DrawSegment(p1, p2, new Box2DX.Dynamics.Color(0.0f, 1.0f, 0.0f));
        }

        public static void DrawText(DrawingContext drawingContext, string text, System.Windows.Point pos, double size, System.Drawing.Color color)
        {
            FormattedText ft = new FormattedText(text, 
                new System.Globalization.CultureInfo("en-us"), 
                FlowDirection.LeftToRight, 
                new Typeface(new System.Windows.Media.FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, new FontStretch()),
                size,
                new SolidColorBrush(new System.Windows.Media.Color() { R = color.R, G = color.G, B = color.B, A = 255 }));
            drawingContext.DrawText(ft, pos);
        }
        public System.Windows.Point ToPoint(Box2DX.Common.Vec2 point)
        {
            return new System.Windows.Point(point.X, point.Y);
        }

        public System.Windows.Media.Pen ToPen(Box2DX.Dynamics.Color color)
        {
            System.Windows.Media.Pen pen = new System.Windows.Media.Pen();
            pen.Brush = new SolidColorBrush(new System.Windows.Media.Color() { R = (byte)(color.R * 255.0f), G = (byte)(color.G * 255.0f), B = (byte)(color.B * 255.0f), A = 255 });
            return pen;
        }

        public System.Windows.Media.Brush ToBrush(Box2DX.Dynamics.Color color)
        {
            var Brush = new SolidColorBrush(new System.Windows.Media.Color() { R = (byte)(color.R * 255.0f), G = (byte)(color.G * 255.0f), B = (byte)(color.B * 255.0f), A = 255 });
            return Brush;
        }
    }
}
