using System;
using UnityEngine;


public class OctreeNode
{
    public Bounds Bounds; // 3D region of this node
    public bool IsLeaf;   // Leaf node indicator
    public Chunk Chunk;   // Associated chunk (only for leaf nodes)
    public OctreeNode[] Children; // 8 children for octree nodes

    // Aggregated data
    public bool ContainsWater { get; private set; } = false;
    public int TreeCount { get; private set; } = 0;
    public float AverageHeight { get; private set; } = 0;
    private int ChunkSize;

    public OctreeNode(Bounds bounds, int chunkSize)
    {
        Bounds = bounds;
        IsLeaf = true; // Start as a leaf
        Children = null; // Initialize to null for safety
        ChunkSize = chunkSize;
    }

    public static Bounds upscaleBoundsIfNeeded(Bounds bounds, int maxHeight)
    {
        if (bounds.size.x > maxHeight || bounds.size.z > maxHeight)
        {
            float factorX = bounds.size.x / maxHeight;
            float factorZ = bounds.size.z / maxHeight;
            double scaleX = Math.Log(factorX, 2) - 1;
            double scaleZ = Math.Log(factorZ, 4) * 2 - 1; // *2 because we add rectangular structure. Z is per definition the longer side
            // double trueUpscale = Math.Ceiling(Math.Max(scaleX, scaleZ));
            Bounds newBounds = new Bounds(bounds.center, new Vector3((float)(maxHeight * Math.Pow(2, scaleX)), bounds.size.y, (float)(maxHeight * Math.Pow(4, scaleZ))));
            // Debug.Log("factorX: " + factorX);
            // Debug.Log("factorZ: " + factorZ);
            // Debug.Log("scaleX: " + scaleX);
            // Debug.Log("scaleZ: " + scaleZ);
            // Debug.Log("True Upscale: " + trueUpscale);
            // Debug.Log("old Bounds: " + bounds);
            // Debug.Log("maxHeight: " + maxHeight);
            // Debug.Log("newBounds: " + newBounds);
            return newBounds;
        }
        return bounds;
    }

    // Subdivide this node into 8 children
    public void Subdivide(int maxHeight)
    {
        if (!IsLeaf) return; // Prevent double subdivision
        // Stop subdividing if all dimensions match the chunk size
        if (Bounds.size.x <= ChunkSize || Bounds.size.y <= ChunkSize || Bounds.size.z <= ChunkSize)
        {
            // Debug.Log("Leaf node with bounds: " + Bounds);
            IsLeaf = true;
            return;
        }

        Children = new OctreeNode[8];
        if (Bounds.size.x > maxHeight || Bounds.size.z > maxHeight)
        {
            if (Bounds.size.x < Bounds.size.z)
            {
                Vector3 size = new Vector3(
                    Bounds.size.x / 2, Bounds.size.y, Bounds.size.z / 4
                );
                Vector3 min = Bounds.min;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 offset = new Vector3(
                        size.x * (i % 2),
                        0,
                        size.z * (i / 2)
                    );
                    Bounds childBounds = new Bounds(min + offset + size / 2, size);

                    Children[i] = new OctreeNode(childBounds, ChunkSize);
                    // Debug.Log("Subdividing child of size: " + Children[i].Bounds);
                    Children[i].Subdivide(maxHeight); // Recursively subdivide each child
                }
                IsLeaf = false;
                Chunk = null; // Clear chunk reference as this is no longer a leaf node

            } else if (Bounds.size.x.Equals(Bounds.size.z)) {
                Vector3 size = new Vector3(
                    Bounds.size.x / 4, Bounds.size.y, Bounds.size.z / 2
                );
                Vector3 min = Bounds.min;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 offset = new Vector3(
                        size.x * (i / 2),
                        0,
                        size.z * (i % 2)
                    );
                    Bounds childBounds = new Bounds(min + offset + size / 2, size);

                    Children[i] = new OctreeNode(childBounds, ChunkSize);
                    // Debug.Log("Subdividing child of size: " + Children[i].Bounds);
                    Children[i].Subdivide(maxHeight); // Recursively subdivide each child
                }
                IsLeaf = false;
                Chunk = null; // Clear chunk reference as this is no longer a leaf node

            } else {
                Debug.Log("Illegal state! Bounds size X larger than Z! " + Bounds);
            }
        }
        else
        {
            Vector3 size = Bounds.size / 2;
            Vector3 min = Bounds.min;

            for (int i = 0; i < 8; i++)
            {
                Vector3 offset = new Vector3(
                    (i & 1) == 0 ? 0 : size.x,
                    (i & 2) == 0 ? 0 : size.y,
                    (i & 4) == 0 ? 0 : size.z
                );
                Bounds childBounds = new Bounds(min + offset + size / 2, size);

                Children[i] = new OctreeNode(childBounds, ChunkSize);
                // Debug.Log("Subdividing child of size: " + Children[i].Bounds);
                Children[i].Subdivide(maxHeight); // Recursively subdivide each child
            }
            IsLeaf = false;
            Chunk = null; // Clear chunk reference as this is no longer a leaf node
        }

    }

    // Update aggregated data for this node
    public void UpdateAggregates()
    {
        if (IsLeaf)
        {
            if (Chunk != null)
            {
                ContainsWater = Chunk.ContainsWater();
                TreeCount = Chunk.TreeCount();
                AverageHeight = Chunk.AverageHeight();
            }
            return;
        }

        ContainsWater = false;
        TreeCount = 0;
        AverageHeight = 0;
        int nonEmptyChildren = 0;

        foreach (var child in Children)
        {
            if (child == null) continue;
            child.UpdateAggregates();

            if (child.ContainsWater) ContainsWater = true;
            TreeCount += child.TreeCount;
            if (child.AverageHeight > 0)
            {
                AverageHeight += child.AverageHeight;
                nonEmptyChildren++;
            }
        }
        if (nonEmptyChildren > 0)
        {
            AverageHeight /= nonEmptyChildren;
        }
    }
    public void ValidateLeafNodes()
    {
        if (IsLeaf)
        {
            Debug.Assert(Bounds.size.x == ChunkSize &&
                         Bounds.size.y == ChunkSize &&
                         Bounds.size.z == ChunkSize,
                         "Leaf node dimensions do not match chunk size");
            return;
        }

        foreach (var child in Children)
        {
            child?.ValidateLeafNodes();
        }
    }

}
