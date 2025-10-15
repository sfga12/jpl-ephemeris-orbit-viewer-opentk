using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JplEphemerisOrbitViewer
{
    // Simple 3D line (two points) renderer using the main Shader
    public sealed class DistanceLineRenderer
    {
        private int _vao;
        private int _vbo;
        private Vector3 _a, _b;
        private bool _dirty = true;

        public DistanceLineRenderer()
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            // 2 vertices (a,b), 3 floats each
            GL.BufferData(BufferTarget.ArrayBuffer, 6 * sizeof(float), System.IntPtr.Zero, BufferUsageHint.DynamicDraw);

            GL.EnableVertexAttribArray(0); // assuming location=0 for position in Shader
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        public void UpdateEndpoints(in Vector3 a, in Vector3 b)
        {
            if (a == _a && b == _b) return;
            _a = a; _b = b; _dirty = true;
        }

        public void Draw(Shader shader, Matrix4 view, Matrix4 projection, in Vector3 color, float lineWidthPx)
        {
            if (_dirty)
            {
                float[] verts = { _a.X, _a.Y, _a.Z, _b.X, _b.Y, _b.Z };
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
                GL.BufferSubData(BufferTarget.ArrayBuffer, System.IntPtr.Zero, verts.Length * sizeof(float), verts);
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                _dirty = false;
            }

            GL.LineWidth(lineWidthPx);
            shader.Use();

            var model = Matrix4.Identity;
            shader.SetMatrix4("model", model);
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", projection);
            shader.SetVector3("color", color);
            shader.SetInt("uHasTex", 0); // solid color

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_vbo != 0) GL.DeleteBuffer(_vbo);
            if (_vao != 0) GL.DeleteVertexArray(_vao);
            _vbo = _vao = 0;
        }
    }
}