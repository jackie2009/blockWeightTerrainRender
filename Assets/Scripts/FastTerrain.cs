﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

using UnityEngine;

public class FastTerrain : MonoBehaviour
{
    public Texture2DArray albedoAtlas;
    public Texture2DArray normalAtlas;

    //splat rgba存放 相邻4个顶点一起计算权重后 权重最大的4个id
    public Texture2D splatID;
    //记录 相邻4个顶点相对splatID的权重,因为不是独立顶点记录所以不会出现常见插值错误,(不同id插值权重导致错误)
    //这里用2张 rgba32 图 来存 原来4张 rgba16的图 容量上一样所以可以这样合并,但采样次数可以从4次减少到2次.如果高版本引擎这里有rgba64格式 那么一张贴图就够用,采样性能最高.否则如果用容量相同的rgbahalf存储,因为涉及复杂的int 浮点数(half)格式转换而不建议
    public Texture2D  []splatWeights;
    [Range(0, 5)]
    public int weightMipmap = 0;

    public Shader terrainShader;
    public TerrainData normalTerrainData;
    public TerrainData empytTerrainData;
    public float[] tilesArray;
#if UNITY_EDITOR
    [ContextMenu("MakeAlbedoAtlas")]

    void MakeAlbedoAtlas()
    {

        int layerCount = normalTerrainData.splatPrototypes.Length;
        print(layerCount);
        int wid = normalTerrainData.splatPrototypes[0].texture.width;
        int hei = normalTerrainData.splatPrototypes[0].texture.height;

        int widNormal = normalTerrainData.splatPrototypes[0].normalMap.width;
        int heiNormal = normalTerrainData.splatPrototypes[0].normalMap.height;
        albedoAtlas = new Texture2DArray(wid, hei, layerCount, normalTerrainData.splatPrototypes[0].texture.format, true, false);
        normalAtlas = new Texture2DArray(widNormal, heiNormal, layerCount, normalTerrainData.splatPrototypes[0].normalMap.format, true, true);

        for (int i = 0; i < layerCount; i++)
        {



            for (int k = 0; k < normalTerrainData.splatPrototypes[i].texture.mipmapCount; k++)
            {
                Graphics.CopyTexture(normalTerrainData.splatPrototypes[i].texture, 0, k, albedoAtlas, i, k);

            }
            for (int k = 0; k < normalTerrainData.splatPrototypes[i].normalMap.mipmapCount; k++)
            {
                Graphics.CopyTexture(normalTerrainData.splatPrototypes[i].normalMap, 0, k, normalAtlas, i, k);

            }


        }

        tilesArray = new float[16];//匹配shader内定长数组
        for (int i = 0; i < layerCount; i++)
        {
            tilesArray[i] = normalTerrainData.size.x / normalTerrainData.splatPrototypes[i].tileSize.x;
        }




    }


    struct SplatData
    {
        public int id;
        public float weight;
    }


    [ContextMenu("MakeSplat")]
    // Update is called once per frame
    void MakeSplat()
    {


        int scale = 1 << weightMipmap;

        int wid = normalTerrainData.alphamapTextures[0].width / scale;
        int hei = normalTerrainData.alphamapTextures[0].height / scale;
    
        List<Texture2D> originSplatTexs = new List<Texture2D>();

        for (int i = 0; i < normalTerrainData.alphamapTextures.Length; i++)
        {
   
            var tex= new Texture2D(wid, hei, TextureFormat.ARGB32, false, true);
            tex.SetPixels(normalTerrainData.alphamapTextures[i].GetPixels(weightMipmap));
            originSplatTexs.Add(tex);
        }

        splatID = new Texture2D(wid, hei, TextureFormat.RGBA32, false, true);

        splatID.filterMode = FilterMode.Point;

        var splatIDColors = splatID.GetPixels();
        splatWeights = new Texture2D[2];
        var splatWeightsColors = new Color[4][];
        for (int i = 0; i < 2; i++)
        {
            splatWeights[i] = new Texture2D(wid, hei, TextureFormat.RGBA32, false, true);//这个权重图做成导入资源后可做dxt压缩
            splatWeights[i].filterMode = FilterMode.Point;
             
        }
        for (int i = 0; i < 4; i++) splatWeightsColors[i] = new Color[wid*hei];
        for (int i = 0; i < hei; i++)
        {
            for (int j = 0; j < wid; j++)
            {
                List<SplatData> splatDatas = new List<SplatData>();
                int index = i * wid + j;



                for (int k = 0; k < originSplatTexs.Count; k++)
                {
                    SplatData sd;
                    sd.id = k * 4;
                    Color corner00 = originSplatTexs[k].GetPixel(j, i);
                    Color corner10 = originSplatTexs[k].GetPixel(j + 1, i);
                    Color corner01 = originSplatTexs[k].GetPixel(j, i + 1);
                    Color corner11 = originSplatTexs[k].GetPixel(j + 1, i + 1);
                    sd.weight = corner00.r + corner10.r + corner01.r + corner11.r;


                    splatDatas.Add(sd);
                    sd.id++;

                    sd.weight = corner00.g + corner10.g + corner01.g + corner11.g;
                    splatDatas.Add(sd);
                    sd.id++;

                    sd.weight = corner00.b + corner10.b + corner01.b + corner11.b;
                    splatDatas.Add(sd);
                    sd.id++;

                    sd.weight = corner00.a + corner10.a + corner01.a + corner11.a;
                    splatDatas.Add(sd);
                }

                Vector4 lostWeight = Vector4.zero;
                //按权排序选出相邻4个点最权重最大的ID 作为4个点都采样的公用id
                splatDatas.Sort((x, y) => -(x.weight).CompareTo(y.weight));
                for (int k = 4; k < originSplatTexs.Count * 4; k++)
                {
                    int layer;
                    int channel;
                    //权重只记录前4张 所以需要统计丢弃部分的权重 并平均加到前4张上
                    getWeightLayerAndChannel(splatDatas[k].id, out layer, out channel);



                    Color corner00 = originSplatTexs[layer].GetPixel(j, i);
                    Color corner10 = originSplatTexs[layer].GetPixel(j + 1, i);
                    Color corner01 = originSplatTexs[layer].GetPixel(j, i + 1);
                    Color corner11 = originSplatTexs[layer].GetPixel(j + 1, i + 1);
                    lostWeight += new Vector4(corner00[channel], corner10[channel], corner01[channel], corner11[channel]);
                    corner00[channel] = 0;
                    originSplatTexs[layer].SetPixel(j, i, corner00);

                    corner10[channel] = 0;
                    originSplatTexs[layer].SetPixel(j + 1, i, corner10);

                    corner01[channel] = 0;
                    originSplatTexs[layer].SetPixel(j, i + 1, corner01);

                    corner11[channel] = 0;
                    originSplatTexs[layer].SetPixel(j + 1, i + 1, corner11);
                }

                Vector4 top4Weight = Vector4.zero;
                for (int k = 0; k < 4; k++)
                {
                    int layer;
                    int channel;
                    //权重只记录前4张 所以需要统计丢弃部分的权重 并平均加到前4张上
                    getWeightLayerAndChannel(splatDatas[k].id, out layer, out channel);



                    Color corner00 = originSplatTexs[layer].GetPixel(j, i);
                    Color corner10 = originSplatTexs[layer].GetPixel(j + 1, i);
                    Color corner01 = originSplatTexs[layer].GetPixel(j, i + 1);
                    Color corner11 = originSplatTexs[layer].GetPixel(j + 1, i + 1);
                    top4Weight += new Vector4(corner00[channel], corner10[channel], corner01[channel], corner11[channel]);
                }
                for (int k = 0; k < 4; k++)
                {
                    int layer;
                    int channel;

                    getWeightLayerAndChannel(splatDatas[k].id, out layer, out channel);

                    Color corner00 = originSplatTexs[layer].GetPixel(j, i);
                    Color corner10 = originSplatTexs[layer].GetPixel(j + 1, i);
                    Color corner01 = originSplatTexs[layer].GetPixel(j, i + 1);
                    Color corner11 = originSplatTexs[layer].GetPixel(j + 1, i + 1);
                    corner00[channel] += lostWeight.x * corner00[channel] / top4Weight.x;
                    originSplatTexs[layer].SetPixel(j, i, corner00);

                    corner10[channel] += lostWeight.y * corner10[channel] / top4Weight.y;
                    originSplatTexs[layer].SetPixel(j + 1, i, corner10);

                    corner01[channel] += lostWeight.z * corner01[channel] / top4Weight.z;
                    originSplatTexs[layer].SetPixel(j, i + 1, corner01);
                    corner11[channel] += lostWeight.w * corner11[channel] / top4Weight.w;
                    originSplatTexs[layer].SetPixel(j + 1, i + 1, corner11);
                }
            }
        }

        
        for (int i = 0; i < hei; i++)
        {
            for (int j = 0; j < wid; j++)
            {
                List<SplatData> splatDatas = new List<SplatData>();
                int index = i * wid + j;
              
                //边界处没有x+ y+纹素 所以不做4顶点计算 只算自己
                int useOffset = i != hei - 1 && j != wid - 1 ? 1 : 0;
                
                for (int k = 0; k < originSplatTexs.Count; k++)
                {
                    SplatData sd;
                    sd.id = k * 4;
                    Color corner00 = originSplatTexs[k].GetPixel(j, i);
                    Color corner10 = originSplatTexs[k].GetPixel(j + 1, i);
                    Color corner01 = originSplatTexs[k].GetPixel(j, i + 1);
                    Color corner11 = originSplatTexs[k].GetPixel(j + 1, i + 1);
                    sd.weight = corner00.r + corner10.r + corner01.r + corner11.r;


                    splatDatas.Add(sd);
                    sd.id++;
                    
                    sd.weight = corner00.g + corner10.g + corner01.g + corner11.g;
                    splatDatas.Add(sd);
                    sd.id++;
                  
                    sd.weight = corner00.b + corner10.b + corner01.b + corner11.b;
                    splatDatas.Add(sd);
                    sd.id++;
                   
                    sd.weight = corner00.a + corner10.a + corner01.a + corner11.a;
                    splatDatas.Add(sd);
                }


                //按权排序选出相邻4个点最权重最大的ID 作为4个点都采样的公用id
                splatDatas.Sort((x, y) => -(x.weight).CompareTo(y.weight));
                splatIDColors[index].r = splatDatas[0].id / 16f; //
                splatIDColors[index].g = splatDatas[1].id / 16f; //
                splatIDColors[index].b = splatDatas[2].id / 16f; //
                splatIDColors[index].a = splatDatas[3].id / 16f; //

                
                for (int k = 0; k < 4; k++)
                {
                    int layer;
                    int channel;

                    getWeightLayerAndChannel(splatDatas[k].id, out layer, out channel);

                    Color corner00 = originSplatTexs[layer].GetPixel(j, i);
                    Color corner10 = originSplatTexs[layer].GetPixel(j + 1, i);
                    Color corner01 = originSplatTexs[layer].GetPixel(j, i + 1);
                    Color corner11 = originSplatTexs[layer].GetPixel(j + 1, i + 1);

                    splatWeightsColors[k][index] = new Vector4(corner00[channel], corner10[channel], corner01[channel], corner11[channel]) ;

                }
                // 合并4张图 到2张
                Vector4 w0 =splatWeightsColors[0][index];
                Vector4 w1 =splatWeightsColors[1][index];
                Vector4 w2 =splatWeightsColors[2][index];
                Vector4 w3 =splatWeightsColors[3][index];
                splatWeightsColors[0][index].r=((int)(w0.x*15+0.5f)+(int)(w0.y * 15 + 0.5f) * 16)/255.0f;
                splatWeightsColors[0][index].g = ((int)(w0.z * 15 + 0.5f) + (int)(w0.w * 15 + 0.5f) * 16) / 255.0f;
                splatWeightsColors[0][index].b = ((int)(w1.x * 15 + 0.5f) + (int)(w1.y * 15 + 0.5f) * 16) / 255.0f;
                splatWeightsColors[0][index].a = ((int)(w1.z * 15 + 0.5f) + (int)(w1.w * 15 + 0.5f) * 16) / 255.0f;

                splatWeightsColors[1][index].r = ((int)(w2.x * 15 + 0.5f) + (int)(w2.y * 15 + 0.5f) * 16) / 255.0f;
                splatWeightsColors[1][index].g = ((int)(w2.z * 15 + 0.5f) + (int)(w2.w * 15 + 0.5f) * 16) / 255.0f;
                splatWeightsColors[1][index].b = ((int)(w3.x * 15 + 0.5f) + (int)(w3.y * 15 + 0.5f) * 16) / 255.0f;
                splatWeightsColors[1][index].a = ((int)(w3.z * 15 + 0.5f) + (int)(w3.w * 15 + 0.5f) * 16) / 255.0f;

            }
        }


        splatID.SetPixels(splatIDColors);
        splatID.Apply();

        for (int k = 0; k < 2; k++)
        {
            splatWeights[k].SetPixels(splatWeightsColors[k]);
            splatWeights[k].Apply();
        }


    }
     
    private void getWeightLayerAndChannel(int id, out int layer, out int channel)
    {
        layer = id / 4;
        channel = id % 4;
    }




#endif

    [ContextMenu("UseFastMode")]
    void useFastMode()
    {
        Terrain t = GetComponent<Terrain>();
        t.terrainData = empytTerrainData;

        t.materialType = Terrain.MaterialType.Custom;

        t.materialTemplate = new Material(terrainShader);


        Shader.SetGlobalTexture("SpaltIDTex", splatID);
      
            Shader.SetGlobalTexture("SplatWeights_0_1Tex", splatWeights[0]);
            Shader.SetGlobalTexture("SplatWeights_2_3Tex", splatWeights[1]);
        

        Shader.SetGlobalTexture("AlbedoAtlas", albedoAtlas);
        Shader.SetGlobalTexture("NormalAtlas", normalAtlas);

        Shader.SetGlobalFloatArray("tilesArray", tilesArray);
        Shader.SetGlobalInt("SpaltIDTexSize", splatID.width);
        Shader.SetGlobalInt("AlbedoSize", albedoAtlas.width);
      



    }

    [ContextMenu("UseBuildinMode")]
    void useBuildinMode()
    {
        Terrain t = GetComponent<Terrain>();
        t.terrainData = normalTerrainData;
        t.materialType = Terrain.MaterialType.BuiltInStandard;
        t.materialTemplate = null;
    }
    [ContextMenu("savePngs")]
    void savePngs()
    {
        System.IO.File.WriteAllBytes(Application.dataPath + @"/splatID.png", splatID.EncodeToPNG());
        for (int i = 0; i < splatWeights.Length; i++)
        {
            System.IO.File.WriteAllBytes(Application.dataPath + @"/splatWeights" + i + ".png", splatWeights[i].EncodeToPNG());
        }

    }

    void Start() {
        fastMode = true;
        useFastMode();

    }
    private bool fastMode = false;

    private void OnGUI()
    {
        if (GUILayout.Button(fastMode ? "自定义渲染ing" : "引擎默认渲染ing"))
        {
            fastMode = !fastMode;
            if (fastMode)
            {
                useFastMode();
            }
            else
            {
                useBuildinMode();
            }
        }
    }
}