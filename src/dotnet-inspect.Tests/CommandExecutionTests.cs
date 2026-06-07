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

    private static (string PackagePath, string TempDir) CreateLocalLibPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var net8Dir = Path.Combine(packageRoot, "lib", "net8.0");
        var net10Dir = Path.Combine(packageRoot, "lib", "net10.0");
        Directory.CreateDirectory(net8Dir);
        Directory.CreateDirectory(net10Dir);
        File.Copy(TestAssemblyPath, Path.Combine(net8Dir, "Older.dll"));
        File.Copy(TestAssemblyPath, Path.Combine(net10Dir, "Latest.One.dll"));
        File.Copy(TestAssemblyPath, Path.Combine(net10Dir, "Latest.Two.dll"));
        File.WriteAllText(Path.Combine(net10Dir, "Latest.One.xml"), "<doc />");

        var packagePath = Path.Combine(tempDir, "Test.LibraryFiles.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PointerPackagePath, string RidPackagePath, string TempDir) CreateLocalToolPackageSet()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tool-package-test-{Guid.NewGuid():N}");

        var pointerRoot = Path.Combine(tempDir, "pointer");
        var pointerToolsDir = Path.Combine(pointerRoot, "tools", "net10.0", "any");
        Directory.CreateDirectory(pointerToolsDir);
        File.WriteAllText(Path.Combine(pointerToolsDir, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="test-tool" EntryPoint="Test.Tool.dll" Runner="dotnet" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="Test.Tool.linux-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="Test.Tool.any" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);

        var payloadRoot = Path.Combine(tempDir, "payload");
        var payloadToolsDir = Path.Combine(payloadRoot, "tools", "net10.0", "any");
        Directory.CreateDirectory(payloadToolsDir);
        File.Copy(TestAssemblyPath, Path.Combine(payloadToolsDir, "Test.Tool.dll"));

        var ridRoot = Path.Combine(tempDir, "rid");
        var ridToolsDir = Path.Combine(ridRoot, "tools", "any", "linux-x64");
        Directory.CreateDirectory(ridToolsDir);
        File.WriteAllText(Path.Combine(ridToolsDir, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="test-tool" EntryPoint="test-tool" Runner="executable" />
              </Commands>
            </DotNetCliTool>
            """);

        var pointerPackagePath = Path.Combine(tempDir, "Test.Tool.1.0.0.nupkg");
        var payloadPackagePath = Path.Combine(tempDir, "Test.Tool.any.1.0.0.nupkg");
        var ridPackagePath = Path.Combine(tempDir, "Test.Tool.linux-x64.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(pointerRoot, pointerPackagePath);
        ZipFile.CreateFromDirectory(payloadRoot, payloadPackagePath);
        ZipFile.CreateFromDirectory(ridRoot, ridPackagePath);

        return (pointerPackagePath, ridPackagePath, tempDir);
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
            Discover = []
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
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Properties | section |", output);
        Assert.Contains("| Methods | section |", output);
        // Code sections (Decompiled Source, Original Source, IL, IL (Annotated)) are member-detail
        // sections not present in the type schema. They must not appear in effective
        // discovery, since they are not queryable via -D <Section>.
        Assert.DoesNotContain("| Decompiled Source | section |", output);
        Assert.DoesNotContain("| Original Source | section |", output);
        Assert.DoesNotContain("| IL | section |", output);
        Assert.DoesNotContain("| IL (Annotated) | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_ExcludesEmptyCustomAttributesSection()
    {
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
            Discover = ["Custom Attributes"]
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
            Discover = ["Bogus"]
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
            Discover = ["Methods"]
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
    public async Task Member_DiscoverEffective_ShowIndexAtNormal_ListsSelectColumn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            ShowSelect = true,
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // With --show-index and full member rows, the Select column renders, so effective discovery lists it.
        Assert.Contains("| Select | column |", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DefaultShowsSignatureOnly()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("Type: System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("## Methods", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.DoesNotContain("## IL", output);
    }

    [Fact]
    public async Task Member_ListDefault_RendersCompactSummaryRows()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverEffective_ListsEnabledDetailSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Signature | section |", output);
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| Original Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.Contains("| IL (Annotated) | section |", output);
        Assert.DoesNotContain("Use -S All to select all sections.", output);
        Assert.DoesNotContain("| Methods | section |", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsDetailSectionsWithoutAllHint()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Original Source | section |", output);
        Assert.DoesNotContain("Use -S All to select all sections.", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_NormalShowsLocalImplementationSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("## Custom Attributes", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## IL", output);
        Assert.Contains("## IL (Annotated)", output);
        Assert.DoesNotContain("## Original Source", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_RendersLoweredCSharp()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("GetTypeInfo", output);
        Assert.Contains("WriteElement", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.DoesNotContain("public static JsonElement SerializeToElement", output);
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
            Discover = ["Properties"]
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
    public async Task Library_ToolPointerPackage_ResolvesAnyPayloadAssembly()
    {
        var (packagePath, _, tempDir) = CreateLocalToolPackageSet();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "Test.Tool.dll", "--package", packagePath, "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Tool.dll", output);
            Assert.Contains("| Name | dotnet-inspect.Tests |", output);
            Assert.DoesNotContain("No DLLs found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Library_ToolRidPackage_ResolvesSiblingAnyPayloadAssembly()
    {
        var (_, packagePath, tempDir) = CreateLocalToolPackageSet();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "Test.Tool.dll", "--package", packagePath, "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Tool.dll", output);
            Assert.Contains("| Name | dotnet-inspect.Tests |", output);
            Assert.DoesNotContain("No DLLs found", error);
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
    public async Task Assembly_Signals_ShowsMetadataSignalsOnly()
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("IsTrimmable", output);
        Assert.Contains("IsAotCompatible", output);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("Public key token", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
        Assert.DoesNotContain("| Signals | Scope |", output);
        Assert.DoesNotContain("## Library Info", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_SignalsSectionSelection_PopulatesReferenceSignals()
    {
        var options = new AssemblyOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
    }

    [Fact]
    public async Task LibraryCommand_BareSelect_RendersInfoPreset()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.Contains("| Async Methods |", output);
        Assert.Contains("| Custom Attributes |", output);
        Assert.Contains("| Extension Methods |", output);
        Assert.Contains("| Resources |", output);
        Assert.Contains("| Type Forwarders |", output);
        Assert.DoesNotContain("## Signals", output);
        Assert.DoesNotContain("## Symbols", output);
        Assert.DoesNotContain("## References", output);
        Assert.DoesNotContain("## Async Methods", output);
        Assert.DoesNotContain("## Custom Attributes", output);
        Assert.DoesNotContain("## Extension Methods", output);
        Assert.DoesNotContain("## Resources", output);
        Assert.DoesNotContain("## Type Forwarders", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverEffective_RendersMarkdownTable()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D");

        Assert.Equal(0, exit);
        Assert.Contains("| Name | Kind |", output);
        Assert.Contains("| Library Info | section |", output);
        Assert.Contains("| Async Methods | section (opt-in) |", output);
        Assert.Contains("| Custom Attributes | section (opt-in) |", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSchema_GroupsOptInSections()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D", "--schema");

        Assert.Equal(0, exit);

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("section", StringComparison.Ordinal))
            .ToArray();
        var names = lines.Select(ExtractSectionName).ToArray();

        var symbolsIndex = Array.IndexOf(names, "Symbols");
        var firstOptInIndex = Array.FindIndex(lines, line => line.Contains("section (opt-in)", StringComparison.Ordinal));

        Assert.True(symbolsIndex >= 0);
        Assert.True(firstOptInIndex >= 0);
        Assert.True(symbolsIndex < firstOptInIndex);

        var optInNames = lines
            .Where(line => line.Contains("section (opt-in)", StringComparison.Ordinal))
            .Select(ExtractSectionName)
            .ToArray();
        Assert.Equal(
            [
                "Async Methods",
                "Custom Attributes",
                "Extension Methods",
                "Resources",
                "SourceLink Availability",
                "SourceLink Integrity",
                "SourceLink Missing Files",
                "Type Forwarders"
            ],
            optInNames);
        Assert.DoesNotContain(lines, line => line.StartsWith("Missing Source Files", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Source Integrity", StringComparison.Ordinal));

        static string ExtractSectionName(string line)
        {
            if (line.StartsWith('|'))
            {
                var cells = line.Split('|', StringSplitOptions.TrimEntries);
                return cells.Length > 1 ? cells[1] : line.Trim();
            }

            var marker = line.IndexOf("  section", StringComparison.Ordinal);
            return marker >= 0 ? line[..marker].TrimEnd() : line.TrimEnd();
        }
    }

    [Fact]
    public async Task LibraryCommand_DetailedOutput_RendersSectionsAlphabetically()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-v:d");

        Assert.Equal(0, exit);

        var sectionHeaders = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..])
            .ToArray();

        Assert.NotEmpty(sectionHeaders);
        Assert.Equal(sectionHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray(), sectionHeaders);
    }

    [Fact]
    public async Task Assembly_Signals_LocalUnsafeAssembly_FocusesOnNewMemorySafetyModel()
    {
        var options = new AssemblyOptions
        {
            AssemblyName = TestAssemblyPath,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
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
    public async Task LibraryCommand_Signals_ShowsSignalsOnly()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformSignals_DownloadsPdbByDefault()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        // Signals authorizes PDB acquisition: SourceLink resolves.
        Assert.DoesNotContain("PDB not checked", output);
    }

    [Fact]
    public async Task LibraryCommand_Signals_ChecksSymbolsByDefault()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("PDB not checked", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("| Signals | Scope |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ExplicitSourceLinkAudit_IncludesSourceLinkAuditSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Signals,SourceLink Availability,SourceLink Missing Files");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("## SourceLink Availability", output);
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
    public async Task LibraryCommand_PlatformVersion_DoesNotFallbackToPackageVersion()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", "0.0.0-definitely-not-installed", "-S", "Signals");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not found", error);
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
    public async Task Package_Detailed_DoesNotShowSignalsByDefault()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                Verbosity = Verbosity.Detailed
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("## Signals", output);
            Assert.Contains("## Manifest", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Discover_DefaultsToEffectiveSchema()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D");

            Assert.Equal(0, exit);
            Assert.Contains("Package Info", output);
            Assert.Contains("| Signals | section (opt-in) |", output);
            Assert.Contains("Manifest", output);
            Assert.DoesNotContain("Vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsStaticSchema()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--schema");

            Assert.Equal(0, exit);
            Assert.Contains("Package Info", output);
            Assert.Contains("Signals", output);
            Assert.Contains("Manifest", output);
            Assert.Contains("Vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_WritesAllSelectorHint()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--schema");

            Assert.Equal(0, exit);
            Assert.Contains("Signals", output);
            Assert.Contains("section (opt-in)", output);
            Assert.Contains("Use -S All to select all sections.", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_BareSelect_RendersInfoPreset()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S");

            Assert.Equal(0, exit);
            Assert.Contains("## Package Info", output);
            Assert.Contains("## Library Files", output);
            Assert.DoesNotContain("## Dependencies", output);
            Assert.DoesNotContain("## Manifest", output);
            Assert.DoesNotContain("## Signals", output);
            Assert.True(output.IndexOf("## Package Info", StringComparison.Ordinal) < output.IndexOf("## Library Files", StringComparison.Ordinal));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_BareSelect_RendersInfoPreset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer.SerializeToNode:1", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.DoesNotContain("## IL", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.True(output.IndexOf("## Signature", StringComparison.Ordinal) < output.IndexOf("## Decompiled Source", StringComparison.Ordinal));
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task MemberList_BareSelect_RendersCompactSummaryPreset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Package_SelectAll_IncludesOptInSignals()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "All");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.DoesNotContain("Version: 1.0.0 |", output);
            Assert.True(output.IndexOf("## Package Info", StringComparison.Ordinal) < output.IndexOf("## Signals", StringComparison.Ordinal));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Manifest_RendersBasicPackageManifestRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Manifest");

            Assert.Equal(0, exit);
            Assert.Contains("## Manifest", output);
            Assert.DoesNotContain("| Info | Schema |", output);
            Assert.Contains("| Info | Package | Test.MultiLib", output);
            Assert.Contains("| Info | Version | 1.0.0 |", output);
            Assert.DoesNotContain("| RID Package |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Manifest_RendersToolManifestRows()
    {
        var (exit, output, error) = await RunAppAsync("package", "Azure.Mcp", "-S", "Manifest");

        Assert.Equal(0, exit);
        Assert.Contains("## Manifest", output);
        Assert.Contains("| Info | Manifest Version | 2 |", output);
        Assert.DoesNotContain("| Info | Schema |", output);
        Assert.Contains("| Info | Commands |", output);
        Assert.Contains("| RID Package | linux-x64 | Azure.Mcp.linux-x64 | yes |", output);
        Assert.DoesNotContain("## RID Packages", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Package_LibraryFiles_RendersAllLibFiles()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Library Files");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Files", output);
            Assert.Contains("| TFM | File |", output);
            Assert.Contains("| net10.0 | lib/net10.0/Latest.One.dll |", output);
            Assert.Contains("| net10.0 | lib/net10.0/Latest.One.xml |", output);
            Assert.Contains("| net10.0 | lib/net10.0/Latest.Two.dll |", output);
            Assert.Contains("| net8.0 | lib/net8.0/Older.dll |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DetailedOutput_RendersSectionsAlphabetically()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-v:d");

            Assert.Equal(0, exit);

            var sectionHeaders = output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => line[3..])
                .ToArray();

            Assert.NotEmpty(sectionHeaders);
            Assert.Equal(sectionHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray(), sectionHeaders);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_ShowsMetadataSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.Contains("Supported TFM", output);
            Assert.Contains("Portable", output);
            Assert.Contains("README", output);
            Assert.Contains("License", output);
            Assert.DoesNotContain("Dependency groups", output);
            Assert.Contains("Direct dependencies", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_Signals_ShowsSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_RendersRegistryBackedRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.Contains("Dependencies with vulnerabilities", output);
            Assert.Contains("Deprecated dependencies", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
