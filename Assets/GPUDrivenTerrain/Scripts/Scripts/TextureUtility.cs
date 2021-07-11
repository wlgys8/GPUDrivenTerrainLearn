using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUDrivenTerrainLearn
{
    public class TextureUtility
    {

        public static RenderTexture CreateRenderTextureWithMipTextures(Texture2D[] mipmaps,RenderTextureFormat format){
            var mip0 = mipmaps[0];
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(mip0.width,mip0.height,format,0,mipmaps.Length);
            descriptor.autoGenerateMips = false;
            descriptor.useMipMap = true;
            RenderTexture rt = new RenderTexture(descriptor);
            rt.filterMode = mip0.filterMode;
            rt.Create();
            for(var i = 0; i < mipmaps.Length; i ++){
                Graphics.CopyTexture(mipmaps[i],0,0,rt,0,i);
            }
            return rt;
        }

        public static RenderTexture CreateLODMap(int size){
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(size,size,RenderTextureFormat.R8,0,1);
            descriptor.autoGenerateMips = false;
            descriptor.enableRandomWrite = true;
            RenderTexture rt = new RenderTexture(descriptor);
            rt.filterMode = FilterMode.Point;
            rt.Create();
            return rt;
        }
     
    }
}
