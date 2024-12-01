using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

public class InfiniteTerrain : World
{
    public Biome[] biomes;
    public Foliage[] foliage;
    public Structure[] structures;

    public int seed;

    public Transform mainCamera;
    private Vector3 lastUpdatedPosition;
    private Vector3 previouslyCheckedPosition;

    //This will contain all modified voxels, structures, whatnot for all chunks, and will effectively be our saving mechanism
    public Queue<Chunk> chunkPool;
    ConcurrentQueue<Vector3> chunksNeedCreation = new ConcurrentQueue<Vector3>();
    ConcurrentQueue<Vector3> deactiveChunks = new ConcurrentQueue<Vector3>();

    ComputeBuffer biomesArray;
    int mainThreadID;

    Thread checkActiveChunks;
    private Thread renderThread;
    bool performedFirstPass = false;
    bool initialGenerationComplete = false;

    public override void OnStart()
    {
        World.onShutdown += ShutDown;
        InitializeWorld();
        InitializeOctree();
        PopulateOctreeWithChunks();
        CheckIfChunksArePresent();
        // Debug.Log("Populated octree!");
        // Debug.Log(rootNode.Bounds);
        // Debug.Log(rootNode.Children.GetValue(0));
        // Start rendering thread
        // renderThread = new Thread(RenderChunksLoop) { Priority = System.Threading.ThreadPriority.BelowNormal };
        // renderThread.Start();
    }

    public void CheckIfChunksArePresent()
    {
        Debug.Log("Finished PopulateOctreeWithChunks");
        Chunk tmpChunk = GetChunkFromLeafNode(rootNode);
        if (tmpChunk != null)
        {
            Debug.Log("Found Chunk? " + tmpChunk.chunkPosition);
            Debug.Log("Found Chunk? " + tmpChunk.chunkState);
            Debug.Log("Found Chunk? " + tmpChunk.ContainsWater());

        }
        Debug.Log("Finished PopulateOctreeWithChunks");
    }

    public Chunk GetChunkFromLeafNode(OctreeNode node)
    {
        if (node == null)
        {
            Debug.LogWarning("Provided node is null.");
            return null;
        }

        // Base case: If this node is a leaf, return its chunk (if it exists).
        if (node.IsLeaf)
        {
            if (node.Chunk != null)
            {
                return node.Chunk;
            }
            else
            {
                Debug.LogWarning("Leaf node does not contain a chunk.");
                return null;
            }
        }

        // Recursive case: Traverse all children and look for a chunk in the leaf nodes.
        foreach (OctreeNode child in node.Children)
        {
            if (child != null) // Safety check for uninitialized children
            {
                Chunk foundChunk = GetChunkFromLeafNode(child);
                if (foundChunk != null)
                {
                    return foundChunk; // Return as soon as a valid chunk is found
                }
            }
        }

        // No chunk was found in the subtree.
        Debug.LogWarning("No chunk found in the subtree starting from the given node.");
        return null;
    }


    // Initialize the octree
    public void InitializeOctree()
    {
        Debug.Log("Starting InitializeOctree");
        Vector3 worldCenter = Vector3.zero;
        Vector3 worldSize = new Vector3(256, 256, 256); // 32*32=1024 // I think I need to start with a cube, otherwise I could not hold cubic chunks in the octree
        rootNode = new OctreeNode(new Bounds(worldCenter, worldSize), WorldSettings.chunkSize);
        GenerateOctreeStructure(rootNode);
        Debug.Log("Finished InitializeOctree");
    }

    public void GenerateOctreeStructure(OctreeNode node)
    {
        node.Subdivide();
        // node.ValidateLeafNodes();
        // PopulateOctreeWithChunks();
    }



    // Insert a chunk into the octree
    public void InsertChunk(Vector3 chunkPosition)
    {
        InsertChunkRecursive(rootNode, chunkPosition);
    }

    private void InsertChunkRecursive(OctreeNode node, Vector3 chunkPosition)
    {
        if (node.IsLeaf)
        {
            if (node.Chunk == null)
            {
                node.Chunk = new Chunk(WorldSettings.chunkSize);
            }
            return;
        }

        foreach (var child in node.Children)
        {
            if (child.Bounds.Contains(chunkPosition))
            {
                InsertChunkRecursive(child, chunkPosition);
                break;
            }
        }
    }

    private void PopulateOctreeWithChunks()
    {
        Debug.Log("Starting PopulateOctreeWithChunks");
        TraverseOctree(rootNode, node =>
        {
            if (node == null)
            {
                Debug.LogError("Encountered a null node during octree traversal.");
                return;
            }

            if (node.IsLeaf)
            {
                if (node.Bounds != null)
                {
                    try
                    {
                        node.Chunk = GetChunk(node.Bounds.center);
                        // GenerationManager.GenerateChunk(node.Bounds.center, null); // node.UpdateAggregates should only be called when the full tree is present
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error generating chunk at {node.Bounds.center}: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogError("Node.Bounds is null for a leaf node.");
                }
            }
        });
        Debug.Log("Finished PopulateOctreeWithChunks");
    }


    private void RenderChunksLoop()
    {
        while (!killThreads)
        {
            Vector3 cameraChunkPos = positionToChunkCoord(mainCamera.transform.position);

            TraverseOctree(rootNode, node =>
            {
                if (node.IsLeaf && node.Bounds.SqrDistance(cameraChunkPos) < WorldSettings.renderDistance * WorldSettings.renderDistance)
                {
                    GenerationManager.RenderChunk(node.Bounds.center);
                }
            });

            Thread.Sleep(500);
        }
    }

    private void TraverseOctree(OctreeNode node, Action<OctreeNode> action)
    {
        action.Invoke(node);

        if (!node.IsLeaf && node.Children != null)
        {
            foreach (var child in node.Children) TraverseOctree(child, action);
        }
    }



    public override void InitializeDensityShader()
    {
        Biome[] biomes = getBiomes();
        biomesArray = new ComputeBuffer(biomes.Length, 56);
        biomesArray.SetData(biomes);


        GenerationManager.voxelData.SetBool("generateCaves", true);
        GenerationManager.voxelData.SetBool("forceFloor", true);

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

        int renderSizePlusExcess = WorldSettings.renderDistance + 3;
        int totalChunks = renderSizePlusExcess * renderSizePlusExcess;

        if (StructureModule.Exists)
            StructureModule.IntializeRandom(seed);

        worldMaterials[0].SetTexture("_TextureArray", GenerateTextureArray());

        activeChunks = new ConcurrentDictionary<Vector3, Chunk>();
        chunkPool = new Queue<Chunk>();
        meshDataPool = new Queue<MeshData>();

        mainThreadID = Thread.CurrentThread.ManagedThreadId;

        for (int i = 0; i < totalChunks; i++)
        {
            GenerateChunk(Vector3.zero, true);
        }

        checkActiveChunks = new Thread(CheckActiveChunksLoop);
        checkActiveChunks.Priority = System.Threading.ThreadPriority.BelowNormal;
        checkActiveChunks.Start();
        Debug.Log("Finished InitializeWorld");
    }
    public override void DoUpdate()
    {

        if (mainCamera?.transform.position != lastUpdatedPosition)
        {
            //Update position so our CheckActiveChunksLoop thread has it
            lastUpdatedPosition = positionToChunkCoord(mainCamera.transform.position);
        }

        Vector3 contToMake;

        while (deactiveChunks.Count > 0 && deactiveChunks.TryDequeue(out contToMake))
        {
            deactiveChunk(contToMake);
        }
        for (int x = 0; x < maxChunksToProcessPerFrame; x++)
        {
            if (x < maxChunksToProcessPerFrame && chunksNeedCreation.Count > 0 && chunksNeedCreation.TryDequeue(out contToMake))
            {
                Chunk chunk = GetChunk(contToMake);
                chunk.chunkPosition = contToMake;
                chunk.chunkState = Chunk.ChunkState.WaitingToMesh;
                activeChunks.TryAdd(contToMake, chunk);
                GenerationManager.GenerateChunk(contToMake);
                x++;
            }
        }
        GenerationManager.ProcessRendering();

        if (!initialGenerationComplete && chunksNeedRegenerated.Count == 0 && chunksNeedCreation.Count == 0)
        {
            onGenerationComplete?.Invoke();
            initialGenerationComplete = true;
        }
    }

    void CheckActiveChunksLoop()
    {
        Profiler.BeginThreadProfiling("Chunks", "ChunkChecker");
        int halfRenderSize = WorldSettings.renderDistance / 2;
        int renderDistPlus1 = WorldSettings.renderDistance + 1;
        Vector3 pos = Vector3.zero;

        Bounds chunkBounds = new Bounds();
        chunkBounds.size = new Vector3(renderDistPlus1 * WorldSettings.chunkSize, 1, renderDistPlus1 * WorldSettings.chunkSize);
        while (true && !killThreads)
        {
            if (previouslyCheckedPosition != lastUpdatedPosition || !performedFirstPass)
            {
                previouslyCheckedPosition = lastUpdatedPosition;

                for (int x = -halfRenderSize; x < halfRenderSize; x++)
                    for (int z = -halfRenderSize; z < halfRenderSize; z++)
                    {
                        pos.x = x * WorldSettings.chunkSize + previouslyCheckedPosition.x;
                        pos.z = z * WorldSettings.chunkSize + previouslyCheckedPosition.z;

                        if (!activeChunks.ContainsKey(pos))
                        {
                            chunksNeedCreation.Enqueue(pos);
                        }
                    }

                chunkBounds.center = previouslyCheckedPosition;

                foreach (var kvp in activeChunks)
                {
                    if (!chunkBounds.Contains(kvp.Key))
                        deactiveChunks.Enqueue(kvp.Key);
                }
            }

            if (!performedFirstPass)
                performedFirstPass = true;

            Thread.Sleep(500);
        }
        Profiler.EndThreadProfiling();
    }


    #region Chunk Pooling
    public Chunk GetChunk(Vector3 pos)
    {
        if (chunkPool.Count > 0)
        {
            return chunkPool.Dequeue();
        }
        else
        {
            return GenerateChunk(pos, false);
        }
    }

    Chunk GenerateChunk(Vector3 position, bool enqueue = true)
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThreadID)
        {
            chunksNeedCreation.Enqueue(position);
            return null;
        }
        Chunk chunk = new GameObject().AddComponent<Chunk>();
        chunk.transform.parent = transform;
        chunk.chunkPosition = position;
        chunk.Initialize(worldMaterials, position);

        if (enqueue)
        {
            chunkPool.Enqueue(chunk);
        }

        return chunk;
    }

    public bool deactiveChunk(Vector3 position)
    {
        if (activeChunks.ContainsKey(position))
        {
            if (activeChunks.TryRemove(position, out Chunk c))
            {
                c.ClearData();
                chunkPool.Enqueue(c);
                return true;
            }
            else
                return false;

        }

        return false;
    }
    #endregion

    private void OnApplicationQuit()
    {
        killThreads = true;
        checkActiveChunks?.Abort();

        foreach (var c in activeChunks.Keys)
        {
            if (activeChunks.TryRemove(c, out var cont))
            {
                cont.Dispose();
            }
        }

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
        GenerationManager.voxelData.SetBuffer(2, "voxelArray", genBuffer.noiseBuffer);
        GenerationManager.voxelData.Dispatch(2, xThreads, yThreads, xThreads);

        GenerationManager.voxelData.SetBuffer(0, "heightMap", genBuffer.heightMap);
        GenerationManager.voxelData.Dispatch(0, xThreads * 4, 1, xThreads * 4);


        GenerationManager.voxelData.SetBuffer(1, "specialBlocksBuffer", genBuffer.specialBlocksBuffer);
        GenerationManager.voxelData.SetBuffer(1, "heightMap", genBuffer.heightMap);
        GenerationManager.voxelData.SetBuffer(1, "voxelArray", genBuffer.noiseBuffer);
        GenerationManager.voxelData.SetBuffer(1, "count", genBuffer.countBuffer);
        GenerationManager.voxelData.Dispatch(1, xThreads, yThreads, xThreads);
    }

    private void ShutDown()
    {
        biomesArray?.Dispose();
        killThreads = true;
        renderThread?.Abort();
        World.onShutdown -= ShutDown;
    }

}

