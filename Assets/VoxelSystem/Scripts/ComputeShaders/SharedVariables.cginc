#include "Voxel.cginc"

int chunkSizeX;
int chunkSizeY;
int chunkSize;
float3 chunkPosition;
int margin;

RWStructuredBuffer<Voxel> voxelArray;
RWStructuredBuffer<uint> count;

uint flattenCoord(float3 id)
{
    return id.x + id.y * chunkSizeX + id.z * chunkSizeX * chunkSizeY;
}
// uint flattenCoord(float3 idx)
// {
//     return round(idx.x) + (round(idx.y) * (chunkSizeX + 5)) + (round(idx.z) * (chunkSizeX + 5) * (chunkSizeY + 1));
// }