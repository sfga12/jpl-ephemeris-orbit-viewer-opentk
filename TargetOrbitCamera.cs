using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;

namespace JplEphemerisOrbitViewer
{
    public class TargetOrbitCamera
    {
        // Orbit state
        public float Distance = 5.0f;
        public float Yaw = MathHelper.DegreesToRadians(45f);
        public float Pitch = MathHelper.DegreesToRadians(20f);

        // speeds and limits
        public float RotateSpeed = 3.0f;    // RMB drag
        public float ZoomFactor = 1.15f;
        public float MinPitch = MathHelper.DegreesToRadians(-89f);
        public float MaxPitch = MathHelper.DegreesToRadians(89f);

        // Prevent runaway zoom that leads to precision loss / NaNs
        public float MaxDistance = 1_000_000f; // tune per your units-per-AU

        // NEW: FOV used by responsive sizing (keep in sync with your projection)
        public float FovY = MathHelper.DegreesToRadians(45f);

        public Vector3 Position(Vector3 target)
        {
            float cosP = MathF.Cos(Pitch);
            float sinP = MathF.Sin(Pitch);
            float cosY = MathF.Cos(Yaw);
            float sinY = MathF.Sin(Yaw);
            var offset = new Vector3(cosP * cosY, sinP, cosP * sinY) * Distance;
            return target + offset;
        }

        public Matrix4 GetViewMatrix(Vector3 target)
            => Matrix4.LookAt(Position(target), target, Vector3.UnitY);

        public void UpdateInput(MouseState mouse, bool allowInput, float dt, Vector3 target, float minDistance)
        {
            if (!allowInput) return;

            // Zoom (wheel)
            float scroll = mouse.ScrollDelta.Y;
            if (scroll != 0f)
            {
                float desired = Distance * MathF.Pow(ZoomFactor, -scroll);
                Distance = MathHelper.Clamp(desired, minDistance, MaxDistance);
            }

            // Orbit (RMB drag)
            if (mouse.IsButtonDown(MouseButton.Right))
            {
                var d = mouse.Delta;
                Yaw -= d.X * RotateSpeed * dt;
                Pitch -= d.Y * RotateSpeed * dt;

                // wrap & clamp
                if (Yaw > MathF.PI) Yaw -= MathF.Tau;
                if (Yaw < -MathF.PI) Yaw += MathF.Tau;
                Pitch = MathHelper.Clamp(Pitch, MinPitch, MaxPitch);
            }
        }

        // OPTIONAL: help choose stable clip planes in Scene
        public void GetClipPlanes(float sceneExtent, out float zNear, out float zFar)
        {
            float dist = MathF.Max(0.1f, Distance);
            // keep near as large as possible, far as tight as possible
            float pad = MathF.Max(1f, sceneExtent * 1.5f);
            zNear = MathF.Max(0.05f, dist - pad);
            zFar  = MathF.Max(zNear * 2f, dist + pad);
        }

        // NEW: scale factor so a sphere with baseWorldRadius shows at least minPixels on screen.
        // baseWorldRadius = BoundingRadiusLocal * Max(baseScale.X, baseScale.Y, baseScale.Z)
        // add a small deadzone so factor stays 1 near threshold and avoids flicker
        public float GetMinScreenScaleForSphere(float baseWorldRadius, int viewportHeight, float minPixels, float maxScale = 1e6f, float deadzone = 0.03f)
        {
            if (baseWorldRadius <= 0f || viewportHeight <= 0) return 1f;
            float k = MathF.Tan(FovY * 0.5f);
            float s = (minPixels * Distance * k) / (baseWorldRadius * viewportHeight);
            if (s < 1f + deadzone) return 1f; // grow-only with deadzone near 1x
            return MathHelper.Clamp(s, 1f, maxScale);
        }

        // NEW: responsive line width (in pixels) that grows with distance.
        // dNear/dFar are thresholds where width lerps from minPx to maxPx.
        public float GetResponsiveLineWidth(float dNear, float dFar, float minPx = 1.0f, float maxPx = 3.0f)
        {
            if (dFar <= dNear) return minPx;
            float t = (Distance - dNear) / (dFar - dNear);
            t = MathHelper.Clamp(t, 0f, 1f);
            return minPx + (maxPx - minPx) * t;
        }
    }
}
