#define SAMPLE_TEXTURE(Name, texCoord) tex2D(Name##Sampler, texCoord)
#define DECLARE_TEXTURE(Name, index) \
    texture Name: register(t##index); \
    sampler Name##Sampler: register(s##index)

#define PS_3_SHADER_COMPILER ps_3_0
#define PS_2_SHADER_COMPILER ps_2_0
#define VS_SHADER_COMPILER vs_3_0
#define VS_2_SHADER_COMPILER vs_2_0

#define SV_TARGET0 COLOR0
#define SV_TARGET1 COLOR1
#define SV_TARGET2 COLOR2

DECLARE_TEXTURE(text, 0);
DECLARE_TEXTURE(smallBufferA, 1);
DECLARE_TEXTURE(smallBufferB, 2);

float4 AddScaledDifferenceFunction(float4 inPosition : SV_Position, float4 inColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    // Sample the large buffer (automatically comes through s0)
    float4 largeValue = SAMPLE_TEXTURE(text, uv);
    
    // Sample both small buffers at the same normalized coordinates
    // (they'll be automatically scaled from 320x180 to match the large buffer)
    float4 smallA = SAMPLE_TEXTURE(smallBufferA, uv);
    float4 smallB = SAMPLE_TEXTURE(smallBufferB, uv);
    
    // Compute difference and add to large buffer
    
    return largeValue + smallA - smallB;
}

technique AddScaledDifference
{
    pass { PixelShader = compile PS_3_SHADER_COMPILER AddScaledDifferenceFunction(); }
}