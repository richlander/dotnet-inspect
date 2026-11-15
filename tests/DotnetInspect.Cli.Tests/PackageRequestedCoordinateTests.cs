using DotnetInspect.Cli.Commands;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// A local archive's file name is display identity. It names a package
/// coordinate only when its id and version are both valid coordinate parts;
/// otherwise Library selection proceeds without one instead of throwing.
/// </summary>
public class PackageRequestedCoordinateTests
{
    [Fact]
    public void ValidNameAndVersion_FormTheCoordinate()
    {
        var coordinate = PackageCommand.TryCreateRequestedPackageCoordinate(
            "Tool.Sample",
            "1.0.0");

        Assert.NotNull(coordinate);
        Assert.Equal("Tool.Sample", coordinate.PackageId, ignoreCase: true);
        Assert.Equal("1.0.0", coordinate.Version);
    }

    [Theory]
    [InlineData("not-a-package-coordinate", "")]
    [InlineData("payload", "not-a-version")]
    [InlineData("Not A Package", "1.0.0")]
    [InlineData("Tool.Sample", "1.0.0 ")]
    [InlineData("Tool.Sample", " 1.0.0")]
    public void InvalidNameOrVersion_FormsNoCoordinate(string packageName, string version)
    {
        Assert.Null(PackageCommand.TryCreateRequestedPackageCoordinate(packageName, version));
    }
}
