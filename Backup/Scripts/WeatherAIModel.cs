using Unity.Barracuda;
using UnityEngine;

public class WeatherAIModel : MonoBehaviour
{
    public NNModel weatherAiModelAsset;
    private Model weatherModel;
    private IWorker worker;

    void Awake()
    {
        weatherModel = ModelLoader.Load(weatherAiModelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, weatherModel);
        Debug.Log("Weather AI Model Initialized!");
    }

    public int PredictWeatherEvent(float[] weatherStateInput)
    {
        var inputTensor = new Tensor(1, 10, weatherStateInput);  // 10 inputs for weather state
        worker.Execute(inputTensor);

        Tensor output = worker.PeekOutput();
        int weatherPrediction = output.ArgMax()[0];  // Get the index of the predicted weather event

        inputTensor.Dispose();
        output.Dispose();
        
        return weatherPrediction;  // e.g., 0 = clear skies, 1 = rain, etc.
    }

    void OnDestroy()
    {
        worker.Dispose();
    }
}
