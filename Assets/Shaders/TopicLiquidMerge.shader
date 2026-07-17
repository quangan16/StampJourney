Shader "StampJourney/TopicLiquidMerge"
{
    Properties
    {
        _LiquidColor ("Liquid Color", Color) = (1,1,1,1)
        _Progress ("Merge Progress", Range(0,1)) = 0
        _Aspect ("Bridge Aspect", Float) = 1
        _CenterOffset ("Blob Center Offset", Float) = 1
        _Radius ("Blob Radius", Float) = 1
        _MaxSmooth ("Maximum Smooth Union", Float) = 1
        _EdgeSoftness ("Edge Softness", Range(0.001,0.2)) = 0.025
        _Wobble ("Liquid Wobble", Range(0,0.15)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TopicLiquidMerge"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _LiquidColor;
                float _Progress;
                float _Aspect;
                float _CenterOffset;
                float _Radius;
                float _MaxSmooth;
                float _EdgeSoftness;
                float _Wobble;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float SmootherStep(float value)
            {
                value = saturate(value);
                return value * value * value * (value * (value * 6.0 - 15.0) + 10.0);
            }

            // Polynomial smooth union. Increasing smoothness makes two separate signed
            // distance fields attract and grow the characteristic metaball neck.
            float SmoothMinimum(float distanceA, float distanceB, float smoothness)
            {
                smoothness = max(smoothness, 0.0001);
                float blendAmount = saturate(
                    0.5 + 0.5 * (distanceB - distanceA) / smoothness);
                return lerp(distanceB, distanceA, blendAmount)
                    - smoothness * blendAmount * (1.0 - blendAmount);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Work in units normalized by half the bridge thickness so circles remain
                // circular even though the connector quad is stretched across the card gap.
                float2 p = (input.uv - 0.5) * 2.0;
                p.x *= _Aspect;

                // Grow the source blobs from zero before pulling them together. This prevents
                // the full-radius capsule silhouette from appearing on the enabled frame next
                // to the rounded-square card backgrounds.
                // DOTween supplies linear progress, so keep the visible shader phases linear
                // as well. Starting close to the final radius avoids spending the beginning
                // of the tween growing invisibly underneath the card background.
                float mergeAmount = saturate(_Progress);
                float radiusAmount = saturate(_Progress / 0.38);
                float unionAmount = saturate((_Progress - 0.02) / 0.78);
                float fullWidthAmount = saturate((_Progress - 0.55) / 0.45);
                float motionEnvelope = sin(unionAmount * 3.14159265)
                    * (1.0 - fullWidthAmount);
                p.y += sin(p.x * 3.0 + mergeAmount * 9.0)
                    * _Wobble * _Radius * motionEnvelope;

                float startRadius = max(
                    0.0, _Radius - max(_Radius * 0.06, _EdgeSoftness * 2.0));
                float animatedRadius = lerp(startRadius, _Radius, radiusAmount);
                float leftDistance = length(
                    p - float2(-_CenterOffset, 0.0)) - animatedRadius;
                float rightDistance = length(
                    p - float2(_CenterOffset, 0.0)) - animatedRadius;
                float unionStrength = max(0.0001, _MaxSmooth * unionAmount);
                float metaballDistance = SmoothMinimum(
                    leftDistance, rightDistance, unionStrength);

                // Preserve the original metaball merge, then expand only its final phase.
                // At progress 1 this horizontal SDF fills the entire connector thickness,
                // matching the full width of the card background with no remaining pinch.
                float fullWidthDistance = abs(p.y) - _Radius;
                float liquidDistance = lerp(
                    metaballDistance, fullWidthDistance, fullWidthAmount);
                float alpha = 1.0 - smoothstep(-_EdgeSoftness, _EdgeSoftness, liquidDistance);

                // Reveal from zero alpha independently of radius so the bridge is visible
                // throughout the tween instead of appearing only after crossing a threshold.
                float revealAmount = saturate(_Progress / 0.14);
                alpha *= revealAmount;

                return half4(_LiquidColor.rgb, _LiquidColor.a * alpha);
            }
            ENDHLSL
        }
    }
}
