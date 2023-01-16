 
 

#ifndef TERRAIN_SPLATMAP_COMMON_CGINC_INCLUDED
#define TERRAIN_SPLATMAP_COMMON_CGINC_INCLUDED
float sum4(float4 v) { return v.x + v.y + v.z + v.w; }
struct Input
{

    float2 tc_Control : TEXCOORD4;  // Not prefixing '_Contorl' with 'uv' allows a tighter packing of interpolators, which is necessary to support directional lightmap.
    UNITY_FOG_COORDS(5)
};

sampler2D _Control;
float4 _Control_ST;
 
uniform sampler2D SpaltIDTex;
uniform int SpaltIDTexSize;
uniform int AlbedoSize;
 
uniform sampler2D SplatWeights_0_1Tex;
 
 
uniform float tilesArray[16];
 
 
   
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
     half2 offsetFix =   -half2(0.5, 0.5) / SpaltIDTexSize;
 
     int4 sharedID = tex2D(SpaltIDTex, IN.tc_Control+ offsetFix)* 16 + 0.5;
     int4 id_10 = tex2D(SpaltIDTex, IN.tc_Control + offsetFix+float2(1.0,0.0)/ SpaltIDTexSize) * 16 + 0.5;
     int4 id_01 = tex2D(SpaltIDTex, IN.tc_Control + offsetFix+float2(0.0,1.0)/ SpaltIDTexSize) * 16 + 0.5;
     int4 id_11 = tex2D(SpaltIDTex, IN.tc_Control + offsetFix+float2(1.0,1.0)/ SpaltIDTexSize) * 16 + 0.5;
     splat_control = 0;
 

     weight = 1;
 
  
    

 
    
    
         //计算双线性插值
         float4 mixedWeight = 0;
         //采样器精度是half 所以有1.0f/512的偏差 不修正这个会有接缝 https://www.reedbeta.com/blog/texture-gathers-and-coordinate-precision/
         const float offsetBilinearFix =   1.0f / 512;
         half2 uv_frac = frac( IN.tc_Control  * SpaltIDTexSize-0.5+ offsetBilinearFix);
   

             float4 weight00 = tex2D(SplatWeights_0_1Tex, IN.tc_Control + offsetFix);

             float4 weight10 = tex2D(SplatWeights_0_1Tex, IN.tc_Control + offsetFix + float2(1.0, 0.0) / SpaltIDTexSize);
             float4 weight01 = tex2D(SplatWeights_0_1Tex, IN.tc_Control + offsetFix + float2(0.0, 1.0) / SpaltIDTexSize);
             float4 weight11 = tex2D(SplatWeights_0_1Tex, IN.tc_Control + offsetFix + float2(1.0, 1.0) / SpaltIDTexSize);
    
         float4 weight4 = 0;
        
         float4 martchWeight10;
         float4 martchWeight01;
         float4 martchWeight11;
        
         int4 id_00 = sharedID;
         martchWeight10.x = sum4((id_00.rrrr == id_10.rgba ? 1 : 0) * weight10);
         martchWeight01.x = sum4((id_00.rrrr == id_01.rgba ? 1 : 0) * weight01);
         martchWeight11.x = sum4((id_00.rrrr == id_11.rgba ? 1 : 0) * weight11);
  
         martchWeight10.y = sum4((id_00.gggg == id_10.rgba ? 1 : 0) * weight10);
         martchWeight01.y = sum4((id_00.gggg == id_01.rgba ? 1 : 0) * weight01);
         martchWeight11.y = sum4((id_00.gggg == id_11.rgba ? 1 : 0) * weight11);
 
         martchWeight10.z = sum4((id_00.bbbb == id_10.rgba ? 1 : 0) * weight10);
         martchWeight01.z = sum4((id_00.bbbb == id_01.rgba ? 1 : 0) * weight01);
         martchWeight11.z = sum4((id_00.bbbb == id_11.rgba ? 1 : 0) * weight11);
          
 
         martchWeight10.w = sum4((id_00.aaaa == id_10.rgba ? 1 : 0) * weight10);
         martchWeight01.w = sum4((id_00.aaaa == id_01.rgba ? 1 : 0) * weight01);
         martchWeight11.w = sum4((id_00.aaaa == id_11.rgba ? 1 : 0) * weight11);
         mixedWeight = lerp(lerp(weight00, martchWeight10, uv_frac.x), lerp(martchWeight01, martchWeight11, uv_frac.x), uv_frac.y);
      
        weight = dot(mixedWeight, half4(1, 1, 1, 1));
  
         mixedWeight  /= (weight + 1e-3f); 
       

         
         float2 dx = ddx(IN.tc_Control);
         float2 dy = ddy(IN.tc_Control);
         float4 tiles = float4(tilesArray[sharedID.r], tilesArray[sharedID.g], tilesArray[sharedID.b], tilesArray[sharedID.a]);
#ifdef UNITY_SAMPLE_TEX2DARRAY_GRAD
         //计算ddx ddy 用grad采样
         half3 color0 =  UNITY_SAMPLE_TEX2DARRAY_GRAD(AlbedoAtlas, float3(IN.tc_Control * tiles.r, sharedID.r), dx * tiles.x, dy * tiles.x);
         half3 color1 = UNITY_SAMPLE_TEX2DARRAY_GRAD(AlbedoAtlas, float3(IN.tc_Control * tiles.g, sharedID.g), dx* tiles.y, dy * tiles.y);
         half3 color2 = UNITY_SAMPLE_TEX2DARRAY_GRAD(AlbedoAtlas, float3(IN.tc_Control * tiles.b, sharedID.b), dx* tiles.z, dy * tiles.z);
         half3 color3 = UNITY_SAMPLE_TEX2DARRAY_GRAD(AlbedoAtlas, float3(IN.tc_Control * tiles.a, sharedID.a), dx* tiles.w, dy * tiles.w);
         
         half4 normal0 = UNITY_SAMPLE_TEX2DARRAY_GRAD(NormalAtlas, float3(IN.tc_Control * tiles.r, sharedID.r), dx * tiles.x, dy * tiles.x);
         half4 normal1 = UNITY_SAMPLE_TEX2DARRAY_GRAD(NormalAtlas, float3(IN.tc_Control * tiles.g, sharedID.g), dx * tiles.y, dy * tiles.y);
         half4 normal2 = UNITY_SAMPLE_TEX2DARRAY_GRAD(NormalAtlas, float3(IN.tc_Control * tiles.b, sharedID.b), dx * tiles.z, dy * tiles.z);
         half4 normal3 = UNITY_SAMPLE_TEX2DARRAY_GRAD(NormalAtlas, float3(IN.tc_Control * tiles.a, sharedID.a), dx * tiles.w, dy * tiles.w);
#else
         //手动计算mipmap lod模式
         float4 md = max(dot(dx, dx), dot(dy, dy))* AlbedoSize* AlbedoSize* tiles* tiles;
         float4 mipmap4 = max(0.5 * log2(md)-1,0)  ;
         half3 color0 = UNITY_SAMPLE_TEX2DARRAY_LOD(AlbedoAtlas, float3(IN.tc_Control * tiles.r, sharedID.r), mipmap4.r);
         half3 color1 = UNITY_SAMPLE_TEX2DARRAY_LOD(AlbedoAtlas, float3(IN.tc_Control * tiles.g, sharedID.g),mipmap4.g);
         half3 color2 = UNITY_SAMPLE_TEX2DARRAY_LOD(AlbedoAtlas, float3(IN.tc_Control * tiles.b, sharedID.b),mipmap4.b);
         half3 color3 = UNITY_SAMPLE_TEX2DARRAY_LOD(AlbedoAtlas, float3(IN.tc_Control * tiles.a, sharedID.a),mipmap4.a);

         half4 normal0 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalAtlas, float3(IN.tc_Control * tiles.r, sharedID.r), mipmap4.r);
         half4 normal1 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalAtlas, float3(IN.tc_Control * tiles.g, sharedID.g), mipmap4.g);
         half4 normal2 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalAtlas, float3(IN.tc_Control * tiles.b, sharedID.b), mipmap4.b);
         half4 normal3 = UNITY_SAMPLE_TEX2DARRAY_LOD(NormalAtlas, float3(IN.tc_Control * tiles.a, sharedID.a), mipmap4.a);
#endif    
   
         mixedDiffuse.rgb = color0 * mixedWeight.x + color1 * mixedWeight.y + color2 * mixedWeight.z  +color3 * mixedWeight.w;
         mixedDiffuse.a = 0;//smoothness


 

     
         

     
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
