namespace ILInspector.Analysis.UnoptimizedAsyncFixtures;

public static class UnoptimizedAsyncFixture
{
    public static async Task<string>
        ReturnsCallStoredBeforeAwait()
    {
        string payload = ProducePayload();
        await Task.Yield();
        return payload;
    }

    static string ProducePayload() => "payload";
}
