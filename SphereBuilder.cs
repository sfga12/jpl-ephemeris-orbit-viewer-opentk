using System;
using System.Collections.Generic;

namespace JplEphemerisOrbitViewer
{
    public static class SphereBuilder
    {
        // Unit sphere (radius=1). Returns interleaved vertices (pos,uv,normal) and indices.
        public static void Create(int stacks, int slices, out float[] vertices, out uint[] indices)
        {
            var verts = new List<float>();
            var inds = new List<uint>();

            for (int i = 0; i <= stacks; i++)
            {
                float v = i / (float)stacks;
                float phi = v * MathF.PI; // 0..PI
                float y = MathF.Cos(phi);
                float r = MathF.Sin(phi);

                for (int j = 0; j <= slices; j++)
                {
                    float u = j / (float)slices;
                    float theta = u * MathF.Tau; // 0..2PI
                    float x = r * MathF.Cos(theta);
                    float z = r * MathF.Sin(theta);

                    // pos
                    verts.Add(x); verts.Add(y); verts.Add(z);
                    // uv
                    verts.Add(u); verts.Add(1f - v);
                    // normal
                    verts.Add(x); verts.Add(y); verts.Add(z);
                }
            }

            int stride = slices + 1;
            for (int i = 0; i < stacks; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    uint a = (uint)(i * stride + j);
                    uint b = (uint)((i + 1) * stride + j);
                    uint c = (uint)((i + 1) * stride + j + 1);
                    uint d = (uint)(i * stride + j + 1);

                    inds.Add(a); inds.Add(b); inds.Add(c);
                    inds.Add(a); inds.Add(c); inds.Add(d);
                }
            }

            vertices = verts.ToArray();
            indices = inds.ToArray();
        }
    }
}