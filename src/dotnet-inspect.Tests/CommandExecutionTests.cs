using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

/// <summary>
/// Integration tests that verify actual command execution produces correct output.
/// Uses platform libraries and the test assembly itself as data sources — no network required.
/// </summary>
[Collection("Console")]
public class CommandExecutionTests
{
    private static readonly string TestAssemblyPath =
        typeof(CommandExecutionTests).Assembly.Location;

    private static (string PackagePath, string TempDir) CreateLocalRefPackage(params string[] assemblyNames)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        string? tfm = null;

        foreach (var assemblyName in assemblyNames)
        {
            var (path, _, _, error) = PlatformResolver.ResolveAssembly(assemblyName);
            Assert.True(error == null && path != null, $"Could not resolve platform assembly '{assemblyName}': {error}");

            tfm ??= Path.GetFileName(Path.GetDirectoryName(path!));
            var targetDir = Path.Combine(packageRoot, "ref", tfm!);
            Directory.CreateDirectory(targetDir);
            File.Copy(path!, Path.Combine(targetDir, Path.GetFileName(path!)));
        }

        var packagePath = Path.Combine(tempDir, "Test.MultiLib.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    public CommandExecutionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    private static Task<(int Exit, string Output, string Error)> RunAppAsync(params string[] args)
    {
        return ConsoleCapture.RunAsync(async () =>
        {
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await root.Parse(args).InvokeAsync();
        });
    }

    // ── api command ──────────────────────────────────────────────────

    [Fact]
    public async Task Api_PlatformLibrary_ListsTypes()
    {
        var options = new ApiOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Api_PlatformLibrary_WithTypeFilter_ShowsMembers()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        Assert.Contains("Deserialize", output);
    }

    [Fact]
    public async Task Api_PlatformLibrary_JsonOutput()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            JsonOutput = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Should be valid JSON
        var doc = JsonDocument.Parse(output);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task Api_PlatformLibrary_OneLine()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            OneLine = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);

        // OneLine format produces tab-separated or columnar output
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, "Expected multiple lines of type output");
    }

    [Fact]
    public async Task Type_SingleType_SelectClasses_ShowsSelectError()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            OneLine = true,
            Select = ["Classes"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error);
    }

    [Fact]
    public async Task Type_SingleType_NoQuery_DefaultsToShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Default single-type invocation renders the tree shape.
        Assert.Contains("├─", output);
        Assert.Contains("Inherits", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectSection_RendersSectionNotShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selection produces a focused section view, not the tree shape.
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectWithColumns_ProjectsColumns()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_ExplicitShapeWithSelect_WarnsAndKeepsShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            ShapeOutput = true,
            ShapeExplicitlySet = true,
            Select = ["Properties"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("--shape does not support", error);
        Assert.Contains("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectEmptySection_WritesNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Values"]  // enum-only section; JsonSerializer is a class
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_SelectPopulatedSection_NoEmptyNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelect_ScopesToSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        var members = doc.RootElement.GetProperty("members");
        Assert.True(members.GetArrayLength() > 0);
        foreach (var m in members.EnumerateArray())
            Assert.Equal("property", m.GetProperty("kind").GetString());
        // Non-selected facets are scoped out.
        Assert.Empty(doc.RootElement.GetProperty("interfaces").EnumerateArray());
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelectEmptySection_EmptyMembersAndNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Values"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Empty(doc.RootElement.GetProperty("members").EnumerateArray());
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverMethods_Schema_ListsAllColumns()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Name", output);
        Assert.Contains("Signature", output);
    }

    [Fact]
    public async Task Type_SingleType_Discover_DefaultsToEffective_DropsEmptySections()
    {
        // -D with no --schema now defaults to effective discovery: it resolves the source
        // and lists only sections that actually have data (the empty-section footgun fix).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Methods | section |", output);
        // JsonSerializer has no custom attributes, so effective-by-default must drop it.
        Assert.DoesNotContain("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverSchema_ListsAllStaticSections()
    {
        // --schema opts back out to the cheap, offline static schema listing, which
        // includes sections that may have no data (e.g. Custom Attributes, Fields).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.Contains("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_OnlyShowsSectionsWithData()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Effective = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Methods | section |", output);
        Assert.DoesNotContain("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_ExcludesMemberDetailCodeSections()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Effective = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Properties | section |", output);
        Assert.Contains("| Methods | section |", output);
        // Code sections (Source, IL, IL (Annotated), Lowered C#) are member-detail
        // sections not present in the type schema. They must not appear in effective
        // discovery, since they are not queryable via -D <Section>.
        Assert.DoesNotContain("| Source | section |", output);
        Assert.DoesNotContain("| IL | section |", output);
        Assert.DoesNotContain("| IL (Annotated) | section |", output);
        Assert.DoesNotContain("| Lowered C# | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_ExcludesEmptyCustomAttributesSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Effective = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Methods | section |", output);
        // Custom Attributes is in the type schema, but its CanRender probe is a coarse
        // "type has methods" proxy; the section only has data when a specific member's
        // attributes are read. JsonSerializer has none, so effective discovery must not list it.
        Assert.DoesNotContain("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_DiscoverEmptySection_Effective_ReportsNoDataNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Custom Attributes"],
            Effective = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A valid-but-empty section reports a clear "no data" note rather than the
        // misleading "Section not found", and exits 0.
        Assert.Equal(0, exit);
        Assert.Contains("section 'Custom Attributes' has no data", error);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverUnknownSection_Effective_ReportsNotFound()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Bogus"],
            Effective = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A genuinely unknown section still reports "not found" (with suggestions) and exits 1.
        Assert.Equal(1, exit);
        Assert.Contains("Section 'Bogus' not found", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_WithoutEffective_HidesSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // The Select overload-index column only renders with --show-index, so plain
        // single-type discovery hides it while still listing the real columns.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverSection_ShowIndex_ListsSelectColumn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"],
            ShowSelect = true,
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // With --show-index the Select column does render, so discovery must list it.
        Assert.Contains("| Select | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_HidesSelectColumn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            Effective = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Without --show-index the Select column is not queryable, so effective
        // discovery must not list it (regression: member effective used to leak it).
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ShowIndex_ListsSelectColumn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            Effective = true,
            ShowSelect = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // With --show-index the Select column renders, so effective discovery lists it.
        Assert.Contains("| Select | column |", output);
    }

    [Fact]
    public async Task Type_SelectWithUnknownColumn_WarnsNotFound()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("column 'Bogus' not found in section 'Properties'", error);
    }

    [Fact]
    public async Task Type_SelectWithSelectColumn_WarnsNoData()
    {
        // Select is valid in the schema but only renders with --show-index, so on the
        // plain type path it produces no data and must be flagged (not silently ignored).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Select"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("no data: Select", error);
    }

    [Fact]
    public async Task Type_SelectWithColumnNotShownAtVerbosity_WarnsNoData()
    {
        // Signature is a valid Properties column but only renders at Detailed verbosity;
        // at the default verbosity it is absent and must be flagged.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Signature"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("no data: Signature", error);
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
            Discover = ["Properties"],
            Effective = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Effective discovery reports only columns that actually render. Select is hidden
        // without --show-index, so it must not appear.
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
            Discover = ["Properties"],
            Effective = true
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
    public async Task Api_NonexistentPackage_ShowsError()
    {
        var options = new ApiOptions { PackagePath = "NonexistentPackage123456" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Api_LocalAssembly_ListsTypes()
    {
        var options = new ApiOptions { AssemblyPath = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("CommandExecutionTests", output);
    }

    // ── find command ─────────────────────────────────────────────────

    [Fact]
    public async Task Find_PlatformLibrary_FindsType()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Member_PackageLibrarySelector_ResolvesBareLibraryName()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime", "System.Text.RegularExpressions");
        try
        {
            var options = new MemberOptions
            {
                TypeName = "RegexOptions",
                PackagePath = packagePath,
                AssemblyPath = "System.Text.RegularExpressions"
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("RegexOptions", output);
            Assert.Contains("None", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_PackageTypeResolution_SearchesAcrossPackageLibraries()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime", "System.Text.RegularExpressions");
        try
        {
            var options = new MemberOptions
            {
                TypeName = "RegexOptions",
                PackagePath = packagePath,
                Verbosity = Verbosity.Minimal
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("RegexOptions", output);
            Assert.Contains("Compiled", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Find_NoPattern_ShowsError()
    {
        var options = new FindOptions
        {
            Pattern = "",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No pattern", error);
    }

    // ── assembly command ─────────────────────────────────────────────

    [Fact]
    public async Task Assembly_PlatformLibrary_ShowsInfo()
    {
        var options = new AssemblyOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Assembly_SingleSectionCount_WritesInteger()
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Async*"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_CountWithoutSingleSection_Errors()
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = "System.Text.Json",
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(CountOutput.SingleSectionRequiredMessage, error);
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
    public async Task Assembly_LocalAssembly_ShowsInfo()
    {
        var options = new AssemblyOptions { AssemblyName = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("dotnet-inspect.Tests", output);
    }

    [Fact]
    public async Task Assembly_Audit_ShowsMetadataSignalsOnly()
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = "System.Text.Json",
            Audit = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Audit", output);
        Assert.Contains("IsTrimmable", output);
        Assert.Contains("IsAotCompatible", output);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
        Assert.Contains("not a security or trust assessment", output);
        Assert.DoesNotContain("## Library Info", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_AuditSectionSelection_PopulatesReferenceSignals()
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Audit" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
    }

    [Fact]
    public async Task Assembly_Audit_LocalUnsafeAssembly_FocusesOnNewMemorySafetyModel()
    {
        var options = new AssemblyOptions
        {
            AssemblyName = TestAssemblyPath,
            Audit = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Memory safety | Memory safety model | Not marked |", output);
        Assert.Contains("| Memory safety | RequiresUnsafe members | 0 | RequiresUnsafeAttribute |", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.Contains("| Interop | P/Invoke methods | 2 | all PInvokeImpl metadata |", output);
    }

    [Fact]
    public async Task AuditCommand_Library_ShowsAuditOnly()
    {
        var (exit, output, error) = await RunAppAsync("audit", "library", TestAssemblyPath);

        Assert.Equal(0, exit);
        Assert.Contains("## Audit", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task AuditCommand_Platform_DefaultDoesNotDownloadPdb()
    {
        var (exit, output, _) = await RunAppAsync("audit", "System.Text.Json");

        Assert.Equal(0, exit);
        Assert.Contains("## Audit", output);
        Assert.Contains("PDB not checked", output);
    }

    [Fact]
    public async Task AuditCommand_LibraryFull_ChecksSymbolsWithoutSourceAudit()
    {
        var (exit, output, error) = await RunAppAsync("audit", "library", TestAssemblyPath, "--full");

        Assert.Equal(0, exit);
        Assert.Contains("## Audit", output);
        Assert.DoesNotContain("PDB not checked", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.Contains("Metadata + symbol signals", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task AuditCommand_LibraryAll_IsAliasForFull()
    {
        var (exit, output, error) = await RunAppAsync("audit", "library", TestAssemblyPath, "--all");

        Assert.Equal(0, exit);
        Assert.Contains("## Audit", output);
        Assert.DoesNotContain("PDB not checked", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.Contains("Metadata + symbol signals", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task AuditCommand_LibraryDetailed_IncludesSourceLinkAuditSection()
    {
        var (exit, output, error) = await RunAppAsync("audit", "library", TestAssemblyPath, "-v:d");

        Assert.Equal(0, exit);
        Assert.Contains("## Audit", output);
        Assert.Contains("## SourceLink Audit", output);
        Assert.Contains("Source Files", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformVersion_UsesPlatformRuntimeRoute()
    {
        var currentRuntimeVersion = Path.GetFileName(Path.GetDirectoryName(typeof(object).Assembly.Location))!;

        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", currentRuntimeVersion, "-v:q");

        Assert.Equal(0, exit);
        Assert.Contains("Source: Platform", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task AuditCommand_PlatformVersion_DoesNotFallbackToPackageVersion()
    {
        var (exit, _, error) = await RunAppAsync(
            "audit", "System.Text.Json", "--version", "0.0.0-definitely-not-installed");

        Assert.Equal(1, exit);
        Assert.Contains("Use 'audit package <package> --version <version>'", error);
    }

    // ── package command ──────────────────────────────────────────────

    [Fact]
    public async Task Package_NonexistentPackage_ShowsError()
    {
        var options = new InspectionOptions
        {
            PackageArgs = ["NonexistentPackage123456"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Package_Audit_ShowsMetadataSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                Audit = true
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("## Audit", output);
            Assert.Contains("Target frameworks", output);
            Assert.Contains("Assemblies", output);
            Assert.Contains("Direct dependencies", output);
            Assert.Contains("Metadata signals only", output);
            Assert.DoesNotContain("Known vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AuditCommand_Package_ShowsAuditOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("audit", "package", packagePath);

            Assert.Equal(0, exit);
            Assert.Contains("## Audit", output);
            Assert.Contains("Metadata signals only", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AuditCommand_PackageFull_EnablesNuGetAudit()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("audit", packagePath, "--full");

            Assert.Equal(0, exit);
            Assert.Contains("## Audit", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.Contains("Metadata + NuGet registry signals", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AuditCommand_PackageAll_IsAliasForFull()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("audit", "package", packagePath, "--all");

            Assert.Equal(0, exit);
            Assert.Contains("## Audit", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.Contains("Metadata + NuGet registry signals", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
