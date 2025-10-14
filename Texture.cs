using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using System;

namespace JplEphemerisOrbitViewer
{
    public class Texture
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
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1); // for some images

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
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }

        public void Use(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, _handle);
        }
    }
}
