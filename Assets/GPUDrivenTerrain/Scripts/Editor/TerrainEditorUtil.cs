using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace GPUDrivenTerrainLearn
{
    
    public class TerrainEditorUtil
    {

        public static string GetSelectedDir(){
            string path = null;
            if(Selection.activeObject){
                path = AssetDatabase.GetAssetPath(Selection.activeObject);
            }
            if(!string.IsNullOrEmpty(path)){
                if(!System.IO.Directory.Exists(path)){
                    path = System.IO.Path.GetDirectoryName(path);
                }
            }
            if(string.IsNullOrEmpty(path)){
                path = "Assets";
            }
            return path;
        }

        [MenuItem("Assets/Create/GPUDrivenTerrainLearn/CreatePlaneMesh")]
        public static void CreatePlaneMeshAsset(){
            var mesh = MeshUtility.CreatePlaneMesh(16);
            string path = GetSelectedDir();
            path += "/Plane.mesh";
            AssetDatabase.CreateAsset(mesh,path);
            AssetDatabase.Refresh();
        }



        [MenuItem("Assets/Create/GPUDrivenTerrainLearn/GenerateNormalMapFromHeightMap")]
        public static void GenerateNormalMapFromHeightMap(){
            if(Selection.activeObject is Texture2D  heightMap){
                GenerateNormalMapFromHeightMap(heightMap,(normalMap)=>{
                    
                });
            }else{
                Debug.LogWarning("必须选中Texture2D");
            }
        }


        public static void GenerateNormalMapFromHeightMap(Texture2D heightMap,System.Action<Texture2D> callback){
            var rtdesc = new RenderTextureDescriptor(heightMap.width,heightMap.height,RenderTextureFormat.RG32);
            rtdesc.enableRandomWrite = true;
            var rt = RenderTexture.GetTemporary(rtdesc);
            ComputeShader computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GPUDrivenTerrain/Shader/HeightToNormal.compute");
            computeShader.SetTexture(0,Shader.PropertyToID("HeightTex"),heightMap,0);
            computeShader.SetTexture(0,Shader.PropertyToID("NormalTex"),rt,0);
            uint tx,ty,tz;
            computeShader.GetKernelThreadGroupSizes(0,out tx,out ty,out tz);
            computeShader.SetVector("TexSize",new Vector4(heightMap.width,heightMap.height,0,0));
            computeShader.SetVector("WorldSize",new Vector3(10240,2048,10240));
            computeShader.Dispatch(0,(int)(heightMap.width / tx),(int)(heightMap.height/ty),1);
            var req = AsyncGPUReadback.Request(rt,0,0,rt.width,0,rt.height,0,1,(res)=>{
                if(res.hasError){
                    Debug.LogError("error");
                }else{
                    Debug.Log("success");
                    SaveRenderTextureTo(rt,"Assets/GPUDrivenTerrain/Textures/TerrainNormal.png");
                }
                RenderTexture.ReleaseTemporary(rt);
                callback(null);
            });
            UpdateGPUAsyncRequest(req);
        }

        public static void UpdateGPUAsyncRequest(AsyncGPUReadbackRequest req){
            EditorApplication.CallbackFunction callUpdate = null;
            callUpdate = ()=>{
                if(req.done){
                    return;
                }
                req.Update();
                EditorApplication.delayCall += callUpdate;
            };
            callUpdate();
        }

        public static Texture2D ConvertToTexture2D(RenderTexture renderTexture,TextureFormat format){
            var original = RenderTexture.active;
            RenderTexture.active = renderTexture;
            var tex = new Texture2D(renderTexture.width,renderTexture.height,format,0,false);
            tex.filterMode = renderTexture.filterMode;
            tex.ReadPixels(new Rect(0,0,tex.width,tex.height),0,0,false);
            tex.Apply(false,false);
            RenderTexture.active = original;
            return tex;
        }

        public static void SaveRenderTextureTo(RenderTexture renderTexture,string path){
            var tex = ConvertToTexture2D(renderTexture,TextureFormat.ARGB32);
            var bytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path,bytes);
            AssetDatabase.Refresh();
        }
    }
}
