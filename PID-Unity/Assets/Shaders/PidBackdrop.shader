//Handles rendering the floor/ceiling.
//There isn't any floor or ceiling geometry in PID, it just paints a vertical gradient straight to the screen:
//fading in at the top, solid black across the middle, fading out at the bottom. This shader does its best to emulate that, with some minor
//differences thanks to Unity being more modern.

Shader "PID/Backdrop"
{
    Properties
    {
        _Edge ("Edge colour", Color) = (0.117647, 0.117647, 0.117647, 1)
        _Lit  ("Lit (1 = flashlight or goggles)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" }
        Cull Off
        ZWrite Off
        ZTest Always
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Edge;
            float  _Lit;

            struct v2f { float4 pos : SV_POSITION; float4 spos : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                o.spos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //0 at the bottom of the screen, 1 at the top.
                float v = saturate(i.spos.y / max(i.spos.w, 1e-5));

                //Where the fade stops and the black starts, as a fraction of screen height.
                float d4 = (_Lit > 0.5) ? 2.0 / 5.0  : 3.0 / 10.0;
                float d5 = (_Lit > 0.5) ? 3.0 / 5.0  : 7.0 / 10.0;

                float fromTop    = 1.0 - v;
                float fromBottom = v;

                float t;
                if (fromTop < d4)               t = 1.0 - fromTop / d4;
                else if (fromBottom < 1.0 - d5) t = 1.0 - fromBottom / (1.0 - d5);
                else                            t = 0.0;

                return fixed4(_Edge.rgb * t, 1);
            }
            ENDCG
        }
    }
}
