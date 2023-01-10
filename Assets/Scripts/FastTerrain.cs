using System;
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
    public Texture2D[] splatWeights;//记录 相邻4个顶点相对splatID的权重,因为不是独立顶点记录所以不会出现常见插值错误,(不同id插值权重导致错误)
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
        splatWeights = new Texture2D[4];
        var splatWeightsColors = new Color[4][];
        for (int i = 0; i < 4; i++)
        {
            splatWeights[i] = new Texture2D(wid, hei, TextureFormat.RGBA32, false, true);//这个权重图做成导入资源后可做dxt压缩
            splatWeights[i].filterMode = FilterMode.Point;
            splatWeightsColors[i] = splatWeights[i].GetPixels();
        }



        float offset = 0;// -0.5f;

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
                    Color corner00 = originSplatTexs[k].GetPixelBilinear((float)(j+ offset) / wid, (float)(i+ offset) / wid);
                    Color corner10 = originSplatTexs[k].GetPixelBilinear((float)(j+1+ offset) / wid, (float)(i+ offset) / wid);
                    Color corner01 = originSplatTexs[k].GetPixelBilinear((float)(j+ offset) / wid, (float)(i+1+ offset) / wid);
                    Color corner11 = originSplatTexs[k].GetPixelBilinear((float)(j+1+ offset) / wid, (float)(i+1+ offset) / wid);
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

                Vector4 lostWeight = Vector4.zero;

                for (int k = 4; k < originSplatTexs.Count * 4; k++)
                {
                    int layer;
                    int channel;
                    //权重只记录前4张 所以需要统计丢弃部分的权重 并平均加到前4张上
                    getWeightLayerAndChannel(splatDatas[k].id, out layer, out channel);
                 
                     

                    Color corner00 = originSplatTexs[layer].GetPixelBilinear((float)(j+ offset) / wid, (float)(i+ offset) / wid);
                    Color corner10 = originSplatTexs[layer].GetPixelBilinear((float)(j+1+ offset) / wid, (float)(i+ offset) / wid);
                    Color corner01 = originSplatTexs[layer].GetPixelBilinear((float)(j+ offset) / wid, (float)(i+1+ offset) / wid);
                    Color corner11 = originSplatTexs[layer].GetPixelBilinear((float)(j + 1 + offset) / wid, (float)(i + 1 + offset) / wid);

                    lostWeight +=new  Vector4( corner00[channel], corner10[channel], corner01[channel], corner11[channel]);


                }
                //因为丢弃的部分占比较小 所以 平均加到前4个权重上就可以,测试过这里按比例加没任何画面区别
                lostWeight /= 4;

                for (int k = 0; k < 4; k++)
                {
                    int layer;
                    int channel;

                    getWeightLayerAndChannel(splatDatas[k].id, out layer, out channel);
                    
                    Color corner00 = originSplatTexs[layer].GetPixelBilinear((float)(j+ offset) / wid, (float)(i+ offset) / wid);
                    Color corner10 = originSplatTexs[layer].GetPixelBilinear((float)(j+1+ offset) / wid, (float)(i+ offset) / wid);
                    Color corner01 = originSplatTexs[layer].GetPixelBilinear((float)(j+ offset) / wid, (float)(i+1+ offset) / wid);
                    Color corner11 = originSplatTexs[layer].GetPixelBilinear((float)(j + 1 + offset) / wid, (float)(i + 1 + offset) / wid);
                    splatWeightsColors[k][index] = new Vector4(corner00[channel], corner10[channel], corner01[channel], corner11[channel]) + lostWeight;

                }


            }
        }


        splatID.SetPixels(splatIDColors);
        splatID.Apply();

        for (int k = 0; k < 4; k++)
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
        for (int k = 0; k < 4; k++)
        {
            Shader.SetGlobalTexture("SplatWeights" + k + "Tex", splatWeights[k]);
        }

        Shader.SetGlobalTexture("AlbedoAtlas", albedoAtlas);
        Shader.SetGlobalTexture("NormalAtlas", normalAtlas);

        Shader.SetGlobalFloatArray("tilesArray", tilesArray);
        Shader.SetGlobalInt("SpaltIDTexSize", splatID.width);
        Shader.SetGlobalInt("WeightMipmapScale",  1 << weightMipmap);



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