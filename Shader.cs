using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.IO;

namespace JplEphemerisOrbitViewer
{
    public class Shader : IDisposable
    {
        private readonly int _handle;

        public Shader(string vertexPath, string fragmentPath)
        {
            string vsSource = File.ReadAllText(vertexPath);
            string fsSource = File.ReadAllText(fragmentPath);

            int vs = CompileShader(vsSource, ShaderType.VertexShader, vertexPath);
            int fs = CompileShader(fsSource, ShaderType.FragmentShader, fragmentPath);

            _handle = GL.CreateProgram();
            GL.AttachShader(_handle, vs);
            GL.AttachShader(_handle, fs);
            GL.LinkProgram(_handle);

            GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string log = GL.GetProgramInfoLog(_handle);
                Console.WriteLine($"PROGRAM LINK ERROR:\n{log}");
            }

            GL.DetachShader(_handle, vs);
            GL.DetachShader(_handle, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        private static int CompileShader(string source, ShaderType type, string path)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                Console.WriteLine($"{type} COMPILE ERROR in [{Path.GetFileName(path)}]:\n{log}");
            }
            return shader;
        }

        public void Use() => GL.UseProgram(_handle);

        // 🔹 Matrix uniform
        public void SetMatrix4(string name, Matrix4 mat)
        {
            int loc = GL.GetUniformLocation(_handle, name);
            if (loc != -1)
                GL.UniformMatrix4(loc, false, ref mat);
        }

        // 🔹 Int uniform
        public void SetInt(string name, int value)
        {
            int loc = GL.GetUniformLocation(_handle, name);
            if (loc != -1)
                GL.Uniform1(loc, value);
        }

        // 🔹 Float uniform
        public void SetFloat(string name, float value)
        {
            int loc = GL.GetUniformLocation(_handle, name);
            if (loc != -1)
                GL.Uniform1(loc, value);
        }

        // 🔹 Vector3 uniform
        public void SetVector3(string name, Vector3 vec)
        {
            int loc = GL.GetUniformLocation(_handle, name);
            if (loc == -1)
            {
                Console.WriteLine($"[Shader] Uniform '{name}' not found. Verify you are loading {Path.GetFullPath("../../../shader.frag")}");
                return;
            }
            GL.Uniform3(loc, vec);
        }

        public void Dispose() => GL.DeleteProgram(_handle);
    }
}
