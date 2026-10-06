using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

// Inherits Speed=Slow; covered by focused pre-merge and daily Deep Inspect gates.
public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_VbInternalExplicitPropertyUsesPropertyKind()
    {
        string fixture =
            FixtureCatalog.MetadataVbInterfaceImplementations.AssemblyPath();
        var (propertyExit, propertyOutput, propertyError) = await RunAppAsync(
            "type", "VbImplementations",
            "--library", fixture,
            "--all",
            "-k", "property");
        var (explicitExit, explicitOutput, explicitError) = await RunAppAsync(
            "type", "VbImplementations",
            "--library", fixture,
            "--all",
            "-k", "explicit-interface-implementation");
        var (decompiledExit, decompiledOutput, decompiledError) =
            await RunAppAsync(
                "type", "VbImplementations",
                "--library", fixture,
                "--all",
                "-S", "Decompiled Source");

        Assert.Equal(0, propertyExit);
        Assert.Empty(propertyError);
        Assert.Contains(
            "PrivateImplementsLocalInternalProperty",
            propertyOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "get_PrivateImplementsLocalInternalProperty",
            propertyOutput,
            StringComparison.Ordinal);
        Assert.Equal(0, explicitExit);
        Assert.Empty(explicitError);
        Assert.DoesNotContain(
            "PrivateImplementsLocalInternalProperty",
            explicitOutput,
            StringComparison.Ordinal);
        Assert.Equal(0, decompiledExit);
        Assert.Empty(decompiledError);
        Assert.Contains(
            "internal virtual int PrivateImplementsLocalInternalProperty",
            decompiledOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Decompiled Source unavailable",
            decompiledOutput,
            StringComparison.Ordinal);
    }
}
