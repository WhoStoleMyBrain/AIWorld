using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IslandLevel : World
{
    public float islandRadius = 256;
    public int seed;
    bool generationStarted = false;

    //You can use this function for tracking the player position and forcing updates
    public override void DoUpdate()
    {
        if (generationStarted && GenerationManager.getQueuedCount == 0)
        {
            onGenerationComplete?.Invoke();
            generationStarted = false;
        }
    }

    //Set not changing Density Shader variables here - like a biome buffer, or seeds. The Shared Variables will be set in GenerationManager for you.
    public override void InitializeDensityShader()
    {
        GenerationManager.voxelData.SetFloat("islandRadius", islandRadius);
        GenerationManager.voxelData.SetInt("seed", seed);
    }

    //This is the custom dispatch for your density shader. It'll call this function before resuming the generation steps
    public override void ExecuteDensityStage(GenerationBuffer genBuffer, int xThreads, int yThreads)
    {
        GenerationManager.voxelData.SetBuffer(0, "voxelArray", genBuffer.noiseBuffer);
        GenerationManager.voxelData.Dispatch(0, xThreads, yThreads, xThreads);

        GenerationManager.voxelData.SetBuffer(1, "voxelArray", genBuffer.noiseBuffer);
        GenerationManager.voxelData.SetBuffer(1, "count", genBuffer.countBuffer);
        GenerationManager.voxelData.Dispatch(1, xThreads, yThreads, xThreads);
    }

    //For any custom startup functionallity
    public override void OnStart()
    {
        GenerateLevel();
    }


    void GenerateLevel()
    {
        for (int x = -WorldSettings.renderDistance; x <= WorldSettings.renderDistance; x++)
            for (int z = -WorldSettings.renderDistance; z <= WorldSettings.renderDistance; z++)
            {
                Vector3 pos = new Vector3(x * WorldSettings.chunkSize, 0, z * WorldSettings.chunkSize);

                Chunk chunk = new GameObject().AddComponent<Chunk>();
                chunk.transform.parent = transform;
                chunk.chunkPosition = pos;
                chunk.Initialize(worldMaterials, pos);

                activeChunks.TryAdd(pos, chunk);

                GenerationManager.EnqueuePosToGenerate(pos);
            }
        generationStarted = true;
    }
}
