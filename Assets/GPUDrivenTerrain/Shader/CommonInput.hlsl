#ifndef TERRAIN_COMMON_INPUT
#define TERRAIN_COMMON_INPUT

//最大的LOD级别是5
#define MAX_TERRAIN_LOD 5
#define MAX_NODE_ID 34124

//一个PatchMesh由16x16网格组成
#define PATCH_MESH_GRID_COUNT 16

//一个PatchMesh边长8米
#define PATCH_MESH_SIZE 8

//一个Node拆成8x8个Patch
#define PATCH_COUNT_PER_NODE 8

//PatchMesh一个格子的大小为0.5x0.5
#define PATCH_MESH_GRID_SIZE 0.5

#define SECTOR_COUNT_WORLD 160


struct NodeDescriptor{
    uint branch;
};

struct RenderPatch{
    float2 position;
    float2 minMaxHeight;
    uint lod;
    uint4 lodTrans;
};


struct Bounds{
    float3 minPosition;
    float3 maxPosition;
};

struct BoundsDebug{
    Bounds bounds;
    float4 color;
};

#endif