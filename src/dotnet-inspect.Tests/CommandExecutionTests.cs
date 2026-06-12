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

    private static (string PackagePath, string TempDir) CreateLocalPrimaryLibPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var libDir = Path.Combine(packageRoot, "lib", "net10.0");
        Directory.CreateDirectory(libDir);
        File.Copy(TestAssemblyPath, Path.Combine(libDir, "Test.Primary.dll"));

        var packagePath = Path.Combine(tempDir, "Test.Primary.1.0.0.nupkg");
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

        // Tabular format produces compact row output
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
    public async Task Type_SingleType_NormalVerbosity_StaysShapeAndExpandsOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-v:n", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("├─", output);
        Assert.Contains("Methods (10 logical, 107 overloads)", output);
        Assert.Contains("Deserialize<TValue>(System.IO.Stream utf8Json", output);
        Assert.DoesNotContain("Deserialize (40 overloads)", output);
        Assert.DoesNotContain("# System.Text.Json.JsonSerializer", output);
    }

    [Fact]
    public async Task Type_SingleType_QuietVerbosity_RequiresMarkdown()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-v:q", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("-v:q is not supported by the type shape renderer", error);
        Assert.Contains("--markdown -v:q", error);
    }

    [Fact]
    public async Task Type_SingleType_MarkdownQuiet_RendersCompactSectionView()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("Kind: class", output);
        Assert.DoesNotContain("├─", output);
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
        Assert.Contains("| Method Groups | section |", output);
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
        Assert.Contains("| Method Groups | section |", output);
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
        Assert.Contains("| Method Groups | section |", output);
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
        Assert.Contains("| Method Groups | section |", output);
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
        Assert.Contains("public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)", output);
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
        Assert.Contains("## Method Groups", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
    }

    [Fact]
    public async Task Member_FilteredDefault_RendersOverloadRows()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            ShowSelect = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Select | Name | Signature |", output);
        Assert.Contains("`SerializeToNode:1`", output);
        Assert.Contains("public static System.Text.Json.Nodes.JsonNode? SerializeToNode(", output);
        Assert.DoesNotContain("## Method Groups", output);
        Assert.DoesNotContain("| Name | Return Type | Overloads |", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_DefaultStaysOverloadInventory()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            ShowSelect = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Select | Name | Signature |", output);
        Assert.Contains("`GetConverter`", output);
        Assert.DoesNotContain("## Signature", output);

        options = options with { Select = ["Methods"] };
        (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Select | Name | Signature |", output);
        Assert.DoesNotContain("## Signature", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_ParamsStillValidatedWithDetailSection()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            ParamTypes = ["Bogus"],
            Select = ["IL"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No overload of GetConverter matches --params", error);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_MixedInfoAndDetailSelect_RendersDetail()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Info", "IL"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## IL", output);
        Assert.Contains("IL_0000:", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_SelectSignature_RendersSignature()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Signature"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("public System.Text.Json.Serialization.JsonConverter GetConverter(System.Type typeToConvert)", output);
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
        Assert.DoesNotContain("Use -S @All to select all sections.", output);
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
        Assert.DoesNotContain("Use -S @All to select all sections.", output);
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
        Assert.Contains("WriteNode(in value", output);
        Assert.DoesNotContain("ref ref", output);
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
        Assert.Contains("public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)", output);
    }

    [Fact]
    public async Task Member_SelectedOperator_SelectDecompiledSource_RendersCSharpOperatorDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "String",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_Equality" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static bool operator ==(string? a, string? b)", output);
        Assert.DoesNotContain("public static bool op_Equality", output);
    }

    [Fact]
    public async Task Member_SelectedCheckedOperator_SelectDecompiledSource_RendersCSharpCheckedOperatorDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Int128",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_CheckedAddition" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static System.Int128 operator checked +(System.Int128 left, System.Int128 right)", output);
        Assert.DoesNotContain("System.Int128 checked operator +", output);
    }

    [Fact]
    public async Task Member_SelectedCheckedConversion_SelectDecompiledSource_RendersCSharpCheckedConversionDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Int128",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_CheckedExplicit" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static explicit operator checked System.Int128(double value)", output);
        Assert.DoesNotContain("checked explicit operator System.Int128", output);
    }

    [Fact]
    public async Task Member_SelectedExtensionMethod_SelectDecompiledSource_RendersThisParameter()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Linq",
            TypeName = "Enumerable",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Where" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static System.Collections.Generic.IEnumerable<TSource> Where<TSource>(this System.Collections.Generic.IEnumerable<TSource> source, System.Func<TSource, bool> predicate)", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_UsesDisplayedOverloadIndex()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Enum",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Parse" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static TEnum Parse<TEnum>(System.ReadOnlySpan<char> value)", output);
        Assert.DoesNotContain("public static object Parse(System.Type enumType, string value)", output);
    }

    [Fact]
    public async Task Member_SelectedGenericTypeConstructor_SelectDecompiledSource_RendersUngenericConstructorName()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "List<T>",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public List()", output);
        Assert.DoesNotContain("public List<T>()", output);
    }

    [Fact]
    public async Task Member_TsvWithMultipleSelectedSections_ReturnsError()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods", "Properties"],
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error);
        Assert.Contains("--table, --tsv, and --jsonl display one section at a time", error);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TsvProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--show-index", "-S", "Methods",
            "--columns", "Select;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("select\tsignature", output);
        Assert.Contains("Serialize:1\tpublic static string Serialize<TValue>", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TableRendersOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--show-index", "-S", "Methods", "--table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Select", output);
        Assert.Contains("Signature", output);
        Assert.Contains("Serialize:1", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("Overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_JsonlProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--show-index", "-S", "Methods",
            "--columns", "Select;Signature", "--jsonl");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);

        using var first = JsonDocument.Parse(lines[0]);
        Assert.True(first.RootElement.TryGetProperty("select", out var select));
        Assert.True(first.RootElement.TryGetProperty("signature", out var signature));
        Assert.Contains("public static string Serialize", signature.GetString());
        Assert.StartsWith("Serialize:", select.GetString());
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_UnknownProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--show-index",
            "--columns", "Name;Signature;Obsolete", "--tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("name\tsignature", output);
        Assert.Contains("warning: column 'Obsolete' not found in section 'Methods'", error);
        Assert.Contains("Run -D \"Methods\" to list available columns.", error);
    }

    [Fact]
    public async Task Member_NarrowedMethods_OptionGatedProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize",
            "--columns", "Select;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("signature", output);
        Assert.Contains("warning: column 'Select' not found in section 'Methods'", error);
        Assert.Contains("Run -D \"Methods\" to list available columns.", error);
    }

    [Fact]
    public async Task Type_DecompiledSource_RendersWholeTypeListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.Contains("public class Stack<T>", output);
        Assert.Contains("private T[] _array;", output);
        Assert.Contains("public void Push(T item)", output);
        Assert.Contains("public bool TryPop(out T result)", output);
        // Using hoisting: qualified names shorten against the metadata
        // namespace tables; the directives appear at the top.
        Assert.Contains("using System.Runtime.CompilerServices;", output);
        Assert.Contains(": IEnumerable<T>, IEnumerable, ICollection, IReadOnlyCollection<T>", output);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences", output);
        Assert.DoesNotContain("System.Collections.Generic.IEnumerable<T>", output);
        // Explicit interface property implementations render as properties,
        // not their accessor methods.
        Assert.Contains("bool ICollection.IsSynchronized => false;", output);
        Assert.DoesNotContain("get_IsSynchronized", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Enum_RendersValuesListing()
    {
        // Enums have no method bodies; the listing renders the declaration
        // and values — following the ref assembly's type forwarder to the
        // defining assembly.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.DayOfWeek", "--platform", "System.Runtime",
            "-S", "Decompiled Source", "--raw");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public enum DayOfWeek", output);
        Assert.Contains("Sunday = 0,", output);
        Assert.Contains("Saturday = 6,", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Raw_EmitsBareListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source", "--raw");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        // Bare C#: no markdown heading, no section title, no code fence, no tips.
        Assert.StartsWith("using System.Collections;", output);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.DoesNotContain("# ", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotContain("Tips:", output);
    }

    [Fact]
    public async Task Member_NonMethodRows_RenderFullDeclarations()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.IO.Stream", "--platform", "System.Runtime",
            "-m", "CanRead", "-S", "Properties",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("CanRead\tpublic abstract bool CanRead { get; }", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Empty", "-S", "Fields",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Empty\tpublic static readonly string Empty", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.AppDomain", "--platform", "System.Runtime",
            "-m", "AssemblyLoad", "-S", "Events",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("AssemblyLoad\tpublic event System.AssemblyLoadEventHandler? AssemblyLoad", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Chars", "-S", "Properties",
            "--columns", "Name;Signature;Description", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Chars\tpublic char this[int index] { get; }", output);
        Assert.Contains("Gets the Char object at a specified position in the current String object.", output);
    }

    [Fact]
    public async Task Member_OutParameter_RendersOutModifier()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonElement", "--platform", "System.Text.Json",
            "-m", "TryGetBytesFromBase64", "-S", "Methods",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("TryGetBytesFromBase64\tpublic bool TryGetBytesFromBase64(out byte[]? value)", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_DescriptionsMatchOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json@10.0.0",
            "-m", "Serialize", "-S", "Methods",
            "--columns", "Signature;Description", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Serialize<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)\tConverts the value of a type specified by a generic type parameter into a JSON string.", output);
        Assert.Contains("public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)\tConverts the provided value to UTF-8 encoded JSON text and write it to the Stream.", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_DescriptionsPreserveGenericAndArrayOverloadShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Join", "-S", "Methods",
            "--columns", "Signature;Description", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Join(char separator, System.ReadOnlySpan<object?> values)\tConcatenates the string representations of a span of objects", output);
        Assert.Contains("public static string Join(char separator, System.ReadOnlySpan<string?> value)\tConcatenates a span of strings", output);
        Assert.Contains("public static string Join(char separator, string?[] value, int startIndex, int count)\tConcatenates an array of strings", output);
    }

    [Fact]
    public async Task Member_ObsoleteMethod_RendersObsoleteAttributeInlineInSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "SampleObsoleteHost", "--library", TestAssemblyPath,
            "-m", "OldMethod", "-S", "Methods",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("OldMethod\t[Obsolete(\"Use NewMethod instead.\")] public void OldMethod()", output);
    }

    [Fact]
    public async Task Member_MixedKindFilter_TsvUsesUnifiedOneLineRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-m", "IsReflectionEnabledByDefault", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("kind\tname\treturn_type\tdetail", output);
        Assert.Contains("property\tIsReflectionEnabledByDefault\tbool\tget", output);
        Assert.Contains("method\tSerialize\tvoid\t15", output);
        Assert.DoesNotContain("\n\n", output);
    }

    [Fact]
    public async Task Member_SelectMethodsAndEvents_PreservesPipelineOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "AppDomain", "--platform", "System.Private.CoreLib",
            "-S", "Methods,Events", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(
            output.IndexOf("## Methods", StringComparison.Ordinal)
            < output.IndexOf("## Events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Member_StringBareSelect_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib", "-S", "--tips", "q", "--rows", "-n", "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "## Constructors",
            "## Fields",
            "## Properties",
            "## Method Groups",
            "## Operators",
            "## Explicit Interface Implementations",
            "## Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Member_StringSelectSpecialMemberKinds_RendersRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Operators,Explicit Interface Implementations,Extension Methods",
            "--tips", "q", "--rows", "-n", "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Operators", output);
        Assert.Contains("operator ==", output);
        Assert.Contains("## Explicit Interface Implementations", output);
        Assert.Contains("System.Collections.IEnumerable.GetEnumerator", output);
        Assert.Contains("## Extension Methods", output);
        Assert.Contains("AsMemory", output);
    }

    [Fact]
    public async Task Member_StringSupplementalSelectors_RoundTrip()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Explicit Interface Implementations", "--show-index", "--tips", "q", "--rows", "-n", "4");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`explicit:System.IConvertible.ToBoolean`", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("bool System.IConvertible.ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "IL", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        Assert.Contains("System.Convert::ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "Decompiled Source", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("bool System.IConvertible.ToBoolean", output);
        Assert.DoesNotContain("public bool System.IConvertible.ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Extension Methods", "--show-index", "--tips", "q", "--rows", "-n", "4");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`extension:AsMemory:1`", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:AsMemory:1", "-S", "IL", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        Assert.Contains("IL_0000:", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:Normalize:1", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Normalize(this string strInput)", output);
        Assert.DoesNotContain("string Normalize()", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:Normalize", "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var doc = JsonDocument.Parse(output);
        foreach (var member in doc.RootElement.GetProperty("members").EnumerateArray())
            Assert.Equal("extension-method", member.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Type_StringShape_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--shape");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "Constructors",
            "Fields",
            "Properties",
            "Methods",
            "Operators",
            "Explicit Interface Implementations",
            "Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf($"─ {heading}", StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Type_BareStringAlias_RendersCoreLibString()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "string", "--shape", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.String", output);
        Assert.Contains("─ Methods", output);
    }

    [Theory]
    [InlineData("Dictionary<TKey,TValue>")]
    [InlineData("Dictionary`2")]
    public async Task Type_BareDictionaryGeneric_RendersCoreLibDictionary(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName, "--shape", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.Contains("void Add(TKey key, TValue value)", output);
    }

    [Fact]
    public async Task Member_BareStringAliasWithMemberFilter_RendersStringMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "string", "-m", "Normalize", "-S", "Methods", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.String", output);
        Assert.Contains("## Methods", output);
        Assert.Contains("Normalize(", output);
    }

    [Fact]
    public async Task Member_EnumValueFilter_TsvAppliesMemberFilter()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DayOfWeek", "--platform", "System.Private.CoreLib",
            "-m", "Friday", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("name\tvalue", output);
        Assert.Contains("Friday\t5", output);
        Assert.DoesNotContain("Sunday", output);
        Assert.DoesNotContain("Saturday", output);
    }

    [Fact]
    public async Task Library_TsvWithMultipleSelectedSections_ReturnsError()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Library Info", "Signals"],
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error);
        Assert.Contains("--table, --tsv, and --jsonl display one section at a time", error);
    }

    [Fact]
    public async Task Type_SelectWithUnknownColumn_ReturnsError()
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

        Assert.Equal(1, exit);
        Assert.Contains("column 'Bogus' not found in section 'Properties'", error);
        Assert.Contains("No columns matched projection: Bogus", error);
    }

    [Fact]
    public async Task Type_SelectWithSelectColumn_ReturnsErrorWhenNotRendered()
    {
        // Select is valid in the static schema but only renders with --show-index. The
        // active table shape has no matching column, so strict projection returns an error.
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
    public async Task Type_SelectWithColumnNotShownAtVerbosity_ReturnsError()
    {
        // Signature is valid in the static schema but does not render at the default
        // verbosity. The active table shape has no matching column, so strict projection
        // returns an error.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Signature"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Signature", error);
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

    // ── library command ─────────────────────────────────────────────

    [Fact]
    public async Task Assembly_PlatformLibrary_ShowsInfo()
    {
        var options = new LibraryOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Assembly_SingleSectionCount_WritesInteger()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Async*"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_CountWithoutSingleSection_Errors()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

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
        var options = new LibraryOptions { AssemblyName = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("dotnet-inspect.Tests", output);
    }

    [Fact]
    public async Task Assembly_Signals_ShowsMetadataSignalsOnly()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

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
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

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
                "AI",
                "Aspire",
                "Async Methods",
                "Custom Attributes",
                "Dependency Injection",
                "Extension Methods",
                "Health Checks",
                "Hosting",
                "HTTP Client",
                "Integrations",
                "Logging",
                "OpenAPI",
                "OpenTelemetry",
                "Options",
                "Resources",
                "SourceLink Availability",
                "SourceLink Integrity",
                "SourceLink Missing Files",
                "Type Forwarders"
            ],
            optInNames);
        Assert.DoesNotContain(lines, line => line.StartsWith("Missing Source Files", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Source Integrity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsSection_RollsUpOpenTelemetry()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | APIs |", output);
        Assert.Contains("| OpenTelemetry |", output);
        Assert.DoesNotContain("## OpenTelemetry", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverIntegrationsCategory_ListsRenderableIntegrationSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-D", "@Integrations", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Integrations", output);
        Assert.Contains("AI", output);
        Assert.Contains("Dependency Injection", output);
        Assert.DoesNotContain("Logging", output);
        Assert.DoesNotContain("OpenTelemetry", output);
        Assert.DoesNotContain("Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectIntegrationsCategory_RendersIntegrationSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "@Integrations", "--rows", "-n", "6");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("## AI", output);
        Assert.Contains("## Dependency Injection", output);
        Assert.DoesNotContain("## Logging", output);
        Assert.DoesNotContain("## OpenTelemetry", output);
        Assert.DoesNotContain("## Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsSection_RollsUpLogging()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Logging", output);
        Assert.DoesNotContain("## Logging", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_DetectsAiCurrencyTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.Abstractions", "--library", "-S", "AI", "--rows", "-n", "80");

        Assert.Equal(0, exit);
        Assert.Contains("## AI", output);
        Assert.Contains("| Kind | Type |", output);
        Assert.DoesNotContain("| API |", output);
        Assert.Contains("| Chat | `Microsoft.Extensions.AI.IChatClient` |", output);
        Assert.Contains("| Embeddings | `Microsoft.Extensions.AI.IEmbeddingGenerator` |", output);
        Assert.Contains("| Tools | `Microsoft.Extensions.AI.AITool` |", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--library", "-S", "AI", "--rows", "-n", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## AI", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIClientBuilderChatClientExtensions.AddChatClient(...)", output);
        Assert.Contains("AspireOpenAIClientBuilderEmbeddingGeneratorExtensions.AddEmbeddingGenerator(...)", output);
        Assert.Contains("Aspire.OpenAI.AspireOpenAIClientBuilder", output);
        Assert.Contains("Aspire.OpenAI.OpenAISettings", output);
        Assert.DoesNotContain("Microsoft.Extensions.AI.IChatClient", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForMicrosoftExtensionsAIOpenAI_ShowsAdapterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.OpenAI", "--library", "-S", "@Integrations", "--rows", "-n", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| AI | 9 |", output);
        Assert.Contains("## AI", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Chat | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIChatClient(...)` |", output);
        Assert.Contains("| Embeddings | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIEmbeddingGenerator(...)` |", output);
        Assert.Contains("| Images | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIImageGenerator(...)` |", output);
        Assert.Contains("| Realtime | `Microsoft.Extensions.AI.OpenAIRealtimeClient` |", output);
        Assert.Contains("| Speech to Text | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsISpeechToTextClient(...)` |", output);
        Assert.Contains("| Text to Speech | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsITextToSpeechClient(...)` |", output);
        Assert.Contains("| Tools | `OpenAI.Responses.MicrosoftExtensionsAIResponsesExtensions.AsAITool(...)` |", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsSection_ForAspireOpenAI_ShowsStarterIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--library", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| AI | 8 |", output);
        Assert.Contains("| OpenTelemetry | 2 |", output);
        Assert.Contains("| Hosting | 2 |", output);
        Assert.DoesNotContain("| Aspire |", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Logging", output);
        Assert.DoesNotContain("Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspireSection_ForAspireHostingRedis_ShowsResourceCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "--library", "-S", "Aspire", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Aspire", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Resource Builder | `Aspire.Hosting.RedisBuilderExtensions.AddRedis(...)` |", output);
        Assert.Contains("| Resource | `Aspire.Hosting.ApplicationModel.RedisResource` |", output);
        Assert.DoesNotContain("IDistributedApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireHostingRedis_RendersAspireSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "--library", "-S", "@Integrations", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Aspire | 4 |", output);
        Assert.Contains("## Aspire", output);
        Assert.Contains("RedisBuilderExtensions.AddRedis(...)", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--library", "-S", "Hosting");

        Assert.Equal(0, exit);
        Assert.Contains("## Hosting", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIExtensions.AddKeyedOpenAIClient(...)", output);
        Assert.DoesNotContain("IHostApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAspireKafka_ShowsTelemetryControls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Confluent.Kafka", "--library", "-S", "@Integrations", "--rows", "-n", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| OpenTelemetry | 4 |", output);
        Assert.Contains("| Hosting | 4 |", output);
        Assert.Contains("## OpenTelemetry", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Metrics | `Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableMetrics` |", output);
        Assert.Contains("| Metrics | `Aspire.Confluent.Kafka.KafkaProducerSettings.DisableMetrics` |", output);
        Assert.Contains("| Tracing | `Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableTracing` |", output);
        Assert.Contains("| Tracing | `Aspire.Confluent.Kafka.KafkaProducerSettings.DisableTracing` |", output);
        Assert.DoesNotContain("OpenTelemetry.Instrumentation.ConfluentKafka", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_DetectsLoggingPrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Logging");

        Assert.Equal(0, exit);
        Assert.Contains("## Logging", output);
        Assert.Contains("| Type |", output);
        Assert.Contains("| `Microsoft.Extensions.Logging.ILogger` |", output);
        Assert.DoesNotContain("| Kind |", output);
        Assert.Contains("Microsoft.Extensions.Logging.ILogger", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForAwsLogger_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWS.Logger.AspNetCore", "--library", "-S", "@Integrations", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Logging | 2 |", output);
        Assert.Contains("## Logging", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AWSLoggerBuilderExtensions.AddAWSProvider(...)", output);
        Assert.Contains("AWSLoggerFactoryExtensions.AddAWSProvider(...)", output);
        Assert.DoesNotContain("| Type |", output);
        Assert.DoesNotContain("AWSLoggerBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForSerilog_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Extensions.Logging", "--library", "-S", "Logging", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Logging", output);
        Assert.Contains("| API |", output);
        Assert.Contains("SerilogLoggingBuilderExtensions.AddSerilog(...)", output);
        Assert.Contains("SerilogLoggerFactoryExtensions.AddSerilog(...)", output);
        Assert.DoesNotContain("SerilogLoggingBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ShowsActionableTypesOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "Dependency Injection");

        Assert.Equal(0, exit);
        Assert.Contains("## Dependency Injection", output);
        Assert.Contains("| API |", output);
        Assert.Contains("ChatClientBuilderServiceCollectionExtensions.AddChatClient(...)", output);
        Assert.Contains("EmbeddingGeneratorBuilderServiceCollectionExtensions.AddEmbeddingGenerator(...)", output);
        Assert.DoesNotContain("| Kind |", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Microsoft.Extensions.DependencyInjection.IServiceCollection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ForAzureClients_ShowsServiceRegistrationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Azure", "--library", "-S", "Dependency Injection", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Dependency Injection", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClients(...)", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClientsCore(...)", output);
        Assert.DoesNotContain("AzureClientServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksSection_ForSqlServer_ShowsHealthCheckBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AspNetCore.HealthChecks.SqlServer", "--library", "-S", "@Integrations", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Health Checks | 1 |", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.Contains("## Health Checks", output);
        Assert.Contains("| API |", output);
        Assert.Contains("SqlServerHealthCheckBuilderExtensions.AddSqlServer(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForSwashbuckle_ShowsOpenApiCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Swagger", "--library", "-S", "@Integrations", "--rows", "-n", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| OpenAPI | 4 |", output);
        Assert.Contains("## OpenAPI", output);
        Assert.Contains("| Configuration | `Swashbuckle.AspNetCore.Swagger.SwaggerOptions` |", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.MapSwagger(...)` |", output);
        Assert.Contains("| Middleware | `Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.UseSwagger(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForMicrosoftOpenApi_ShowsServiceAndEndpointApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.OpenApi", "--library", "-S", "OpenAPI", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## OpenAPI", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.OpenApi.OpenApiOptions` |", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.OpenApiEndpointRouteBuilderExtensions.MapOpenApi(...)` |", output);
        Assert.Contains("| Service Registration | `Microsoft.Extensions.DependencyInjection.OpenApiServiceCollectionExtensions.AddOpenApi(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForMassTransit_ShowsHostBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "MassTransit", "--library", "-S", "Hosting", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Hosting", output);
        Assert.Contains("| API |", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMassTransit(...)", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMediator(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAzureMonitorExporter_ShowsBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Monitor.OpenTelemetry.Exporter", "--library", "-S", "OpenTelemetry", "--rows", "-n", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## OpenTelemetry", output);
        Assert.Contains("| Logging | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorLogExporter(...)` |", output);
        Assert.Contains("| Metrics | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorMetricExporter(...)` |", output);
        Assert.Contains("| OpenTelemetry | `Azure.Monitor.OpenTelemetry.Exporter.OpenTelemetryBuilderExtensions.UseAzureMonitorExporter(...)` |", output);
        Assert.Contains("| Tracing | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorTraceExporter(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HttpClientDiagnostics_ShowsUserFacingHttpClientCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Http.Diagnostics", "--library", "-S", "@Integrations", "--rows", "-n", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Dependency Injection | 4 |", output);
        Assert.Contains("| HTTP Client | 10 |", output);
        Assert.DoesNotContain("OpenTelemetry", output);
        Assert.Contains("## HTTP Client", output);
        Assert.Contains("| Kind | API |", output);
        var diagnosticsRow = "| HTTP Diagnostics | `Microsoft.Extensions.Http.Diagnostics.HttpDependencyMetadataResolver` |";
        var latencyRow = "| HTTP Latency | `Microsoft.Extensions.DependencyInjection.HttpClientLatencyTelemetryExtensions.AddHttpClientLatencyTelemetry(...)` |";
        var loggingRow = "| HTTP Logging | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |";
        Assert.Contains(diagnosticsRow, output);
        Assert.Contains(latencyRow, output);
        Assert.Contains(loggingRow, output);
        Assert.True(output.IndexOf(diagnosticsRow, StringComparison.Ordinal)
            < output.IndexOf(latencyRow, StringComparison.Ordinal));
        Assert.True(output.IndexOf(latencyRow, StringComparison.Ordinal)
            < output.IndexOf(loggingRow, StringComparison.Ordinal));
        Assert.Contains("| HTTP Logging | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |", output);
        Assert.Contains("HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)", output);
        Assert.Contains("| HTTP Logging | `Microsoft.Extensions.Http.Logging.LoggingOptions` |", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.IHttpClientLogEnricher", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.LoggingOptions", output);
        Assert.DoesNotContain("Microsoft.Extensions.Telemetry.Internal", output);
        Assert.DoesNotContain("| `Microsoft.Extensions.DependencyInjection.HttpClientLoggingServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_DetectsDiagnosticSourcePrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "OpenTelemetry");

        Assert.Equal(0, exit);
        Assert.Contains("## OpenTelemetry", output);
        Assert.Contains("| Kind | Type |", output);
        Assert.Contains("| Tracing | `System.Diagnostics.ActivitySource` |", output);
        Assert.Contains("| Metrics | `System.Diagnostics.Metrics.Meter` |", output);
        Assert.Contains("| Metrics | `System.Diagnostics.Metrics.UpDownCounter<T>` |", output);
        Assert.Contains("System.Diagnostics.ActivitySource", output);
        Assert.Contains("System.Diagnostics.Metrics.Meter", output);
        Assert.DoesNotContain("UpDownCounter&#96;1", output);
        Assert.DoesNotContain("Tip:", error);
    }

    private static string ExtractSectionName(string line)
    {
        if (line.StartsWith('|'))
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            return cells.Length > 1 ? cells[1] : line.Trim();
        }

        var marker = line.IndexOf("  section", StringComparison.Ordinal);
        return marker >= 0 ? line[..marker].TrimEnd() : line.TrimEnd();
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
        var options = new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

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
    public async Task PackageCommand_LibraryFlag_BareSelectsUnambiguousLibrary()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_ExplicitSelectsLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "Latest.Two.dll", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Latest.Two.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_BareReportsAmbiguousLibraries()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--library");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("contains multiple libraries", error);
            Assert.Contains("lib/net10.0/Latest.One.dll", error);
            Assert.Contains("lib/net10.0/Latest.Two.dll", error);
            Assert.Contains("dotnet-inspect package", error);
            Assert.Contains("--library <dll>", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Router_LibraryFlag_RoutesPackageToLibraryInspection()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(packagePath, "--library", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
            Assert.Contains("@Default", output);
            Assert.Contains("@All", output);
            Assert.Contains("Use -S @All to select all sections.", output);
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
        Assert.Contains("## Method Groups", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task MemberList_ExplicitPackageQualifiedType_DoesNotSplitTrailingTypeName()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Type 'System.Text.Json' not found", error);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("## Method Groups", output);
    }

    [Fact]
    public async Task MemberList_QualifiedPlatformTypeTypo_SuggestsType()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializizer", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Type 'JsonSerializizer' not found.", error);
        Assert.Contains("System.Text.Json.JsonSerializer", error);
        Assert.DoesNotContain("member requires a type name", error);
    }

    [Fact]
    public async Task Member_FilteredGenericMethod_RendersMethodTypeParameters()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--rows", "-n", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Serialize<TValue>(TValue value", output);
    }

    [Fact]
    public async Task Package_SelectAll_IncludesOptInSignals()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "@All");

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
