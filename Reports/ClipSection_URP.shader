Shader "VehicleMeasurement/ClipSection_URP"
{
    /*
    ═══════════════════════════════════════════════════════════════════
    CLIP SECTION SHADER - URP VERSION
    
    Universal Render Pipeline compatible clipping shader.
    Supports up to 3 clip planes (X, Y, Z) via global shader properties.
    Two-sided rendering for proper clipped view.
    
    COMPATIBLE WITH: Unity 2019.4+ with URP
    ═══════════════════════════════════════════════════════════════════
    */
    
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        
        LOD 300
        
        // Two-sided rendering for clipped areas
        Cull Off
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // URP Keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            // Texture and sampler
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            
            // Material properties
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                half _BumpScale;
            CBUFFER_END
            
            // GLOBAL clip plane properties (set via Shader.SetGlobalFloat)
            float _GlobalClipXEnabled;
            float _GlobalClipXPosition;
            float _GlobalClipXDirection;
            
            float _GlobalClipYEnabled;
            float _GlobalClipYPosition;
            float _GlobalClipYDirection;
            
            float _GlobalClipZEnabled;
            float _GlobalClipZPosition;
            float _GlobalClipZDirection;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float3 viewDirWS : TEXCOORD5;
                float fogFactor : TEXCOORD6;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 7);
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
                output.normalWS = normalInput.normalWS;
                output.tangentWS = normalInput.tangentWS;
                output.bitangentWS = normalInput.bitangentWS;
                output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                
                return output;
            }
            
            half4 frag(Varyings input, half facing : VFACE) : SV_Target
            {
                // === CLIP PLANE LOGIC ===
                float3 worldPos = input.positionWS;
                
                // Calculate distance to each clip plane
                float distX = (worldPos.x - _GlobalClipXPosition) * _GlobalClipXDirection;
                float distY = (worldPos.y - _GlobalClipYPosition) * _GlobalClipYDirection;
                float distZ = (worldPos.z - _GlobalClipZPosition) * _GlobalClipZDirection;
                
                // Discard pixels beyond clip planes
                if (_GlobalClipXEnabled > 0.5 && distX > 0) discard;
                if (_GlobalClipYEnabled > 0.5 && distY > 0) discard;
                if (_GlobalClipZEnabled > 0.5 && distZ > 0) discard;
                
                // === SURFACE DATA ===
                // Sample textures
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 albedo = baseMap * _BaseColor;
                
                // Normal mapping
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3x3 tangentToWorld = float3x3(input.tangentWS, input.bitangentWS, input.normalWS);
                float3 normalWS = normalize(mul(normalTS, tangentToWorld));
                
                // Flip normal for back faces
                normalWS = facing > 0 ? normalWS : -normalWS;
                
                // === LIGHTING ===
                float3 viewDirWS = normalize(input.viewDirWS);
                
                // Main light
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(worldPos));
                
                // BRDF data
                half3 diffuseColor = albedo.rgb * (1.0 - _Metallic);
                half3 specularColor = lerp(half3(0.04, 0.04, 0.04), albedo.rgb, _Metallic);
                
                // Diffuse lighting
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = diffuseColor * mainLight.color * NdotL * mainLight.shadowAttenuation;
                
                // Specular lighting (Blinn-Phong approximation)
                half3 halfDir = normalize(mainLight.direction + viewDirWS);
                half NdotH = saturate(dot(normalWS, halfDir));
                half specularPower = exp2(10.0 * _Smoothness + 1.0);
                half3 specular = specularColor * mainLight.color * pow(NdotH, specularPower) * mainLight.shadowAttenuation;
                
                // Ambient/GI
                half3 ambient = SampleSH(normalWS) * diffuseColor;
                
                // Additional lights
                half3 additionalLights = half3(0, 0, 0);
                #ifdef _ADDITIONAL_LIGHTS
                uint additionalLightsCount = GetAdditionalLightsCount();
                for (uint i = 0; i < additionalLightsCount; i++)
                {
                    Light light = GetAdditionalLight(i, worldPos);
                    half addNdotL = saturate(dot(normalWS, light.direction));
                    additionalLights += diffuseColor * light.color * addNdotL * light.distanceAttenuation * light.shadowAttenuation;
                }
                #endif
                
                // Final color
                half3 finalColor = ambient + diffuse + specular + additionalLights;
                
                // Apply fog
                finalColor = MixFog(finalColor, input.fogFactor);
                
                return half4(finalColor, albedo.a);
            }
            ENDHLSL
        }
        
        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            // Global clip properties
            float _GlobalClipXEnabled;
            float _GlobalClipXPosition;
            float _GlobalClipXDirection;
            float _GlobalClipYEnabled;
            float _GlobalClipYPosition;
            float _GlobalClipYDirection;
            float _GlobalClipZEnabled;
            float _GlobalClipZPosition;
            float _GlobalClipZDirection;
            
            float3 _LightDirection;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            
            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                
                return output;
            }
            
            half4 ShadowFrag(Varyings input) : SV_Target
            {
                // Clip in shadow pass too
                float3 worldPos = input.positionWS;
                
                float distX = (worldPos.x - _GlobalClipXPosition) * _GlobalClipXDirection;
                float distY = (worldPos.y - _GlobalClipYPosition) * _GlobalClipYDirection;
                float distZ = (worldPos.z - _GlobalClipZPosition) * _GlobalClipZDirection;
                
                if (_GlobalClipXEnabled > 0.5 && distX > 0) discard;
                if (_GlobalClipYEnabled > 0.5 && distY > 0) discard;
                if (_GlobalClipZEnabled > 0.5 && distZ > 0) discard;
                
                return 0;
            }
            ENDHLSL
        }
        
        // Depth pass
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            
            ZWrite On
            ColorMask 0
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // Global clip properties
            float _GlobalClipXEnabled;
            float _GlobalClipXPosition;
            float _GlobalClipXDirection;
            float _GlobalClipYEnabled;
            float _GlobalClipYPosition;
            float _GlobalClipYDirection;
            float _GlobalClipZEnabled;
            float _GlobalClipZPosition;
            float _GlobalClipZDirection;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            
            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            
            half4 DepthFrag(Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                
                float distX = (worldPos.x - _GlobalClipXPosition) * _GlobalClipXDirection;
                float distY = (worldPos.y - _GlobalClipYPosition) * _GlobalClipYDirection;
                float distZ = (worldPos.z - _GlobalClipZPosition) * _GlobalClipZDirection;
                
                if (_GlobalClipXEnabled > 0.5 && distX > 0) discard;
                if (_GlobalClipYEnabled > 0.5 && distY > 0) discard;
                if (_GlobalClipZEnabled > 0.5 && distZ > 0) discard;
                
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}
