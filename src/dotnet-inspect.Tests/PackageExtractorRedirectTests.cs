using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for the RID-wrapper tool-package redirect helpers on <see cref="PackageExtractor"/>.
/// These cover the pure file/XML inspection logic without any network access.
/// </summary>
public class PackageExtractorRedirectTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotnet-inspect-redirect-tests-" + Guid.NewGuid().ToString("N"));

    public PackageExtractorRedirectTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteToolSettings(string anyId)
    {
        var dir = Path.Combine(_root, "tools", "net10.0", "any");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "DotnetToolSettings.xml");
        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="dotnet-inspect" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="dotnet-inspect.linux-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="{anyId}" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);
        return path;
    }

    [Fact]
    public void HasManagedLibraries_TrueForNonResourceDll()
    {
        var dir = Path.Combine(_root, "tools", "net10.0", "any");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "MyLib.dll"), "stub");

        Assert.True(PackageExtractor.HasManagedLibraries(_root));
    }

    [Fact]
    public void HasManagedLibraries_FalseForResourceOnlyOrEmpty()
    {
        var dir = Path.Combine(_root, "tools", "net10.0", "any", "de");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "MyLib.resources.dll"), "stub");

        Assert.False(PackageExtractor.HasManagedLibraries(_root));
    }

    [Fact]
    public void HasManagedLibraries_FalseForMissingPath()
        => Assert.False(PackageExtractor.HasManagedLibraries(Path.Combine(_root, "does-not-exist")));

    [Fact]
    public void TryGetAnyRidToolPackageId_ReturnsAnyPackage()
    {
        WriteToolSettings("dotnet-inspect.any");
        Assert.Equal("dotnet-inspect.any", PackageExtractor.TryGetAnyRidToolPackageId(_root));
    }

    [Fact]
    public void TryGetAnyRidToolPackageId_NullWhenNoManifest()
        => Assert.Null(PackageExtractor.TryGetAnyRidToolPackageId(_root));
}
