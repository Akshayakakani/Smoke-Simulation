Shader "Unlit/SmokeDisplay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D ObstacleMap;
            sampler2D DebugMap;
            sampler2D SmokeMap;
            float4 SmokeMap_TexelSize;


            sampler2D VelocityMapA;
            sampler2D VelocityMapB;
            sampler2D PressureMap;
            float2 resolution;
            int displayMode;

            float temperatureDisplayFactor;
            float divergenceDisplayFactor;
            float ambientTemperature;
            float smokeDisplayFactor;
            float displayPressureFactor;
            float displayVelocityFactor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            float3 HSVToRGB(float3 hsv)
            {
                // Thanks to http://lolengine.net/blog/2013/07/27/rgb-to-hsv-in-glsl
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(hsv.xxx + K.xyz) * 6.0 - K.www);
                return hsv.z * lerp(K.xxx, saturate(p - K.xxx), hsv.y);
            }

            float2 ClosestPointOnLineSegment(float2 p, float2 a1, float2 a2)
            {
                float2 lineDelta = a2 - a1;
                float2 pointDelta = p - a1;
                float sqrLineLength = dot(lineDelta, lineDelta);

                if (sqrLineLength == 0) return a1;

                float t = saturate(dot(pointDelta, lineDelta) / sqrLineLength);
                return a1 + lineDelta * t;
            }

            float3 VelocityDisplay(v2f i)
            {
                float2 refRes = float2(1280, 720) * 1;
                float2 texelSize = SmokeMap_TexelSize.xy;

                float smoke = tex2D(SmokeMap, i.uv).y;

                float2 scale = refRes / 16;
                float2 uv = i.uv;
                float2 uvDiscrete = (int2)(uv * scale) / scale + 0.5 / scale;

                float2 coord = uv * refRes;
                float2 coordDiscrete = uvDiscrete * refRes;

                float2 edgeVelocityComponents = tex2D(VelocityMapA, i.uv);
                float2 edgeVelocityComponents2 = tex2D(VelocityMapA, i.uv + texelSize);
                float2 vel = (edgeVelocityComponents + edgeVelocityComponents2) / 2;

                float2 dir = normalize(vel);

                const float lengthExaggerationFactor = 1.75;
                float displayLengthT = saturate(length(vel) * lengthExaggerationFactor * displayVelocityFactor) * 0.5;
                float2 vecScale = refRes / scale * 0.5 * displayLengthT * 2;
                float2 c = ClosestPointOnLineSegment(coord, coordDiscrete, coordDiscrete + dir * vecScale);

                float lineWeight = smoothstep(0.9, 0.4, length(coord - c)) * (1 - tex2D(ObstacleMap, i.uv).r);
                float pointWeight = smoothstep(2, 1, length(coord - coordDiscrete)) * (1 - tex2D(ObstacleMap, i.uv).r);
                
                float3 col = HSVToRGB(float3((1-saturate(length(vel) * 0.5 * displayVelocityFactor)) * 0.9, 1, 1));
                return lerp(0.3, col * lineWeight, 1 - pointWeight) + float3(1, 1, 1) * smoke * 0.8;
            }

            float3 DivergenceDisplay(v2f i)
            {
                float divergence = tex2D(DebugMap, i.uv).a;
                divergence *= divergenceDisplayFactor;
                return float3(saturate(divergence), 0, saturate(-divergence));
            }

            float3 TemperatureDisplay(v2f i)
            {
                float4 smokeData = tex2D(SmokeMap, i.uv);
                float temperature = smokeData.a;
                float3 smoke = smokeData.rgb;

                float delta = ambientTemperature - temperature;

                float3 col = delta > 0 ? float3(0, 0, 1) : float3(1, 0, 0);
                float3 smokeCol = float3(0, 1, 0) * dot(smoke, 1.0 / 3) * 1.25;
                
                float3 temperatureDisplayCol = col * abs(delta) * temperatureDisplayFactor + smokeCol;
                return temperatureDisplayCol;
            }

            float3 DebugDisplay(v2f i)
            {
                return tex2D(DebugMap, i.uv) * smokeDisplayFactor;
            }

            float3 SmokeDisplay(v2f i)
            {
                float obstacle = tex2D(ObstacleMap, i.uv).r;
                float3 smoke = tex2D(SmokeMap, i.uv).rgb;
                float3 smokeCol = smoke * smokeDisplayFactor;
                return lerp(smokeCol, 0, obstacle);
            }

            float3 PressureDisplay(v2f i)
            {
                float pressure = tex2D(PressureMap, i.uv).x;
                float3 col = pressure < 0 ? float3(0, 0, 1) : float3(1, 0, 0);
                return col * abs(pressure * displayPressureFactor);
            }
            
            float4 frag(v2f i) : SV_Target
            {
                float3 col = float3(1, 0, 1);

                if (displayMode == 0) col = SmokeDisplay(i);
                else if (displayMode == 1) col = TemperatureDisplay(i);
                else if (displayMode == 2) col = VelocityDisplay(i);
                else if (displayMode == 3) col = DivergenceDisplay(i);
                else if (displayMode == 4) col = PressureDisplay(i);
                else if (displayMode == 5) col = DebugDisplay(i);

                return float4(col, 1);
            }
            ENDCG
        }
    }
}