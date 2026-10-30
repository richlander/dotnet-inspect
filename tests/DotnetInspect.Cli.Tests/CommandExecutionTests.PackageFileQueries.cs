namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Package_FileQueries_WrappersAndPredicatesSelectSameRows()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Package.FileQuery", "README.md", "readme",
            extraFiles:
            [
                ("lib/net8.0/A.dll", "a"),
                ("lib/net8.0/A.xml", "x"),
                ("lib/net6.0/A.dll", "old"),
                ("buildTransitive/net8.0/A.targets", "build"),
                ("runtimes/win/lib/net8.0/A.dll", "runtime"),
                ("docs/net8.0.txt", "filename"),
            ]);
        try
        {
            var flags = await RunAppAsync("package", packagePath, "--files",
                "--lib", "--tfm", "net8.0", "--paths");
            var predicates = await RunAppAsync("package", packagePath, "-S", "Files",
                "--where", "Root=lib", "--where", "Target=NET8.0", "--paths");
            Assert.Equal(0, flags.Exit);
            Assert.Equal(0, predicates.Exit);
            Assert.Equal(flags.Output, predicates.Output);
            Assert.Contains("lib/net8.0/A.dll", flags.Output);
            Assert.Contains("lib/net8.0/A.xml", flags.Output);
            Assert.DoesNotContain("buildTransitive", flags.Output);
            Assert.DoesNotContain("runtimes", flags.Output);
            Assert.DoesNotContain("net6.0", flags.Output);

            var count = await RunAppAsync("package", packagePath, "--files",
                "--where", "Root=lib", "--where", "Target=net8.0", "--count");
            Assert.Equal(0, count.Exit);
            Assert.Equal("2", count.Output.Trim());
            var window = await RunAppAsync("package", packagePath, "--files",
                "--where", "Root=lib", "--where", "Target=net8.0",
                "--rows", "2..2", "--paths");
            Assert.Equal(0, window.Exit);
            Assert.Contains("A.xml", window.Output);
            Assert.DoesNotContain("A.dll", window.Output);

            var tree = await RunAppAsync("package", packagePath, "--files",
                "--lib", "--tfm", "net8.0");
            Assert.Equal(0, tree.Exit);
            Assert.Contains("lib/net8.0", tree.Output);
            Assert.Contains("└─", tree.Output);
            Assert.DoesNotContain("buildTransitive", tree.Output);
            var sectionTree = await RunAppAsync("package", packagePath, "-S", "Files",
                "--where", "Root=lib", "--where", "Target=net8.0");
            Assert.Equal(tree.Output, sectionTree.Output);
            var composed = await RunAppAsync("package", packagePath, "--files",
                "-S", "Package Info", "--markdown");
            Assert.Equal(0, composed.Exit);
            Assert.Contains("## Files", composed.Output);
            Assert.Contains("## Package Info", composed.Output);
            var empty = await RunAppAsync("package", packagePath, "--files",
                "--where", "Root=missing", "--count");
            Assert.Equal(0, empty.Exit);
            Assert.Equal("0", empty.Output.Trim());
            var names = await RunAppAsync("package", packagePath, "--files",
                "--where", "Directory=lib/net8.0", "--where", "Name=*.xml", "--paths");
            Assert.Equal(0, names.Exit);
            Assert.Contains("lib/net8.0/A.xml", names.Output);
            Assert.DoesNotContain("A.dll", names.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--where", "Missing=value")]
    [InlineData("--where", "Root contains lib")]
    [InlineData("--layout", null)]
    public async Task Package_FileQueries_InvalidPredicatesFailBeforeAcquisition(string flag, string? value)
    {
        string[] args = ["--offline", "package", "Package.Must.Not.Resolve", "--files", flag];
        var result = await RunAppAsync(value is null ? args : [.. args, value]);
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("Could not resolve", result.Error);
    }
}
