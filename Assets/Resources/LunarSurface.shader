Shader "Rovernauts/Lunar Surface"
{
    Properties
    {
        _Color ("Dust tint", Color) = (0.44, 0.47, 0.53, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;
        struct Input { float3 worldPos; };

        float hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float noise(float2 p)
        {
            float2 cell = floor(p);
            float2 k = frac(p);
            k = k * k * (3.0 - 2.0 * k);
            return lerp(lerp(hash(cell), hash(cell + float2(1, 0)), k.x),
                lerp(hash(cell + float2(0, 1)), hash(cell + float2(1, 1)), k.x), k.y);
        }

        void surf(Input input, inout SurfaceOutput output)
        {
            float2 p = input.worldPos.xz;
            float deposits = noise(p * 0.16) * 0.23 + noise(p * 0.67) * 0.16;
            float grit = noise(p * 13.0) * 0.11;
            float striation = sin(dot(p, float2(1.15, 0.59)) + noise(p * 0.13) * 8.0) * 0.035;

            float2 cell = floor((p + 6.0) / 15.0);
            float2 center = (cell + 0.5) * 15.0 - 6.0;
            center += (float2(hash(cell), hash(cell + 7.0)) - 0.5) * 5.0;
            float radius = lerp(1.8, 4.9, hash(cell + 3.0));
            float d = (length((p - center) * float2(0.96, 1.12)) + (noise(p * 0.8) - 0.5) * 0.8) / radius;
            float bowl = (1.0 - smoothstep(0.25, 0.98, d)) * 0.35;
            float rim = smoothstep(0.76, 0.99, d) * (1.0 - smoothstep(1.0, 1.24, d)) * 0.18;
            output.Albedo = _Color.rgb * (0.7 + deposits + grit + striation + rim - bowl);
            output.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
