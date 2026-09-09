namespace DotnetInspector.Tests;

public class HostileOutputAssertTests
{
    [Fact]
    public void NoLineSplit_RejectsAMarkerAtTheStartOfOutput()
    {
        const string Marker = "Error: INJECTEDARG";

        Exception? exception = Record.Exception(
            () => HostileOutputAssert.NoLineSplit(Marker, Marker));

        Assert.NotNull(exception);
        Assert.Contains("starts the output", exception.Message, StringComparison.Ordinal);
    }
}
