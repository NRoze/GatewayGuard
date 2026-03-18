namespace SampleApi;

public static class TestState
{
    static private int executionCount = 0;
    static public int ExecutionCount { get { return executionCount; } }
   
    static public void Increment()
    {
        Interlocked.Increment(ref executionCount);
    }

    public static void Reset()
    {
        executionCount = 0;
    }
}
