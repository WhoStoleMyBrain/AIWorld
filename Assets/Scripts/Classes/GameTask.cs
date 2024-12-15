public class GameTask
{
    public TaskType Type { get; private set; }
    public object Payload { get; private set; }

    public GameTask(TaskType type, object payload = null)
    {
        Type = type;
        Payload = payload;
    }
}
