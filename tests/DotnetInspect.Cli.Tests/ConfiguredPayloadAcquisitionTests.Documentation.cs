using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task
        TypeCommand_ConfiguredRuntimeAssetPreservesInspectionWithoutDocumentation()
    {
        string id =
            $"Package.RuntimeDocumentation.{Guid.NewGuid():N}";
        string source = Path.Combine(_root, "runtime-documentation-feed");
        string assemblyName =
            FixtureCatalog.InspectWebDocumentation.AssemblyFileName;
        const string AssetDirectory =
            "runtimes/linux-x64/lib/net11.0";
        WriteLocalPackage(
            source,
            id,
            "Runtime-only package fixture.",
            library: await File.ReadAllBytesAsync(
                FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
                TestContext.Current.CancellationToken),
            libraryName: assemblyName,
            libraryDirectory: AssetDirectory);

        var (exit, output, error) = await RunCommandAsync(
            [
                "type",
                "InspectWeb.DocumentationFixtures.Widget",
                "--package",
                $"{id}@{Version}",
                "--library",
                $"{AssetDirectory}/{assemblyName}",
                "--source",
                source,
                "-v:d",
                "--tips",
                "q",
            ]);

        Assert.True(exit == 0, $"Exit {exit}\n{output}\n{error}");
        Assert.Empty(error);
        Assert.Contains(
            "class InspectWeb.DocumentationFixtures.Widget",
            output);
        Assert.DoesNotContain(
            "A receiver for projected extension methods.",
            output);
    }

    [Fact]
    public async Task
        MemberCommand_ConfiguredPackageUsesPackageDocumentationHouse()
    {
        string id =
            $"Package.Documentation.{Guid.NewGuid():N}";
        string source = Path.Combine(_root, "documentation-feed");
        string assemblyName =
            FixtureCatalog.InspectWebDocumentation.AssemblyFileName;
        WriteLocalPackage(
            source,
            id,
            "Package documentation fixture.",
            library: await File.ReadAllBytesAsync(
                FixtureCatalog.InspectWebDocumentation.AssemblyPath(),
                TestContext.Current.CancellationToken),
            libraryName: assemblyName,
            documentation: await File.ReadAllBytesAsync(
                FixtureCatalog.InspectWebDocumentation.AssetPath(
                    "documentation"),
                TestContext.Current.CancellationToken));

        var (exit, output, error) = await RunCommandAsync(
            [
                "member",
                "InspectWeb.DocumentationFixtures.WidgetExtensions",
                "Measure",
                "--package",
                $"{id}@{Version}",
                "--source",
                source,
                "-v:d",
                "--tips",
                "q",
            ]);

        Assert.True(exit == 0, $"Exit {exit}\n{output}\n{error}");
        Assert.Empty(error);
        Assert.Contains(
            "Methods that extend documentation fixture types.",
            output);
        Assert.Contains(
            "Measures a widget through its declaring extension member.",
            output);
    }

    [Fact]
    public async Task
        MemberCommand_ConfiguredPackageUsesAuthoredDocumentationHouse()
    {
        string id =
            $"Package.AuthoredDocumentation.{Guid.NewGuid():N}";
        string source = Path.Combine(
            _root,
            "authored-documentation-feed");
        string assemblyPath =
            typeof(CSharpText.MemberSlicing.MemberTextSlicer)
                .Assembly.Location;
        string assemblyName = Path.GetFileName(assemblyPath);
        WriteLocalPackage(
            source,
            id,
            "Authored package documentation fixture.",
            library: await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken),
            libraryName: assemblyName,
            pdb: await File.ReadAllBytesAsync(
                Path.ChangeExtension(assemblyPath, ".pdb"),
                TestContext.Current.CancellationToken));

        var (exit, output, error) = await RunCommandAsync(
            [
                "member",
                "CSharpText.MemberSlicing.MemberTextSlicer",
                nameof(
                    CSharpText.MemberSlicing.MemberTextSlicer
                        .ExtractMemberText),
                "--package",
                $"{id}@{Version}",
                "--source",
                source,
                "-v:d",
                "--tips",
                "q",
            ]);

        Assert.True(exit == 0, $"Exit {exit}\n{output}\n{error}");
        Assert.Empty(error);
        Assert.Contains(
            "Locates the declaration",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        MemberCommand_ImplicitDocsDoNotAuthorizeAuthoredDocumentation()
    {
        string id =
            $"Package.ImplicitDocumentation.{Guid.NewGuid():N}";
        string source = Path.Combine(
            _root,
            "implicit-documentation-feed");
        string assemblyPath =
            typeof(CSharpText.MemberSlicing.MemberTextSlicer)
                .Assembly.Location;
        string assemblyName = Path.GetFileName(assemblyPath);
        WriteLocalPackage(
            source,
            id,
            "Implicit documentation fixture.",
            library: await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken),
            libraryName: assemblyName,
            pdb: await File.ReadAllBytesAsync(
                Path.ChangeExtension(assemblyPath, ".pdb"),
                TestContext.Current.CancellationToken));

        var (exit, output, error) = await RunCommandAsync(
            [
                "member",
                "CSharpText.MemberSlicing.MemberTextSlicer",
                nameof(
                    CSharpText.MemberSlicing.MemberTextSlicer
                        .ExtractMemberText),
                "--package",
                $"{id}@{Version}",
                "--source",
                source,
                "--tips",
                "q",
            ]);

        Assert.True(exit == 0, $"Exit {exit}\n{output}\n{error}");
        Assert.Empty(error);
        Assert.DoesNotContain(
            "Locates the declaration",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        MemberCommand_AuthoredDocumentationSurvivesMalformedCompiledXml()
    {
        string id =
            $"Package.IndependentDocumentation.{Guid.NewGuid():N}";
        string source = Path.Combine(
            _root,
            "independent-documentation-feed");
        string assemblyPath =
            typeof(CSharpText.MemberSlicing.MemberTextSlicer)
                .Assembly.Location;
        string assemblyName = Path.GetFileName(assemblyPath);
        WriteLocalPackage(
            source,
            id,
            "Independent documentation fixture.",
            library: await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken),
            libraryName: assemblyName,
            documentation: "<doc><members>"u8.ToArray(),
            pdb: await File.ReadAllBytesAsync(
                Path.ChangeExtension(assemblyPath, ".pdb"),
                TestContext.Current.CancellationToken));

        var (exit, output, error) = await RunCommandAsync(
            [
                "member",
                "CSharpText.MemberSlicing.MemberTextSlicer",
                nameof(
                    CSharpText.MemberSlicing.MemberTextSlicer
                        .ExtractMemberText),
                "--package",
                $"{id}@{Version}",
                "--source",
                source,
                "-v:d",
                "--tips",
                "q",
            ]);

        Assert.True(exit == 0, $"Exit {exit}\n{output}\n{error}");
        Assert.Contains(
            "Locates the declaration",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "Compiled documentation was malformed or unreadable",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Authored documentation",
            error,
            StringComparison.Ordinal);
    }
}
