using System.Runtime.Versioning;

namespace DotnetInspect.Web.Tests;

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
