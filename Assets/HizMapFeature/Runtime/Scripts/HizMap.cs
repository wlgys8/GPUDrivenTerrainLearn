using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace GPUDrivenTerrainLearn
{
    public class HizMap
    {
        private CommandBuffer _commandBuffer;
        private RenderTexture _hizmap;

        private ComputeShader _computeShader;

        public HizMap(ComputeShader computeShader){
            _commandBuffer = new CommandBuffer();
            _commandBuffer.name = "HizMap";
            _computeShader = computeShader;
            if(SystemInfo.usesReversedZBuffer){
                computeShader.EnableKeyword("_REVERSE_Z");
            }
        }

        public RenderTexture hizTexture{
            get{
                return _hizmap;
            }
        }

        private RenderTexture GetTempHizMapTexture(Camera camera){
            var preferMapSize = GetHiZMapSize(camera);
            if(_hizmap && _hizmap.width == preferMapSize && _hizmap.height == preferMapSize){
                return _hizmap;
            }
            if(_hizmap){
                RenderTexture.ReleaseTemporary(_hizmap);
            }
            var mipCount = (int)Mathf.Log(preferMapSize,2) + 1;
            var desc = new RenderTextureDescriptor(preferMapSize,preferMapSize,RenderTextureFormat.RFloat,0,mipCount);
            var rt = RenderTexture.GetTemporary(desc);
            rt.autoGenerateMips = false;
            rt.useMipMap = true;
            rt.filterMode = FilterMode.Point;
            rt.enableRandomWrite = true;
            rt.Create();
            _hizmap = rt;
            return rt;
        }

        /// <summary>
        /// 生成HizMap
        /// </summary>
        public void Update(ScriptableRenderContext context, Camera camera){
            var hizMap = this.GetTempHizMapTexture(camera);
            _commandBuffer.Clear(); 
            _commandBuffer.Blit(ShaderConstants.CameraDepthTexture,hizMap);
            var width = hizMap.width;
            var height = hizMap.height;
            uint threadX,threadY,threadZ;
            _computeShader.GetKernelThreadGroupSizes(0,out threadX,out threadY,out threadZ);
            _commandBuffer.SetComputeTextureParam(_computeShader,0,ShaderConstants.InTex,hizMap);
            for(var i = 1; i < hizMap.mipmapCount;i++){
                width = Mathf.CeilToInt(width / 2.0f);
                height = Mathf.CeilToInt(height / 2.0f);
                _commandBuffer.SetComputeIntParam(_computeShader,ShaderConstants.Mip,i);
                _commandBuffer.SetComputeTextureParam(_computeShader,0,ShaderConstants.ReduceTex,hizMap,i);
                var groupX = Mathf.CeilToInt(width * 1.0f / threadX);
                var groupY = Mathf.CeilToInt(height * 1.0f / threadY);
                _commandBuffer.DispatchCompute(_computeShader,0,groupX,groupY,1);
            }
            _commandBuffer.SetGlobalTexture(ShaderConstants.HizMap,hizMap);
            var matrixVP = GL.GetGPUProjectionMatrix(camera.projectionMatrix,false) * camera.worldToCameraMatrix;
            _commandBuffer.SetGlobalMatrix(ShaderConstants.HizCameraMatrixVP,matrixVP);
            _commandBuffer.SetGlobalVector(ShaderConstants.HizMapSize,new Vector4(hizMap.width,hizMap.height,hizMap.mipmapCount));
            _commandBuffer.SetGlobalVector(ShaderConstants.HizCameraPosition,camera.transform.position);
            context.ExecuteCommandBuffer(_commandBuffer);
            // Debug.Log(hizMap.mipmapCount);
        }

        private class ShaderConstants{
            
            public static readonly int HizCameraMatrixVP = Shader.PropertyToID("_HizCameraMatrixVP");
            public static readonly int HizCameraPosition = Shader.PropertyToID("_HizCameraPositionWS");
            public static readonly RenderTargetIdentifier CameraDepthTexture = "_CameraDepthTexture";
            public static readonly int InTex = Shader.PropertyToID("InTex");
            public static readonly int ReduceTex = Shader.PropertyToID("ReduceTex");
            public static readonly int Mip = Shader.PropertyToID("_Mip");
            public static readonly int HizMap = Shader.PropertyToID("_HizMap");
            public static readonly int HizMapSize = Shader.PropertyToID("_HizMapSize");
            
            
        }

        public static int GetHiZMapSize(Camera camera){
            var screenSize = Mathf.Max(camera.pixelWidth,camera.pixelHeight);
            var textureSize = Mathf.NextPowerOfTwo(screenSize);
            return textureSize;
        }
    }
}
