using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;

namespace JplEphemerisOrbitViewer
{
    /// Ray-picking
    public class ScenePicker
    {
        public SceneObject? Hovered { get; private set; }
        public SceneObject? Selected { get; private set; }

        private readonly Func<Vector2i> _getViewportSize;
        private readonly Func<Vector2> _getMousePos;
        private readonly Func<MouseState> _getMouseState;

        private Func<System.Numerics.Vector2>? _getRectMin;     
        private Func<System.Numerics.Vector2>? _getRectSize;
        private Func<System.Numerics.Vector2>? _getImGuiMousePos;

        public ScenePicker(Func<Vector2i> getViewportSize,
                           Func<Vector2> getMousePos,
                           Func<MouseState> getMouseState)
        {
            _getViewportSize = getViewportSize;
            _getMousePos = getMousePos;
            _getMouseState = getMouseState;
        }

        // NEW: allow UI to drive selection/hover
        public void SetSelected(SceneObject? obj) => Selected = obj;
        public void SetHovered(SceneObject? obj)  => Hovered  = obj;

        public void SetImageRectProviders(
            Func<System.Numerics.Vector2> getRectMin,
            Func<System.Numerics.Vector2> getRectSize,
            Func<System.Numerics.Vector2> getImGuiMousePos)
        {
            _getRectMin = getRectMin;
            _getRectSize = getRectSize;
            _getImGuiMousePos = getImGuiMousePos;
        }

        public void Update(Matrix4 view, Matrix4 projection, IReadOnlyList<SceneObject> objects)
        {
            var (rayOrigin, rayDir) = GetMouseRay(view, projection);

            Hovered = null;
            float bestT = float.MaxValue;

            foreach (var obj in objects)
            {
                float radius = obj.BoundingRadius * Max3(obj.Scale);
                if (RaySphere(rayOrigin, rayDir, obj.Position, radius, out float t) && t < bestT)
                {
                    bestT = t;
                    Hovered = obj;
                }
            }

            if (_getMouseState().IsButtonPressed(MouseButton.Left) && Hovered != null)
            {
                Selected = Hovered;
                Console.WriteLine($"Clicked: {Selected.Name}");
            }
        }

        private (Vector3 origin, Vector3 dir) GetMouseRay(Matrix4 view, Matrix4 projection)
        {
            var size = _getViewportSize();
            float sx = MathF.Max(1, size.X);
            float sy = MathF.Max(1, size.Y);

            Vector2 mp = (_getImGuiMousePos != null)
                ? ToOTK(_getImGuiMousePos())
                : _getMousePos();

            float mx = mp.X, my = mp.Y;

            if (_getRectMin != null && _getRectSize != null)
            {
                var min = ToOTK(_getRectMin());
                var sz = ToOTK(_getRectSize());
                mx -= min.X;
                my -= min.Y;
                sx = MathF.Max(1f, sz.X);
                sy = MathF.Max(1f, sz.Y);
            }

            // Screen -> NDC
            float x = 2f * (mx / sx) - 1f;
            float y = 1f - 2f * (my / sy);

            var pNear = new Vector4(x, y, 0f, 1f);
            var pFar = new Vector4(x, y, 1f, 1f);

            Matrix4.Invert(view * projection, out var invVP);
            var wNear = Vector4.TransformRow(pNear, invVP);
            var wFar = Vector4.TransformRow(pFar, invVP);

            wNear /= wNear.W;
            wFar /= wFar.W;

            var o = new Vector3(wNear.X, wNear.Y, wNear.Z);
            var d = Vector3.Normalize(new Vector3(wFar.X - wNear.X, wFar.Y - wNear.Y, wFar.Z - wNear.Z));
            return (o, d);
        }

        private static bool RaySphere(Vector3 rayOrigin, Vector3 rayDir, Vector3 center, float radius, out float t)
        {
            var oc = rayOrigin - center;
            float b = Vector3.Dot(oc, rayDir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) { t = float.NaN; return false; }
            float sqrt = MathF.Sqrt(disc);
            float t0 = -b - sqrt;
            float t1 = -b + sqrt;
            t = (t0 > 0f) ? t0 : (t1 > 0f ? t1 : float.NaN);
            return !float.IsNaN(t);
        }

        private static float Max3(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));

        private static Vector2 ToOTK(System.Numerics.Vector2 v) => new Vector2(v.X, v.Y);
    }
}
