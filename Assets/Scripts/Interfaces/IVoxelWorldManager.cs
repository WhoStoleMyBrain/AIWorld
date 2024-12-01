using System;
using System.Collections.Generic;
using UnityEngine;

public interface IVoxelWorldManager
{
    // Initialization
    void Initialize(World world);

    // Query Methods
    float GetTerrainHeight(Vector3 worldPosition);
    bool IsNearWater(Vector3 worldPosition, float radius);
    List<Vector3> GetFlatAreas(Vector3 center, float radius, float maxSlope);
    Voxel GetVoxelAt(Vector3 worldPosition);

    // Distance and Pathfinding
    float DistanceToWater(Vector3 worldPosition);
    float DistanceToNearestFlatArea(Vector3 worldPosition, float radius, float maxSlope);

    // Modifications
    void FlattenArea(Vector3 center, float radius, float targetHeight);
    void ModifyVoxel(Vector3 worldPosition, Voxel newVoxel);

    // Utility Methods
    // void RebuildChunk(Vector3 chunkPosition);
}
