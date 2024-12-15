using UnityEngine;

public class GameManager : MonoBehaviour
{
    private ICoreAI coreAI;
    private IWeatherAI weatherAI;

    void Start()
    {
        // Initialize Core AI and Weather AI
        string basePath = Application.dataPath;
        coreAI = new CoreAIOnnx(basePath + "/AI/core_ai_model.onnx");
        weatherAI = new WeatherAIONNX(basePath + "/AI/weather_ai_model.onnx");
        weatherAI.Initialize();

        // Register Weather AI with the TaskHandler to handle UPDATE_WEATHER tasks
        TaskHandler.Instance.RegisterAI(TaskType.UPDATE_WEATHER, weatherAI);

        // Example usage:
        float[] worldState = new float[5] {1f, 0.5f, 0.3f, 2f, 0.1f};
        TaskType nextTask = coreAI.EvaluateNextTask(worldState);
        Debug.Log("Core AI decided: " + nextTask);
        
        // If that caused UPDATE_WEATHER task to be queued, Weather AI will handle it in Update()
        // when TaskHandler processes the queue.
    }
}
