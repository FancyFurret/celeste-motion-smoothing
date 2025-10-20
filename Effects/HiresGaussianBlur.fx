// Copied from Common.fxh

#define DECLARE_TEXTURE(Name, index) \
    texture Name: register(t##index); \
    sampler Name##Sampler: register(s##index)

#define SAMPLE_TEXTURE(Name, texCoord) tex2D(Name##Sampler, texCoord)

#define PS_3_SHADER_COMPILER ps_3_0
#define PS_2_SHADER_COMPILER ps_2_0
#define VS_SHADER_COMPILER vs_3_0
#define VS_2_SHADER_COMPILER vs_2_0

#define SV_TARGET0 COLOR0
#define SV_TARGET1 COLOR1
#define SV_TARGET2 COLOR2

//-----------------------------------------------------------------------------
// Globals.
//-----------------------------------------------------------------------------

float2 pixel;
float fade = 0;

DECLARE_TEXTURE(text, 0);

//-----------------------------------------------------------------------------
// Pixel Shaders.
//-----------------------------------------------------------------------------

float4 PS_GaussianBlur_9(float4 inPosition : SV_Position, float4 inColor : COLOR0, float2 uv : TEXCOORD0) : SV_TARGET0
{
    float4 color = 0;
    float4 center = SAMPLE_TEXTURE(text, float2(uv.x, uv.y));

    color += SAMPLE_TEXTURE(text, uv - pixel * 26) * 0.00137f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 25) * 0.00274f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 24) * 0.00412f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 23) * 0.00549f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 22) * 0.00686f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 21) * 0.00823f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 20) * 0.00960f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 19) * 0.01097f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 18) * 0.01235f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 17) * 0.01372f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 16) * 0.01509f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 15) * 0.01646f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 14) * 0.01783f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 13) * 0.01920f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 12) * 0.02058f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 11) * 0.02195f;
	color += SAMPLE_TEXTURE(text, uv - pixel * 10) * 0.02332f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  9) * 0.02469f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  8) * 0.02606f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  7) * 0.02743f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  6) * 0.02881f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  5) * 0.03018f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  4) * 0.03155f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  3) * 0.03292f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  2) * 0.03429f;
	color += SAMPLE_TEXTURE(text, uv - pixel *  1) * 0.03567f;
	color += center                                * 0.03704f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  1) * 0.03567f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  2) * 0.03429f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  3) * 0.03292f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  4) * 0.03155f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  5) * 0.03018f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  6) * 0.02881f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  7) * 0.02743f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  8) * 0.02606f;
	color += SAMPLE_TEXTURE(text, uv + pixel *  9) * 0.02469f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 10) * 0.02332f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 11) * 0.02195f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 12) * 0.02058f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 13) * 0.01920f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 14) * 0.01783f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 15) * 0.01646f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 16) * 0.01509f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 17) * 0.01372f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 18) * 0.01235f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 19) * 0.01097f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 20) * 0.00960f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 21) * 0.00823f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 22) * 0.00686f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 23) * 0.00549f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 24) * 0.00412f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 25) * 0.00274f;
	color += SAMPLE_TEXTURE(text, uv + pixel * 26) * 0.00137f;

    return lerp(color, float4(0,0,0,0), (1.0 - length(uv - 0.5)) * fade);
}


//-----------------------------------------------------------------------------
// Techniques.
//-----------------------------------------------------------------------------

technique GaussianBlur9
{
	pass { PixelShader = compile PS_3_SHADER_COMPILER PS_GaussianBlur_9(); }
}