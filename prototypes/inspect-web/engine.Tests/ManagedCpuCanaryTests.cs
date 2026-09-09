using System.Runtime.Versioning;

[SupportedOSPlatform("browser")]
public sealed class ManagedCpuCanaryTests
{
    [Fact]
    public void ManagedCpuCanary_ReturnsStableChecksum()
    {
        Assert.Equal("c0583d8c", InspectionEngine.ManagedCpuCanary());
    }
}
