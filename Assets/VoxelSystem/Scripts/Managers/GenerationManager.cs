using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public static class GenerationManager
{
    public static ComputeShader voxelData;
    private static ComputeShader voxelContouring;

    private static ConcurrentBag<GenerationBuffer> generationBuffers = new ConcurrentBag<GenerationBuffer>();
    private static List<GenerationBuffer> allGenerationBuffers = new List<GenerationBuffer>();

    private static ConcurrentQueue<Vector3> needRendering = new ConcurrentQueue<Vector3>();
    private static ConcurrentQueue<Vector3> generatedChunks = new ConcurrentQueue<Vector3>();

    static ComputeBuffer voxelColorsArray;

    private static int xThreads;
    private static int yThreads;

    static int maxActionsPerFrame = 2;
    static bool asyncCompute = false;
    static int mainThreadID = -1;

    public static void Initialize(ComputeShader contouring, int maxActions = 2, int initialBuffers = 18)
    {
        maxActionsPerFrame = maxActions;

        voxelContouring = contouring;

        xThreads = World.WorldSettings.ChunkSizeWithMarginX / 8 + 1;
        yThreads = World.WorldSettings.maxHeight / 8;

        VoxelDetails[] convertedVoxelDetails = getVoxelDetails();
        voxelColorsArray = new ComputeBuffer(convertedVoxelDetails.Length, 12);
        voxelColorsArray.SetData(convertedVoxelDetails);

        voxelData.SetInt("chunkSizeX", World.WorldSettings.ChunkSizeWithMarginX);
        voxelData.SetInt("chunkSizeY", World.WorldSettings.ChunkSizeWithMarginY);
        voxelData.SetInt("chunkSize", World.WorldSettings.chunkSize);
        voxelData.SetInt("margin", World.WorldSettings.margin);
        voxelData.SetInt("marginY", World.WorldSettings.marginY);

        voxelContouring.SetInt("chunkSizeX", World.WorldSettings.ChunkSizeWithMarginX);
        voxelContouring.SetInt("chunkSizeY", World.WorldSettings.ChunkSizeWithMarginY);
        voxelContouring.SetInt("chunkSize", World.WorldSettings.chunkSize);
        voxelContouring.SetInt("margin", World.WorldSettings.margin);
        voxelContouring.SetInt("marginY", World.WorldSettings.marginY);
        voxelContouring.SetBool("smoothNormals", World.WorldSettings.smoothNormals);
        voxelContouring.SetBool("useTextures", World.WorldSettings.useTextures);
        voxelContouring.SetBuffer(2, "voxelColors", voxelColorsArray);

        for (int i = 0; i < initialBuffers; i++)
        {
            GenerationBuffer buffer = new GenerationBuffer();
            generationBuffers.Add(buffer);
            allGenerationBuffers.Add(buffer);
        }

        asyncCompute = SystemInfo.supportsAsyncCompute;
        mainThreadID = Thread.CurrentThread.ManagedThreadId;
    }

    public static void EnqueuePosToGenerate(Vector3 chunkPos)
    {
        generatedChunks.Enqueue(chunkPos);
    }

    public static void GenerateChunk(OctreeNode node, Action onComplete = null)
    {
        if (node == null || !node.IsLeaf || node.Chunk == null) return;

        // Debug.Log("Trying to generate chunk for position: " + node.Bounds.center);
        var genBuffer = GetGenerationBuffer();
        genBuffer.countBuffer.SetData(new uint[] { 0, 0, 0, 0, 0 });

        voxelData.SetVector("chunkPosition", node.Bounds.center);

        World.Instance.ExecuteDensityStage(genBuffer, xThreads, yThreads);

        // After ExecuteDensityStage and before contouring
        if (!ChunkContainsSolidVoxels(genBuffer))
        {
            // The chunk is empty; no need to contour or render
            RequeueBuffer(genBuffer);
            node.Chunk = null; // Optionally, remove the chunk reference
            return;
        }
        node.Chunk.generationState = Chunk.GeneratingState.Generating;

        AsyncGPUReadback.Request(genBuffer.heightMap, (callback) =>
        {
            node.Chunk.ProcessNoiseForStructs(genBuffer);
            onComplete?.Invoke();
            Contour(node, genBuffer);
        });
    }

    // Helper method to check if the chunk contains solid voxels
    private static bool ChunkContainsSolidVoxels(GenerationBuffer genBuffer)
    {
        // Implement logic to check the countBuffer or voxel data
        uint[] counts = new uint[5];
        genBuffer.countBuffer.GetData(counts);
        return counts[0] > 0; // counts[0] might represent the number of solid voxels
    }

    public static void RenderChunk(OctreeNode node)
    {
        if (node == null || !node.IsLeaf || node.Chunk == null || node.Chunk.IsRendered) return;

        var genBuffer = GetGenerationBuffer();
        voxelContouring.SetVector("chunkPosition", node.Bounds.center);

        voxelContouring.SetBuffer(0, "voxelArray", genBuffer.noiseBuffer);
        voxelContouring.Dispatch(0, xThreads, yThreads, xThreads);

        AsyncGPUReadback.Request(genBuffer.transparentIndexBuffer, callback =>
        {
            node.Chunk.UploadMesh(genBuffer);
            RequeueBuffer(genBuffer);
        });
    }

    public static void ProcessRendering(OctreeNode rootNode, Vector3 cameraChunkPos, float renderDistanceInWorldUnits)
    {
        World.Instance.TraverseOctree(rootNode, node =>
       {
           if (node.IsLeaf)
           {
               bool withinDistance = node.Bounds.SqrDistance(cameraChunkPos) < renderDistanceInWorldUnits * renderDistanceInWorldUnits;

               if (withinDistance && !node.Chunk.IsRendered)
               {
                   node.Chunk.Render();
               }
               else if (!withinDistance && node.Chunk.IsRendered)
               {
                   node.Chunk.Unrender();
               }
           }
       });
    }

    static void Contour(OctreeNode node, GenerationBuffer genBuffer)
    {
        voxelContouring.SetBuffer(3, "cellVertices", genBuffer.cellVerticesBuffer);
        voxelContouring.Dispatch(3, xThreads, yThreads, xThreads); // sets default values all across the voxel array

        voxelContouring.SetVector("chunkPosition", node.Bounds.center);
        voxelContouring.SetBuffer(0, "voxelArray", genBuffer.noiseBuffer);
        voxelContouring.SetBuffer(0, "count", genBuffer.countBuffer);
        voxelContouring.SetBuffer(0, "cellVertices", genBuffer.cellVerticesBuffer);
        voxelContouring.Dispatch(0, xThreads, yThreads, xThreads);

        voxelContouring.SetBuffer(1, "voxelArray", genBuffer.noiseBuffer);
        voxelContouring.SetBuffer(1, "cellVertices", genBuffer.cellVerticesBuffer);
        voxelContouring.Dispatch(1, xThreads, yThreads, xThreads);

        voxelContouring.SetBuffer(2, "voxelArray", genBuffer.noiseBuffer);
        voxelContouring.SetBuffer(2, "count", genBuffer.countBuffer);
        voxelContouring.SetBuffer(2, "vertexBuffer", genBuffer.vertexBuffer);
        voxelContouring.SetBuffer(2, "normalBuffer", genBuffer.normalBuffer);
        voxelContouring.SetBuffer(2, "cellVertices", genBuffer.cellVerticesBuffer);
        voxelContouring.SetBuffer(2, "colorBuffer", genBuffer.colorBuffer);
        voxelContouring.SetBuffer(2, "indexBuffer", genBuffer.indexBuffer);
        voxelContouring.SetBuffer(2, "transparentIndexBuffer", genBuffer.transparentIndexBuffer);
        voxelContouring.Dispatch(2, xThreads, yThreads, xThreads);

        AsyncGPUReadback.Request(genBuffer.transparentIndexBuffer, (callback) =>
        {
            if (node.Chunk != null)
            {
                node.Chunk.UploadMesh(genBuffer);
            }
            else
            {
                Debug.Log("Generated mesh for inactive chunk.");
                RequeueBuffer(genBuffer);
            }
        });
    }

    //To be executed on main thread only
    public static void Tick(OctreeNode rootNode)
    {
        Debug.Log("Running Generation Manager Tick");
        if (generatedChunks.Count > 0)
        {
            for (int i = 0; i < maxActionsPerFrame; i++)
            {
                if (generatedChunks.TryDequeue(out var chunkPos))
                {
                    // Find the corresponding node in the octree
                    OctreeNode targetNode = FindLeafNode(rootNode, chunkPos);

                    if (targetNode != null && targetNode.IsLeaf && targetNode.Chunk != null)
                    {
                        GenerateChunk(targetNode);
                    }
                    else
                    {
                        Debug.LogWarning($"Chunk at {chunkPos} not found or invalid in the octree.");
                    }
                }
            }
        }
        else
        {
            Debug.Log("No generatedChunks present. Returning...");
        }
    }

    private static OctreeNode FindLeafNode(OctreeNode node, Vector3 position)
    {
        if (node == null) return null;

        if (node.IsLeaf)
        {
            if (node.Bounds.Contains(position))
            {
                return node;
            }
            Debug.Log("Leaf node not in desired position: " + node.Bounds + "/" + position);
            return null;
        }

        foreach (var child in node.Children)
        {
            if (child.Bounds.Contains(position))
            {
                return FindLeafNode(child, position);
            }
        }
        Debug.Log("Helpless case. Returning...");
        return null;
    }



    private static GenerationBuffer GetGenerationBuffer()
    {
        if (generationBuffers.TryTake(out var buffer)) return buffer;
        var newBuffer = new GenerationBuffer();
        allGenerationBuffers.Add(newBuffer);
        return newBuffer;
    }

    public static void RequeueBuffer(GenerationBuffer ToQueue)
    {
        generationBuffers.Add(ToQueue);
    }

    public static void Shutdown()
    {
        foreach (var buffer in allGenerationBuffers) buffer.Dispose();
        voxelColorsArray.Dispose();
    }

    static int ColorToBits(Color32 c)
    {
        return (c.r << 16) | (c.g << 8) | (c.b << 0);
    }

    static VoxelDetails[] getVoxelDetails()
    {
        VoxelDetails[] voxelDetails = new VoxelDetails[World.Instance.voxelDetails.Length];
        int count = 0;
        foreach (Voxels vT in World.Instance.voxelDetails)
        {
            VoxelDetails vD = new VoxelDetails();
            vD.color = World.WorldSettings.useTextures && vT.texture != null ? -1 : ColorToBits(vT.color);
            vD.smoothness = vT.smoothness;
            vD.metallic = vT.metallic;

            voxelDetails[count++] = vD;
        }
        return voxelDetails;
    }

    public static int getQueuedCount
    {
        get
        {
            return generatedChunks.Count;
        }
    }
}

public class GenerationBuffer : IDisposable
{
    public ComputeBuffer noiseBuffer;
    public ComputeBuffer countBuffer;
    public ComputeBuffer heightMap;
    public ComputeBuffer specialBlocksBuffer;

    public ComputeBuffer vertexBuffer;
    public ComputeBuffer normalBuffer;
    public ComputeBuffer cellVerticesBuffer;
    public ComputeBuffer colorBuffer;
    public ComputeBuffer indexBuffer;
    public ComputeBuffer transparentIndexBuffer;

    public IndexedArray<Voxel> voxelArray;

    public GenerationBuffer()
    {
        int chunkSizeX = World.WorldSettings.ChunkSizeWithMarginX;
        int chunkY = World.WorldSettings.maxHeight + 1; // or just maxHeight if you prefer indexing
        int volume = chunkSizeX * chunkY * chunkSizeX;
        specialBlocksBuffer = new ComputeBuffer(64, 16);
        int heightMapSize = chunkSizeX * chunkSizeX;
        heightMap = new ComputeBuffer(heightMapSize, sizeof(float) * 2);
        countBuffer = new ComputeBuffer(5, 4);
        ClearCountBuffer();

        voxelArray = new IndexedArray<Voxel>();
        voxelArray.array = new Voxel[volume];
        noiseBuffer = new ComputeBuffer(volume, 12);

        int maxTris = World.WorldSettings.chunkSize * World.WorldSettings.maxHeight * World.WorldSettings.chunkSize / 3;
        //width*height*width*faces*tris
        int maxVertices = World.WorldSettings.smoothNormals ? maxTris * 2 : maxTris * 4;
        int maxNormals = World.WorldSettings.smoothNormals ? maxVertices : 1;

        vertexBuffer ??= new ComputeBuffer(maxVertices, 12);
        normalBuffer ??= new ComputeBuffer(maxNormals, 12);
        cellVerticesBuffer ??= new ComputeBuffer(volume, 32);
        colorBuffer ??= new ComputeBuffer(maxVertices, 16);
        indexBuffer ??= new ComputeBuffer(maxTris * 3, 4);
        transparentIndexBuffer ??= new ComputeBuffer(maxTris * 3, 4);
    }

    public void ClearCountBuffer()
    {
        countBuffer.SetData(new uint[] { 0, 0, 0, 0, 0 });
    }

    public void Dispose()
    {
        noiseBuffer?.Dispose();
        countBuffer?.Dispose();
        heightMap?.Dispose();
        specialBlocksBuffer?.Dispose();

        vertexBuffer?.Dispose();
        normalBuffer?.Dispose();
        cellVerticesBuffer?.Dispose();
        colorBuffer?.Dispose();
        indexBuffer?.Dispose();
        transparentIndexBuffer?.Dispose();
        voxelArray.Clear();
    }
}