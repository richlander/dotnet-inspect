using System.Runtime.Versioning;

[SupportedOSPlatform("browser")]
public sealed class AsyncLoweringCanaryTests
{
    [Fact]
    public async Task AsyncLoweringCanary_ReturnsStableResult()
    {
        Assert.Equal(
            "inspect-web-async-lowering-ok",
            await InspectionEngine.AsyncLoweringCanary());
    }
}
