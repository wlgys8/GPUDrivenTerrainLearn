Shader "GPUTerrainLearn/Terrain"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _HeightMap ("Texture", 2D) = "white" {}
        _NormalMap ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode" = "UniversalForward"}
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature ENABLE_MIP_DEBUG
            #pragma shader_feature ENABLE_PATCH_DEBUG
            #pragma shader_feature ENABLE_LOD_SEAMLESS
            #pragma shader_feature ENABLE_NODE_DEBUG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "./CommonInput.hlsl"

            StructuredBuffer<RenderPatch> PatchList;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                half3 color: TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _HeightMap;
            sampler2D _NormalMap;
            uniform float3 _WorldSize;
            float4x4 _WorldToNormalMapMatrix;

            static half3 debugColorForMip[6] = {
                half3(0,1,0),
                half3(0,0,1),
                half3(1,0,0),
                half3(1,1,0),
                half3(0,1,1),
                half3(1,0,1),
            };

            float3 TransformNormalToWorldSpace(float3 normal){
                return SafeNormalize(mul(normal,(float3x3)_WorldToNormalMapMatrix));
            }


            float3 SampleNormal(float2 uv){
                float3 normal;
                normal.xz = tex2Dlod(_NormalMap,float4(uv,0,0)).xy * 2 - 1;
                normal.y = sqrt(max(0,1 - dot(normal.xz,normal.xz)));
                normal = TransformNormalToWorldSpace(normal);
                return normal;
            }


            //修复接缝
            void FixLODConnectSeam(inout float4 vertex,inout float2 uv,RenderPatch patch){
                uint4 lodTrans = patch.lodTrans;
                uint2 vertexIndex = floor((vertex.xz + PATCH_MESH_SIZE * 0.5 + 0.01) / PATCH_MESH_GRID_SIZE);
                float uvGridStrip = 1.0/PATCH_MESH_GRID_COUNT;

                uint lodDelta = lodTrans.x;
                if(lodDelta > 0 && vertexIndex.x == 0){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.y % gridStripCount;
                    if(modIndex != 0){
                        vertex.z -= PATCH_MESH_GRID_SIZE * modIndex;
                        uv.y -= uvGridStrip * modIndex;
                        return;
                    }
                }

                lodDelta = lodTrans.y;
                if(lodDelta > 0 && vertexIndex.y == 0){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.x % gridStripCount;
                    if(modIndex != 0){
                        vertex.x -= PATCH_MESH_GRID_SIZE * modIndex;
                        uv.x -= uvGridStrip * modIndex;
                        return;
                    }
                }

                lodDelta = lodTrans.z;
                if(lodDelta > 0 && vertexIndex.x == PATCH_MESH_GRID_COUNT){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.y % gridStripCount;
                    if(modIndex != 0){
                        vertex.z += PATCH_MESH_GRID_SIZE * (gridStripCount - modIndex);
                        uv.y += uvGridStrip * (gridStripCount- modIndex);
                        return;
                    }
                }

                lodDelta = lodTrans.w;
                if(lodDelta > 0 && vertexIndex.y == PATCH_MESH_GRID_COUNT){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.x % gridStripCount;
                    if(modIndex != 0){
                        vertex.x += PATCH_MESH_GRID_SIZE * (gridStripCount- modIndex);
                        uv.x += uvGridStrip * (gridStripCount- modIndex);
                        return;
                    }
                }
            }

            //在Node之间留出缝隙供Debug
            float3 ApplyNodeDebug(RenderPatch patch,float3 vertex){
                uint nodeCount = (uint)(5 * pow(2,5 - patch.lod));
                float nodeSize = _WorldSize.x / nodeCount;
                uint2 nodeLoc = floor((patch.position + _WorldSize.xz * 0.5) / nodeSize);
                float2 nodeCenterPosition = - _WorldSize.xz * 0.5 + (nodeLoc + 0.5) * nodeSize ;
                vertex.xz = nodeCenterPosition + (vertex.xz - nodeCenterPosition) * 0.95;
                return vertex;
            }
uniform float4x4 _HizCameraMatrixVP;
            float3 TransformWorldToUVD(float3 positionWS)
            {
                float4 positionHS = mul(_HizCameraMatrixVP, float4(positionWS, 1.0));
                float3 uvd = positionHS.xyz / positionHS.w;
                uvd.xy = (uvd.xy + 1) * 0.5;
                return uvd;
            }

            v2f vert (appdata v)
            {
                v2f o;
                
                float4 inVertex = v.vertex;
                float2 uv = v.uv;

                RenderPatch patch = PatchList[v.instanceID];
                #if ENABLE_LOD_SEAMLESS
                FixLODConnectSeam(inVertex,uv,patch);
                #endif
                uint lod = patch.lod;
                float scale = pow(2,lod);

                uint4 lodTrans = patch.lodTrans;
                

                inVertex.xz *= scale;
                #if ENABLE_PATCH_DEBUG
                inVertex.xz *= 0.9;
                #endif
                inVertex.xz += patch.position;

                #if ENABLE_NODE_DEBUG
                inVertex.xyz = ApplyNodeDebug(patch,inVertex.xyz);
                #endif

                float2 heightUV = (inVertex.xz + (_WorldSize.xz * 0.5) + 0.5) / (_WorldSize.xz + 1);
                float height = tex2Dlod(_HeightMap,float4(heightUV,0,0)).r;
                inVertex.y = height * _WorldSize.y;

                float3 normal = SampleNormal(heightUV);
                Light light = GetMainLight();
                o.color = max(0.05,dot(light.direction,normal));

                float4 vertex = TransformObjectToHClip(inVertex.xyz);
                o.vertex = vertex;
                o.uv = uv * scale * 8;

                #if ENABLE_MIP_DEBUG
                
                uint4 lodColorIndex = lod + lodTrans;
                o.color *= (debugColorForMip[lodColorIndex.x] + 
                debugColorForMip[lodColorIndex.y] +
                debugColorForMip[lodColorIndex.z] +
                debugColorForMip[lodColorIndex.w]) * 0.25;
                #endif

                // o.color = half4(TransformWorldToUVD(inVertex.xyz).xy,0,1);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = tex2D(_MainTex, i.uv);
                col.rgb *= i.color;
                return col;
            }
            ENDHLSL
        }
    }
}
