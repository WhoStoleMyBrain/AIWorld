using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public interface IWorldData
{
    Vector3 GetTerrainHeight(Vector3 position);
    bool IsWaterAt(Vector3 position);
    bool IsSuitableForTown(Vector3 position);
    void PlaceTown(Vector3 position, string townName);
}
