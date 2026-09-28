using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_SelectWithSelectColumn_ReturnsErrorWhenNotRendered()
    {
        // Select is a historical schema column, but the active table shape has no matching
        // column, so strict projection returns an error.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Select"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Select", error);
    }

    [Fact]
    public async Task Type_ExactSelectionPromotesRequestedColumnVerbosity()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Signature"],
            TipLevel = TipLevel.Quiet,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Signature |", output);
        Assert.Contains(
            "`public static bool IsReflectionEnabledByDefault { get; }`",
            output);
    }

    [Fact]
    public async Task Type_SelectWithValidColumn_NoWarning()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("no data", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_Effective_DropsSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Effective discovery reports only columns that actually render. The historical
        // Select column must not appear.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverSection_Effective_ReflectsVerbosityColumns()
    {
        // At default (Minimal) verbosity, Properties renders the summary row (Return Type/Accessors).
        var minimal = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"]
        };
        var (exitMin, minOutput, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(minimal));

        Assert.Equal(0, exitMin);
        Assert.Contains("| Return Type | column |", minOutput);
        Assert.DoesNotContain("| Signature | column |", minOutput);

        // At Detailed verbosity, Properties renders the full member row (Signature, no Return Type).
        var detailed = minimal with { Verbosity = Verbosity.Detailed };
        var (exitDet, detOutput, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(detailed));

        Assert.Equal(0, exitDet);
        Assert.Contains("| Signature | column |", detOutput);
        Assert.DoesNotContain("| Return Type | column |", detOutput);
    }

    [Fact]
    public async Task Type_NonexistentPackage_ShowsError()
    {
        var options = new TypeOptions { PackagePath = "NonexistentPackage123456" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Type_LocalAssembly_ListsTypes()
    {
        var options = new TypeOptions { AssemblyPath = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("CommandExecutionTests", output);
    }

    [Fact]
    public async Task Type_SingleSectionCount_WritesInteger()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Type_DefaultCount_MatchesMemberIndex()
    {
        var options = new TypeOptions
        {
            AssemblyPath = TestAssemblyPath,
            TypeName = typeof(CommandExecutionTests).FullName,
            Count = true,
        };

        var (defaultExit, defaultOutput, defaultError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(options));
        var (explicitExit, explicitOutput, explicitError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options with
                    {
                        Select = [SectionNames.MemberIndex],
                    }));

        Assert.Equal(0, defaultExit);
        Assert.Equal(0, explicitExit);
        Assert.Empty(defaultError);
        Assert.Empty(explicitError);
        Assert.Equal(explicitOutput, defaultOutput);
        Assert.True(
            int.Parse(
                defaultOutput.Trim(),
                CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Type_DefaultCount_EmptyMemberPopulation_WritesZero()
    {
        var options = new TypeOptions
        {
            AssemblyPath = TestAssemblyPath,
            TypeName = typeof(EmptyDiscoveryFixture).FullName,
            Count = true,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_DefaultCount_EnumMatchesMemberIndex()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "System.DayOfWeek",
            Count = true,
        };

        var (defaultExit, defaultOutput, defaultError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(options));
        var (explicitExit, explicitOutput, explicitError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options with
                    {
                        Select = [SectionNames.MemberIndex],
                    }));

        Assert.Equal(0, defaultExit);
        Assert.Equal(0, explicitExit);
        Assert.Empty(defaultError);
        Assert.Empty(explicitError);
        Assert.Equal(explicitOutput, defaultOutput);
        Assert.True(
            int.Parse(
                defaultOutput.Trim(),
                CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Type_SingleSectionCount_WithPlainText_WritesInteger()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"],
            Count = true,
            PlainText = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task TypeDiscovery_NoMemberType_DoesNotListMethodBodySections()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(EmptyDiscoveryFixture).FullName!, "--library", TestAssemblyPath, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);

        var sections = ExtractDiscoveryRows(output)
            .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Name)
            .ToArray();

        Assert.DoesNotContain("Top Leverage", sections);
        Assert.DoesNotContain("Performance Triage", sections);
        Assert.DoesNotContain("Facts", sections);
        Assert.DoesNotContain("Cost Overlay", sections);
        Assert.DoesNotContain("Semantics Overlay", sections);
        Assert.DoesNotContain("IL", sections);
        Assert.DoesNotContain("Source Files", sections);
    }

    [Fact]
    public async Task Type_UnknownSelectValue_ListsAvailableSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "ZzzNoSuchSection", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("not found", error);
        Assert.Contains("Available sections:", error);
        Assert.Contains("Run with -D to discover sections", error);
    }

    [Fact]
    public async Task Type_MemberIndexSection_OmitsEmptyDecodeColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.StringBuilder", "-S", "Member Index", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Empty(error);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.DoesNotContain("Decode", output);
    }
}
