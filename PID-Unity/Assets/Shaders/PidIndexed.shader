//Handles drawing walls, doors, and sprites as close to the original PID as possible.
//Textures hold palette indices, not colours, since the actual colour comes from a 256x16 LUT stored as a seperate texture.
//We pick a column with the index, and a row with how far away you are. The shading effect does what more modern Unity render fog solutions
//can't, and provides more control for how we handle render features like the goggles or flashlight.

Shader "PID/Indexed"
{
    Properties
    {
        _MainTex        ("Index map (R8)", 2D) = "black" {}
        _Lut            ("Shade LUT (256x16)", 2D) = "white" {}
        _AffineUV       ("Affine UV (1993 warp)", Float) = 0
        _Dither         ("Band dither", Float) = 1
        _Cutout         ("Alpha: 0/1 clip index 2, 2 paint it", Float) = 0
        _ClipPadding    ("Clip padding (index 0)", Float) = 1

        //Debug only, leave both at 0.
        _View14Override ("View14 override (0 = use global)", Float) = 0
        _DebugMode      ("DEBUG ONLY: 0 off, 1 band, 2 index, 3 UV, 4 magenta", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            sampler2D _Lut;
            float     _AffineUV;
            float     _Dither;
            float     _Cutout;
            float     _ClipPadding;
            float     _View14Override;
            float     _DebugMode;

            //Set by PidShade for the whole scene. 3 is unlit, 5 flashlight, 7 goggles. We can't inspect globals per material, which makes
            //them a pain to debug, thus the override property above.
            float _PidView14;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;

                //Two copies of the same UV. The original steps u per screen column, which a triangle can't do, so affine here is a worse
                //approximation than perspective-correct rather than a better one. Near walls smear badly. Off by default, kept because
                //it's the only way to see the difference.
                float2 uvP : TEXCOORD0;
                noperspective float2 uvA : TEXCOORD1;

                //Screen-linear in PID proper
                noperspective float band : TEXCOORD2;

                float4 spos : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvP = uv;
                o.uvA = uv;

                float3 vp      = UnityObjectToViewPos(v.vertex);
                float  depth   = max(-vp.z, 0.0);
                float  lateral = vp.x;

                //PID adds half the sideways offset to the depth, which over-darkens towards the screen edges
                float d5 = depth + abs(lateral) * 0.5;

                float g = (_View14Override > 0.5) ? _View14Override : _PidView14;
                float view14 = max(g, 1.0);

                o.band = 5.0 * d5 / view14;

                o.spos = ComputeScreenPos(o.pos);
                return o;
            }

            //The original runs a shift register per pixel and compares it to the leftover fraction of the band, which crawls as you move
            //Not quite the same, but we do our best to get close
            float hash21 (float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (_AffineUV > 0.5) ? i.uvA : i.uvP;
                float idx = tex2D(_MainTex, uv).r * 255.0;

                //Debug views: If a surface doesn't respond to these it isn't using this shader, and thus you have a problem
                if (_DebugMode > 0.5)
                {
                    if (_DebugMode < 1.5)
                    {
                        float b = clamp(i.band, 0.0, 15.0) / 15.0;
                        return fixed4(b, b, b, 1);
                    }
                    if (_DebugMode < 2.5)
                    {
                        float g2 = idx / 255.0;
                        return fixed4(g2, g2, g2, 1);
                    }
                    if (_DebugMode < 3.5) return fixed4(frac(uv.x), frac(uv.y), 0, 1);
                    return fixed4(1, 0, 1, 1);
                }

                float band = clamp(i.band, 0.0, 15.0);
                float lo    = floor(band);
                float fracB = band - lo;

                if (_Dither > 0.5 && lo < 15.0)
                {
                    float2 sp = i.spos.xy / max(i.spos.w, 1e-5) * _ScreenParams.xy;
                    float  r  = hash21(floor(sp) + _Time.y * 60.0);
                    lo += step(r, fracB);
                }
                lo = clamp(lo, 0.0, 15.0);

                //Index 0 is the blank margin around a tile's artwork, but palette entry 0 is white, so leaving it in paints white boxes on 
                //the walls. The original never draws it, so we discard it.
                if (_ClipPadding > 0.5 && idx < 0.5) discard;

                //Row 0 of the LUT is the first line of the PNG, which lands at the top in Unity's texture space. Thus we flip it, and if we
                //don't, everything shades backwards, and looks wrong.
                float2 luv = float2((idx + 0.5) / 256.0, 1.0 - (lo + 0.5) / 16.0);
                fixed4 c = tex2D(_Lut, luv);

                //Index 2 is transparent everywhere, walls included
                if (_Cutout < 1.5) clip(c.a - 0.5);

                c.a = 1.0;
                return c;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}
