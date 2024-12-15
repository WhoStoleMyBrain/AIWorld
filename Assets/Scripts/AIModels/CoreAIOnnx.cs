using Unity.Barracuda;
using UnityEngine;
using System.Linq;

public class CoreAIOnnx : ICoreAI
{
    private Model _model;
    private IWorker _worker;
    private TaskType[] _taskMapping;

    // Constructor: modelName is the name of the model asset without extension inside "Resources" folder.
    public CoreAIOnnx(string modelName)
    {
        // Load the NNModel from Resources
        NNModel nnModel = Resources.Load<NNModel>(modelName);
        if (nnModel == null)
        {
            Debug.LogError($"CoreAIOnnx: Could not load NNModel '{modelName}' from Resources.");
            return;
        }

        _model = ModelLoader.Load(nnModel);
        _worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, _model);

        _taskMapping = new TaskType[]
        {
            TaskType.EVALUATE_RESOURCES,
            TaskType.MANAGE_SETTLEMENTS,
            TaskType.UPDATE_WEATHER,
            TaskType.EVALUATE_SOCIAL_STABILITY,
            TaskType.NO_TASK
        };
    }

    public TaskType EvaluateNextTask(float[] worldState)
    {
        if (worldState.Length != 5) 
            throw new System.ArgumentException("World state must be length 5.");

        // Create input tensor (1 batch, 5 features)
        using (var inputTensor = new Tensor(1, 5, worldState))
        {
            _worker.Execute(inputTensor);
            var output = _worker.PeekOutput(); // Assuming single output layer

            // Output expected as a vector of length 5 (probabilities or logits)
            float[] outputArray = output.ToReadOnlyArray();
            int maxIndex = 0;
            float maxVal = outputArray[0];
            for (int i = 1; i < outputArray.Length; i++)
            {
                if (outputArray[i] > maxVal)
                {
                    maxVal = outputArray[i];
                    maxIndex = i;
                }
            }

            TaskType decidedTask = _taskMapping[maxIndex];

            if (decidedTask == TaskType.UPDATE_WEATHER)
            {
                // Add Weather Task
                TaskHandler.Instance.AddTask(new GameTask(TaskType.UPDATE_WEATHER));
            }

            return decidedTask;
        }
    }
}
