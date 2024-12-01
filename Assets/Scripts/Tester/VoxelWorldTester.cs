using System.Diagnostics;
using UnityEngine;

public class VoxelWorldTester : MonoBehaviour
{
    [Header("Key Bindings")]
    public KeyCode keyDistanceToWater = KeyCode.W;
    public KeyCode keyGetFlatAreas = KeyCode.F;
    public KeyCode keyFlattenArea = KeyCode.R;
    public KeyCode keyModifyVoxel = KeyCode.M;

    [Header("Test Parameters")]
    public Vector3 testPosition = new Vector3(0, 50, 0); // Position for testing
    public float testRadius = 10f;                      // Radius for queries
    public float maxSlope = 1f;                         // Max slope for flat area detection
    public float targetHeight = 5f;                     // Target height for flattening area
    public Voxel testVoxel = new Voxel { ID = 1 };      // Test voxel for modification

    [Header("Debugging")]
    public bool showExecutionTime = true;              // Show execution time
    public bool logResults = true;                     // Log results to console

    private IVoxelWorldManager _voxelWorldManager;

    void Start()
    {
        // Initialize the VoxelWorldManager and associate it with the active world
        _voxelWorldManager = new VoxelWorldManager();
        _voxelWorldManager.Initialize(World.Instance);
    }

    void Update()
    {
        if (Input.GetKeyDown(keyDistanceToWater))
            TestDistanceToWater();

        if (Input.GetKeyDown(keyGetFlatAreas))
            TestGetFlatAreas();

        if (Input.GetKeyDown(keyFlattenArea))
            TestFlattenArea();

        if (Input.GetKeyDown(keyModifyVoxel))
            TestModifyVoxel();
    }

    private void TestDistanceToWater()
    {
        Stopwatch stopwatch = StartStopwatch();
        float distance = _voxelWorldManager.DistanceToWater(testPosition);
        StopStopwatch(stopwatch, "DistanceToWater");

        if (logResults)
            UnityEngine.Debug.Log($"Distance to water from {testPosition}: {distance}");
    }

    private void TestGetFlatAreas()
    {
        Stopwatch stopwatch = StartStopwatch();
        var flatAreas = _voxelWorldManager.GetFlatAreas(testPosition, testRadius, maxSlope);
        StopStopwatch(stopwatch, "GetFlatAreas");

        if (logResults)
        {
            UnityEngine.Debug.Log($"Flat areas near {testPosition} within radius {testRadius}:");
            foreach (var area in flatAreas)
            {
                UnityEngine.Debug.Log($"Flat Area: {area}");
            }
        }
    }

    private void TestFlattenArea()
    {
        Stopwatch stopwatch = StartStopwatch();
        _voxelWorldManager.FlattenArea(testPosition, testRadius, targetHeight);
        StopStopwatch(stopwatch, "FlattenArea");

        if (logResults)
            UnityEngine.Debug.Log($"Flattened area around {testPosition} with radius {testRadius} to height {targetHeight}");
    }

    private void TestModifyVoxel()
    {
        Stopwatch stopwatch = StartStopwatch();
        _voxelWorldManager.ModifyVoxel(testPosition, testVoxel);
        StopStopwatch(stopwatch, "ModifyVoxel");

        if (logResults)
            UnityEngine.Debug.Log($"Modified voxel at {testPosition} to ID {testVoxel.ID}");
    }

    private Stopwatch StartStopwatch()
    {
        if (showExecutionTime)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            return stopwatch;
        }
        return null;
    }

    private void StopStopwatch(Stopwatch stopwatch, string methodName)
    {
        if (showExecutionTime && stopwatch != null)
        {
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{methodName} execution time: {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
