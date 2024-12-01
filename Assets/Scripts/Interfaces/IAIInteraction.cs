using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public interface IAIInteraction
{
    void ProcessTask(string taskType, Vector3 location);
}
