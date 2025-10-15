using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using System;

namespace JplEphemerisOrbitViewer
{
    public class Texture : IDisposable
    {
        private int _handle;

        public Texture(string path)
        {
            if (!File.Exists(path))
                Console.WriteLine($"Texture not found: {Path.GetFullPath(path)}");
            else
                Console.WriteLine($"Texture loaded: {path}");

            _handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _handle);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.Default);

            PixelFormat format = image.Comp switch
            {
                ColorComponents.Grey => PixelFormat.Red,
                ColorComponents.GreyAlpha => PixelFormat.Rg,
                ColorComponents.RedGreenBlue => PixelFormat.Rgb,
                _ => PixelFormat.Rgba
            };

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                image.Width, image.Height, 0,
                format, PixelType.UnsignedByte, image.Data);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }

        // Safe: build from raw RGBA pixels (no unsafe)
        public Texture(int width, int height, byte[] rgba)
        {
            _handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _handle);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        // Convenience factory
        public static Texture CreateSolidColor(int width, int height, byte r, byte g, byte b, byte a)
        {
            var data = new byte[width * height * 4];
            for (int i = 0; i < data.Length; i += 4)
            {
                data[i + 0] = r;
                data[i + 1] = g;
                data[i + 2] = b;
                data[i + 3] = a;
            }
            return new Texture(width, height, data);
        }

        public void Use(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, _handle);
        }

        public void Dispose()
        {
            if (_handle != 0)
            {
                GL.DeleteTexture(_handle);
                _handle = 0;
            }
        }
    }
}
