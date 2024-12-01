using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class VoxelWorldManager : IVoxelWorldManager
{
    private World _world;

    public void Initialize(World world)
    {
        _world = world ?? throw new System.ArgumentNullException(nameof(world));
    }

    public float GetTerrainHeight(Vector3 worldPosition)
    {
        Vector3 chunkPosition = World.positionToChunkCoord(worldPosition);
        Vector3 localPosition = worldPosition - chunkPosition;

        if (_world.GetVoxelAtCoord(chunkPosition, localPosition, out Voxel voxel))
        {
            if (voxel.ID != 0) return localPosition.y;
        }

        return -1; // Default value for "no terrain"
    }

    public bool IsNearWater(Vector3 worldPosition, float radius)
    {
        Vector3 chunkPosition = World.positionToChunkCoord(worldPosition);

        // Check voxels within the radius
        foreach (var offset in GetVoxelOffsetsInRadius(radius))
        {
            Vector3 localPosition = worldPosition + offset;
            if (_world.GetVoxelAtCoord(chunkPosition, localPosition, out Voxel voxel))
            {
                if (voxel.ID == 240) // Assuming 240 is water
                    return true;
            }
        }
        return false;
    }

    public List<Vector3> GetFlatAreas(Vector3 center, float radius, float maxSlope)
    {
        List<Vector3> flatAreas = new List<Vector3>();
        Vector3 chunkPosition = World.positionToChunkCoord(center);

        foreach (var offset in GetVoxelOffsetsInRadius(radius))
        {
            Vector3 localPosition = center + offset;
            float height = GetTerrainHeight(localPosition);
            float neighborHeight = GetTerrainHeight(localPosition + new Vector3(1, 0, 1));

            if (math.abs(height - neighborHeight) <= maxSlope)
            {
                flatAreas.Add(localPosition);
            }
        }
        return flatAreas;
    }

    public Voxel GetVoxelAt(Vector3 worldPosition)
    {
        Vector3 chunkPosition = World.positionToChunkCoord(worldPosition);
        Vector3 localPosition = worldPosition - chunkPosition;

        if (_world.GetVoxelAtCoord(chunkPosition, localPosition, out Voxel voxel))
        {
            return voxel;
        }

        return default;
    }

    public float DistanceToWater(Vector3 worldPosition)
    {
        float radius = 1.0f;
        while (radius <= 1000) // Set a reasonable max search radius
        {
            if (IsNearWater(worldPosition, radius))
                return radius;

            radius *= 2; // Exponential search
        }

        return -1; // Water not found
    }

    public float DistanceToNearestFlatArea(Vector3 worldPosition, float radius, float maxSlope)
    {
        var flatAreas = GetFlatAreas(worldPosition, radius, maxSlope);
        if (flatAreas.Count == 0) return -1;

        float closestDistance = float.MaxValue;
        foreach (var area in flatAreas)
        {
            float distance = Vector3.Distance(worldPosition, area);
            closestDistance = math.min(closestDistance, distance);
        }

        return closestDistance;
    }

    public void FlattenArea(Vector3 center, float radius, float targetHeight)
    {
        Vector3 chunkPosition = World.positionToChunkCoord(center);

        foreach (var offset in GetVoxelOffsetsInRadius(radius))
        {
            Vector3 localPosition = center + offset;
            _world.SetVoxelAtCoord(chunkPosition, localPosition, new Voxel { ID = 1, ActiveValue = 0 }); // Assuming ID=1 is terrain
        }

        _world.chunksNeedRegenerated.Enqueue(chunkPosition);
    }

    public void ModifyVoxel(Vector3 worldPosition, Voxel newVoxel)
    {
        Vector3 chunkPosition = World.positionToChunkCoord(worldPosition);
        Vector3 localPosition = worldPosition - chunkPosition;

        _world.SetVoxelAtCoord(chunkPosition, localPosition, newVoxel);
        _world.chunksNeedRegenerated.Enqueue(chunkPosition);
    }

    // public void RebuildChunk(Vector3 chunkPosition)
    // {
    //     if (_world.activeChunks.TryGetValue(chunkPosition, out Chunk chunk))
    //     {
    //         chunk.chunkState = Chunk.ChunkState.WaitingToMesh;
    //         _world.chunksNeedRegenerated.Enqueue(chunkPosition);
    //     }
    // }

    private IEnumerable<Vector3> GetVoxelOffsetsInRadius(float radius)
    {
        List<Vector3> offsets = new List<Vector3>();

        for (float x = -radius; x <= radius; x++)
        {
            for (float z = -radius; z <= radius; z++)
            {
                if (math.sqrt(x * x + z * z) <= radius)
                {
                    offsets.Add(new Vector3(x, 0, z));
                }
            }
        }

        return offsets;
    }
}
