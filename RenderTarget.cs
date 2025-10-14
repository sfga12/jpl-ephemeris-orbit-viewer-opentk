using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JplEphemerisOrbitViewer
{
    public sealed class RenderTarget : IDisposable
    {
        public int Fbo { get; private set; }
        public int ColorTex { get; private set; }
        public int DepthRb { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public RenderTarget(int w, int h) => Resize(w, h);

        public void Resize(int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);

            // Eski kaynakları sil
            if (ColorTex != 0) GL.DeleteTexture(ColorTex);
            if (DepthRb != 0) GL.DeleteRenderbuffer(DepthRb);
            if (Fbo != 0) GL.DeleteFramebuffer(Fbo);

            Width = w; Height = h;

            // Color texture
            ColorTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, ColorTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Depth-stencil (yalnız depth yeter)
            DepthRb = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthRb);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, w, h);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            // FBO
            Fbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTex, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, DepthRb);
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new Exception($"FBO incomplete: {status}");
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void Dispose()
        {
            if (ColorTex != 0) GL.DeleteTexture(ColorTex);
            if (DepthRb != 0) GL.DeleteRenderbuffer(DepthRb);
            if (Fbo != 0) GL.DeleteFramebuffer(Fbo);
            ColorTex = DepthRb = Fbo = 0;
        }
    }

}
