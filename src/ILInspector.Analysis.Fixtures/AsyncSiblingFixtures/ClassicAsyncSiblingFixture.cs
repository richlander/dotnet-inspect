namespace ILInspector.Analysis.ClassicAsyncFixtures;

public static class ClassicAsyncSiblingFixture
{
    public static int ReadValue(int value)
        => value;

    public static Task<int> ReadValueAsync(
        int value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    public static async Task<int> CallsSyncSiblingFromAsync(
        int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }
}
