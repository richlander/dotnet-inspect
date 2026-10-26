namespace ILInspector.Metadata.CompilationProfileFixtures;

public static class RuntimeAsyncProfileFixtures
{
    public static int Synchronous(int value) =>
        value;

    /// <summary>The Library's only runtime-async method, and it is internal.</summary>
    internal static async Task<int> InternalRuntimeAsync(int value)
    {
        await Task.Yield();
        return value;
    }

    /// <summary>Runtime async does not apply to async iterators; the compiler keeps the state machine.</summary>
    public static async IAsyncEnumerable<int> PublicAsyncIterator()
    {
        await Task.Yield();
        yield return 42;
    }

    /// <summary>Runtime async applies only to Task and ValueTask returns; async void keeps the state machine.</summary>
    public static async void FireAndForget()
    {
        await Task.Yield();
    }

    public static IEnumerable<int> Iterator()
    {
        yield return 42;
    }
}
