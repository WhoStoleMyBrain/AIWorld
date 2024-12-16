using System.Collections.Generic;
using System.Threading;
using System;
using UnityEngine; // If using Unity-specific features

public class TaskHandler : MonoBehaviour
{
    private static TaskHandler _instance;
    public static TaskHandler Instance 
    {
        get
        {
            if (_instance == null)
            {
                var obj = new GameObject("TaskHandler");
                _instance = obj.AddComponent<TaskHandler>();
            }
            return _instance;
        }
    }

    private Queue<GameTask> _taskQueue = new Queue<GameTask>();
    private object _lockObj = new object();

    // Map of TaskType to a registered IAI handler
    private Dictionary<TaskType, IAI> _aiMap = new Dictionary<TaskType, IAI>();

    // For controlling how many tasks per frame or per cycle
    [SerializeField] private int _tasksPerCycle = 1;

    // Optional: Run in a background thread 
    // (For simplicity, we will just do it in Update, but you can use a thread if needed.)
    // private Thread _backgroundThread;
    // private bool _running = true;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    // Call this method to register AI implementations for specific tasks
    public void RegisterAI(TaskType taskType, IAI aiImplementation)
    {
        if (!_aiMap.ContainsKey(taskType))
        {
            _aiMap.Add(taskType, aiImplementation);
        }
        else
        {
            _aiMap[taskType] = aiImplementation;
        }
    }

    public void AddTask(GameTask task)
    {
        lock (_lockObj)
        {
            _taskQueue.Enqueue(task);
        }
    }

    private void Update()
    {
        Debug.Log("Running update on TaskHandler");
        // Process a limited number of tasks per frame to avoid lag
        for (int i = 0; i < _tasksPerCycle; i++)
        {
            GameTask task = null;
            lock (_lockObj)
            {
                if (_taskQueue.Count > 0)
                {
                    task = _taskQueue.Dequeue();
                }
            }

            if (task != null)
            {
                ProcessTask(task);
            }
        }
    }

    private void ProcessTask(GameTask task)
    {
        if (_aiMap.ContainsKey(task.Type))
        {
            _aiMap[task.Type].ProcessTask(task);
        }
        else
        {
            Debug.LogWarning($"No AI registered for task type: {task.Type}");
        }
    }
}
