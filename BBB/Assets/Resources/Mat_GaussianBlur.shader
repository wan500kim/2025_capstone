Shader "UI/GaussianBlurSeparable"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Radius  ("Radius",  Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;   // x=1/w, y=1/h
            float  _Radius;
            float2 _Direction;           // (1,0)=가로, (0,1)=세로

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }

            // 9탭 근사 가우시안
            fixed4 frag (v2f i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy * _Radius * _Direction;
                const float w0=0.227027, w1=0.316216, w2=0.070270;

                fixed4 c = tex2D(_MainTex, i.uv) * w0;
                c += tex2D(_MainTex, i.uv + texel * 1.384615) * w1;
                c += tex2D(_MainTex, i.uv - texel * 1.384615) * w1;
                c += tex2D(_MainTex, i.uv + texel * 3.230769) * w2;
                c += tex2D(_MainTex, i.uv - texel * 3.230769) * w2;
                return c;
            }
            ENDHLSL
        }
    }
}
