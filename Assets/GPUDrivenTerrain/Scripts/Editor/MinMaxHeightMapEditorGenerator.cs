using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace GPUDrivenTerrainLearn
{
    public class MinMaxHeightMapEditorGenerator
    {
        private static ComputeShader _computeShader;
        private const int patchMapSize = 1280;

        private static ComputeShader computeShader{
            get{
                if(!_computeShader){
                    _computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GPUDrivenTerrain/Shader/MinMaxHeights.compute");
                }
                return _computeShader;
            }
        }
        private Texture2D _heightmap;
        private string _dir;
        
        public MinMaxHeightMapEditorGenerator(Texture2D heightMap){
            _heightmap = heightMap;
        }

        private RenderTexture CreateMinMaxHeightTexture(int texSize){
            RenderTextureDescriptor desc = new RenderTextureDescriptor(texSize,texSize,RenderTextureFormat.RG32,0,1);
            desc.enableRandomWrite = true;
            desc.autoGenerateMips = false;
            var rt = RenderTexture.GetTemporary(desc);
            rt.filterMode = FilterMode.Point;
            rt.Create();
            return rt;
        }

        private void CalcuateGroupXY(int kernelIndex,int textureSize,out int groupX,out int groupY){
            uint threadX,threadY,threadZ;
            computeShader.GetKernelThreadGroupSizes(kernelIndex,out threadX,out threadY,out threadZ);
            groupX = (int)(textureSize / threadX);
            groupY = (int)(textureSize / threadY);
        }

        private void WaitRenderTexture(RenderTexture renderTexture,System.Action<RenderTexture> callback){
            var request = AsyncGPUReadback.Request(renderTexture,0,TextureFormat.RG32,(res)=>{
                callback(renderTexture);
            });
            TerrainEditorUtil.UpdateGPUAsyncRequest(request);    
        }
     

        private void SaveMipTextures(List<RenderTexture> mipTextures){
            for(var i = 0; i < mipTextures.Count; i ++){
                var path = GetMipTexPath(i);
                var tex2D = TerrainEditorUtil.ConvertToTexture2D(mipTextures[i],TextureFormat.RG32);
                var bytes = tex2D.EncodeToPNG();
                System.IO.File.WriteAllBytes(path,bytes);
            }
            AssetDatabase.Refresh();
        }

        private void EnsureDir(){
            var heightMapPath = AssetDatabase.GetAssetPath(_heightmap);
            var dir = System.IO.Path.GetDirectoryName(heightMapPath);
            var heightMapName = System.IO.Path.GetFileNameWithoutExtension(heightMapPath);
            _dir = $"{dir}/{heightMapName}";
            if(!System.IO.Directory.Exists(_dir)){
                System.IO.Directory.CreateDirectory(_dir);
            }
        }

        private string GetMipTexPath(int mipIndex){
            var path = $"{_dir}/MinMaxHeight_{mipIndex}.png";
            return path;
        }
        private void GeneratePatchMinMaxHeightTexMip0(System.Action<RenderTexture> callback){
            int kernelIndex = 0;
            var minMaxHeightTex = CreateMinMaxHeightTexture(patchMapSize);
            int groupX,groupY;
            CalcuateGroupXY(kernelIndex,patchMapSize,out groupX,out groupY);
            computeShader.SetTexture(kernelIndex,"HeightTex",_heightmap);
            computeShader.SetTexture(kernelIndex,"PatchMinMaxHeightTex",minMaxHeightTex);
            computeShader.Dispatch(kernelIndex,groupX,groupY,1);
            WaitRenderTexture(minMaxHeightTex,callback);
        }

        public void Generate(){
            this.EnsureDir();
            List<RenderTexture> textures = new List<RenderTexture>();
            GeneratePatchMinMaxHeightTexMip0((rt)=>{
                textures.Add(rt);
                GenerateMipMaps(9,textures,()=>{
                    SaveMipTextures(textures);
                    foreach(var rt in textures){
                        RenderTexture.ReleaseTemporary(rt);
                    }
                });
            });
        }

        private void GenerateMipMaps(int totalMips,List<RenderTexture> mipTextures,System.Action callback){
            GenerateMipMap(mipTextures[mipTextures.Count - 1],(mipTex)=>{
                mipTextures.Add(mipTex);
                if(mipTextures.Count < totalMips){
                    GenerateMipMaps(totalMips,mipTextures,callback);
                }else{
                    callback();
                }
            });
        }

        private void GenerateMipMap(RenderTexture inTex,System.Action<RenderTexture> callback){
            var kernelIndex = 1;
            var reduceTex = CreateMinMaxHeightTexture(inTex.width / 2);
            computeShader.SetTexture(kernelIndex,"InTex",inTex);
            computeShader.SetTexture(kernelIndex,"ReduceTex",reduceTex);
            int groupX,groupY;
            CalcuateGroupXY(kernelIndex,reduceTex.width,out groupX,out groupY);
            computeShader.Dispatch(kernelIndex,groupX,groupY,1);
            WaitRenderTexture(reduceTex,callback);
        }

        [MenuItem("Assets/Create/GPUDrivenTerrainLearn/GenerateMinMaxHeightMapFromSelectedHeightMap")]
        public static void GenerateMinMaxHeightMapFromSelectedHeightMap(){
            if(Selection.activeObject is Texture2D  heightMap){
                var filePath = AssetDatabase.GetAssetPath(heightMap);
                new MinMaxHeightMapEditorGenerator(heightMap).Generate();
            }
        }
    }
}
