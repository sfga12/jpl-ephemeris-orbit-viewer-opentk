using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4; 
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.Globalization;
using JplEphemerisOrbitViewer.Horizons;
using System.Diagnostics;
using NativeFileDialogSharp;

namespace JplEphemerisOrbitViewer
{
    internal class Scene : GameWindow
    {
        private Shader _shader = null!;
        private readonly List<SceneObject> _objects = new();
        private ImGuiController _imgui = null!;
        private ScenePicker _picker = null!;
        private RenderTarget _rt = null!;
        private TargetOrbitCamera _cam = new();
        private SceneObject? _selected;

       
        private string? _sceneCenter; // enforced center body name
        private Mesh? _sphereMesh;
        private string _importPath = "";
        private string _importError = "";

        // Scales (decoupled)
        private float _distUnitsPerAU = 500f;
        private float _radiiUnitsPerKm = 0.0001f;
        private float _minRadiusUnits = 0.01f;

        private const double KmPerAU = 149_597_870.7;

        private SceneObject? _centerObject;

     
        private readonly Dictionary<SceneObject, EphemerisTrack> _tracks = new();
        private readonly Dictionary<SceneObject, OrbitLineRenderer> _orbits = new();

  
        private readonly Dictionary<SceneObject, Vector3> _orbitColors = new();
        private readonly Dictionary<SceneObject, Vector3> _baseScales = new();

        private DateTime? _sceneStartUtc;
        private DateTime? _sceneEndUtc;
        private TimeSpan _sceneStep = TimeSpan.Zero;

        private DateTime _simTimeUtc;         // current simulation time (UTC)
        private bool _isPlaying = false;
        private bool _loopPlayback = true;
        private float _speedStepsPerSec = 1.0f;  // how many ephemeris steps per real second
        private bool _drawOrbits = true;
        private float _orbitLineWidth = 1.5f;
        private Vector3 _orbitColor = new(0.25f, 0.6f, 1.0f);

        private float _lastOrbitUnitsPerAU = -1f;
        private float _orbitLineWidthPx = 1.5f;

        public Scene(int width, int height)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title = "JPL Ephemeris Orbit Viewer"
            })
        { }

        protected override void OnLoad()
        {
            base.OnLoad();
            _rt = new RenderTarget(800, 450);

            _cam.Distance = 50f;
            _cam.Yaw = MathHelper.DegreesToRadians(35f);
            _cam.Pitch = MathHelper.DegreesToRadians(15f);

            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            _imgui = new ImGuiController(this, Size.X, Size.Y);

            // Shader (solid color + optional texture)
            _shader = new Shader("../../../shader.vert", "../../../shader.frag");

            // Picker
            _picker = new ScenePicker(
                () => Size,
                () => MousePosition,
                () => MouseState
            );

            // Prebuild unit sphere mesh
            SphereBuilder.Create(32, 64, out var v, out var idx);
            _sphereMesh = new Mesh(v, idx);

            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
            _imgui.WindowResized(Size.X, Size.Y);
            ImGui.StyleColorsDark();

            var style = ImGui.GetStyle();
            var colors = style.Colors;
            colors[(int)ImGuiCol.Text] = new System.Numerics.Vector4(1f, 1f, 1f, 1f);
            colors[(int)ImGuiCol.TextDisabled] = new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f);
            colors[(int)ImGuiCol.MenuBarBg] = new System.Numerics.Vector4(0.14f, 0.14f, 0.14f, 1f);

            _selected = null;
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            _imgui.Update(args.Time);

            // Menu
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("Exit")) Close();
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Edit"))
                {
                    ImGui.MenuItem("Copy", "Ctrl+C");
                    ImGui.MenuItem("Paste", "Ctrl+V");
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }

            float pad = 12f, leftPanelW = 420f, rightPanelW = 320f;
            var vpMain = ImGui.GetMainViewport();

            // LEFT: Objects + Import + Playback
            ImGui.SetNextWindowPos(vpMain.WorkPos + new System.Numerics.Vector2(pad, pad), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(leftPanelW, vpMain.WorkSize.Y - 2 * pad), ImGuiCond.Always);
            ImGui.Begin("Objects",
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);

            ImGui.Text("Add from Horizons .txt");
            ImGui.InputText("Path", ref _importPath, 512);
            if (ImGui.Button("Add object from file"))
            {
                TryAddObjectFromHorizons(_importPath);
            }
            ImGui.SameLine();
            if (ImGui.Button("Use sample path"))
            {
                _importPath = Path.GetFullPath("../../../Downloads/horizons_results.txt");
            }
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                var res = NativeFileDialogSharp.Dialog.FileOpen("txt");
                if (res.IsOk && !string.IsNullOrWhiteSpace(res.Path) && File.Exists(res.Path))
                {
                    _importPath = res.Path;
                    TryAddObjectFromHorizons(_importPath);
                }
            }

            if (!string.IsNullOrEmpty(_sceneCenter))
                ImGui.Text($"Scene Center: {_sceneCenter}");
            if (!string.IsNullOrEmpty(_importError))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1, 0.4f, 0.4f, 1));
                ImGui.TextWrapped(_importError);
                ImGui.PopStyleColor();
            }

            ImGui.Separator();
            ImGui.Text("Playback");
            ImGui.Separator();

            if (_sceneStartUtc.HasValue && _sceneEndUtc.HasValue)
            {
                var start = _sceneStartUtc.Value;
                var end = _sceneEndUtc.Value;
                var step = _sceneStep;

                ImGui.Text($"Start: {start:yyyy-MM-dd HH:mm:ss}  End: {end:yyyy-MM-dd HH:mm:ss}");
                ImGui.Text($"Step:  {step}");
                ImGui.Text($"Now:   {_simTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");

                if (ImGui.Button(_isPlaying ? "Pause" : "Play"))
                    _isPlaying = !_isPlaying;
                ImGui.SameLine();
                if (ImGui.Button("Stop"))
                {
                    _isPlaying = false;
                    _simTimeUtc = start;
                }
                ImGui.SameLine();
                if (ImGui.Button("Prev"))
                    _simTimeUtc = ClampTime(_simTimeUtc - step);
                ImGui.SameLine();
                if (ImGui.Button("Next"))
                    _simTimeUtc = ClampTime(_simTimeUtc + step);

                ImGui.Checkbox("Loop", ref _loopPlayback);
                ImGui.SameLine();
                ImGui.Checkbox("Draw orbits", ref _drawOrbits);

                ImGui.SliderFloat("Speed (steps/s)", ref _speedStepsPerSec, 0.0f, 50.0f);

                // Scrub timeline
                double totalSecs = Math.Max((end - start).TotalSeconds, 1);
                double posSecs = Math.Clamp((_simTimeUtc - start).TotalSeconds, 0, totalSecs);
                float t01 = (float)(posSecs / totalSecs);
                if (ImGui.SliderFloat("Timeline", ref t01, 0f, 1f))
                {
                    _simTimeUtc = start + TimeSpan.FromSeconds(t01 * totalSecs);
                }

                // Advance time
                if (_isPlaying)
                {
                    var dt = (float)args.Time;
                    double advanceSecs = _sceneStep.TotalSeconds * _speedStepsPerSec * dt;
                    _simTimeUtc = _simTimeUtc + TimeSpan.FromSeconds(advanceSecs);
                    if (_simTimeUtc > end)
                    {
                        _simTimeUtc = _loopPlayback ? start : end;
                        if (!_loopPlayback) _isPlaying = false;
                    }
                }
            }
            else
            {
                ImGui.Text("Import objects with Horizons data to enable playback.");
            }

            ImGui.Separator();
            ImGui.Text("Scale (distance vs radius)");
            ImGui.Separator();

            ImGui.DragFloat("Units per AU", ref _distUnitsPerAU, 1f, 50f, 5000f);
            ImGui.DragFloat("Radii units per km", ref _radiiUnitsPerKm, 1e-6f, 1e-6f, 0.01f, "%.6f");
            ImGui.DragFloat("Min radius (units)", ref _minRadiusUnits, 0.01f, 0.01f, 20f);

            if (Math.Abs(_lastOrbitUnitsPerAU - _distUnitsPerAU) > 1e-6f)
            {
                RebuildAllOrbits();
                _lastOrbitUnitsPerAU = _distUnitsPerAU;
            }

            ImGui.Separator();
            ImGui.Text("Objects");
            ImGui.Separator();

            ImGui.BeginChild("ObjectsList", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.None);
            for (int i = 0; i < _objects.Count; i++)
            {
                var o = _objects[i];
                bool isSelected = ReferenceEquals(_selected, o) || ReferenceEquals(_picker.Selected, o);

                ImGui.PushID(i);
                if (ImGui.Selectable(o.Name, isSelected))
                {
                    _selected = o;
                    _picker.SetSelected(o);
                }
                if (ImGui.IsItemHovered())
                    _picker.SetHovered(o);
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    FrameOnObject(o);
                ImGui.PopID();
            }
            ImGui.EndChild();
            ImGui.End();

            // RIGHT: Inspector
            ImGui.SetNextWindowPos(
                new System.Numerics.Vector2(vpMain.WorkPos.X + vpMain.WorkSize.X - pad - rightPanelW, vpMain.WorkPos.Y + pad),
                ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(rightPanelW, vpMain.WorkSize.Y - 2 * pad), ImGuiCond.Always);
            ImGui.Begin("Inspector",
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
            if (_selected != null)
            {
                ImGui.Separator();
                ImGui.Text($"Name:     {_selected.Name}");
                ImGui.Text($"Position: {_selected.Position.X:F3}, {_selected.Position.Y:F3}, {_selected.Position.Z:F3}");
                ImGui.Text($"Scale (units): {_selected.Scale.X:F3}, {_selected.Scale.Y:F3}, {_selected.Scale.Z:F3}");

                ImGui.Separator();
                bool isCenter = ReferenceEquals(_selected, _centerObject);
                ImGui.BeginDisabled(isCenter);
                if (ImGui.Button("Delete Object"))
                {
                    DeleteSelectedObject();
                }
                ImGui.EndDisabled();
                if (isCenter)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("(center cannot be deleted)");
                }

                if (_orbits.ContainsKey(_selected))
                {
                    var cur = _orbitColors.TryGetValue(_selected, out var c) ? c : _orbitColor;
                    var colorSys = new System.Numerics.Vector3(cur.X, cur.Y, cur.Z);
                    if (ImGui.ColorEdit3("Orbit Color", ref colorSys))
                    {
                        _orbitColors[_selected] = new Vector3(colorSys.X, colorSys.Y, colorSys.Z);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Reset Orbit Color"))
                    {
                        _orbitColors[_selected] = _orbitColor;
                    }
                }
            }
            else
            {
                ImGui.Text("No selection");
            }
            ImGui.End();

            // CENTER: Viewport
            float viewX = vpMain.WorkPos.X + leftPanelW + 2 * pad;
            float viewY = vpMain.WorkPos.Y + pad;
            float viewWf = MathF.Max(1, vpMain.WorkSize.X - leftPanelW - rightPanelW - 4 * pad);
            float viewHf = MathF.Max(1, vpMain.WorkSize.Y - 2 * pad);

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(viewX, viewY), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(viewWf, viewHf), ImGuiCond.Always);
            ImGui.Begin("Viewport",
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar);

            var avail = ImGui.GetContentRegionAvail();
            int rtW = Math.Max(1, (int)avail.X);
            int rtH = Math.Max(1, (int)avail.Y);
            if (_rt.Width != rtW || _rt.Height != rtH) _rt.Resize(rtW, rtH);

            var contentMin = ImGui.GetCursorScreenPos();
            var contentMax = contentMin + new System.Numerics.Vector2(avail.X, avail.Y);
            var mp = ImGui.GetMousePos();
            bool allowCamInput =
                (mp.X >= contentMin.X && mp.X <= contentMax.X &&
                 mp.Y >= contentMin.Y && mp.Y <= contentMax.Y);

            if (_selected == null)
                _selected = _picker.Selected ?? (_objects.FirstOrDefault());

            Vector3 target = _selected?.Position ?? Vector3.Zero;
            float radius = (_selected?.BoundingRadiusLocal ?? 1f) * Max3(_selected?.Scale ?? Vector3.One);
            float minDist = MathF.Max(0.1f, radius * 1.05f);

            _cam.Distance = MathF.Max(_cam.Distance, minDist + 0.001f);
            _cam.UpdateInput(MouseState, allowCamInput, (float)args.Time, target, minDist);

            foreach (var (obj, tr) in _tracks)
                obj.Position = tr.Evaluate(_simTimeUtc, _distUnitsPerAU);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _rt.Fbo);
            GL.Viewport(0, 0, _rt.Width, _rt.Height);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.Blend);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Responsive 
            UpdateResponsiveSizes();

            var view = _cam.GetViewMatrix(target);

            
            _cam.GetClipPlanes(sceneExtent: _cam.Distance, out float zNear, out float zFar);

            var projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f),
                (float)_rt.Width / _rt.Height, zNear, zFar);

            if (_drawOrbits)
            {
                
                GL.LineWidth(_orbitLineWidthPx);
                foreach (var (obj, orbit) in _orbits)
                {
                    var col = _orbitColors.TryGetValue(obj, out var c) ? c : _orbitColor;
                    orbit.Draw(_shader, view, projection, col, _orbitLineWidthPx); 
                }
                GL.LineWidth(1.0f);
            }

            foreach (var o in _objects)
            {
                o.Update((float)args.Time);
                o.Render(view, projection);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            ImGui.Image((IntPtr)_rt.ColorTex,
                new System.Numerics.Vector2(rtW, rtH),
                new System.Numerics.Vector2(0, 1),
                new System.Numerics.Vector2(1, 0));

            var imgMin = ImGui.GetItemRectMin();
            var imgMax = ImGui.GetItemRectMax();
            var imgSize = imgMax - imgMin;

            _picker.SetImageRectProviders(
                () => imgMin,
                () => imgSize,
                () => ImGui.GetMousePos()
            );
            if (ImGui.IsItemHovered())
                _picker.Update(view, projection, _objects);

            if (_picker.Selected != null)
                _selected = _picker.Selected;

            ImGui.End(); // Viewport

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
            GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _imgui.Render();
            SwapBuffers();
        }

        private void TryAddObjectFromHorizons(string path)
        {
            Console.WriteLine($"[Scene] TryAddObjectFromHorizons called with: '{path}'");
            Debug.WriteLine($"[Scene] TryAddObjectFromHorizons called with: '{path}'");

            _importError = "";
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    _importError = "File not found.";
                    return;
                }

                var eph = JplEphemerisOrbitViewer.Horizons.HorizonsParser.ParseFile(path);
                var track = EphemerisTrack.FromHorizons(eph);

                if (_sceneStartUtc == null)
                {
                    _sceneStartUtc = track.StartUtc;
                    _sceneEndUtc = track.EndUtc;
                    _sceneStep = track.Step;
                    _simTimeUtc = track.StartUtc;
                }
                else
                {
                    if (track.StartUtc != _sceneStartUtc.Value || track.EndUtc != _sceneEndUtc!.Value)
                    {
                        _importError = $"Date range mismatch. Scene [{_sceneStartUtc:yyyy-MM-dd HH:mm:ss} .. {_sceneEndUtc:yyyy-MM-dd HH:mm:ss}], file [{track.StartUtc:yyyy-MM-dd HH:mm:ss} .. {track.EndUtc:yyyy-MM-dd HH:mm:ss}]";
                        return;
                    }
                    if (Math.Abs((track.Step - _sceneStep).TotalSeconds) > 1.0)
                    {
                        _importError = $"Step mismatch. Scene step={_sceneStep}, file step={track.Step}";
                        return;
                    }
                }

                if (_sceneCenter == null)
                {
                    _sceneCenter = eph.CenterName;
                }
                else if (!string.Equals(_sceneCenter, eph.CenterName, StringComparison.OrdinalIgnoreCase))
                {
                    _importError = $"Center mismatch: scene center is '{_sceneCenter}' but file center is '{eph.CenterName}'. Object not added.";
                    return;
                }

               
                if (_centerObject == null)
                {
                    var centerScale = ToRadiusUnits(eph.CenterRadiiKmABC);
                    var centerTex = CreateTextureForBody(eph.CenterName);
                    _centerObject = new SceneObject(_sphereMesh!, _shader, texture: centerTex)
                    {
                        Name = eph.CenterName,
                        Position = Vector3.Zero,
                        Scale = centerScale,
                        BoundsCenterLocal = Vector3.Zero,
                        BoundingRadiusLocal = 1f,
                        Color = Vector3.One,
                    };
                    _objects.Add(_centerObject);
                    _baseScales[_centerObject] = _centerObject.Scale; // NEW
                }

                if (track.Count == 0)
                {
                    _importError = $"No ephemeris rows found. Parsed rows=0. File: {Path.GetFullPath(path)}";
                    return;
                }

                var pos = track.Evaluate(_simTimeUtc, _distUnitsPerAU);

                var targetScale = ToRadiusUnits(eph.TargetRadiiKmABC);
                var targetTex = CreateTextureForBody(eph.TargetName);

                
                var go = new SceneObject(_sphereMesh!, _shader, texture: targetTex)
                {
                    Name = eph.TargetName,
                    Position = pos,
                    Scale = targetScale,
                    BoundsCenterLocal = Vector3.Zero,
                    BoundingRadiusLocal = 1f,
                    Color = new Vector3(0.85f, 0.9f, 1.0f)
                };

                _objects.Add(go);
                _baseScales[go] = go.Scale; // NEW
                _selected = go;
                _picker.SetSelected(go);

               
                _tracks[go] = track; 

                var orbit = new OrbitLineRenderer();
                orbit.UpdateFromTrack(track, _distUnitsPerAU);
                _orbits[go] = orbit;
                _lastOrbitUnitsPerAU = _distUnitsPerAU;
                _orbitColors[go] = _orbitColor;
            }
            catch (Exception ex)
            {
                _importError = $"Import error: {ex.Message}";
                Console.WriteLine(ex);
            }
        }

        private void RebuildAllOrbits()
        {
            foreach (var (go, tr) in _tracks)
            {
                if (_orbits.TryGetValue(go, out var orbit))
                {
                    orbit.UpdateFromTrack(tr, _distUnitsPerAU);
                }
            }
        }

        private DateTime ClampTime(DateTime t)
        {
            if (!_sceneStartUtc.HasValue || !_sceneEndUtc.HasValue) return t;
            if (t < _sceneStartUtc.Value) return _sceneStartUtc.Value;
            if (t > _sceneEndUtc.Value) return _sceneEndUtc.Value;
            return t;
        }

        private static float Max3(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));

        
        private void UpdateResponsiveSizes()
        {
            // Viewport yüksekliğini al
            int[] vp = new int[4];
            GL.GetInteger(GetPName.Viewport, vp);
            int viewportH = Math.Max(1, vp[3]);

            
            _cam.FovY = MathHelper.DegreesToRadians(45f);

            
            const float minPixels = 6f;     
            const float maxScale  = 1e6f;    
            foreach (var o in _objects)
            {
                if (!_baseScales.TryGetValue(o, out var baseScale))
                    baseScale = o.Scale;

                float baseRadius = o.BoundingRadiusLocal * Max3(baseScale);
                float factor = _cam.GetMinScreenScaleForSphere(baseRadius, viewportH, minPixels, maxScale);
                o.Scale = baseScale * factor;
            }

           
            _orbitLineWidthPx = _cam.GetResponsiveLineWidth(
                dNear: 100f,       
                dFar:  100000f,    
                minPx: 1.25f,
                maxPx: 3.0f);
        }

       
        private void FrameOnObject(SceneObject o)
        {
            float baseRadius = o.BoundingRadiusLocal * MathF.Max(o.Scale.X, MathF.Max(o.Scale.Y, o.Scale.Z));
            float fovY = MathHelper.DegreesToRadians(45f);
            float desired = MathF.Max(0.1f, baseRadius / MathF.Tan(fovY * 0.5f) * 1.2f);
            _cam.Distance = desired;
            _cam.FovY = fovY;            
            _selected = o;

            UpdateResponsiveSizes();     
        }

        protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
        {
            base.OnFramebufferResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
            _imgui.WindowResized(e.Width, e.Height);
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            foreach (var obj in _objects)
                obj.Dispose();
            foreach (var kv in _orbits)
                kv.Value.Dispose();
            _sphereMesh?.Dispose();
            _shader.Dispose();
            _imgui.Dispose();
            _rt?.Dispose();
        }

        private Vector3 ToRadiusUnits(Vector3d radiiKm)
        {
            var baseUnits = new Vector3(
                (float)(radiiKm.X * _radiiUnitsPerKm),
                (float)(radiiKm.Y * _radiiUnitsPerKm),
                (float)(radiiKm.Z * _radiiUnitsPerKm)
            );

            float maxAxis = Max3(baseUnits);
            if (maxAxis < _minRadiusUnits)
            {
                float factor = _minRadiusUnits / MathF.Max(maxAxis, 1e-6f);
                baseUnits *= factor;
            }
            return baseUnits;
        }

        private Texture? CreateTextureForBody(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            string? fileName = null;
            if (name.Contains("earth", StringComparison.OrdinalIgnoreCase))
                fileName = "EarthTexture.jpg";
            else if (name.Contains("mars", StringComparison.OrdinalIgnoreCase))
                fileName = "MarsTexture.jpg";
            else if (name.Contains("sun", StringComparison.OrdinalIgnoreCase))
                fileName = "SunTexture.jpg";
            else if (name.Contains("moon", StringComparison.OrdinalIgnoreCase))
                fileName = "MoonTexture.jpg";
            else if (name.Contains("venus", StringComparison.OrdinalIgnoreCase))
                fileName = "VenusTexture.jpg";

            if (fileName == null) return null;

            string fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Textures", fileName));
            if (!File.Exists(fullPath))
            {
                _importError = $"Texture not found: {fullPath}";
                return null;
            }

            try
            {
                return new Texture(fullPath);
            }
            catch (Exception ex)
            {
                _importError = $"Texture load failed for '{name}': {ex.Message}";
                return null;
            }
        }

        private void DeleteSelectedObject()
        {
            if (_selected == null) return;

            var toRemove = _selected;

            if (ReferenceEquals(toRemove, _centerObject))
            {
                _importError = "Cannot delete the scene center.";
                return;
            }

            if (ReferenceEquals(_picker.Selected, toRemove))
                _picker.SetSelected(null);
            if (ReferenceEquals(_picker.Hovered, toRemove))
                _picker.SetHovered(null);

            _objects.Remove(toRemove);

            if (_tracks.ContainsKey(toRemove)) _tracks.Remove(toRemove);
            if (_orbits.TryGetValue(toRemove, out var orbit))
            {
                orbit.Dispose();
                _orbits.Remove(toRemove);
            }

            _orbitColors.Remove(toRemove);

            _selected = _objects.LastOrDefault();
        }
    }
}