Shader "Rovernauts/Lunar Surface"
{
    Properties
    {
        _Color ("Dust tint", Color) = (0.44, 0.47, 0.53, 1)
        _Period ("Tile size", Float) = 128
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;
        float _Period;
        struct Input { float3 worldPos; };

        // Every pattern repeats exactly once per terrain tile (_Period world units), so the ground matches
        // across tile seams and does not change when the endless world wraps the rover back a tile.
        float2 wrapCell(float2 cell, float count) { return cell - count * floor(cell / count); }

        float hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        // Value noise on a lattice of `count` cells per tile.
        float noise(float2 worldXZ, float count)
        {
            float2 p = worldXZ * (count / _Period);
            float2 cell = floor(p);
            float2 k = frac(p);
            k = k * k * (3.0 - 2.0 * k);
            return lerp(lerp(hash(wrapCell(cell, count)), hash(wrapCell(cell + float2(1, 0), count)), k.x),
                lerp(hash(wrapCell(cell + float2(0, 1), count)), hash(wrapCell(cell + float2(1, 1), count)), k.x), k.y);
        }

        void surf(Input input, inout SurfaceOutput output)
        {
            float2 p = input.worldPos.xz;
            // Lattice counts are about the original frequencies (0.16, 0.67, 13, 0.13, 0.8 per unit) times a 128-unit tile.
            float deposits = noise(p, 20.0) * 0.23 + noise(p, 86.0) * 0.16;
            float grit = noise(p, 1664.0) * 0.11;
            float2 waves = float2(23.0, 12.0) * (6.2831853 / _Period);
            float striation = sin(dot(p, waves) + noise(p, 16.0) * 8.0) * 0.035;

            const float craters = 8.0;
            float size = _Period / craters;
            float2 cell = floor((p + 6.0) / size);
            float2 id = wrapCell(cell, craters);
            float2 center = (cell + 0.5) * size - 6.0;
            center += (float2(hash(id), hash(id + 7.0)) - 0.5) * 5.0;
            float radius = lerp(1.8, 4.9, hash(id + 3.0));
            float d = (length((p - center) * float2(0.96, 1.12)) + (noise(p, 102.0) - 0.5) * 0.8) / radius;
            float bowl = (1.0 - smoothstep(0.25, 0.98, d)) * 0.35;
            float rim = smoothstep(0.76, 0.99, d) * (1.0 - smoothstep(1.0, 1.24, d)) * 0.18;
            output.Albedo = _Color.rgb * (0.7 + deposits + grit + striation + rim - bowl);
            output.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
