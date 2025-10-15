using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace JplEphemerisOrbitViewer
{
    public class SceneObject : IDisposable
    {
        public string Name { get; set; } = "Object";
        public Vector3 Position = Vector3.Zero;
        public Vector3 Rotation = Vector3.Zero;
        public Vector3 Scale = Vector3.One;

        // New: per-object color (default white)
        public Vector3 Color = Vector3.One;

        // --- Picking bounds in MODEL (local) space ---
        // If the model's pivot is not centered, this offset stores the true local center.
        public Vector3 BoundsCenterLocal = Vector3.Zero;
        // Spherical bound radius in local space (before scale).
        public float BoundingRadiusLocal = 1.0f;

        // Backward-compat alias (optional). You can remove this if not needed.
        public float BoundingRadius
        {
            get => BoundingRadiusLocal;
            set => BoundingRadiusLocal = value;
        }

        private readonly Mesh _mesh;
        private readonly Shader _shader;
        private Texture? _texture; // 1) Make this mutable (remove readonly)

        public SceneObject(Mesh mesh, Shader shader, Texture? texture = null)
        {
            _mesh = mesh;
            _shader = shader;
            _texture = texture;
        }

        // Full model matrix (S * Rz * Ry * Rx * T). Adjust order if your pipeline differs.
        public Matrix4 ModelMatrix =>
            Matrix4.CreateScale(Scale) *
            Matrix4.CreateRotationX(Rotation.X) *
            Matrix4.CreateRotationY(Rotation.Y) *
            Matrix4.CreateRotationZ(Rotation.Z) *
            Matrix4.CreateTranslation(Position);

        // World-space spherical bound computed from local center/radius, including rotation and non-uniform scale.
        public (Vector3 centerW, float radiusW) GetWorldBound()
        {
            // Use the largest axis for non-uniform scale so the sphere always encloses the mesh.
            float s = MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));

            // Apply object rotation to the local center offset, then translate and scale.
            var rot =
                Matrix4.CreateRotationX(Rotation.X) *
                Matrix4.CreateRotationY(Rotation.Y) *
                Matrix4.CreateRotationZ(Rotation.Z);

            var centerW = Position + Vector3.Transform(BoundsCenterLocal, rot.ExtractRotation()) * s;
            var radiusW = BoundingRadiusLocal * s;
            return (centerW, radiusW);
        }

        // Build local bounds (center + radius) from interleaved vertex array: [Px,Py,Pz, Tx,Ty, Nx,Ny,Nz] with stride=8.
        public void SetBoundsFromInterleavedVertices(float[] vertexData, int stride = 8)
        {
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);

            for (int i = 0; i < vertexData.Length; i += stride)
            {
                var p = new Vector3(vertexData[i + 0], vertexData[i + 1], vertexData[i + 2]);
                min = Vector3.ComponentMin(min, p);
                max = Vector3.ComponentMax(max, p);
            }

            BoundsCenterLocal = 0.5f * (min + max);
            BoundingRadiusLocal = (0.5f * (max - min)).Length;
        }

        public void Update(float deltaTime)
        {
            // If you rotate the object here, picking and orbit may appear to drift visually.
            // Disable this for stable interaction, or keep it for a spinning demo.
            // Rotation.Y += 0.3f * deltaTime;
        }

        public void Render(Matrix4 view, Matrix4 projection)
        {
            _shader.Use();

            var model = ModelMatrix;
            _shader.SetMatrix4("model", model);
            _shader.SetMatrix4("view", view);
            _shader.SetMatrix4("projection", projection);

            // Per-object color (tint). Use Vector3.One for pure texture.
            _shader.SetVector3("color", Color);

            if (_texture != null)
            {
                // Bind texture to unit 0 and point sampler uniform to it
                _texture.Use(TextureUnit.Texture0);
                _shader.SetInt("uTex", 0);
                _shader.SetInt("uHasTex", 1);
            }
            else
            {
                _shader.SetInt("uHasTex", 0);
            }

            _mesh.Draw();
        }

        public Texture? Texture => _texture; // 2) Add accessor + setter inside the class

        public void SetTexture(Texture? texture, bool disposeOld = true)
        {
            if (!ReferenceEquals(_texture, texture))
            {
                if (disposeOld && _texture is IDisposable d && !ReferenceEquals(_texture, texture))
                    d.Dispose();
                _texture = texture;
            }
        }

        public void Dispose() => _mesh.Dispose();
    }
}
