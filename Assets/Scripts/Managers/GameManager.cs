using System.Threading;
using Unity.Barracuda;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [SerializeField]
    NNModel coreAIModel;
    [SerializeField]
    NNModel weatherAIModel;
    [SerializeField]
    public int tickTime = 5000;
    private ICoreAI coreAI;
    private IWeatherAI weatherAI;
    Thread tickThread;
    protected volatile bool killThreads = false;

    public void TickLoop()
    {
        while (true && !killThreads)
        {
            TaskHandler.Instance.AddTask(new GameTask(TaskType.CORE_AI_PERIODIC_TASK));
            Thread.Sleep(tickTime);
        }
    }

    void Start()
    {

        // Initialize Core AI and Weather AI
        // string basePath = Application.dataPath;
        coreAI = new CoreAIOnnx(coreAIModel);
        weatherAI = new WeatherAIONNX(weatherAIModel);
        weatherAI.Initialize();

        // Register Weather AI with the TaskHandler to handle UPDATE_WEATHER tasks
        TaskHandler.Instance.RegisterAI(TaskType.CORE_AI_PERIODIC_TASK, coreAI);
        TaskHandler.Instance.RegisterAI(TaskType.UPDATE_WEATHER, weatherAI);

        // Example usage:
        // float[] worldState = new float[5] {1f, 0.5f, 0.3f, 2f, 0.1f};
        // TaskType nextTask = coreAI.EvaluateNextTask(worldState);
        // Debug.Log("Core AI decided: " + nextTask);

        // If that caused UPDATE_WEATHER task to be queued, Weather AI will handle it in Update()
        // when TaskHandler processes the queue.
        tickThread = new Thread(TickLoop);
        tickThread.Priority = System.Threading.ThreadPriority.BelowNormal;
        tickThread.Start();
    }
    private void ShutDown()
    {
        killThreads = true;
    }
}
