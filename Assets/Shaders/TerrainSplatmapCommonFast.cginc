 
 

#ifndef TERRAIN_SPLATMAP_COMMON_CGINC_INCLUDED
#define TERRAIN_SPLATMAP_COMMON_CGINC_INCLUDED

struct Input
{

    float2 tc_Control : TEXCOORD4;  // Not prefixing '_Contorl' with 'uv' allows a tighter packing of interpolators, which is necessary to support directional lightmap.
    UNITY_FOG_COORDS(5)
};

sampler2D _Control;
float4 _Control_ST;
 
uniform sampler2D SpaltIDTex;
uniform int SpaltIDTexSize;
 
uniform sampler2D SplatWeights0Tex;
uniform sampler2D SplatWeights1Tex;
uniform sampler2D SplatWeights2Tex;
uniform sampler2D SplatWeights3Tex;
uniform float tilesArray[16];
uniform int WeightMipmapScale;
 
   
UNITY_DECLARE_TEX2DARRAY(AlbedoAtlas);
UNITY_DECLARE_TEX2DARRAY(NormalAtlas);
UNITY_DECLARE_TEX2DARRAY(SpaltWeightTex);


 

void SplatmapVert(inout appdata_full v, out Input data)
{
    UNITY_INITIALIZE_OUTPUT(Input, data);
    data.tc_Control = TRANSFORM_TEX(v.texcoord, _Control);  // Need to manually transform uv here, as we choose not to use 'uv' prefix for this texcoord.
    float4 pos = UnityObjectToClipPos(v.vertex);
    UNITY_TRANSFER_FOG(data, pos);


    v.tangent.xyz = cross(v.normal, float3(0,0,1));
    v.tangent.w = -1;

}
 

#ifdef TERRAIN_STANDARD_SHADER
 void SplatmapMix(Input IN, half4 defaultAlpha, out half4 splat_control, out half weight, out fixed4 mixedDiffuse, inout fixed3 mixedNormal)
#else
 void SplatmapMix(Input IN, out float4 splat_control, out half weight, out fixed4 mixedDiffuse, inout fixed3 mixedNormal)
#endif
 {
     half2 offsetFix = -half2(0.5, 0.5) / SpaltIDTexSize;
     half2 offsetBilinearFix = -half2(0.5, 0.5) /(SpaltIDTexSize+4.0/WeightMipmapScale);
     int4 sharedID = tex2D(SpaltIDTex, IN.tc_Control+ offsetFix)* 16 + 0.5;
 
     splat_control = 0;
 

     weight = 1;
 
  
    
     //计算第一重要 相邻4个点颜色
 
     half3 color0 =  UNITY_SAMPLE_TEX2DARRAY(AlbedoAtlas, float3(IN.tc_Control * tilesArray[sharedID.r], sharedID.r));
     half3 color1 =  UNITY_SAMPLE_TEX2DARRAY(AlbedoAtlas, float3(IN.tc_Control * tilesArray[sharedID.g], sharedID.g));
     half3 color2 =  UNITY_SAMPLE_TEX2DARRAY(AlbedoAtlas, float3(IN.tc_Control * tilesArray[sharedID.b], sharedID.b));
     half3 color3 =  UNITY_SAMPLE_TEX2DARRAY(AlbedoAtlas, float3(IN.tc_Control * tilesArray[sharedID.a], sharedID.a));
 
    
    
         //计算双线性插值
        float4 mixedWeight = 0;
         half2 uv_frac = frac((IN.tc_Control + offsetBilinearFix) * SpaltIDTexSize);
         float4 weight4 = tex2D(SplatWeights0Tex, IN.tc_Control + offsetFix);;
         mixedWeight.x = lerp(lerp(weight4.r, weight4.g, uv_frac.x), lerp(weight4.b, weight4.a, uv_frac.x), uv_frac.y);
         weight4 = tex2D(SplatWeights1Tex, IN.tc_Control + offsetFix);
         mixedWeight.y= lerp(lerp(weight4.r, weight4.g, uv_frac.x), lerp(weight4.b, weight4.a, uv_frac.x), uv_frac.y);
         weight4 = tex2D(SplatWeights2Tex, IN.tc_Control + offsetFix);
         mixedWeight.z = lerp(lerp(weight4.r, weight4.g, uv_frac.x), lerp(weight4.b, weight4.a, uv_frac.x), uv_frac.y);
         weight4 = tex2D(SplatWeights3Tex, IN.tc_Control + offsetFix);
         mixedWeight.w = lerp(lerp(weight4.r, weight4.g, uv_frac.x), lerp(weight4.b, weight4.a, uv_frac.x), uv_frac.y);
     
        
         weight = dot(mixedWeight, half4(1, 1, 1, 1));
  
         mixedWeight  /= (weight + 1e-3f);
       
         
         mixedDiffuse.rgb = color0 * mixedWeight.x + color1 * mixedWeight.y + color2 * mixedWeight.z  +color3 * mixedWeight.w;
         mixedDiffuse.a = 0;//smoothness


         

         half4 normal0 = UNITY_SAMPLE_TEX2DARRAY(NormalAtlas, float3(IN.tc_Control * tilesArray[sharedID.r], sharedID.r));
         half4 normal1 = UNITY_SAMPLE_TEX2DARRAY(NormalAtlas, float3(IN.tc_Control * tilesArray[sharedID.g], sharedID.g));
         half4 normal2 = UNITY_SAMPLE_TEX2DARRAY(NormalAtlas, float3(IN.tc_Control * tilesArray[sharedID.b], sharedID.b));
         half4 normal3= UNITY_SAMPLE_TEX2DARRAY(NormalAtlas, float3(IN.tc_Control * tilesArray[sharedID.a], sharedID.a));
     
 

     
         half4 nrm = normal0 * mixedWeight.x + normal1 * mixedWeight.y + normal2 * mixedWeight.z +normal3 * mixedWeight.w;
         mixedNormal =   UnpackNormal(nrm); 
        


 }
 

#ifndef TERRAIN_SURFACE_OUTPUT
    #define TERRAIN_SURFACE_OUTPUT SurfaceOutput
#endif

void SplatmapFinalColor(Input IN, TERRAIN_SURFACE_OUTPUT o, inout fixed4 color)
{
    color *= o.Alpha;
    #ifdef TERRAIN_SPLAT_ADDPASS
        UNITY_APPLY_FOG_COLOR(IN.fogCoord, color, fixed4(0,0,0,0));
    #else
        UNITY_APPLY_FOG(IN.fogCoord, color);
    #endif
}

void SplatmapFinalPrepass(Input IN, TERRAIN_SURFACE_OUTPUT o, inout fixed4 normalSpec)
{
    normalSpec *= o.Alpha;
}

void SplatmapFinalGBuffer(Input IN, TERRAIN_SURFACE_OUTPUT o, inout half4 outGBuffer0, inout half4 outGBuffer1, inout half4 outGBuffer2, inout half4 emission)
{
    UnityStandardDataApplyWeightToGbuffer(outGBuffer0, outGBuffer1, outGBuffer2, o.Alpha);
    emission *= o.Alpha;
}

#endif // TERRAIN_SPLATMAP_COMMON_CGINC_INCLUDED
