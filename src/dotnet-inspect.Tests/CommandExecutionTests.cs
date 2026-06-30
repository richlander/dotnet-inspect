using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Core;
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

    private sealed class NestedDrillTarget
    {
        public NestedDrillTarget(int value) => Value = value;

        public int Value { get; }
    }

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

    private static (string PackagePath, string TempDir) CreateLocalReadmePackage(
        string id,
        string readmeFile,
        string readmeText,
        string? agentsText = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(Path.Combine(packageRoot, readmeFile), readmeText);
        if (agentsText != null)
            File.WriteAllText(Path.Combine(packageRoot, "AGENTS.md"), agentsText);
        File.WriteAllText(Path.Combine(packageRoot, $"{id}.nuspec"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{{id}}</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>{{readmeFile}}</readme>
              </metadata>
            </package>
            """);

        var packagePath = Path.Combine(tempDir, $"{id}.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private sealed record ProjectDocPackage(
        string Id,
        string Version,
        string ReadmeFile,
        string ReadmeText,
        string? AgentsText = null);

    private static (string ProjectPath, string TempDir) CreateProjectWithPackageDocs(params ProjectDocPackage[] packages)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"project-doc-test-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(tempDir, "App");
        var objDir = Path.Combine(projectDir, "obj");
        Directory.CreateDirectory(objDir);

        var projectPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        foreach (var package in packages)
        {
            var packageRoot = Path.Combine(tempDir, "packages", package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
            Directory.CreateDirectory(packageRoot);

            var readmePath = Path.Combine(packageRoot, package.ReadmeFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);
            File.WriteAllText(readmePath, package.ReadmeText);
            if (package.AgentsText != null)
                File.WriteAllText(Path.Combine(packageRoot, "AGENTS.md"), package.AgentsText);

            File.WriteAllText(Path.Combine(packageRoot, $"{package.Id.ToLowerInvariant()}.nuspec"), $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>{{package.Id}}</id>
                    <version>{{package.Version}}</version>
                    <authors>tests</authors>
                    <description>test package</description>
                    <readme>{{package.ReadmeFile}}</readme>
                  </metadata>
                </package>
                """);
        }

        var targetEntries = string.Join(",\n", packages.Select(package =>
            $"{JsonString($"{package.Id}/{package.Version}")}: {{}}"));
        var libraryEntries = string.Join(",\n", packages.Select(package =>
        {
            var packageRoot = Path.Combine(tempDir, "packages", package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
            return $"{JsonString($"{package.Id}/{package.Version}")}: {{ \"type\": \"package\", \"path\": {JsonString(packageRoot.Replace('\\', '/'))} }}";
        }));
        var dependencyEntries = string.Join(",\n", packages.Select(package =>
            $"{JsonString(package.Id)}: {{ \"target\": \"Package\", \"version\": {JsonString($"[{package.Version}, )")} }}"));
        File.WriteAllText(Path.Combine(objDir, "project.assets.json"), $$"""
            {
              "targets": {
                "net10.0": {
                  {{targetEntries}}
                }
              },
              "libraries": {
                {{libraryEntries}}
              },
              "project": {
                "frameworks": {
                  "net10.0": {
                    "dependencies": {
                      {{dependencyEntries}}
                    }
                  }
                }
              }
            }
            """);

        return (projectPath, tempDir);
    }

    private static string JsonString(string value) => JsonSerializer.Serialize(value);

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

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriagePredicates_DoNotAlterDiscovery(string command)
    {
        string[] BaseArgs() => command switch
        {
            "library" => [command, TestAssemblyPath, "--discover"],
            "type" => [command, nameof(OutputFormatterTests), "--library", TestAssemblyPath, "--discover"],
            "member" => [command, nameof(OutputFormatterTests), "--library", TestAssemblyPath, "--discover"],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

        var baseline = await RunAppAsync(BaseArgs());
        var filtered = await RunAppAsync([.. BaseArgs(), "--loop"]);

        Assert.Equal(0, baseline.Exit);
        Assert.Equal(0, filtered.Exit);
        Assert.Equal(baseline.Output, filtered.Output);
    }

    [Fact]
    public async Task PerformanceTriageShape_UnknownShapeReportsValidShapes()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "--triage-shape", "typo-shape", "--tsv");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unknown Performance Triage shape 'typo-shape'", error);
        Assert.Contains("capturing-delegate", error);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task TypeCommand_AllowsDrillingNonPublicNestedTypes(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Member Index",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains(".ctor", output);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task MemberCommand_AllowsDrillingNonPublicNestedConstructors(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeName, ".ctor:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget..ctor")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget..ctor:1")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget..ctor")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget..ctor:1")]
    public async Task MemberCommand_AllowsCopiedNestedConstructorSelector(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", selector,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task MemberCommand_AllowsCopiedNestedConstructorDigestSelector(string typeName)
    {
        var index = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Member Index",
            "-n", "80");
        Assert.Equal(0, index.Exit);
        var stable = index.Output
            .Split('\n')
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 3 && parts[1] == "`.ctor`")
            .Select(parts => parts[2].Trim('`'))
            .Single();

        var selector = $"{typeName}.{stable}";
        var (exit, output, error) = await RunAppAsync(
            "member", selector,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    // ── bare router ───────────────────────────────────────────────────

    [Fact]
    public async Task BareName_PlatformLibrary_RoutesToLibrary()
    {
        var (exit, output, error) = await RunAppAsync("System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Text.Json.dll", output);
        Assert.Contains("## Library Info", output);
    }

    [Fact]
    public async Task BareName_PlatformNamespacePrefix_RoutesToTypePrefixBrowse()
    {
        var (exit, output, error) = await RunAppAsync("System.Text", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Contains("Showing best-effort platform prefix matches for 'System.Text'", error);
        Assert.Contains("# System.Text", output);
        Assert.Contains("Source: Platform", output);
    }

    [Fact]
    public async Task BareName_ExactNuGetPackageId_RoutesToPackage()
    {
        var (exit, output, error) = await RunAppAsync("System.CommandLine", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.CommandLine", output);
        Assert.Contains("## Package Info", output);
        Assert.DoesNotContain("Library: System.CommandLine.dll | Types:", output);
    }

    [Fact]
    public async Task BareName_CommandTypo_SuggestsCommandWithoutNuGetLookup()
    {
        var (exit, output, error) = await RunAppAsync("packag", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Empty(output);
        Assert.Contains("Error: Unknown command 'packag'.", error);
        Assert.Contains("Did you mean:", error);
        Assert.Contains("  package", error);
        Assert.DoesNotContain("Package 'packag' not found", error);
        Assert.DoesNotContain("Network traffic", error);
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
    public async Task Type_SingleType_MarkdownMinimal_IncludesLibraryContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.FrozenDictionary", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("Library: System.Collections.Immutable", output);
        Assert.Contains("Source: Platform", output);
        Assert.Contains("## Method Groups", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_InferredPlatformTypo_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Runtime.CompilerService", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Runtime.CompilerService", error);
        Assert.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute", output);
    }

    [Fact]
    public async Task Router_PrefixBrowse_InferredPlatformTypo_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime.CompilerService", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Runtime.CompilerService", error);
        Assert.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute", output);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_UnresolvedNamespace_ListsPlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Text", error);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("Package 'System.Text' not found", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_WildcardNote_DoesNotDoubleStar()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text*", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("find \"System.Text*\" --platform", error);
        Assert.DoesNotContain("System.Text**", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_AllMissProjection_ReportsCleanError()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--columns", "Library", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("No columns matched projection: Library", error);
        Assert.DoesNotContain("Unhandled exception", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_PartialProjection_WarnsForMissingColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--columns", "Type,Library,Members", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("note: 1 field has no data: Library", error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Regex", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("Library: System.Text.RegularExpressions", output);
        Assert.Contains("Note: Type 'Regex' resolved via platform find", error);
    }

    [Fact]
    public async Task Router_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "Regex", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("Library: System.Text.RegularExpressions", output);
        Assert.Contains("Note: Type 'Regex' resolved via platform find", error);
    }

    [Fact]
    public async Task Router_BareGenericTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Collections.Generic.List&lt;T&gt;", output);
        Assert.Contains("Library: System.Collections", output);
        Assert.Contains("Note: Type 'List<T>' resolved via platform find", error);
    }

    [Fact]
    public async Task Router_VoidKeyword_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "void", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Package 'void'", error);
    }

    [Fact]
    public async Task Router_FullyQualifiedPlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.String.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_FullyQualifiedPlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String.IndexOf", "--table", "--show-index", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf:1", output);
        Assert.Contains("IndexOf~", output);
        Assert.Contains("M:System.String.IndexOf(char)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_SimplePlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_PrimitiveKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "string.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_NumericKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "int.Parse", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Parse", output);
        Assert.Contains("Return Type", output);
        Assert.Contains("int", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_BooleanKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "bool.TryParse", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("TryParse", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ObjectKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "object.GetType", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("GetType", output);
        Assert.Contains("System.Type GetType()", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericMemberSelector_NormalizesGenericTypeArguments()
    {
        var (exit, output, error) = await RunAppAsync(
            "JsonSerializer.Deserialize<TValue>", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Deserialize<TValue>", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_GenericMemberSelector_NormalizesGenericTypeArguments()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Deserialize<TValue>", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Deserialize<TValue>", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public String(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DoubleDotConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>..ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public List(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DoubleDotConstructorSelector_PreservesOverloadIndex()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>..ctor:3", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("(int)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_IndexerSelector_NormalizesThisAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.this[]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Chars", output);
        Assert.Contains("this[int index]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_IndexerSelector_NormalizesThisAliasWithIllustrativeArgument()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.this[0]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Chars", output);
        Assert.Contains("this[int index]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericIndexerSelector_NormalizesThisAliasWithTypeArgument()
    {
        var (exit, output, error) = await RunAppAsync(
            "Dictionary<TKey,TValue>.this[TKey]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Item", output);
        Assert.Contains("this[TKey key]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_OperatorSelector_NormalizesOperatorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "DateTime.operator+", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("operator +", output);
        Assert.Contains("public static System.DateTime operator +", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ConversionSelector_NormalizesImplicitAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "Decimal.implicit", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("implicit operator", output);
        Assert.Contains("public static implicit operator System.Decimal", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_OperatorMemberFilter_NormalizesOperatorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DateTime", "-m", "operator+", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("operator +", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_ConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "-m", "ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public String(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_PrefersPlatformTypeOverSameNamedPackage()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonSerializer", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("Library: System.Text.Json", output);
        Assert.DoesNotContain("Package 'JsonSerializer'", error);
        Assert.Contains("Note: Type 'JsonSerializer' resolved via platform find", error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_PrefersExactNonGenericMatch()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "FrozenDictionary", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("Library: System.Collections.Immutable", output);
        Assert.Contains("## Method Groups", output);
        Assert.DoesNotContain("## Type Parameters", output);
        Assert.Contains("Note: Type 'FrozenDictionary' resolved via platform find", error);
    }

    [Fact]
    public async Task Type_BareCoreLibSimpleName_PrefersNonGenericExactMatch()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Task", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Threading.Tasks.Task", output);
        Assert.DoesNotContain("# System.Threading.Tasks.Task&lt;TResult&gt;", output);
    }

    [Fact]
    public async Task Member_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "Regex", "-m", "Match", "--show-index", "--rows", "-n", "4", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("`Match:1`", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Find_NamespaceExactMiss_RetriesAsPrefix()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "System.Text", "--platform", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("No exact matches for 'System.Text'", error);
        Assert.Contains("System.Text*", error);
        Assert.Contains("StringBuilder", output);
        Assert.Contains("System.Text.Json", output);
        Assert.DoesNotContain("TextInfo", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_LongNamespacePrefixMatchesChangedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "System.Text.Json.Serialization", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Summary", output);
        Assert.Contains("5 additive", output);
        Assert.Contains("JsonKnownReferenceHandler", output);
        Assert.DoesNotContain("JsonSerializer |", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_ShortNamespacePrefixMatchesChangedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "Serialization", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("5 additive", output);
        Assert.Contains("JsonKnownReferenceHandler", output);
        Assert.DoesNotContain("JsonSerializer |", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_NoMatches_PrintsNote()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "DefinitelyMissingNamespace", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Summary", output);
        Assert.Contains("no changes", output);
        Assert.Contains("type filter matched no changed types", error);
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_UnresolvedNamespace_ListsPlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Text", error);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("Package 'system.text' not found", error);
    }

    [Fact]
    public async Task Router_ExactPlatformAssembly_StillRoutesToLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Field", output);
        Assert.Contains("Value", output);
        Assert.Contains("Name", output);
        Assert.Contains("System.Runtime", output);
        Assert.Contains("Type Forwarders", output);
    }

    [Fact]
    public async Task SourceCommand_RemovedFromRoot()
    {
        var (exit, _, error) = await RunAppAsync("source", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized command or argument 'source'", error);
    }

    [Fact]
    public async Task BrowsableUrlsAlias_Removed()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--browsable-urls", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized option '--browsable-urls'", error);
    }

    [Fact]
    public async Task Type_ExactPlatformAssembly_DoesNotUseWidePlatformPrefixBrowse()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.DoesNotContain("System.Collections.Immutable.ImmutableArray", output);
        Assert.DoesNotContain("best-effort platform prefix matches", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_NarrowSourceMissFallsBackToWidePlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Frozen", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Collections.Frozen", error);
        Assert.Contains("System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("System.Collections.Frozen.FrozenSet", output);
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_NarrowSourceMissFallsBackToWidePlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Frozen", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Collections.Frozen", error);
        Assert.Contains("System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("System.Collections.Frozen.FrozenSet", output);
    }

    [Fact]
    public async Task RelationshipCommands_NamespacePrefixInputs_PrintPrefixBrowseHint()
    {
        var (implementsExit, implementsOutput, implementsError) = await RunAppAsync(
            "implements", "System.Text", "--tips", "q");

        Assert.Equal(0, implementsExit);
        Assert.Empty(implementsOutput);
        Assert.Contains("looks like a namespace prefix", implementsError);
        Assert.Contains("type System.Text", implementsError);
        Assert.Contains("find \"System.Text*\" --platform", implementsError);

        var (extensionsExit, extensionsOutput, extensionsError) = await RunAppAsync(
            "extensions", "System.Text", "--tips", "q");

        Assert.Equal(0, extensionsExit);
        Assert.Contains("No extension methods found", extensionsOutput);
        Assert.Contains("looks like a namespace prefix", extensionsError);
        Assert.Contains("type System.Text", extensionsError);
        Assert.Contains("find \"System.Text*\" --platform", extensionsError);
    }

    [Fact]
    public async Task Depends_NamespacePrefixInput_PrintsPrefixBrowseHint()
    {
        var (dependsExit, dependsOutput, dependsError) = await RunAppAsync(
            "depends", "System.Text", "--tips", "q");

        Assert.Equal(1, dependsExit);
        Assert.Empty(dependsOutput);
        Assert.Contains("Could not resolve 'System.Text'", dependsError);
        Assert.Contains("looks like a namespace prefix", dependsError);
        Assert.Contains("type System.Text", dependsError);
        Assert.Contains("find \"System.Text*\" --platform", dependsError);
    }

    [Fact]
    public async Task Member_NamespacePrefixInput_PrintsPrefixBrowseHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("member requires a type name", error);
        Assert.Contains("looks like a namespace prefix", error);
        Assert.Contains("type System.Text", error);
        Assert.Contains("find \"System.Text*\" --platform", error);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitPlatformNamespace_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.Serialization", "--platform", "System.Text.Json", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Text.Json.Serialization", error);
        Assert.Contains("System.Text.Json.Serialization.JsonConverter", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitLibraryNamespace_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspector.Tests.Sample", "--library", TestAssemblyPath, "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("DotnetInspector.Tests.Sample", error);
        Assert.Contains("DotnetInspector.Tests.SampleClassForTesting", output);
        Assert.Contains("DotnetInspector.Tests.SampleGenericClass", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitPackageNamespace_ListsBestEffortMatches()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "type", "DotnetInspector.Tests.Sample", "--package", packagePath,
                "--library", "Test.Primary.dll", "--table", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Contains("best-effort prefix matches", error);
            Assert.Contains("DotnetInspector.Tests.Sample", error);
            Assert.Contains("DotnetInspector.Tests.SampleClassForTesting", output);
            Assert.Contains("DotnetInspector.Tests.SampleGenericClass", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeListing_FacadePlatformLibrary_ShowsTypeForwardingDescription()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("This is a type-forwarding library", output);
    }

    [Fact]
    public async Task TypeListing_NonFacadePlatformLibrary_DoesNotShowTypeForwardingDescription()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.DoesNotContain("This is a type-forwarding library", output);
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
    public async Task Type_SingleType_Discover_DefaultsToDiscoverableSections()
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
        Assert.Contains("| Custom Attributes | section |", output);
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
        Assert.Contains("| Methods | section (verbose) |", output);
        Assert.Contains("| Source Files | section (opt-in) |", output);
        Assert.DoesNotContain("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_IncludesSelectableCodeSections()
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
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| Original Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.DoesNotContain("| Facts | section", output);
    }

    [Fact]
    public async Task Type_SingleType_SourceFilesSection_RendersTypeSourceUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializer", "-S", "Source Files", "--tips", "q", "-n", "28");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Files", output);
        Assert.Contains("| Url |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_IncludesSelectableCustomAttributesSection()
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
        Assert.Contains("| Custom Attributes | section |", output);
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
        // The historical Select overload-index column is no longer queryable; selectors
        // live in the dedicated Member Index section.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverSection_ShowIndex_ListsMemberIndexColumns()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Member Index"],
            ShowMemberIndex = true,
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // --show-index is now a compatibility alias for the dedicated Member Index section.
        Assert.Contains("| Stable | column |", output);
        Assert.Contains("| Canonical Signature | column |", output);
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
        // The historical Select column is not queryable, so effective discovery
        // must not list it (regression: member effective used to leak it).
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ListsMethodsAlternate()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Methods | section (verbose) |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ShowIndexAtNormal_ListsMemberIndexColumns()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Member Index"],
            ShowMemberIndex = true,
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // With --show-index the dedicated Member Index section renders.
        Assert.Contains("| Stable | column |", output);
        Assert.Contains("| Canonical Signature | column |", output);
    }

    [Fact]
    public async Task Member_SourceLocations_Group_RendersSelectorsAndSourceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-S", "Source Locations", "--rows", "-n", "6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("| Selector | Signature | File | Line | End Line | Url |", output);
        Assert.Contains("`Serialize:1`", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
        Assert.Contains("raw.githubusercontent.com", output);
        Assert.DoesNotContain("## Member Index", output);
    }

    [Fact]
    public async Task Member_SourceLocations_SelectedSignature_RendersWithoutSelectorColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "Serialize:1", "-S", "Source Locations", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("| Signature | File | Line | End Line | Url |", output);
        Assert.DoesNotContain("| Selector |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Fact]
    public async Task Member_SourceLocations_BareSelectedSignature_EmitsSingleUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "SerializeObject:1", "-S", "Source Locations", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", output);
        Assert.Contains("JsonConvert.cs", output);
        Assert.DoesNotContain("## Source Locations", output);
        Assert.DoesNotContain("| Url |", output);
    }

    [Fact]
    public async Task Member_SourceLocations_BareGroup_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.All(lines, line =>
        {
            Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line);
            Assert.Contains("JsonConvert.cs", line);
        });
        Assert.DoesNotContain("## Source Locations", output);
        Assert.DoesNotContain("| Url |", output);
    }

    [Fact]
    public async Task Type_SourceFiles_Bare_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--bare", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line));
        Assert.DoesNotContain("url", lines);
    }

    [Fact]
    public async Task Type_CountAndBare_ComposesForTableSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonConvert", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Member Index", "--count", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("67", output.Trim());
    }

    [Fact]
    public async Task Type_CountAndBare_ComposesForVectorSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--count", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task Member_SourceLocations_UnpinnedSnupkgPackage_ResolvesSourceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json",
            "-m", "SerializeObject", "-S", "Source Locations", "--rows", "-n", "6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("`SerializeObject:1`", output);
        Assert.Contains("JsonConvert.cs", output);
        Assert.Contains("raw.githubusercontent.com/JamesNK/Newtonsoft.Json", output);
    }

    [Fact]
    public async Task Member_SourceLocations_Tsv_RepeatsStartLineForSingleLineMethods()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var rows = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 5)
            .ToDictionary(cells => cells[0], cells => (Line: cells[3], EndLine: cells[4]));

        Assert.Equal(("532", "532"), rows["SerializeObject:1"]);
        Assert.Equal(("548", "548"), rows["SerializeObject:2"]);
        Assert.Equal(("581", "585"), rows["SerializeObject:4"]);
    }

    [Fact]
    public async Task Member_SourceLocations_BlobUrls_RendersBrowserUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("raw.githubusercontent.com", output);
    }

    [Fact]
    public async Task Member_SourceLocations_RawOverridesBlob()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--blob", "--raw", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("raw.githubusercontent.com/JamesNK/Newtonsoft.Json", output);
        Assert.DoesNotContain("github.com/JamesNK/Newtonsoft.Json/blob/", output);
    }

    [Fact]
    public async Task Member_SourceLocations_Discovery_DoesNotAcquirePdb()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-D", "Source Locations", "--verbose", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("| File | column |", output);
        Assert.Contains("| Line | column |", output);
        Assert.DoesNotContain("Loaded PDB", error);
        Assert.DoesNotContain("MSDL symbol", error);
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
            ShowMemberIndex = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Member Index", output);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.Contains("`SerializeToNode:1`", output);
        Assert.Contains("SerializeToNode~", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.SerializeToNode(", output);
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
            ShowMemberIndex = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Member Index", output);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.Contains("`GetConverter`", output);
        Assert.DoesNotContain("## Signature", output);

        options = options with { Select = ["Methods"], ShowMemberIndex = false };
        (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Name | Signature |", output);
        Assert.DoesNotContain("## Signature", output);
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
        Assert.Contains("| Annotated Source | section (opt-in) |", output);
        Assert.Contains("| Original Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.Contains("| Calls | section (opt-in) |", output);
        Assert.Contains("| Callers | section (opt-in) |", output);
        Assert.Contains("| Unsafe Operations | section (opt-in) |", output);
        Assert.Contains("| Call Graph | section (opt-in) |", output);
        Assert.Contains("| Facts | section (opt-in) |", output);
        Assert.DoesNotContain("IR (Stages)", output);
        Assert.Contains("Use -S @All to select all sections.", output);
        Assert.DoesNotContain("| Methods | section |", output);
    }

    [Fact]
    public async Task Member_DumpStages_IsNotRegistered()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json", "--dump-stages", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Contains("Unrecognized option '--dump-stages'", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsDetailSectionsWithCallGraphOptIn()
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
        Assert.Contains("| Calls | section (opt-in) |", output);
        Assert.Contains("| Callers | section (opt-in) |", output);
        Assert.Contains("| Call Graph | section (opt-in) |", output);
        Assert.Contains("| Facts | section (opt-in) |", output);
        Assert.Contains("| Unsafe Operations | section (opt-in) |", output);
        Assert.Contains("Use -S @All to select all sections.", output);
    }

    [Fact]
    public async Task Member_BareNameCallerGraph_AutoSelectsSingleOverload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Caller Graph", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Caller Graph", output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), output);
        Assert.DoesNotContain("Select value 'Caller Graph' not found", error);
    }

    [Fact]
    public async Task Member_BareNameCallGraph_AmbiguousOverloadReportsSelectorHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", "Call Graph", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Call Graph' requires a single selected overload", error);
        Assert.Contains("Use Overloaded:1 through Overloaded:2", error);
        Assert.Contains("-S \"Member Index\"", error);
        Assert.DoesNotContain("Select value 'Call Graph' not found", error);
    }

    [Fact]
    public async Task Member_BareNameCallersWithCallerScope_AutoSelectsSingleOverload()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Callers", "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_BareNameCallersWithCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", "Callers", "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Use Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_BareNameCallerScope_AutoSelectsSingleOverloadWithoutExplicitSection()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_BareNameCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Use Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_RendersPlainCSharp()
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
        Assert.Contains("```csharp", output);
        Assert.DoesNotMatch(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public async Task Member_KeywordParameterNames_EscapesSignatureAndDecompiledSourceHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleKeywordParameterHost).FullName!, "--library", TestAssemblyPath,
            nameof(SampleKeywordParameterHost.Instance), "-S", "Signature,Decompiled Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public int Instance(int @object, string @class)", output);
        Assert.DoesNotContain("public int Instance(int object, string class)", output);
        Assert.Contains("@object + @class.Length", output);
    }

    [Fact]
    public async Task Member_RefReadonlyReturn_PreservesDecompiledSourceHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleRefReadonlyReturnHost).FullName!, "--library", TestAssemblyPath,
            nameof(SampleRefReadonlyReturnHost.ChooseReadonly), "-S", "Signature,Decompiled Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static ref readonly int ChooseReadonly(in int left, in int right, bool chooseLeft)", output);
        Assert.DoesNotContain("public static ref int ChooseReadonly", output);
        Assert.Contains("return ref left;", output);
        Assert.Contains("return ref right;", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectAnnotatedSource_RendersMixedView()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Annotated Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("```csharp", output);
        Assert.Matches(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public async Task Member_NonPublicMethod_UnderIncludeAll_RendersBodyAndIL()
    {
        // #1323: a non-public method selected under --all must render its body and IL, not
        // report "has no IL body". The body-load path counts overloads on the same
        // visibility basis the member index numbered them on (all overloads under --all).
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "--all", "InternalHelper:1", "-S", "Decompiled Source,IL", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("has no IL body", output);
        Assert.Contains("InternalHelper", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SourceCategory_IncludesIlAndNoLoweredSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "Overloaded:2", "-S", "@Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("## Lowered Source", output);
    }

    [Fact]
    public async Task Member_SelectDecompiledSource_RequestTrace_RecordsDecompileOutcome()
    {
        // The app converts the decompiler's telemetry-free trace shape into a
        // request-trace breadcrumb: a decompile.method stage carrying the
        // fidelity outcome and the symbol source the render actually consulted.
        using var diagram = RequestMermaidDiagram.Start();

        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, _, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var mermaid = diagram.ToMermaid();
        Assert.Matches(@"decompile\.method<br/>SerializeToElement \(\w+, pdb:\w+\)", mermaid);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_RendersHiddenFactSection()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Facts" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Facts is explicitly selected, so the section renders even when the
        // method body has no hidden facts (positive-only: the empty-state notes
        // absence of findings, never asserts the method is fact-free).
        Assert.Contains("## Facts", output);
        Assert.Contains("No hidden facts found", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectCostOverlay_RendersExplicitCostFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-S", "Cost Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Cost Overlay", output);
        Assert.Contains("cost.callee", output);
        Assert.Contains("alloc-loop", output);
        Assert.Contains("cost.method", output);
    }

    [Fact]
    public async Task Member_CostOverlay_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Cost Overlay", output);
        Assert.DoesNotContain("cost.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_CostOverlay_BareRendersPayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-S", "Cost Overlay", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Cost Overlay", output);
        Assert.Contains("cost.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoveryListsCostOverlayAsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Cost Overlay        section (opt-in)", output);
    }

    [Fact]
    public async Task Member_Facts_IsExplicitOnly_NotShownAtDetailed()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Detailed
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Facts is opt-in: the inline Annotated Source view shows the same facts
        // for humans, so the structured table never auto-renders, even at -v:d.
        Assert.DoesNotContain("## Facts", output);
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
        Assert.DoesNotContain("## Annotated Source", output);
        Assert.DoesNotContain("## Original Source", output);
        // WriteNode<TValue>'s first parameter is `in TValue`; the call site
        // passes it without a keyword (an explicit `ref` here is CS1615). The
        // operand renders bare, not as the old `ref value` spelling.
        Assert.Contains("WriteNode<TValue>(value", output);
        Assert.DoesNotContain("WriteNode<TValue>(ref value", output);
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
    public async Task Member_SelectDecompiledSource_UsesExpressionBodiedSyntaxForOneLineReturn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "CallsInterfaceItem", "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static int CallsInterfaceItem(System.Collections.Generic.IList<int> values) => values[0];", output);
        Assert.DoesNotContain("{", output);
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
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("stable\tcanonical_signature", output);
        Assert.Contains("Serialize~", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize<TValue>", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TableRendersOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--show-index", "--table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Stable", output);
        Assert.Contains("Canonical Signature", output);
        Assert.Contains("Serialize:1", output);
        Assert.Contains("Serialize~", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("Overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_JsonlProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature", "--jsonl");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);

        using var first = JsonDocument.Parse(lines[0]);
        Assert.True(first.RootElement.TryGetProperty("stable", out var selector));
        Assert.True(first.RootElement.TryGetProperty("canonical_signature", out var signature));
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize", signature.GetString());
        Assert.StartsWith("Serialize~", selector.GetString());
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_StableSelectorRoundTripsToSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var stableSelector = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();
        Assert.StartsWith("Serialize~", stableSelector);

        (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            stableSelector, "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Signature", output);
        Assert.Contains("public static string Serialize", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_UnknownProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature;Obsolete", "--tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("stable\tcanonical_signature", output);
        Assert.Contains("warning: column 'Obsolete' not found in section 'Member Index'", error);
        Assert.Contains("Run -D \"Member Index\" to list available columns.", error);
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
    public async Task Type_GenericInstantiation_PreservesNestedTypeSuffix()
    {
        // #1154: an instantiated nested type (Dictionary`2.Enumerator) must keep
        // its nested segment instead of collapsing to Dictionary<TKey, TValue>.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Dictionary`2", "--platform", "System.Private.CoreLib",
            "-S", "Methods");

        Assert.Equal(0, exit);
        Assert.Contains("Dictionary<TKey, TValue>.Enumerator GetEnumerator()", output);
        Assert.DoesNotContain("Dictionary<TKey, TValue> GetEnumerator()", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_UsesExpressionBodiedSyntaxForOneLineMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static int CallsInterfaceItem(IList<int> values) => values[0];", output);
        Assert.Contains("    public static void CallsWriteLineTwice()\n    {", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TypeListing_NestedTypes_ShowDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.KeyCollection", output);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.ValueCollection", output);
        Assert.Contains("System.Collections.Generic.Stack<T>.Enumerator", output);
        Assert.DoesNotContain("class   KeyCollection", output);
        Assert.DoesNotContain("struct  Enumerator", output);
    }

    [Fact]
    public async Task TypeListing_NestedDelegate_ShowsFullDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy.StructGetter<TStruct, TResult>`", output);
        Assert.DoesNotContain("| `StructGetter<TStruct, TResult>` |", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Enum_RendersValuesListing()
    {
        // Enums have no method bodies; the listing renders the declaration
        // and values — following the ref assembly's type forwarder to the
        // defining assembly.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.DayOfWeek", "--platform", "System.Runtime",
            "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public enum DayOfWeek", output);
        Assert.Contains("Sunday = 0,", output);
        Assert.Contains("Saturday = 6,", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Bare_EmitsBareListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source", "--bare");

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
    public async Task Type_BareWithoutSection_Errors()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--bare", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--bare requires exactly one -S section", error);
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
        Assert.Contains("explicit:System.IConvertible.ToBoolean~", output);

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
        Assert.Contains("extension:AsMemory~", output);

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
    public async Task Type_StaticClass_RendersStaticClassModifierOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Math", "--shape", "--tips", "q", "-n", "1");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("static class System.Math", output, StringComparison.Ordinal);
        Assert.DoesNotContain("static abstract sealed class", output);
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibrary_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryBeforeTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--platform", "System.Text.Json", "JsonSerializer", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryBeforeOptionAndTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--platform", "System.Text.Json", "--count", "JsonSerializer");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryAfterOptionAndTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--tfm", "net10.0", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithRootOptionBeforeCommandAndNamedPlatformLibrary_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "-v:q", "find", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_ComposesBareAndNamedPlatformScopes()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "JsonSerializer", "--platform", "--platform", "System.Linq", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_Count_RendersOnlyCount()
    {
        var (implementsExit, implementsOutput, implementsError) = await RunAppAsync(
            "implements", "IDisposable", "--platform", "--count");
        var (extensionsExit, extensionsOutput, extensionsError) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "--count");
        var (dependsExit, dependsOutput, dependsError) = await RunAppAsync(
            "depends", "System.Int128", "--count");

        Assert.Equal(0, implementsExit);
        Assert.Empty(implementsError);
        Assert.True(int.Parse(implementsOutput.Trim()) > 0);

        Assert.Equal(0, extensionsExit);
        Assert.Empty(extensionsError);
        Assert.True(int.Parse(extensionsOutput.Trim()) > 0);

        Assert.Equal(0, dependsExit);
        Assert.Empty(dependsError);
        Assert.True(int.Parse(dependsOutput.Trim()) > 0);
    }

    [Fact]
    public async Task Depends_Count_WithEmptyDependencyTree_RendersZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Object", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public async Task Depends_Count_WithEmptyLibraryDependencyTree_RendersZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "--library", "System.Private.CoreLib", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_BarePlatformBeforeTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "--platform", "IEnumerable<T>", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim()) > 0);
    }

    [Fact]
    public async Task RelationshipCommands_NamedPlatformLibrary_IsAccepted()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+$", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_NamedPlatformLibrary_FormatsSourceAsFrameworkAtVersion()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "-v:n", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("runtime@", output);
        Assert.DoesNotContain("@runtime", output);
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
    public async Task NoArguments_Commands_ReportMissingInputConsistently()
    {
        // Issue #1690: every command reports a missing required argument the Unix way —
        // a concise error on stderr with a non-zero exit, not full help with exit 0.
        foreach (var command in new[] { "type", "member", "find", "depends", "extensions", "implements" })
        {
            var (exit, output, error) = await RunAppAsync(command, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Error:", error);
            Assert.Contains($"dotnet-inspect {command} --help", error);
        }
    }

    [Fact]
    public async Task LibraryCommand_NonexistentLibraryPath_ReportsFileNotFound()
    {
        // Regression for issue #1690: a missing local library path must report a file error,
        // not be misclassified as a NuGet package.
        var (exit, output, error) = await RunAppAsync("library", "./does-not-exist/MyLib.dll");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("File not found: ./does-not-exist/MyLib.dll", error);
        Assert.DoesNotContain("Package", error);
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
    public async Task LibraryCommand_PlatformFacade_LibraryInfoShowsFacadeAssemblyYes()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime.CompilerServices.Unsafe is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "library", "System.Runtime.CompilerServices.Unsafe", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("| Facade | Yes |", output);
    }

    [Fact]
    public async Task LibraryCommand_PlatformNonFacade_LibraryInfoShowsFacadeAssemblyNo()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("| Facade | No |", output);
    }

    [Fact]
    public async Task LibraryCommand_NonPlatformLibraryInfo_DoesNotShowFacadeAssembly()
    {
        var (exit, output, runError) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.DoesNotContain("| Facade |", output);
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
    public async Task CliDiscoverySections_AreSelectable()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var diffV1 = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(TestAssemblyPath)!, "..", "..",
                "DiffFixtures.V1", "release", "DiffFixtureSample.dll"));
            var diffV2 = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(TestAssemblyPath)!, "..", "..",
                "DiffFixtures.V2", "release", "DiffFixtureSample.dll"));

            List<string[]> commands =
            [
                ["library", TestAssemblyPath],
                ["type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath],
                ["type", typeof(EmptyDiscoveryFixture).FullName!, "--library", TestAssemblyPath],
                ["member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.CallsInterfaceItem), "--library", TestAssemblyPath],
                ["member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.Overloaded), "--library", TestAssemblyPath],
                ["package", packagePath],
                ["diff", "--library", $"{diffV1}..{diffV2}"]
            ];

            foreach (var command in commands)
            {
                var discoveryArgs = command.Concat(["-D", "--tips", "q"]).ToArray();
                var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(discoveryArgs);
                Assert.Equal(0, discoverExit);

                var sections = ExtractDiscoveryRows(discoverOutput)
                    .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.Name)
                    .ToArray();
                if (!IsNoMemberTypeDiscoveryCommand(command))
                    Assert.NotEmpty(sections);

                foreach (var section in sections)
                {
                    var selectArgs = command.Concat(["-S", section, "--table", "--tips", "q", "-n", "40"]).ToArray();
                    var (selectExit, selectOutput, selectError) = await RunAppAsync(selectArgs);
                    Assert.True(selectExit == 0,
                        $"{command[0]} -S '{section}' failed after being listed by -D. Discovery stderr: {discoverError}. Selection stderr: {selectError}");
                    if (RequiresRealDataDiscoveryGuard(command))
                    {
                        Assert.False(string.IsNullOrWhiteSpace(selectOutput),
                            $"{command[0]} -S '{section}' produced no data after being listed by -D.");
                        Assert.DoesNotContain("has no data", selectOutput, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("has no data", selectError, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static bool RequiresRealDataDiscoveryGuard(string[] command)
        => IsNoMemberTypeDiscoveryCommand(command)
           || command is ["member", _, var memberName, ..]
                && memberName == nameof(MemberCallsFixture.Overloaded);

    private static bool IsNoMemberTypeDiscoveryCommand(string[] command)
        => command is ["type", var typeName, ..]
           && typeName == typeof(EmptyDiscoveryFixture).FullName;

    [Fact]
    public async Task MemberDiscovery_MultiOverload_DoesNotListSingleOverloadSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.Overloaded),
            "--library", TestAssemblyPath, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);

        var sections = ExtractDiscoveryRows(output)
            .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Name)
            .ToArray();

        foreach (var section in SingleOverloadDiscoverySections)
            Assert.DoesNotContain(section, sections);
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
        Assert.DoesNotContain("IL", sections);
        Assert.DoesNotContain("Source Files", sections);
    }

    private static readonly string[] SingleOverloadDiscoverySections =
    [
        "Signature",
        "Custom Attributes",
        "Decompiled Source",
        "Annotated Source",
        "Cost Overlay",
        "Original Source",
        "Calls",
        "Callers",
        "Call Graph",
        "Caller Graph",
        "Unsafe Operations",
        "Top Leverage",
        "Performance Triage",
        "Facts",
        "IL"
    ];

    [Fact]
    public async Task LibraryCommand_DiscoverEffective_ListsSourceLinkAuditSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);
        Assert.Contains("SourceLink Availability", output);
        Assert.Contains("SourceLink Missing Files", output);
        Assert.Contains("SourceLink Integrity", output);

        var (allExit, allOutput, allError) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "-D", "@All", "--table", "--tips", "q");

        Assert.Equal(0, allExit);
        Assert.DoesNotContain("Tip:", allError);
        Assert.Contains("SourceLink Availability", allOutput);
        Assert.Contains("SourceLink Missing Files", allOutput);
        Assert.Contains("SourceLink Integrity", allOutput);
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
                "ASP.NET Core",
                "Aspire",
                "Async Methods",
                "Authentication",
                "Configuration",
                "Custom Attributes",
                "Dependency Injection",
                "Extension Methods",
                "Health Checks",
                "Hosting",
                "HTTP Client",
                "Integration Opportunities",
                "Integrations",
                "Logging",
                "OpenAPI",
                "OpenTelemetry",
                "Options",
                "Performance Triage",
                "Resources",
                "Source Files",
                "SourceLink Availability",
                "SourceLink Integrity",
                "SourceLink Missing Files",
                "Switches",
                "Top Leverage",
                "Type Forwarders",
                "Unsafe Members"
            ],
            optInNames);
        Assert.DoesNotContain(lines, line => line.StartsWith("Missing Source Files", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Source Integrity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_SourceFilesSection_RendersTypeUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.CommandLine.dll", "--package", "System.CommandLine",
            "-S", "Source Files", "--tips", "q", "-n", "18");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Files", output);
        Assert.Contains("| Type | Url |", output);
        Assert.Contains("System.CommandLine.Command", output);
        Assert.Contains("Command.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_SourceFilesSection_TypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Newtonsoft.Json.JsonConvert", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffset_ResolvesSourceLocation()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"token\": \"0x6000001\"", output);
        Assert.Contains("\"line\": 527", output);
        Assert.Contains("HexConverter.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_SwitchesSection_DetectsFeatureSwitchDefinitions()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Switches", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Switches", output);
        Assert.Contains("| Kind | Switch | API |", output);
        Assert.Contains("| Feature Switch | `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` | `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` |", output);
        Assert.Contains("| AppContext | `System.Text.Json.Serialization.RespectNullableAnnotationsDefault` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSwitchesCategory_ListsSwitchesSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Switches", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverAuditCategory_ListsAuditWorkflowSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Audit", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Switches", output);
        Assert.DoesNotContain("Signals", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAwsS3_ShowsCloudClientSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.S3", "--library", "-S", "Integration Opportunities", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Integration | API | Integration Type | Look For |", output);
        Assert.Contains("| Aspire | `Amazon.S3.AmazonS3Client` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Dependency Injection | `Amazon.S3.AmazonS3Client` | IServiceCollection registration | IServiceCollection, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForCognito_ShowsAuthenticationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.CognitoAuthentication", "--library", "-S", "Integration Opportunities", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Authentication | `Amazon.Extensions.CognitoAuthentication.CognitoUser` | Authentication/Identity registration | AuthenticationBuilder, Add*Identity*, Add*Cognito* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForNpgsql_ShowsResourceSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Npgsql", "--library", "-S", "Integration Opportunities", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration | IHealthChecksBuilder, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAzureAppConfiguration_ShowsConfigurationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Data.AppConfiguration", "--library", "-S", "Integration Opportunities", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Configuration | `Azure.Data.AppConfiguration.ConfigurationClient` | IConfigurationBuilder source | IConfigurationBuilder, AddAzureAppConfiguration |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForSystemsManager_ShowsConfigurationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.Configuration.SystemsManager", "--library", "-S", "Configuration", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Configuration", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.SystemsManagerExtensions.AddSystemsManager(...)` |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.AppConfigExtensions.AddAppConfig(...)` |", output);
        Assert.Contains("| Provider | `Amazon.Extensions.Configuration.SystemsManager.SystemsManagerConfigurationProvider` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForJson_ShowsConfigurationProviderShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Json", "--library", "-S", "Configuration", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Configuration", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)` |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonStream(...)` |", output);
        Assert.Contains("| Provider | `Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider` |", output);
        Assert.Contains("| Source | `Microsoft.Extensions.Configuration.Json.JsonConfigurationSource` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForUserSecrets_ShowsConfigurationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.UserSecrets", "--library", "-S", "Configuration", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Configuration", output);
        Assert.Contains("| API |", output);
        Assert.Contains("| `Microsoft.Extensions.Configuration.UserSecretsConfigurationExtensions.AddUserSecrets(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForBinder_ShowsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Binder", "--library", "-S", "Configuration", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Configuration", output);
        Assert.Contains("| Binding | `Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(...)` |", output);
        Assert.Contains("| Binding | `Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForOptionsConfiguration_ShowsOptionsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Options.ConfigurationExtensions", "--library", "-S", "Configuration", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Configuration", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions.BindConfiguration(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions.Configure(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionIntegration_ForScrutor_ShowsScanningAndDecorationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Scrutor", "--library", "-S", "Dependency Injection", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Dependency Injection", output);
        Assert.Contains("| Assembly Scanning | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Scan(...)` |", output);
        Assert.Contains("| Decoration | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Decorate(...)` |", output);
        Assert.Contains("| Decoration | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.TryDecorate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OptionsIntegration_ForValidationPackage_ShowsValidationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "ReHackt.Extensions.Options.Validation", "--library", "-S", "Options", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Options", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderValidationExtensions.ValidateDataAnnotationsRecursively(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.ConfigureAndValidate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksIntegration_ForAspNetCoreMiddleware_ShowsUseHealthChecks()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Diagnostics.HealthChecks", "--library", "-S", "Health Checks", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Health Checks", output);
        Assert.Contains("| `Microsoft.AspNetCore.Builder.HealthCheckApplicationBuilderExtensions.UseHealthChecks(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingIntegration_ForHostedServiceRegistration_ShowsHostedServiceApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "App.Metrics.Extensions.Hosting", "--library", "-S", "Hosting", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Hosting", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionMetricsReportingExtensions.AddMetricsReportingHostedService(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiIntegration_ForAnnotations_ShowsAnnotationSupport()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Annotations", "--library", "-S", "OpenAPI", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## OpenAPI", output);
        Assert.Contains("| Annotation | `Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute` |", output);
        Assert.Contains("| Configuration | `Microsoft.Extensions.DependencyInjection.AnnotationsSwaggerGenOptionsExtensions.EnableAnnotations(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetryIntegration_ForSerilogSink_ShowsOtlpLoggingApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Sinks.OpenTelemetry", "--library", "-S", "OpenTelemetry", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## OpenTelemetry", output);
        Assert.Contains("| Logging | `Serilog.OpenTelemetryLoggerConfigurationExtensions.OpenTelemetry(...)` |", output);
        Assert.Contains("| OpenTelemetry | `Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForOpenIddictValidation_ShowsValidationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "OpenIddict.Validation.AspNetCore", "--library", "-S", "Authentication", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Authentication", output);
        Assert.Contains("| Validation | `Microsoft.Extensions.DependencyInjection.OpenIddictValidationAspNetCoreExtensions.UseAspNetCore(...)` |", output);
        Assert.Contains("| Validation | `OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreHandler` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForBlazorAuthorization_ShowsAuthenticationStateApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Components.Authorization", "--library", "-S", "Authentication", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Authentication", output);
        Assert.Contains("| Authentication State | `Microsoft.Extensions.DependencyInjection.CascadingAuthenticationStateServiceCollectionExtensions.AddCascadingAuthenticationState(...)` |", output);
        Assert.Contains("| Authorization UI | `Microsoft.AspNetCore.Components.Authorization.AuthorizeView` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForGraphQlPackages_ShowsAuthorizationBuilderApis()
    {
        var (hotChocolateExit, hotChocolateOutput, hotChocolateError) = await RunAppAsync(
            "package", "HotChocolate.Authorization", "--library", "-S", "Authentication", "--rows", "-n", "20");
        var (graphQlExit, graphQlOutput, graphQlError) = await RunAppAsync(
            "package", "GraphQL.Authorization", "--library", "-S", "Authentication", "--rows", "-n", "20");

        Assert.Equal(0, hotChocolateExit);
        Assert.Contains("## Authentication", hotChocolateOutput);
        Assert.Contains("| Authorization | `Microsoft.Extensions.DependencyInjection.AuthorizeRequestExecutorBuilder.AddAuthorizationCore(...)` |", hotChocolateOutput);
        Assert.Contains("| Handler | `HotChocolate.Authorization.IAuthorizationHandler` |", hotChocolateOutput);
        Assert.DoesNotContain("Tip:", hotChocolateError);

        Assert.Equal(0, graphQlExit);
        Assert.Contains("## Authentication", graphQlOutput);
        Assert.Contains("| Authorization | `GraphQL.AuthorizationGraphQLBuilderExtensions.AddAuthorization(...)` |", graphQlOutput);
        Assert.Contains("| Requirement | `GraphQL.Authorization.IAuthorizationRequirement` |", graphQlOutput);
        Assert.DoesNotContain("Tip:", graphQlError);
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
    public async Task LibraryCommand_LibraryInfo_CountsStarterApiOnlyIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.Extensions.Bedrock.MEAI", "--library", "-S", "Library Info");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.Contains("| Integrations | 1 |", output);
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
        Assert.DoesNotContain("Configuration", output);
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
    public async Task LibraryCommand_AuthenticationSection_ForJwtBearer_ShowsSchemeCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication.JwtBearer", "--library", "-S", "@Integrations", "--rows", "-n", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Authentication | 5 |", output);
        Assert.Contains("## Authentication", output);
        Assert.Contains("| Authentication | `Microsoft.Extensions.DependencyInjection.JwtBearerExtensions.AddJwtBearer(...)` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthenticationCore_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication", "--library", "-S", "Authentication", "--rows", "-n", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Authentication", output);
        Assert.Contains("| Authentication | `Microsoft.Extensions.DependencyInjection.AuthenticationServiceCollectionExtensions.AddAuthentication(...)` |", output);
        Assert.Contains("| Middleware | `Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication(...)` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthorization_ShowsAuthorizationCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authorization", "--library", "-S", "Authentication", "--rows", "-n", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Authentication", output);
        Assert.Contains("| Authorization | `Microsoft.Extensions.DependencyInjection.AuthorizationServiceCollectionExtensions.AddAuthorizationCore(...)` |", output);
        Assert.Contains("| Builder | `Microsoft.AspNetCore.Authorization.AuthorizationBuilder` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authorization.AuthorizationOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAwsCognitoIdentity_ShowsIdentityCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.AspNetCore.Identity.Cognito", "--library", "-S", "@Integrations", "--rows", "-n", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Authentication | 1 |", output);
        Assert.Contains("| Dependency Injection | 1 |", output);
        Assert.Contains("## Authentication", output);
        Assert.Contains("| API |", output);
        Assert.Contains("CognitoServiceCollectionExtensions.AddCognitoIdentity(...)", output);
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
    public async Task LibraryCommand_AspNetCoreSection_ForSerilog_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.AspNetCore", "--library", "-S", "ASP.NET Core", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## ASP.NET Core", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Configuration | `Serilog.AspNetCore.RequestLoggingOptions` |", output);
        Assert.Contains("| Middleware | `Serilog.SerilogApplicationBuilderExtensions.UseSerilogRequestLogging(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForHangfire_ShowsEndpointAndMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Hangfire.AspNetCore", "--library", "-S", "ASP.NET Core", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## ASP.NET Core", output);
        Assert.Contains("| Endpoint | `Hangfire.HangfireEndpointRouteBuilderExtensions.MapHangfireDashboard(...)` |", output);
        Assert.Contains("| Middleware | `Hangfire.HangfireApplicationBuilderExtensions.UseHangfireDashboard(...)` |", output);
        Assert.Contains("| Middleware | `Hangfire.HangfireApplicationBuilderExtensions.UseHangfireServer(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForGrpc_ShowsEndpointCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Grpc.AspNetCore.Server", "--library", "-S", "@Integrations", "--rows", "-n", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| ASP.NET Core | 3 |", output);
        Assert.Contains("| Dependency Injection | 1 |", output);
        Assert.Contains("## ASP.NET Core", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService(...)` |", output);
        Assert.Contains("## Dependency Injection", output);
        Assert.Contains("GrpcServicesExtensions.AddGrpc(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionBlobs_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Extensions.AspNetCore.DataProtection.Blobs@1.5.3", "--all-libraries", "-S", "ASP.NET Core", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## ASP.NET Core", output);
        Assert.Contains("| API |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureStorageBlobDataProtectionBuilderExtensions.PersistKeysToAzureBlobStorage(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionKeys_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Extensions.AspNetCore.DataProtection.Keys@1.6.3", "--all-libraries", "-S", "ASP.NET Core", "--rows", "-n", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## ASP.NET Core", output);
        Assert.Contains("| API |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureDataProtectionKeyVaultKeyBuilderExtensions.ProtectKeysWithAzureKeyVault(...)", output);
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
        Assert.Contains("| HTTP Client | 11 |", output);
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

    private static List<(string Name, string Kind)> ExtractDiscoveryRows(string output)
    {
        List<(string Name, string Kind)> rows = [];
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith('|'))
                continue;
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 4 || cells[1] == "Name" || cells[1].All(ch => ch == '-'))
                continue;
            rows.Add((cells[1], cells[2]));
        }

        return rows;
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
        // Target a platform assembly with known SourceLink data so the audit deterministically
        // renders. The local test assembly's SourceLink presence is environment-dependent
        // (SDK 8+ auto-enables SourceLink only when building inside a git repo), so it cannot
        // reliably exercise the section (#675).
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Signals,SourceLink Availability,SourceLink Missing Files");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("## SourceLink Availability", output);
        Assert.Contains("Source Files", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformVersion_UsesPlatformRuntimeRoute()
    {
        // Decouple from the host's running runtime version (#1256). Probe an installed
        // shared runtime version the resolver can find rather than binding to wherever
        // the test host's System.Private.CoreLib happens to live, which fails on
        // preview/self-contained hosts whose running version isn't a discoverable
        // shared framework.
        var (_, installedVersion, frameworkError) = PlatformResolver.ResolveRuntimeFramework("runtime");
        Assert.SkipWhen(
            installedVersion is null,
            $"No installed Microsoft.NETCore.App shared runtime found: {frameworkError}");

        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", installedVersion!, "-v:q");

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
    public async Task PackageCommand_AllLibraries_RendersLibraryInfoPerHighestTfmLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "Library Info", "--rows", "-n", "20");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Info (lib/net10.0/Latest.One.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.Two.dll)", output);
            Assert.DoesNotContain("## Library Info (lib/net8.0/Older.dll)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TfmAllIncludesEveryTfmLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "--tfm", "all", "-S", "Library Info", "--rows", "-n", "12");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Info (lib/net8.0/Older.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.One.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.Two.dll)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatesIntegrationsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "@Integrations", "--rows", "-n", "40");

            Assert.Equal(0, exit);
            Assert.Contains("## Integrations", output);
            Assert.Contains("| Configuration |", output);
            Assert.Contains("## Configuration", output);
            Assert.Contains("| Library | Kind | API |", output);
            Assert.Contains("Microsoft.Extensions.Configuration.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.Json.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TsvEmitsIntegrationRowsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "Integrations", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tlibrary\ttfm\tintegration\tapis", output);
            Assert.Contains("Microsoft.Extensions.Configuration.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.Json.dll", output);
            Assert.Contains("\tConfiguration\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TsvRejectsIntegrationCategory()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "@Integrations", "--tsv");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("requires one concrete section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_JsonlEmitsIntegrationRowsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "Integrations", "--jsonl");

            Assert.Equal(0, exit);
            var documents = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            Assert.Contains(documents, document =>
                document.RootElement.GetProperty("integration").GetString() == "Configuration"
                && document.RootElement.GetProperty("library").GetString()?.EndsWith("Microsoft.Extensions.Configuration.Json.dll", StringComparison.Ordinal) == true);
            Assert.All(documents, document =>
                Assert.False(document.RootElement.TryGetProperty("section", out _)));
            Assert.DoesNotContain("Tip:", error);

            foreach (var document in documents)
                document.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_CannotCombineWithLibrary()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "--library");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--all-libraries cannot be combined with --library", error);
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
    public async Task Router_FacadePlatformBareName_RoutesToForwardedType()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime.CompilerServices.Unsafe is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "System.Runtime.CompilerServices.Unsafe", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("# System.Runtime.CompilerServices.Unsafe", output);
        Assert.DoesNotContain("# System.Runtime.CompilerServices.Unsafe.dll", output);
        Assert.Contains("Kind: class", output);
    }

    [Fact]
    public async Task Router_NonFacadePlatformBareName_RoutesToLibrary()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "System.Text.Json", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("# System.Text.Json.dll", output);
        Assert.Contains("Name: System.Text.Json", output);
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
            Assert.Contains("| Source Files | section (opt-in) |", output);
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
    public async Task Package_Discover_OrdersRowsByDiscoveryGroup()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Discovery",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            var rows = ExtractDiscoveryRows(output);

            Assert.Contains(rows, row => row.Name == "Files" && row.Kind == "section (opt-in)");

            var regular = rows.Where(row => row.Kind == "section").Select(row => row.Name).ToArray();
            var categories = rows.Where(row => row.Kind == "category").Select(row => row.Name).ToArray();
            var optIn = rows.Where(row => row.Kind == "section (opt-in)").Select(row => row.Name).ToArray();

            Assert.Equal(regular.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), regular);
            Assert.Equal(categories.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), categories);
            Assert.Equal(optIn.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), optIn);
            Assert.True(rows.FindLastIndex(row => row.Kind == "section") < rows.FindIndex(row => row.Kind == "category"));
            Assert.True(rows.FindLastIndex(row => row.Kind == "category") < rows.FindIndex(row => row.Kind == "section (opt-in)"));
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
            Assert.Contains("Source Files", output);
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
    public async Task Package_SourceFilesSection_RendersLibraryTypeUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "System.CommandLine",
            "-S", "Source Files", "--tips", "q", "-n", "18");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Files", output);
        Assert.Contains("| Library | Type | Url |", output);
        Assert.Contains("lib/net8.0/System.CommandLine.dll", output);
        Assert.Contains("System.CommandLine.Command", output);
        Assert.Contains("Command.cs", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_TypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("lib/net6.0/Newtonsoft.Json.dll\tNewtonsoft.Json.JsonConvert\t", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_Bare_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "-t", "JsonReader", "--bare", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line));
    }

    [Fact]
    public async Task Package_LibrarySourceFilesSection_PreservesTypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--library",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Name: Newtonsoft.Json", output);
        Assert.Contains("Newtonsoft.Json.JsonConvert\t", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_NewtonsoftJson_DoesNotBleedJTokenRowsAcrossTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json",
            "-S", "Source Files", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var rows = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 3)
            .Select(cells => (Library: cells[0], Type: cells[1], Url: cells[2]))
            .ToList();

        var jTokenRows = rows.Where(row => row.Type == "Newtonsoft.Json.Linq.JToken").ToArray();
        Assert.NotEmpty(jTokenRows);
        Assert.All(jTokenRows, row => Assert.Contains("/Linq/JToken", row.Url));
        Assert.DoesNotContain(jTokenRows, row => row.Url.Contains("JTokenReader.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(jTokenRows, row => row.Url.Contains("JValue.cs", StringComparison.OrdinalIgnoreCase));

        var jTokenReaderRows = rows.Where(row => row.Type == "Newtonsoft.Json.Linq.JTokenReader").ToArray();
        Assert.NotEmpty(jTokenReaderRows);
        Assert.All(jTokenReaderRows, row => Assert.Contains("/Linq/JTokenReader.cs", row.Url));
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
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| lib/net10.0/Latest.One.dll |", output);
            Assert.Contains("| lib/net10.0/Latest.One.xml | 7 |", output);
            Assert.Contains("| lib/net10.0/Latest.Two.dll |", output);
            Assert.Contains("| lib/net8.0/Older.dll |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NormalOutput_RendersLibraryFileSizes()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-v:n");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Files", output);
            Assert.Contains("| lib/net10.0/Latest.One.xml | 7 |", output);
            Assert.DoesNotContain("| lib/net10.0/Latest.One.xml | 0 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MarkdownFiles_RendersAllMarkdownFilesWithFileSchema()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.MarkdownFiles", "PACKAGE.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Markdown Files");

            Assert.Equal(0, exit);
            Assert.Contains("## Markdown Files", output);
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| AGENTS.md | 6 |", output);
            Assert.Contains("| PACKAGE.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageInfoReadme_UsesBestReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Info", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package Info");

            Assert.Equal(0, exit);
            Assert.Contains("| Readme | AGENTS.md |", output);
            Assert.DoesNotContain("| Readme | README.md |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageReadme_RendersSingleBestReadmeFile()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Section", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Grounding");

            Assert.Equal(0, exit);
            Assert.Contains("## Grounding", output);
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| AGENTS.md | 6 |", output);
            Assert.DoesNotContain("| README.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_GroundingSection_LegacyReadmeSelectorStillResolves()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Grounding.Alias", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README");

            Assert.Equal(0, exit);
            Assert.Contains("## Grounding", output);
            Assert.Contains("| AGENTS.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Print_PrintsBestGroundingContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Print.Grounding", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("agents\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_PrintsBestReadmeContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Content", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--readme");

            Assert.Equal(0, exit);
            Assert.Contains("agents", output);
            Assert.DoesNotContain("readme", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_Bare_PrintsBestReadmeBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Bare", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--readme", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("agents\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageReadmeSection_Bare_PrintsReadmeBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.PackageReadme.Bare", "PACKAGE.md", "package docs");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Grounding", "--bare", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("package docs\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_Bare_PrintsSingleSelectedFileBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Bare", "README.md", "readme", "agents body");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "@readme", "--content", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("agents body\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_Bare_IgnoresEnvironmentRowFormat()
    {
        var originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.BareEnv", "README.md", "readme", "agents body");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "table");
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "@readme", "--content", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("agents body\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_DefaultNormalizesGithubBlobLinksToRaw()
    {
        const string readme = """
            [code](https://github.com/owner/repo/blob/main/src/File.cs)
            ![image](https://github.com/owner/repo/blob/main/images/logo.png)
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.RawLinks", "README.md", readme);
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--readme");

            Assert.Equal(0, exit);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/src/File.cs", output);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/images/logo.png", output);
            Assert.DoesNotContain("github.com/owner/repo/blob", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_BlobLeavesMarkdownLinksVerbatim()
    {
        const string readme = "[code](https://github.com/owner/repo/blob/main/src/File.cs)";
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.BlobLinks", "README.md", readme);
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--readme", "--blob");

            Assert.Equal(0, exit);
            Assert.Contains("https://github.com/owner/repo/blob/main/src/File.cs", output);
            Assert.DoesNotContain("raw.githubusercontent.com", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeInfo_RecordsSelectedReadmeProvenance()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Info", "README.md", "readme", "agents");
        try
        {
            InfoTracker.ResetForTests();
            var (exit, output, error) = await ConsoleCapture.RunAsync(async () =>
            {
                InfoTracker.Start();
                return await PackageCommand.ExecuteAsync(new InspectionOptions
                {
                    PackageArgs = [packagePath],
                    ShowReadme = true,
                    TipLevel = TipLevel.Quiet
                });
            });

            Assert.Equal(0, exit);
            Assert.Contains("agents", output);
            Assert.DoesNotContain("readme", output);
            Assert.Empty(error);
            Assert.Equal("AGENTS.md (6 B)", InfoTracker.GetDetail("readme"));
        }
        finally
        {
            InfoTracker.ResetForTests();
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_ReportsAgentDocumentation()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.AgentDocs.Signal", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("| Documentation | Agent documentation | Yes | AGENTS.md |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_TsvCombinesRowsWithPackageColumn()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.First", "PACKAGE.md", "first readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Second", "docs-readme.md", "second readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@readme", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.First\t1.0.0\tPACKAGE.md\t12", output);
            Assert.Contains("Test.Second\t1.0.0\tdocs-readme.md\t13", output);
            Assert.DoesNotContain("not a valid package version", error);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_TsvIncludesEmptyPackageRow()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.HasReadme\t1.0.0\t\t", output);
            Assert.Contains("Test.NoMatch\t1.0.0\t\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_JsonlIncludesEmptyPackageObject()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Jsonl.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Jsonl.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--jsonl");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"package\":\"Test.Jsonl.HasReadme\"", lines[0]);
            Assert.Contains("\"version\":\"1.0.0\"", lines[0]);
            Assert.Contains("\"path\":\"\"", lines[0]);
            Assert.Contains("\"package\":\"Test.Jsonl.NoMatch\"", lines[1]);
            Assert.Contains("\"version\":\"1.0.0\"", lines[1]);
            Assert.Contains("\"path\":\"\"", lines[1]);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathReadme_JsonEmitsNumericSizeForDeclaredReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Json.Readme", "PACKAGE.md", "declared readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--json");

            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output);
            var file = Assert.Single(document.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal("PACKAGE.md", file.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, file.GetProperty("size").ValueKind);
            Assert.Equal(15, file.GetProperty("size").GetInt64());
            Assert.False(file.TryGetProperty("is_readme", out _));
            Assert.False(file.TryGetProperty("is_agents", out _));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathReadme_JsonlEmitsNumericSizeForDeclaredReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Jsonl.Readme", "PACKAGE.md", "declared readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("PACKAGE.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("size").ValueKind);
            Assert.Equal(15, document.RootElement.GetProperty("size").GetInt64());
            Assert.False(document.RootElement.TryGetProperty("is_readme", out _));
            Assert.False(document.RootElement.TryGetProperty("is_agents", out _));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_JsonlEmitsNumericSizeForDeclaredReadme()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Multi.Jsonl.Readme", "PACKAGE.md", "declared readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Multi.Jsonl.Empty", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "PACKAGE.md", "--jsonl");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);

            using var hit = JsonDocument.Parse(lines[0]);
            Assert.Equal("Test.Multi.Jsonl.Readme", hit.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", hit.RootElement.GetProperty("version").GetString());
            Assert.Equal("PACKAGE.md", hit.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, hit.RootElement.GetProperty("size").ValueKind);
            Assert.Equal(15, hit.RootElement.GetProperty("size").GetInt64());
            Assert.False(hit.RootElement.TryGetProperty("is_readme", out _));

            using var empty = JsonDocument.Parse(lines[1]);
            Assert.Equal("Test.Multi.Jsonl.Empty", empty.RootElement.GetProperty("package").GetString());
            Assert.Equal("", empty.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("size").ValueKind);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathAgents_TsvResolvesAgentsRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Agents.One", "README.md", "readme", "agents one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Agents.Two", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.Agents.One\t1.0.0\tAGENTS.md\t10", output);
            Assert.Contains("Test.Agents.Two\t1.0.0\t\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathFirst_UsesFirstMatchingSelector()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Match.First", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Match.Second", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--path", "@readme", "--match", "first", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Match.First\t1.0.0\tAGENTS.md\t6", output);
            Assert.Contains("Test.Match.Second\t1.0.0\tREADME.md\t6", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathAll_ReturnsAllMatchingSelectors()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Match.All", "README.md", "readme", "agents");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, packagePath, "--path", "@agents", "--path", "README.md", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Match.All\t1.0.0\tAGENTS.md\t6", output);
            Assert.Contains("Test.Match.All\t1.0.0\tREADME.md\t6", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathSkipEmpty_OmitsEmptyPackages()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Skip.HasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Skip.NoAgents", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--skip-empty", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Skip.HasAgents", output);
            Assert.DoesNotContain("Test.Skip.NoAgents", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_CountIncludesEmptyPackageRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Count.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Count.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--tsv", "--count");

            Assert.Equal(0, exit);
            Assert.Equal("2", output.Trim());
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContent_PrintsSelectedFileWithSeparator()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Agents", "README.md", "readme", "agents body");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content");

            Assert.Equal(0, exit);
            Assert.Contains("------------ Test.Content.Agents :: AGENTS.md ------------", output);
            Assert.Contains("agents body", output);
            Assert.DoesNotContain("readme", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathContent_PreservesEmptyPackageBlock()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Content.HasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Content.NoAgents", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--content");

            Assert.Equal(0, exit);
            Assert.Contains("------------ Test.Content.HasAgents :: AGENTS.md ------------", output);
            Assert.Contains("agents", output);
            Assert.Contains("------------ Test.Content.NoAgents :: <absent> ------------", output);
            Assert.Contains("(absent)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathContentSkipEmpty_OmitsAbsentBlock()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Content.SkipHasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Content.SkipNoAgents", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--content", "--skip-empty");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Content.SkipHasAgents :: AGENTS.md", output);
            Assert.DoesNotContain("Test.Content.SkipNoAgents", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContent_JsonlIncludesContentField()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Jsonl", "README.md", "readme", "line one\nline two");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("Test.Content.Jsonl", document.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", document.RootElement.GetProperty("version").GetString());
            Assert.Equal("AGENTS.md", document.RootElement.GetProperty("path").GetString());
            Assert.True(document.RootElement.GetProperty("found").GetBoolean());
            Assert.Equal("line one\nline two", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeFrontmatter_PrintsOnlyYamlHeader()
    {
        var readme = """
            ---
            name: test
            description: resident
            ---
            # Body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Frontmatter", "README.md", readme);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--readme", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: test", output);
            Assert.Contains("description: resident", output);
            Assert.DoesNotContain("# Body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeBody_PrintsContentAfterYamlHeader()
    {
        var readme = """
            ---
            name: test
            ---
            # Body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Body", "README.md", readme);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--readme", "--body");

            Assert.Equal(0, exit);
            Assert.Contains("# Body", output);
            Assert.DoesNotContain("name: test", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeJsonl_IncludesPathSizeAndContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Jsonl", "PACKAGE.md", "package docs");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--readme", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("Test.Readme.Jsonl", document.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", document.RootElement.GetProperty("version").GetString());
            Assert.Equal("PACKAGE.md", document.RootElement.GetProperty("path").GetString());
            Assert.True(document.RootElement.GetProperty("size").GetInt64() > 0);
            Assert.Equal("package docs", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_ReadmeFrontmatter_AllowsMultiplePackages()
    {
        var firstReadme = """
            ---
            name: first
            ---
            # First Body
            """;
        var secondReadme = """
            ---
            name: second
            ---
            # Second Body
            """;
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.MultiReadme.First", "README.md", firstReadme);
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.MultiReadme.Second", "README.md", secondReadme);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--readme", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: first", output);
            Assert.Contains("name: second", output);
            Assert.DoesNotContain("# First Body", output);
            Assert.DoesNotContain("# Second Body", output);
            Assert.DoesNotContain("Multiple package inspection cannot be combined with --readme", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContentFrontmatter_PrintsOnlyYamlHeader()
    {
        var agents = """
            ---
            name: agents
            ---
            # Agent body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Frontmatter", "README.md", "readme", agents);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: agents", output);
            Assert.DoesNotContain("# Agent body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_AgentsIndex_EmitsDirectDependencyAgentsFrontmatter()
    {
        var agents = """
            ---
            name: Markout guidance
            description: Prefer Markout tables for structured markdown.
            ---
            # Body
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Agents", "1.2.3", "README.md", "readme", agents),
            new ProjectDocPackage("Test.Project.NoAgents", "4.5.6", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--agents-index", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            using var agentsDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.Agents")));
            Assert.Equal("Test.Project.Agents", agentsDocument.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.2.3", agentsDocument.RootElement.GetProperty("version").GetString());
            Assert.Equal("Markout guidance", agentsDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal("Prefer Markout tables for structured markdown.", agentsDocument.RootElement.GetProperty("description").GetString());
            Assert.Equal("AGENTS.md", agentsDocument.RootElement.GetProperty("path").GetString());

            using var emptyDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.NoAgents")));
            Assert.Equal("", emptyDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal("", emptyDocument.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_AgentsIndex_FoldsBlockScalarDescription()
    {
        var agents = """
            ---
            name: markout
            description: >-
              Source-generated .NET serializer that renders objects as Markdown.
              Reach for it when a CLI needs structured, agent-readable output
              instead of hand-built strings.
            ---
            # Body
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Folded", "1.0.0", "README.md", "readme", agents));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--agents-index", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single());
            Assert.Equal("markout", document.RootElement.GetProperty("name").GetString());
            Assert.Equal(
                "Source-generated .NET serializer that renders objects as Markdown. Reach for it when a CLI needs structured, agent-readable output instead of hand-built strings.",
                document.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Readme_UsesProjectResolvedDependencyVersion()
    {
        var agents = """
            ---
            name: selected
            ---
            # Agent guidance
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Readme", "2.0.0", "README.md", "# README body", agents));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--readme", "Test.Project.Readme");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("# Agent guidance", output);
            Assert.DoesNotContain("# README body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Readme_NormalizesGithubBlobLinksToRaw()
    {
        const string agents = "See https://github.com/owner/repo/blob/main/docs/guide.md";
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.RawLinks", "1.0.0", "README.md", "readme", agents));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--readme", "Test.Project.RawLinks");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/docs/guide.md", output);
            Assert.DoesNotContain("github.com/owner/repo/blob", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_JsonEmitsArray()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Json.One", "README.md", "one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Json.Two", "README.md", "two");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", firstPackage, secondPackage, "--json");

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(output);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal("Test.Json.One", doc.RootElement[0].GetProperty("package_name").GetString());
            Assert.Equal("Test.Json.Two", doc.RootElement[1].GetProperty("package_name").GetString());
            Assert.DoesNotContain("not a valid package version", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_TableCombinesPackageInfoRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Info.One", "README.md", "one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Info.Two", "README.md", "two");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", firstPackage, secondPackage, "--table");

            Assert.Equal(0, exit);
            Assert.Contains("Package", output);
            Assert.Contains("Field", output);
            Assert.Contains("Value", output);
            Assert.Contains("Test.Info.One", output);
            Assert.Contains("Test.Info.Two", output);
            Assert.Contains("Version", output);
            Assert.DoesNotContain("not a valid package version", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
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

public interface EmptyDiscoveryFixture
{
}

public static class CostOverlayFixture
{
    public static int Caller(int count) => HotCallee(count);

    public static int LowSignal(int value) => value + 1;

    public static int CallsLowSignal(int value) => LowSignal(value);

    public static int HotCallee(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += new object().GetHashCode();
        return total;
    }
}
