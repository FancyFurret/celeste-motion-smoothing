#define DECLARE_TEXTURE(Name, index) \
    texture Name: register(t##index); \
    sampler Name##Sampler: register(s##index)

#define SAMPLE_TEXTURE(Name, texCoord) tex2D(Name##Sampler, texCoord)

//-----------------------------------------------------------------------------
// Globals.
//-----------------------------------------------------------------------------

DECLARE_TEXTURE(text, 0);
DECLARE_TEXTURE(map, 1);
uniform float anxiety = 0.0;
uniform float2 anxietyOrigin = float2(0.5, 0.5);
uniform float gamerate = 1.0;
uniform float waterSine = 0.0;
uniform float waterCameraY = 0.0;
uniform float waterAlpha = 1.0;
uniform float2 cameraOffset = float2(0.0, 0.0);

//-----------------------------------------------------------------------------
// Pixel Shaders.
//-----------------------------------------------------------------------------

float PixelateFloat(float value)
{
    return floor(value * 320.0) / 320.0;
}

float2 PixelateFloat2(float2 value)
{
    return float2(PixelateFloat(value.x), PixelateFloat(value.y));
}

float2 GetDisplacement(float2 texcoord)
{
    // normal displacement
    float4 displacementPixel = SAMPLE_TEXTURE(map, texcoord);
    float2 position = texcoord;
    position.x += (displacementPixel.r * 2.0 - 1.0) * 0.044;
    position.y += (displacementPixel.g * 2.0 - 1.0) * 0.078;

    // water shifting stuff
    // amount of BLUE describes how FAST it should wave (range 0.0 -> 1.0)
    float y = floor(texcoord.y * 180.0 + cameraOffset.y) / 180.0;
    float shift = waterAlpha * sin((y * 180.0 + waterCameraY) * 0.3 - waterSine * displacementPixel.b + 3) * 0.004;
    position.x += PixelateFloat(shift * ceil(displacementPixel.b));
    
    return position;
}

float4 GetAnxietyColor(float2 texcoord)
{
    float2 pixelatedTexcoord = PixelateFloat2(texcoord);
    
    // get anxiety amount
    float len = length(pixelatedTexcoord - anxietyOrigin) * 2.0;
    float anx = 0.02 * len * anxiety;

    // offset R & B samples by anxiety amount
    float4 r = SAMPLE_TEXTURE(text, pixelatedTexcoord + float2(anx, 0.0));
    float4 g = SAMPLE_TEXTURE(text, pixelatedTexcoord + float2(0.0, 0.0));
    float4 b = SAMPLE_TEXTURE(text, pixelatedTexcoord + float2(-anx, 0.0));
    
    return float4(r.x, g.y, b.z, b.w);
}

float4 GetGrayscaleColor(float4 color)
{
    // gamerate -> black & white
    float gray = float(color.r * 0.3 + color.g * 0.59 + color.b * 0.11);
    return lerp(color, float4(gray, gray, gray, color.w), 1 - gamerate);
}

// displacement and anxiety
float4 PS_Distort(float4 inPosition : SV_Position, float4 inColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    return GetGrayscaleColor(GetAnxietyColor(GetDisplacement(uv)));
}

// only do displacement
float4 PS_Displace(float4 inPosition : SV_Position, float4 inColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    return SAMPLE_TEXTURE(text, GetDisplacement(uv));
}

//-----------------------------------------------------------------------------
// Techniques.
//-----------------------------------------------------------------------------

technique Distort
{
    pass
    {
        PixelShader = compile ps_2_0 PS_Distort();
    }
}

technique Displace
{
    pass
    {
        PixelShader = compile ps_2_0 PS_Displace();
    }
}