using Unity.Barracuda;
using UnityEngine;
using System;

public class WeatherAIONNX : IWeatherAI
{
    private Model _model;
    private IWorker _worker;

    public WeatherAIONNX(NNModel model)
    {
        NNModel nnModel = model;;
        // if (nnModel == null)
        // {
        //     Debug.LogError($"WeatherAIONNX: Could not load NNModel '{nnModel.name}' from Resources.");
        //     return;
        // }

        _model = ModelLoader.Load(nnModel);
        _worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, _model);
    }

    public void Initialize()
    {
        // Any initialization code if needed
    }

    public void ProcessTask(GameTask task)
    {
        float[] weatherInput = task.Payload as float[];
        if (weatherInput == null || weatherInput.Length != 10)
        {
            Debug.LogError("Weather AI received invalid input.");
            return;
        }

        float result = EvaluateWeather(weatherInput);
        Debug.Log($"Weather AI result: {result}");
        
        // Enqueue other tasks based on result if needed.
        // Example:
        // if (result < 0.5f) TaskHandler.Instance.AddTask(new GameTask(TaskType.EVALUATE_RESOURCES));
    }

    public float EvaluateWeather(float[] input)
    {
        if (input.Length != 10) 
            throw new ArgumentException("Input must be length 10.");

        using (var inputTensor = new Tensor(1, 10, input))
        {
            _worker.Execute(inputTensor);
            var output = _worker.PeekOutput(); 
            float prediction = output[0]; 
            
            // Ensure between 0 and 1
            prediction = Mathf.Clamp01(prediction);
            return prediction;
        }
    }
}
