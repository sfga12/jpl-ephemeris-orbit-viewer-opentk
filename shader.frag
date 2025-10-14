#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

// Solid color fallback / tint
uniform vec3 color;

// Texture inputs
uniform sampler2D uTex;
uniform int uHasTex; // 1 = use texture, 0 = solid color

void main()
{
    if (uHasTex == 1)
    {
        vec4 tex = texture(uTex, TexCoord);
        // Multiply by color to allow tinting; use vec3(1) if you want pure texture
        FragColor = tex * vec4(color, 1.0);
    }
    else
    {
        FragColor = vec4(color, 1.0);
    }
}