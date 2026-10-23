Shader "VehicleMeasurement/ClipSection_Lit"
{
    // ─────────────────────────────────────────────────────────────────────────
    // DAS clip section - the stand-in material for vehicle parts whose own shader can't clip.
    //
    // Replaces the old ClipSection_URP (Blinn-Phong, ambient only, no reflections, always opaque, only
    // _BaseColor/_BaseMap copied) which turned the vehicle white/black and the glass solid black.
    // This one uses URP's own PBR lighting (UniversalFragmentPBR): reflection probes, specular, clear coat,
    // shadows, additional lights, fog, SSAO - so a clipped car looks like the car.
    //
    // ClipMaterials.cs fills it from the vehicle's material - including the DAS Shader Graph property names
    // (_Base_Map colour, _Metallic_Map, _RoughnessValues, _Use_BaseMap, _Tiling_UV, _ClearCoat, _Alpha, ...)
    // and their world-space (triplanar) texturing - so colours and finishes match.
    //
    // Pixels past the clip plane are removed in every pass (colour, depth, depth-normals, shadows), and the
    // inside of a cut part is drawn (two-sided) with a slight tint so the cut reads clearly.
    // ─────────────────────────────────────────────────────────────────────────
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _UseBaseMap ("Use Base Map", Float) = 0
        _BaseMapLevel ("Base Map Level", Float) = 1

        _Metallic ("Metallic", Range(0,1)) = 0
        _MetallicMap ("Metallic Map (R)", 2D) = "white" {}
        _UseMetallicMap ("Use Metallic Map", Float) = 0

        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _SmoothnessMap ("Smoothness Map", 2D) = "white" {}
        _UseSmoothnessMap ("Smoothness Map: 0 off, 1 red, 2 alpha", Float) = 0

        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _UseBumpMap ("Use Normal Map", Float) = 0

        _ClearCoatMask ("Clear Coat", Range(0,1)) = 0
        _ClearCoatSmoothness ("Clear Coat Smoothness", Range(0,1)) = 1

        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)

        _Triplanar ("World-space (triplanar) texturing", Float) = 0
        _TriplanarTiling ("Triplanar Tiling", Float) = 1

        _DASSurfaceAlpha ("Material Alpha (glass)", Range(0,1)) = 1
        _Opacity ("Opacity (comparison)", Range(0,1)) = 1
        _CutFaceTint ("Inside / Cut Face Tint", Color) = (0.72,0.72,0.72,1)

        // Render state, set by ClipMaterials.cs
        [HideInInspector] _Surface ("__surface", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _Cull ("__cull", Float) = 0
        [HideInInspector] _DASKeepTransparent ("__keepTransparent", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "DASClip.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _UseBaseMap;
            half _BaseMapLevel;
            half _Metallic;
            half _UseMetallicMap;
            half _Smoothness;
            half _UseSmoothnessMap;
            half _BumpScale;
            half _UseBumpMap;
            half _ClearCoatMask;
            half _ClearCoatSmoothness;
            half4 _EmissionColor;
            half _Triplanar;
            float _TriplanarTiling;
            half _DASSurfaceAlpha;
            half _Opacity;
            half4 _CutFaceTint;
            half _Surface;
            half _SrcBlend;
            half _DstBlend;
            half _ZWrite;
            half _Cull;
        CBUFFER_END
        ENDHLSL

        // ── Colour ──────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitVert
            #pragma fragment LitFrag

            #pragma shader_feature_local_fragment _CLEARCOAT
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicMap);   SAMPLER(sampler_MetallicMap);
            TEXTURE2D(_SmoothnessMap); SAMPLER(sampler_SmoothnessMap);
            TEXTURE2D(_BumpMap);       SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;   // w = bitangent sign
                half   fogFactor  : TEXCOORD4;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD5;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                o.tangentWS = float4(n.tangentWS, sign);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                o.shadowCoord = GetShadowCoord(p);
                #endif
                return o;
            }

            // Shader Graph "Triplanar" (Blend = 1): planar projections on world X/Y/Z, weighted by the normal.
            float3 TriWeights(float3 n)
            {
                float3 w = abs(n);
                return w / max(w.x + w.y + w.z, 1e-5);
            }

            half4 SampleTri(TEXTURE2D_PARAM(tex, samp), float3 uvw, float3 w)
            {
                return SAMPLE_TEXTURE2D(tex, samp, uvw.zy) * w.x
                     + SAMPLE_TEXTURE2D(tex, samp, uvw.xz) * w.y
                     + SAMPLE_TEXTURE2D(tex, samp, uvw.xy) * w.z;
            }

            // Shader Graph "Triplanar" in Normal mode (world-space result, whiteout blend).
            float3 SampleTriNormalWS(float3 uvw, float3 n, float3 w)
            {
                float3 tx = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvw.zy));
                float3 ty = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvw.xz));
                float3 tz = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvw.xy));
                tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
                return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
            }

            half4 LitFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                DASClip(input.positionWS);

                bool isFront = IS_FRONT_VFACE(face, true, false);
                float3 geomN = normalize(input.normalWS);
                float3 tangent = normalize(input.tangentWS.xyz);
                if (!isFront) geomN = -geomN;

                bool tri = _Triplanar > 0.5;
                float3 uvw = input.positionWS * _TriplanarTiling;
                float3 w = TriWeights(geomN);

                // Base colour
                half4 texel = half4(1, 1, 1, 1);
                if (_UseBaseMap > 0.5)
                {
                    texel = tri ? SampleTri(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), uvw, w)
                                : SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                    texel.rgb *= _BaseMapLevel;
                }
                half3 albedo = texel.rgb * _BaseColor.rgb;

                // Metallic / smoothness
                half metallic = _Metallic;
                if (_UseMetallicMap > 0.5)
                {
                    half m = tri ? SampleTri(TEXTURE2D_ARGS(_MetallicMap, sampler_MetallicMap), uvw, w).r
                                 : SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, input.uv).r;
                    metallic *= m;
                }
                half smoothness = _Smoothness;
                if (_UseSmoothnessMap > 0.5)
                {
                    half4 s = tri ? SampleTri(TEXTURE2D_ARGS(_SmoothnessMap, sampler_SmoothnessMap), uvw, w)
                                  : SAMPLE_TEXTURE2D(_SmoothnessMap, sampler_SmoothnessMap, input.uv);
                    smoothness *= (_UseSmoothnessMap > 1.5) ? s.a : s.r;
                }

                // Normal
                float3 normalWS = geomN;
                if (_UseBumpMap > 0.5)
                {
                    if (tri)
                    {
                        float3 triN = SampleTriNormalWS(uvw, geomN, w);
                        normalWS = normalize(lerp(geomN, triN, _BumpScale));
                    }
                    else
                    {
                        half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                        float3 bitangent = input.tangentWS.w * cross(normalize(input.normalWS), tangent);
                        float3x3 tbn = float3x3(tangent, bitangent, normalize(input.normalWS));
                        normalWS = normalize(TransformTangentToWorld(nTS, tbn));
                        if (!isFront) normalWS = -normalWS;
                    }
                }

                // Inside of a cut part
                if (!isFront) albedo *= _CutFaceTint.rgb;

                half alpha = saturate(_DASSurfaceAlpha * _Opacity * _BaseColor.a * texel.a);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = saturate(metallic);
                surface.specular = half3(0, 0, 0);
                surface.smoothness = saturate(smoothness);
                surface.normalTS = half3(0, 0, 1);
                surface.emission = _EmissionColor.rgb;
                surface.occlusion = 1;
                surface.alpha = alpha;
                surface.clearCoatMask = saturate(_ClearCoatMask);
                surface.clearCoatSmoothness = saturate(_ClearCoatSmoothness);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                    float4 vertexProbeOcclusion = float4(1, 1, 1, 1);
                    inputData.bakedGI = SAMPLE_GI(half3(0, 0, 0),
                        GetAbsolutePositionWS(inputData.positionWS),
                        inputData.normalWS,
                        inputData.viewDirectionWS,
                        input.positionCS.xy,
                        vertexProbeOcclusion,
                        inputData.shadowMask);
                #else
                    inputData.bakedGI = SampleSH(inputData.normalWS);
                #endif

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = _Surface > 0.5 ? alpha : 1.0;
                return color;
            }
            ENDHLSL
        }

        // ── Shadows ─────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                o.positionWS = positionWS;
                return o;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                DASClip(input.positionWS);
                return 0;
            }
            ENDHLSL
        }

        // ── Depth (depth prepass, depth texture) ────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                DASClip(input.positionWS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ── Depth + normals (SSAO, decals) ──────────────────────────────────
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                DASClip(input.positionWS);
                float3 n = normalize(input.normalWS);
                if (!IS_FRONT_VFACE(face, true, false)) n = -n;
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(n);
                    float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
