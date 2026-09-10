using System.Runtime.Versioning;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class ManagedCpuCanaryTests
{
    [Fact]
    public void ManagedCpuCanary_ReturnsStableChecksum()
    {
        Assert.Equal("c0583d8c", InspectionEngine.ManagedCpuCanary());
    }
}
