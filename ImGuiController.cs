// ImGuiController.cs
// NuGet: ImGui.NET
using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

public sealed class ImGuiController : IDisposable
{
    private readonly GameWindow _window;

    private int _vertexArray;
    private int _vertexBuffer;
    private int _indexBuffer;
    private int _fontTexture;
    private int _shader;
    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationVtxPos;
    private int _attribLocationVtxUV;
    private int _attribLocationVtxColor;

    private int _windowWidth;
    private int _windowHeight;
    private bool _frameBegun;

    const int SizeOfImDrawVert = 20; // vec2 pos (8) + vec2 uv (8) + u32 col (4)
    const int OffsetPos = 0;
    const int OffsetUV = 8;
    const int OffsetColor = 16;

    public ImGuiController(GameWindow window, int width, int height)
    {
        _window = window;
        _windowWidth = width;
        _windowHeight = height;

        // --- ImGui Context ---
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        io.Fonts.AddFontDefault();
        io.DisplaySize = new System.Numerics.Vector2(_windowWidth, _windowHeight);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1f, 1f);

        // Text input event optional
        _window.TextInput += e => io.AddInputCharacter((uint)e.Unicode);

        CreateDeviceResources();
    }

    public void WindowResized(int width, int height)
    {
        _windowWidth = Math.Max(width, 1);
        _windowHeight = Math.Max(height, 1);
        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(_windowWidth, _windowHeight);
    }

    public void Update(double deltaSeconds)
    {
        var io = ImGui.GetIO();

        io.DeltaTime = (float)(deltaSeconds > 0 ? deltaSeconds : 1f / 60f);

        UpdateInput();

        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Render()
    {
        if (!_frameBegun) return;
        _frameBegun = false;

        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    private void UpdateInput()
    {
        var io = ImGui.GetIO();

        // Mouse
        var mouse = _window.MouseState;
        var p = mouse.Position;
        io.AddMousePosEvent((float)p.X, (float)p.Y);

        io.AddMouseButtonEvent(0, mouse.IsButtonDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, mouse.IsButtonDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, mouse.IsButtonDown(MouseButton.Middle));

        var wheel = mouse.ScrollDelta; // frame delta
        if (wheel.Y != 0 || wheel.X != 0)
            io.AddMouseWheelEvent((float)wheel.X, (float)wheel.Y);

        // Klavye (minimal)
        var kb = _window.KeyboardState;
        AddKey(io, ImGuiKey.Tab, kb.IsKeyDown(Keys.Tab));
        AddKey(io, ImGuiKey.LeftArrow, kb.IsKeyDown(Keys.Left));
        AddKey(io, ImGuiKey.RightArrow, kb.IsKeyDown(Keys.Right));
        AddKey(io, ImGuiKey.UpArrow, kb.IsKeyDown(Keys.Up));
        AddKey(io, ImGuiKey.DownArrow, kb.IsKeyDown(Keys.Down));
        AddKey(io, ImGuiKey.PageUp, kb.IsKeyDown(Keys.PageUp));
        AddKey(io, ImGuiKey.PageDown, kb.IsKeyDown(Keys.PageDown));
        AddKey(io, ImGuiKey.Home, kb.IsKeyDown(Keys.Home));
        AddKey(io, ImGuiKey.End, kb.IsKeyDown(Keys.End));
        AddKey(io, ImGuiKey.Insert, kb.IsKeyDown(Keys.Insert));
        AddKey(io, ImGuiKey.Delete, kb.IsKeyDown(Keys.Delete));
        AddKey(io, ImGuiKey.Backspace, kb.IsKeyDown(Keys.Backspace));
        AddKey(io, ImGuiKey.Space, kb.IsKeyDown(Keys.Space));
        AddKey(io, ImGuiKey.Enter, kb.IsKeyDown(Keys.Enter));
        AddKey(io, ImGuiKey.Escape, kb.IsKeyDown(Keys.Escape));
        AddKey(io, ImGuiKey.LeftCtrl, kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl));
        AddKey(io, ImGuiKey.LeftShift, kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift));
        AddKey(io, ImGuiKey.LeftAlt, kb.IsKeyDown(Keys.LeftAlt) || kb.IsKeyDown(Keys.RightAlt));
        AddKey(io, ImGuiKey.LeftSuper, kb.IsKeyDown(Keys.LeftSuper) || kb.IsKeyDown(Keys.RightSuper));
    }

    private static void AddKey(ImGuiIOPtr io, ImGuiKey key, bool down) => io.AddKeyEvent(key, down);

    private void CreateDeviceResources()
    {
        // VAO + VBO + EBO
        _vertexArray = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        _indexBuffer = GL.GenBuffer();

        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);

        // Shader
        const string vertexSource = @"#version 330 core
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
layout (location = 2) in vec4 Color;

uniform mat4 ProjMtx;

out vec2 Frag_UV;
out vec4 Frag_Color;

void main()
{
    Frag_UV = UV;
    Frag_Color = Color;
    gl_Position = ProjMtx * vec4(Position.xy, 0.0, 1.0);
}";
        const string fragmentSource = @"#version 330 core
in vec2 Frag_UV;
in vec4 Frag_Color;

uniform sampler2D Texture;

out vec4 Out_Color;

void main()
{
    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
}";

        int vert = CompileShader(ShaderType.VertexShader, vertexSource);
        int frag = CompileShader(ShaderType.FragmentShader, fragmentSource);
        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, vert);
        GL.AttachShader(_shader, frag);
        GL.LinkProgram(_shader);
        GL.DetachShader(_shader, vert);
        GL.DetachShader(_shader, frag);
        GL.DeleteShader(vert);
        GL.DeleteShader(frag);

        _attribLocationTex = GL.GetUniformLocation(_shader, "Texture");
        _attribLocationProjMtx = GL.GetUniformLocation(_shader, "ProjMtx");
        _attribLocationVtxPos = 0;
        _attribLocationVtxUV = 1;
        _attribLocationVtxColor = 2;

        GL.EnableVertexAttribArray(_attribLocationVtxPos);
        GL.EnableVertexAttribArray(_attribLocationVtxUV);
        GL.EnableVertexAttribArray(_attribLocationVtxColor);

        GL.VertexAttribPointer(_attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, SizeOfImDrawVert, OffsetPos);
        GL.VertexAttribPointer(_attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, SizeOfImDrawVert, OffsetUV);
        GL.VertexAttribPointer(_attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, SizeOfImDrawVert, OffsetColor);

        CreateFontsTexture();
        GL.BindVertexArray(0);
    }

    private static int CompileShader(ShaderType type, string src)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetShaderInfoLog(s);
            GL.DeleteShader(s);
            throw new Exception($"{type} compile error:\n{log}");
        }
        return s;
    }

    private void CreateFontsTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                      PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }


    private void RenderImDrawData(ImDrawDataPtr drawData)
    {
        int fbW = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbH = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbW <= 0 || fbH <= 0) return;

        // --- BACKUP (kritik) ---
        GL.GetInteger(GetPName.CurrentProgram, out int lastProgram);
        GL.GetInteger(GetPName.ArrayBufferBinding, out int lastArrayBuffer);
        GL.GetInteger(GetPName.ElementArrayBufferBinding, out int lastElementArray);
        GL.GetInteger(GetPName.VertexArrayBinding, out int lastVertexArray);
        GL.GetInteger(GetPName.ActiveTexture, out int lastActiveTex);
        bool lastBlend = GL.IsEnabled(EnableCap.Blend);
        bool lastCull = GL.IsEnabled(EnableCap.CullFace);
        bool lastDepth = GL.IsEnabled(EnableCap.DepthTest);
        bool lastSciss = GL.IsEnabled(EnableCap.ScissorTest);
        int[] lastScissorBox = new int[4];
        GL.GetInteger(GetPName.ScissorBox, lastScissorBox);

        // ImGui’nin istediği state
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindSampler(0, 0); // olası sampler objeleri etkisiz

        GL.Viewport(0, 0, fbW, fbH);
        var ortho = Matrix4.CreateOrthographicOffCenter(0f, drawData.DisplaySize.X, drawData.DisplaySize.Y, 0f, -1f, 1f);

        GL.UseProgram(_shader);
        GL.Uniform1(_attribLocationTex, 0);
        GL.UniformMatrix4(_attribLocationProjMtx, false, ref ortho);

        GL.BindVertexArray(_vertexArray);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer,
                cmdList.VtxBuffer.Size * SizeOfImDrawVert, cmdList.VtxBuffer.Data,
                BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                cmdList.IdxBuffer.Size * sizeof(ushort), cmdList.IdxBuffer.Data,
                BufferUsageHint.StreamDraw);

            int idxOffset = 0;
            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var pcmd = cmdList.CmdBuffer[i];

                GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);

                var clip = pcmd.ClipRect;
                var clipOff = drawData.DisplayPos;
                var clipScale = drawData.FramebufferScale;

                // Correct scissor: compute min/max first, then convert to GL coords
                float clipMinX = (clip.X - clipOff.X) * clipScale.X;
                float clipMinY = (clip.Y - clipOff.Y) * clipScale.Y;
                float clipMaxX = (clip.Z - clipOff.X) * clipScale.X;
                float clipMaxY = (clip.W - clipOff.Y) * clipScale.Y;

                int scX = (int)clipMinX;
                int scY = (int)(fbH - clipMaxY);
                int scW = (int)(clipMaxX - clipMinX);
                int scH = (int)(clipMaxY - clipMinY);

                if (scW <= 0 || scH <= 0)
                {
                    idxOffset += (int)pcmd.ElemCount;
                    continue;
                }

                GL.Scissor(scX, scY, scW, scH);

                GL.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (int)pcmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(idxOffset * sizeof(ushort)),
                    (int)pcmd.VtxOffset);

                idxOffset += (int)pcmd.ElemCount;
            }
        }

        // --- RESTORE (kritik) ---
        if (lastBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
        if (lastCull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        if (lastDepth) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        if (lastSciss) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
        GL.Scissor(lastScissorBox[0], lastScissorBox[1], lastScissorBox[2], lastScissorBox[3]);
        GL.ActiveTexture((TextureUnit)lastActiveTex);
        GL.BindVertexArray(lastVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, lastElementArray);
        GL.UseProgram(lastProgram);
    }


    public void Dispose()
    {
        if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
        if (_vertexBuffer != 0) GL.DeleteBuffer(_vertexBuffer);
        if (_indexBuffer != 0) GL.DeleteBuffer(_indexBuffer);
        if (_vertexArray != 0) GL.DeleteVertexArray(_vertexArray);
        if (_shader != 0) GL.DeleteProgram(_shader);

        ImGui.DestroyContext();
    }
}
