Shader "Rovernauts/Deep Space"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Interpolators { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };

            Interpolators vert(float4 vertex : POSITION)
            {
                Interpolators output;
                output.position = UnityObjectToClipPos(vertex);
                output.direction = vertex.xyz;
                return output;
            }

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(132.23, 263.41))) * 43758.5453);
            }

            fixed4 frag(Interpolators input) : SV_Target
            {
                float3 ray = normalize(input.direction);
                float2 sky = float2(atan2(ray.x, ray.z), asin(ray.y)) * 95.0;
                float2 cell = floor(sky);
                float2 offset = float2(hash(cell), hash(cell + 68.0));
                float star = 1.0 - smoothstep(0.015, 0.115, length(frac(sky) - lerp(0.15, 0.85, offset)));
                star *= lerp(0.18, 0.87, pow(hash(cell + 20.0), 3.0));
                float3 background = float3(0.024, 0.043, 0.082) + star * float3(0.61, 0.75, 1.0);

                float3 center = normalize(float3(-0.22, 0.031, 1.0));
                float3 east = normalize(cross(float3(0.0, 1.0, 0.0), center));
                float3 north = cross(center, east);
                float2 p = float2(dot(ray, east), dot(ray, north)) / 0.052;
                float radius = length(p);
                if (dot(ray, center) > 0.0 && radius < 1.0)
                {
                    float z = sqrt(saturate(1.0 - radius * radius));
                    float lit = saturate(dot(float3(p, z), normalize(float3(-0.8, 0.4, 0.35))));
                    float bands = 0.68 + 0.22 * sin((p.y + p.x * 0.26 + z * 0.15) * 16.0);
                    float3 surface = bands * (0.21 + 0.79 * lit) * float3(0.72, 0.59, 0.48);
                    background = lerp(background, surface, 1.0 - smoothstep(0.96, 1.0, radius));
                }
                return float4(background, 1.0);
            }
            ENDCG
        }
    }
}
