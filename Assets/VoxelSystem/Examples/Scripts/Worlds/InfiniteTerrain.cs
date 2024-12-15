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
    private HashSet<Vector3> activeChunks = new HashSet<Vector3>();
    public Queue<Chunk> chunkPool;
    ConcurrentQueue<OctreeNode> nodeNeedsChunkCreation = new ConcurrentQueue<OctreeNode>();
    private ConcurrentQueue<OctreeNode> nodesToCreateChunks = new ConcurrentQueue<OctreeNode>();
    private ConcurrentQueue<OctreeNode> nodesToDispose = new ConcurrentQueue<OctreeNode>();
    ComputeBuffer biomesArray;
    private Vector3 previouslyCheckedPosition;

    bool performedFirstPass = false;
    Thread checkActiveChunks;
    private readonly object cameraPosLock = new object();
    private object activeChunksLock = new object(); // add this field

    public override void OnStart()
    {
        World.onShutdown += ShutDown;
        InitializeWorld();
        renderThread = new Thread(RenderChunksLoop) { Priority = System.Threading.ThreadPriority.BelowNormal };
        renderThread.Start();
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
                lastUpdatedPosition = positionToChunkCoord(mainCamera.transform.position);
            }
        }
        int chunksProcessed = 0;
        OctreeNode node;
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

        while (chunksProcessed < maxChunksToProcessPerFrame && nodesToDispose.TryDequeue(out node))
        {
            if (node.Chunk != null)
            {
                node.Chunk.Unrender();
                node.Chunk.Dispose();
                node.Chunk = null;
                lock (activeChunksLock)
                {
                    activeChunks.Remove(node.Bounds.center);
                }
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
                lock (activeChunksLock)
                {
                    activeChunks.Add(node.Bounds.center);
                }
            }
            chunksProcessed++;
        }
    }

    bool IsCircleFullyContained(Bounds bounds, float renderDistance, Vector3 worldPosition)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float cx = worldPosition.x;
        float cz = worldPosition.z;

        // Check if the circle defined by (cx, cz) and radius renderDistance
        // fits entirely within the rectangular area [min.x, max.x] x [min.z, max.z]
        float r = renderDistance;
        return (cx - r >= min.x) & (cx + r <= max.x) & (cz - r >= min.z) & (cz + r <= max.z);
    }

    enum ExpandDirection
    {
        XDown,
        XUp,
        ZDown,
        ZUp,
        None
    }

    ExpandDirection GetNotFullyInCircleContainedDirections(Bounds bounds, float renderDistance, Vector3 worldPosition)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float cx = worldPosition.x;
        float cz = worldPosition.z;

        // Check if the circle defined by (cx, cz) and radius renderDistance
        // fits entirely within the rectangular area [min.x, max.x] x [min.z, max.z]
        float r = renderDistance;
        if (cx - r < min.x)
        {
            return ExpandDirection.XDown;
        }
        if (cx + r > max.x)
        {
            return ExpandDirection.XUp;
        }
        if (cz - r < min.z)
        {
            return ExpandDirection.ZDown;
        }
        if (cz + r > max.z)
        {
            return ExpandDirection.ZUp;
        }
        return ExpandDirection.None;
    }

    private void ExpandRootNode(ExpandDirection expandDirection)
    {
        Bounds oldRootBounds = rootNode.Bounds;
        float factorX = oldRootBounds.size.x < oldRootBounds.size.z ? 4 : 2;
        float factorZ = oldRootBounds.size.x < oldRootBounds.size.z ? 2 : 4;
        Vector3 newSize = new Vector3(oldRootBounds.size.x * factorX, oldRootBounds.size.y, oldRootBounds.size.z * factorZ); // hard coded: x dimension is only 2 chunks, y is 4 given that we are above maxHeight in size. Should always be the case here...
        Bounds newBounds = new Bounds(oldRootBounds.center, newSize);
        switch (expandDirection)
        {
            case ExpandDirection.XDown:
                newBounds.center = oldRootBounds.center + new Vector3((float)(oldRootBounds.size.x * -0.5), 0, (float)(oldRootBounds.size.z * 0.5));
                break;
            case ExpandDirection.XUp:
                newBounds.center = oldRootBounds.center + new Vector3((float)(oldRootBounds.size.x * 0.5), 0, (float)(oldRootBounds.size.z * -0.5));
                break;
            case ExpandDirection.ZDown:
                newBounds.center = oldRootBounds.center + new Vector3((float)(oldRootBounds.size.x * -0.5), 0, (float)(oldRootBounds.size.z * -0.5));
                break;
            case ExpandDirection.ZUp:
                newBounds.center = oldRootBounds.center + new Vector3((float)(oldRootBounds.size.x * 0.5), 0, (float)(oldRootBounds.size.z * 0.5));
                break;
            case ExpandDirection.None:
                return;
        }
        OctreeNode newRootNode = new OctreeNode(newBounds, WorldSettings.chunkSize);
        newRootNode.Subdivide(WorldSettings.maxHeight);
        switch (expandDirection)
        {
            case ExpandDirection.XDown:
                newRootNode.Children[factorX < factorZ ? 3 : 4] = rootNode;
                break;
            case ExpandDirection.XUp:
                newRootNode.Children[factorX < factorZ ? 4 : 3] = rootNode;
                break;
            case ExpandDirection.ZDown:
                newRootNode.Children[5] = rootNode;
                break;
            case ExpandDirection.ZUp:
                newRootNode.Children[2] = rootNode;
                break;
            case ExpandDirection.None:
                return;
        }
        rootNode = newRootNode;
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

                // Step 0: Check if octree needs to be expanded, i.e. if any area from cameraChunkPos + renderDistance would fall outside of the root node bounds
                if (!IsCircleFullyContained(rootNode.Bounds, renderDistanceInWorldUnits, cameraChunkPos))
                {
                    ExpandDirection expandDirection = GetNotFullyInCircleContainedDirections(rootNode.Bounds, renderDistanceInWorldUnits, cameraChunkPos);
                    ExpandRootNode(expandDirection);
                }

                // Step 1: Query octree for leaf nodes in range
                List<OctreeNode> nodesInRange = new List<OctreeNode>();
                FindLeafNodesWithinDistance(rootNode, cameraChunkPos, renderDistanceInWorldUnits, nodesInRange);
                // Convert these node positions to a set for quick checks
                HashSet<Vector3> desiredActive = new HashSet<Vector3>();
                foreach (var n in nodesInRange)
                {
                    // Debug.Log("added to desiredActive: " + n.Bounds);
                    desiredActive.Add(n.Bounds.center);
                }
                // Make a safe copy of activeChunks or lock while enumerating


                Vector3[] activeSnapshot;
                lock (activeChunksLock)
                {
                    activeSnapshot = new Vector3[activeChunks.Count];
                    activeChunks.CopyTo(activeSnapshot);
                }
                // Step 2: Any currently active chunk not in desiredActive should be disposed
                foreach (var cPos in activeSnapshot)
                {
                    if (!desiredActive.Contains(cPos))
                    {
                        // Find node by cPos if needed, or store node references in a dictionary
                        // For simplicity, re-traverse or maintain a map from positions to nodes
                        OctreeNode n = FindLeafNode(rootNode, cPos);
                        if (n != null && n.Chunk != null && n.Chunk.IsRendered)
                            nodesToDispose.Enqueue(n);
                        // else {
                            // Debug.Log("not disposing of chunk at " + cPos + "even though we should...");
                            // if (n != null) Debug.Log(n);
                            // if (n.Chunk != null) {
                            //     Debug.Log(n.Chunk.chunkPosition);
                            //     Debug.Log("is rendered: " + n.Chunk.IsRendered);
                            // }
                        // }
                    }
                }

                // Step 3: Any chunk in desiredActive not in activeChunks should be created
                foreach (var n in nodesInRange)
                {
                    if (!activeChunks.Contains(n.Bounds.center))
                    {
                        // Enqueue for creation
                        nodesToCreateChunks.Enqueue(n);
                    }
                }
            }
            if (!performedFirstPass)
            {
                Vector3 cameraChunkPos;
                lock (cameraPosLock)
                {
                    cameraChunkPos = lastUpdatedPosition;
                }
                float renderDistanceInWorldUnits = WorldSettings.renderDistance * WorldSettings.chunkSize;
                if (IsCircleFullyContained(rootNode.Bounds, renderDistanceInWorldUnits, cameraChunkPos))
                {
                    performedFirstPass = true;
                }
            }
            Thread.Sleep(500);
        }
        Profiler.EndThreadProfiling();
    }

    // Helper method to find a node by center position if needed
    private OctreeNode FindLeafNode(OctreeNode node, Vector3 position)
    {
        // If node doesn't contain position at all, return null
        if (!node.Bounds.Contains(position))
            return null;

        if (node.IsLeaf)
            return node;

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindLeafNode(child, position);
                if (found != null)
                    return found;
            }
        }

        return null;
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

