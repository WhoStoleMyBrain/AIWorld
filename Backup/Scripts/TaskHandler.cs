using System.Collections.Generic;
using UnityEngine;

public class TaskHandler : MonoBehaviour
{
    public CoreAIModel coreAI;
    public WeatherAIModel weatherAI;
    
    private Queue<Dictionary<string, object>> taskQueue = new Queue<Dictionary<string, object>>();

    // Task types and mappings
    private enum TaskType { EVALUATE_RESOURCES, MANAGE_SETTLEMENTS, UPDATE_WEATHER, EVALUATE_SOCIAL_STABILITY, NO_TASK }
    private enum WeatherEvent { CLEAR, RAIN, STORM, SNOW, FOG, DROUGHT, HEATWAVE, WINDY, SEASONAL_CHANGE }

    void Start()
    {
        // Initial task generation
        AddTaskFromCoreAI();
        Debug.Log("Task Handler Initialized!");
    }

    void Update()
    {
        ProcessTaskQueue();
    }

    public void AddTaskFromCoreAI()
    {
        if (coreAI == null)
        {
            Debug.Log("Core AI is not initialized yet.");
            return;
        }
        // Example input for the Core AI model
        float[] worldStateInput = new float[5];  // Populate with actual world state data
        
        // Predict task
        int predictedTask = coreAI.PredictTask(worldStateInput);

        // Create task dictionary
        var task = new Dictionary<string, object>
        {
            { "type", (TaskType)predictedTask },
            { "ai", predictedTask == (int)TaskType.UPDATE_WEATHER ? "WeatherAI" : "CoreAI" },
            { "params", new Dictionary<string, object>() }
        };
        taskQueue.Enqueue(task);
        var weatherTask = new Dictionary<string, object>
        {
            { "type", TaskType.UPDATE_WEATHER },
            { "ai", "WeatherAI" },
            { "params", new Dictionary<string, object>() }
        };
        taskQueue.Enqueue(weatherTask);
    }

    private void ProcessTaskQueue()
    {
        while (taskQueue.Count > 0)
        {
            var task = taskQueue.Dequeue();
            string targetAI = (string)task["ai"];

            switch (targetAI)
            {
                case "CoreAI":
                    HandleCoreTask((TaskType)task["type"], (Dictionary<string, object>)task["params"]);
                    break;
                case "WeatherAI":
                    HandleWeatherTask((TaskType)task["type"], (Dictionary<string, object>)task["params"]);
                    break;
            }
        }
    }

    private void HandleCoreTask(TaskType taskType, Dictionary<string, object> parameters)
    {
        switch (taskType)
        {
            case TaskType.EVALUATE_RESOURCES:
                Debug.Log("CoreAI: Evaluating resources.");
                break;
            case TaskType.MANAGE_SETTLEMENTS:
                Debug.Log("CoreAI: Managing settlements.");
                break;
            case TaskType.EVALUATE_SOCIAL_STABILITY:
                Debug.Log("CoreAI: Evaluating social stability");
                break;  
            // Additional cases as needed
        }
    }

    private void HandleWeatherTask(TaskType taskType, Dictionary<string, object> parameters)
    {
        float[] weatherStateInput = new float[10];  // Populate with actual weather state data

        // Predict the weather event
        int predictedWeather = weatherAI.PredictWeatherEvent(weatherStateInput);
        Debug.Log($"WeatherAI: Predicted weather event {(WeatherEvent)predictedWeather}");
    }
}
