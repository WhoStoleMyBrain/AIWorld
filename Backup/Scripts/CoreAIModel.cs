using Unity.Barracuda;
using UnityEngine;

public class CoreAIModel : MonoBehaviour
{
    public NNModel coreAiModelAsset;
    private Model coreModel;
    private IWorker worker;

    void Awake()
    {
        coreModel = ModelLoader.Load(coreAiModelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, coreModel);
        Debug.Log("Core AI Model Initialized!");
    }

    public int PredictTask(float[] worldStateInput)
    {
        var inputTensor = new Tensor(1, 5, worldStateInput);  // 5 inputs for world state
        worker.Execute(inputTensor);

        Tensor output = worker.PeekOutput();
        int taskPrediction = output.ArgMax()[0];  // Get the index of the highest probability task

        inputTensor.Dispose();
        output.Dispose();
        
        return taskPrediction;  // 0 = EVALUATE_RESOURCES, 1 = MANAGE_SETTLEMENTS, etc.
    }

    void OnDestroy()
    {
        worker.Dispose();
    }
}
