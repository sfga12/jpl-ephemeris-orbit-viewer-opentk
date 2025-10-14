using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace JplEphemerisOrbitViewer
{
    public sealed class OrbitLineRenderer : IDisposable
    {
        private int _vao;
        private int _vbo;
        private int _count;

        public OrbitLineRenderer()
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.EnableVertexAttribArray(0); // aPosition
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        public void UpdateFromTrack(EphemerisTrack tr, float unitsPerAU)
        {
            if (tr.Count == 0) { _count = 0; return; }

            var data = new List<float>(tr.Count * 3);
            for (int i = 0; i < tr.Count; i++)
            {
                var p = tr.ToUnits(i, unitsPerAU);
                data.Add(p.X); data.Add(p.Y); data.Add(p.Z);
            }

            _count = tr.Count;

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _count * 3 * sizeof(float), data.ToArray(), BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        public void Draw(Shader shader, Matrix4 view, Matrix4 projection, Vector3 color, float lineWidth = 1.5f)
        {
            if (_count <= 1) return;

            shader.Use();
            var model = Matrix4.Identity;
            shader.SetMatrix4("model", model);
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", projection);
            shader.SetVector3("color", color);
            shader.SetInt("uHasTex", 0); // solid line

            GL.LineWidth(lineWidth);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, _count);
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