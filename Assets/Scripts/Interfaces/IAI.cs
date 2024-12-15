
public interface IAI
{
    // Called by the TaskHandler to process a given task
    void ProcessTask(GameTask task);

    // Potential initialization method if needed
    void Initialize();
}
