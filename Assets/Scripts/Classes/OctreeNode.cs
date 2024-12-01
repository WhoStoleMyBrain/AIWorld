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

    // Subdivide this node into 8 children
    public void Subdivide(int depth=0)
    {
        if (depth > 3) return;

        if (!IsLeaf) return; // Prevent double subdivision
        // Stop subdividing if all dimensions match the chunk size
        if (Bounds.size.x <= ChunkSize || Bounds.size.y <= ChunkSize || Bounds.size.z <= ChunkSize)
        {
            IsLeaf = true;
            return;
        }

        Children = new OctreeNode[8];
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
            Children[i].Subdivide(depth); // Recursively subdivide each child
        }
        IsLeaf = false;
        Chunk = null; // Clear chunk reference as this is no longer a leaf node
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
