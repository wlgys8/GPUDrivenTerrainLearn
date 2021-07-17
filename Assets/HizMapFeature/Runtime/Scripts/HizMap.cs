#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
//mac上支持将同一张贴图的不同mips同时作为输入输出。
//但是在win平台上不支持，因此需要使用两张RT进行PingPong模式来生成
//其他平台暂未确认
#define PING_PONG_COPY
#endif

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
            #if PING_PONG_COPY
            computeShader.EnableKeyword("_PING_PONG_COPY");
            #endif
        }

        public RenderTexture hizTexture{
            get{
                return _hizmap;
            }
        }


        private void GetTempHizMapTexture(int nameId,int size,CommandBuffer commandBuffer){
            var desc = new RenderTextureDescriptor(size,size,RenderTextureFormat.RFloat,0,1);
            desc.autoGenerateMips = false;
            desc.useMipMap = false;
            desc.enableRandomWrite = true;
            commandBuffer.GetTemporaryRT(nameId,desc,FilterMode.Point);
        }

        private RenderTexture GetTempHizMapTexture(int size,int mipCount){
            var desc = new RenderTextureDescriptor(size,size,RenderTextureFormat.RFloat,0,mipCount);
            var rt = RenderTexture.GetTemporary(desc);
            rt.autoGenerateMips = false;
            rt.useMipMap = mipCount > 1;
            rt.filterMode = FilterMode.Point;
            rt.enableRandomWrite = true;
            rt.Create();
            return rt;
        }

        private RenderTexture EnsureHizMap(Camera camera){
            var preferMapSize = GetHiZMapSize(camera);
            if(_hizmap && _hizmap.width == preferMapSize && _hizmap.height == preferMapSize){
                return _hizmap;
            }
            if(_hizmap){
                RenderTexture.ReleaseTemporary(_hizmap);
            }
            var mipCount = (int)Mathf.Log(preferMapSize,2) + 1;
            _hizmap = GetTempHizMapTexture(preferMapSize,mipCount);
            return _hizmap;
        }

        private const int KERNEL_BLIT = 0;
        private const int KERNEL_REDUCE = 1;

        /// <summary>
        /// 生成HizMap
        /// </summary>
        public void Update(ScriptableRenderContext context, Camera camera){
            var hizMap = this.EnsureHizMap(camera);
            _commandBuffer.Clear(); 
            var dstWidth = hizMap.width;
            var dstHeight = hizMap.height;
            uint threadX,threadY,threadZ;
            _computeShader.GetKernelThreadGroupSizes(KERNEL_BLIT,out threadX,out threadY,out threadZ);
            //blit begin
            _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_BLIT,ShaderConstants.InTex,ShaderConstants.CameraDepthTexture);
            _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_BLIT,ShaderConstants.MipTex,hizMap,0);

            _commandBuffer.SetComputeVectorParam(_computeShader,ShaderConstants.SrcTexSize,new Vector4(camera.pixelWidth,camera.pixelHeight,0,0));
            _commandBuffer.SetComputeVectorParam(_computeShader,ShaderConstants.DstTexSize,new Vector4(dstWidth,dstHeight,0,0));

            #if PING_PONG_COPY
            GetTempHizMapTexture(ShaderConstants.PingTex,hizMap.width,_commandBuffer);
            _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_BLIT,ShaderConstants.MipCopyTex,ShaderConstants.PingTex,0);
            #endif

            var groupX = Mathf.CeilToInt(dstWidth * 1.0f / threadX);
            var groupY = Mathf.CeilToInt(dstHeight * 1.0f / threadY);
            _commandBuffer.DispatchCompute(_computeShader,KERNEL_BLIT,groupX,groupY,1);
            //blit end

            //mip begin
            _computeShader.GetKernelThreadGroupSizes(KERNEL_REDUCE,out threadX,out threadY,out threadZ);
            #if PING_PONG_COPY
            _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_REDUCE,ShaderConstants.InTex,ShaderConstants.PingTex);
            #else
            _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_REDUCE,ShaderConstants.InTex,hizMap);
            #endif

            int pingTex = ShaderConstants.PingTex;
            int pongTex = ShaderConstants.PongTex;

            for(var i = 1; i < hizMap.mipmapCount;i++){
                dstWidth = Mathf.CeilToInt(dstWidth / 2.0f);
                dstHeight = Mathf.CeilToInt(dstHeight / 2.0f);
                _commandBuffer.SetComputeVectorParam(_computeShader,ShaderConstants.DstTexSize,new Vector4(dstWidth,dstHeight,0,0));
                _commandBuffer.SetComputeIntParam(_computeShader,ShaderConstants.Mip,i);
                _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_REDUCE,ShaderConstants.MipTex,hizMap,i);
                #if PING_PONG_COPY
                GetTempHizMapTexture(pongTex,dstWidth,_commandBuffer);
                _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_REDUCE,ShaderConstants.MipCopyTex,pongTex,0);
                #endif
                groupX = Mathf.CeilToInt(dstWidth * 1.0f / threadX);
                groupY = Mathf.CeilToInt(dstHeight * 1.0f / threadY);
                _commandBuffer.DispatchCompute(_computeShader,KERNEL_REDUCE,groupX,groupY,1);
                
                #if PING_PONG_COPY
                //释放ping
                _commandBuffer.ReleaseTemporaryRT(pingTex);
                //将pong设置为输入
                _commandBuffer.SetComputeTextureParam(_computeShader,KERNEL_REDUCE,ShaderConstants.InTex,pongTex);
                //交换PingPong
                var temp = pingTex;
                pingTex = pongTex;
                pongTex = temp;
                #endif
            }
            //mip end
            #if PING_PONG_COPY
            _commandBuffer.ReleaseTemporaryRT(pingTex);
            #endif

            _commandBuffer.SetGlobalTexture(ShaderConstants.HizMap,hizMap);
            var matrixVP = GL.GetGPUProjectionMatrix(camera.projectionMatrix,false) * camera.worldToCameraMatrix;
            _commandBuffer.SetGlobalMatrix(ShaderConstants.HizCameraMatrixVP,matrixVP);
            _commandBuffer.SetGlobalVector(ShaderConstants.HizMapSize,new Vector4(hizMap.width,hizMap.height,hizMap.mipmapCount));
            _commandBuffer.SetGlobalVector(ShaderConstants.HizCameraPosition,camera.transform.position);
            context.ExecuteCommandBuffer(_commandBuffer);
        }

        private class ShaderConstants{
            
            public static readonly int HizCameraMatrixVP = Shader.PropertyToID("_HizCameraMatrixVP");
            public static readonly int HizCameraPosition = Shader.PropertyToID("_HizCameraPositionWS");
            public static readonly RenderTargetIdentifier CameraDepthTexture = "_CameraDepthTexture";
            public static readonly int InTex = Shader.PropertyToID("InTex");
            public static readonly int MipTex = Shader.PropertyToID("MipTex");
            public static readonly int MipCopyTex = Shader.PropertyToID("MipCopyTex");
            public static readonly int PingTex = Shader.PropertyToID("PingTex");
            public static readonly int PongTex = Shader.PropertyToID("PongTex");

            public static readonly int SrcTexSize = Shader.PropertyToID("_SrcTexSize");
            public static readonly int DstTexSize = Shader.PropertyToID("_DstTexSize");
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
