using System.Collections.Immutable;

using DotnetInspector.Sections;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public sealed class PackageChildrenTreePresentationTests
{
    [Fact]
    public void Markdown_StreamsLibrariesWithOwnerIssuedTitle()
    {
        PackageChildrenDocument document = Libraries(
        [
            Library(
                "tools/net10.0/any/Example.Tool.dll",
                "Example.Tool.dll",
                PackageLibraryChildRole.ToolEntryPoint),
            Library(
                "lib/net10.0/Example.Core.dll",
                "Example.Core.dll",
                PackageLibraryChildRole.Compile),
        ]);
        using var output = new StringWriter();

        PackageChildrenTreePresentation.Write(
            document,
            Plan(PackageChildrenTreePresentationFormat.Markdown),
            output);

        Assert.Equal(
            """
            Example.Package 1.0.0 (NuGet; net10.0; lib, tools)
            ├─ Example.Tool.dll (entry point)
            └─ Example.Core.dll

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void Mermaid_WrapsChildrenInThePackageTitle()
    {
        PackageChildrenDocument document = Libraries(
        [
            Library(
                "lib/net10.0/Example.Core.dll",
                "Example.Core.dll",
                PackageLibraryChildRole.Compile),
        ]);
        using var output = new StringWriter();

        PackageChildrenTreePresentation.Write(
            document,
            Plan(PackageChildrenTreePresentationFormat.Mermaid),
            output);

        string result = output.ToString();
        Assert.Contains(
            "Example.Package 1.0.0",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "Example.Core.dll",
            result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SuppressedWindow_WritesOnlyTheTextTitle()
    {
        PackageChildrenDocument document = Libraries(
        [
            Library(
                "lib/net10.0/Example.Core.dll",
                "Example.Core.dll",
                PackageLibraryChildRole.Compile),
        ]);
        using var output = new StringWriter();

        PackageChildrenTreePresentation.Write(
            document,
            new(
                PackageChildrenTreePresentationFormat.PlainText,
                suppressNodes: true,
                collapseToolDependencies: false,
                duplicateLibraryNames: new HashSet<string>()),
            output);

        Assert.Equal(
            "Example.Package 1.0.0 (NuGet; net10.0; lib)"
                + Environment.NewLine,
            output.ToString());
    }

    [Fact]
    public void MinimalToolTree_CollapsesLargeDependencyPopulation()
    {
        var libraries =
            ImmutableArray.CreateBuilder<PackageLibraryChild>();
        libraries.Add(
            Library(
                "tools/net10.0/any/Example.Tool.dll",
                "Example.Tool.dll",
                PackageLibraryChildRole.ToolEntryPoint));
        for (int index = 0; index < 9; index++)
        {
            libraries.Add(
                Library(
                    $"tools/net10.0/any/Dependency.{index}.dll",
                    $"Dependency.{index}.dll",
                    PackageLibraryChildRole.ToolLibrary));
        }
        using var output = new StringWriter();

        PackageChildrenTreePresentation.Write(
            Libraries(libraries.ToImmutable()),
            new(
                PackageChildrenTreePresentationFormat.Markdown,
                suppressNodes: false,
                collapseToolDependencies: true,
                duplicateLibraryNames: new HashSet<string>()),
            output);

        string result = output.ToString();
        Assert.Contains(
            "Example.Tool.dll (entry point)",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dependencies (9 Libraries; use -v:n for full inventory)",
            result,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Dependency.0.dll",
            result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateLibraryName_UsesAssetPath()
    {
        PackageChildrenDocument document = Libraries(
        [
            Library(
                "lib/net10.0/Example.dll",
                "Example.dll",
                PackageLibraryChildRole.Compile),
        ]);
        using var output = new StringWriter();

        PackageChildrenTreePresentation.Write(
            document,
            new(
                PackageChildrenTreePresentationFormat.Markdown,
                suppressNodes: false,
                collapseToolDependencies: false,
                duplicateLibraryNames:
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "example.dll",
                    }),
            output);

        Assert.Contains(
            "lib/net10.0/Example.dll",
            output.ToString(),
            StringComparison.Ordinal);
    }

    private static PackageChildrenTreePresentationPlan Plan(
        PackageChildrenTreePresentationFormat format) =>
        new(
            format,
            suppressNodes: false,
            collapseToolDependencies: false,
            duplicateLibraryNames: new HashSet<string>());

    private static PackageChildrenDocument Libraries(
        ImmutableArray<PackageLibraryChild> libraries) =>
        new(
            new(
                "Example.Package",
                "1.0.0",
                "net10.0",
                "NuGet"),
            PackageChildrenKind.Libraries,
            PackageChildrenStatus.Available,
            libraries,
            [],
            detail: null,
            isComplete: true);

    private static PackageLibraryChild Library(
        string path,
        string assemblyName,
        PackageLibraryChildRole role) =>
        new(
            Text(path),
            Text(path),
            Text(assemblyName),
            role);

    private static InertString Text(string value) =>
        new(TextPolicy.Field, value);
}
