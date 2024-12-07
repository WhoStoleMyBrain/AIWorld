using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Profiling;

public class InfiniteTerrain : World
{
    public Biome[] biomes;
    public Foliage[] foliage;
    public Structure[] structures;
    public int seed;
    public Transform mainCamera;

    //This will contain all modified voxels, structures, whatnot for all chunks, and will effectively be our saving mechanism
    public Queue<Chunk> chunkPool;
    ConcurrentQueue<OctreeNode> nodeNeedsChunkCreation = new ConcurrentQueue<OctreeNode>();
    private ConcurrentQueue<OctreeNode> nodesToCreateChunks = new ConcurrentQueue<OctreeNode>();
    private ConcurrentQueue<OctreeNode> nodesToDispose = new ConcurrentQueue<OctreeNode>();
    ComputeBuffer biomesArray;
    private Vector3 previouslyCheckedPosition;

    bool performedFirstPass = false;
    Thread checkActiveChunks;
    // bool initialGenerationComplete = false;
    private readonly object cameraPosLock = new object();

    public override void OnStart()
    {
        World.onShutdown += ShutDown;
        InitializeWorld();
        renderThread = new Thread(RenderChunksLoop) { Priority = System.Threading.ThreadPriority.BelowNormal };
        renderThread.Start();
        // Start rendering thread
    }

    public override void InitializeDensityShader()
    {
        Biome[] biomes = getBiomes();
        biomesArray = new ComputeBuffer(biomes.Length, 56);
        biomesArray.SetData(biomes);


        GenerationManager.voxelData.SetBool("generateCaves", false);
        GenerationManager.voxelData.SetBool("forceFloor", false);

        GenerationManager.voxelData.SetInt("maxHeight", World.WorldSettings.maxHeight);
        GenerationManager.voxelData.SetInt("oceanHeight", 42);
        GenerationManager.voxelData.SetInt("seed", seed);

        GenerationManager.voxelData.SetInt("biomeCount", biomes.Length);
        GenerationManager.voxelData.SetBuffer(0, "biomeArray", biomesArray);
        GenerationManager.voxelData.SetBuffer(1, "biomeArray", biomesArray);
    }

    private void InitializeWorld()
    {
        Debug.Log("Starting InitializeWorld");
        WorldSettings = worldSettings;

        if (StructureModule.Exists)
            StructureModule.IntializeRandom(seed);

        worldMaterials[0].SetTexture("_TextureArray", GenerateTextureArray());

        chunkPool = new Queue<Chunk>();
        meshDataPool = new Queue<MeshData>();

        Debug.Log("Finished InitializeWorld");
    }
    public override void DoUpdate()
    {

        if (mainCamera?.transform.position != lastUpdatedPosition)
        {
            lock (cameraPosLock)
            {
                //Update position so our CheckActiveChunksLoop thread has it
                lastUpdatedPosition = positionToChunkCoord(mainCamera.transform.position);
            }
        }
        OctreeNode node;
        int chunksProcessed = 0;

        // Process nodes that need chunk creation
        while (chunksProcessed < maxChunksToProcessPerFrame && nodesToCreateChunks.TryDequeue(out node))
        {
            if (node.Chunk == null)
            {
                node.Chunk = GenerateChunk(node.Bounds.min);
                nodeNeedsChunkCreation.Enqueue(node); // Enqueue for generation
            }
            chunksProcessed++;
        }

        chunksProcessed = 0;

        // Process nodes that need to be disposed
        while (chunksProcessed < maxChunksToProcessPerFrame && nodesToDispose.TryDequeue(out node))
        {
            if (node.Chunk != null)
            {
                node.Chunk.Unrender();
                node.Chunk.Dispose();
                node.Chunk = null;
            }
            chunksProcessed++;
        }


        chunksProcessed = 0;
        while (chunksProcessed < maxChunksToProcessPerFrame && nodeNeedsChunkCreation.TryDequeue(out node))
        {
            if (node.Chunk != null && node.Chunk.chunkState == Chunk.ChunkState.Idle)
            {
                node.Chunk.chunkState = Chunk.ChunkState.WaitingToMesh;
                GenerationManager.GenerateChunk(node);
            }
            else if (node.Chunk != null && node.Chunk.generationState == Chunk.GeneratingState.Idle && !node.Chunk.IsRendered)
            {
                node.Chunk.Render();
            }
            chunksProcessed++;
        }
    }

    private void RenderChunksLoop()
    {
        Profiler.BeginThreadProfiling("Chunks", "ChunkChecker");
        while (!killThreads)
        {
            if (previouslyCheckedPosition != lastUpdatedPosition || !performedFirstPass)
            {
                previouslyCheckedPosition = lastUpdatedPosition;
                Vector3 cameraChunkPos;

                lock (cameraPosLock)
                {
                    cameraChunkPos = lastUpdatedPosition;
                }

                float renderDistanceInWorldUnits = WorldSettings.renderDistance * WorldSettings.chunkSize;

                TraverseOctree(rootNode, node =>
                {
                    if (node.IsLeaf)
                    {
                        bool withinDistance = node.Bounds.SqrDistance(cameraChunkPos) < renderDistanceInWorldUnits * renderDistanceInWorldUnits;

                        if (withinDistance)
                        {
                            if (node.Chunk == null)
                            {
                                // Initialize the chunk and enqueue it for generation
                                // node.Chunk = GenerateChunk(node.Bounds.center);
                                // QueueOctreeChunkForRendering(node);
                                nodesToCreateChunks.Enqueue(node);
                            }
                            else if (!node.Chunk.IsRendered && node.Chunk.generationState == Chunk.GeneratingState.Idle)
                            {
                                // node.Chunk.Render();
                                nodesToCreateChunks.Enqueue(node);
                            }
                        }
                        else if (node.Chunk != null)
                        {
                            if (node.Chunk.IsRendered)
                            {
                                nodesToDispose.Enqueue(node);
                            }
                        }
                    }
                });
            }
            if (!performedFirstPass)
                performedFirstPass = true;

            Thread.Sleep(500);
        }
        Profiler.EndThreadProfiling();
    }

    #region Chunk Pooling

    #endregion

    private void OnApplicationQuit()
    {
        killThreads = true;
        checkActiveChunks?.Abort();

        //Try to force cleanup of editor memory
#if UNITY_EDITOR
        EditorUtility.UnloadUnusedAssetsImmediate();
        GC.Collect();
#endif
    }



    Biome[] getBiomes()
    {
        Dictionary<int, List<Foliage>> foliageByEnvId = new Dictionary<int, List<Foliage>>();
        Dictionary<int, List<Structure>> structuresByEnvId = new Dictionary<int, List<Structure>>();

        foreach (var str in foliage)
        {
            foreach (int envID in str.environmentsToSpawnIn)
                if (foliageByEnvId.ContainsKey(envID))
                {
                    foliageByEnvId[envID].Add(str);
                }
                else
                {
                    foliageByEnvId.Add(envID, new List<Foliage>(new Foliage[] { str }));
                }
        }

        foreach (var str in structures)
        {
            foreach (int envID in str.environmentsToSpawnIn)
                if (structuresByEnvId.ContainsKey(envID))
                {
                    structuresByEnvId[envID].Add(str);
                }
                else
                {
                    structuresByEnvId.Add(envID, new List<Structure>(new Structure[] { str }));
                }
        }

        int count = 0;
        Biome[] biomes = this.biomes;

        foreach (var b in biomes)
        {
            biomes[count].structureCount = structuresByEnvId.Count;
            biomes[count].foliageCount = foliageByEnvId.Count;

            if (foliageByEnvId.ContainsKey(count))
                biomes[count].SetFoliageIds(foliageByEnvId[count].ToArray());
            if (structuresByEnvId.ContainsKey(count))
                biomes[count].SetStructureIds(structuresByEnvId[count].ToArray());
            count++;
        }
        return biomes;
    }

    public override void ExecuteDensityStage(GenerationBuffer genBuffer, int xThreads, int yThreads)
    {

        int chunkSizeX = World.WorldSettings.ChunkSizeWithMarginX;
        int chunkSizeY = World.WorldSettings.ChunkSizeWithMarginY;
        int threadGroupsX = Mathf.CeilToInt(chunkSizeX / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(chunkSizeY / 8.0f);
        GenerationManager.voxelData.SetBuffer(2, "voxelArray", genBuffer.noiseBuffer);
        GenerationManager.voxelData.Dispatch(2, xThreads, yThreads, xThreads);

        // GenHeightMap kernel
        GenerationManager.voxelData.SetBuffer(0, "heightMap", genBuffer.heightMap);
        GenerationManager.voxelData.Dispatch(0, threadGroupsX, 1, threadGroupsX);

        GenerationManager.voxelData.SetBuffer(1, "specialBlocksBuffer", genBuffer.specialBlocksBuffer);
        GenerationManager.voxelData.SetBuffer(1, "heightMap", genBuffer.heightMap);
        GenerationManager.voxelData.SetBuffer(1, "voxelArray", genBuffer.noiseBuffer);
        GenerationManager.voxelData.SetBuffer(1, "count", genBuffer.countBuffer);
        GenerationManager.voxelData.Dispatch(1, threadGroupsX, threadGroupsY, threadGroupsX);
    }

    private void ShutDown()
    {
        biomesArray?.Dispose();
        renderThread?.Abort();
        World.onShutdown -= ShutDown;
    }

}

