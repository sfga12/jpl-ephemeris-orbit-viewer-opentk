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

            // --- Zoom (wheel ) ---
            float scroll = mouse.ScrollDelta.Y;
            if (scroll != 0f)
            {
                float desired = Distance * MathF.Pow(ZoomFactor, -scroll);
                Distance = MathF.Max(minDistance, desired);
            }

            // --- Orbit (RMB drag) ---
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

            
            if (mouse.IsButtonDown(MouseButton.Middle))
            {

            }
        }
    }
}
