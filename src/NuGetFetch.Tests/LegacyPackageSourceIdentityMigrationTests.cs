using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NuGetFetch.Tests;

public sealed class LegacyPackageSourceIdentityMigrationTests
{
    const string LegacyTypeName = "PackageSource" + "Identity";
    const string DescriptorTypeName = "PackageSource" + "Descriptor";
    const string IdentityPropertyName = "Identity";

    [Fact]
    public void LegacyPackageSourceIdentitySurfaceMatchesMigrationSet()
    {
        DirectoryInfo root = FindRepositoryRoot();
        MigrationEntry[] actual = DiscoverReferences(root);
        MigrationEntry[] expected =
        [
            new(
                "#4795",
                "src/NuGetFetch/PackageSourceClients.cs",
                ExplicitReferences: 14,
                ImplicitReferences: 1),
            new(
                "#4795",
                "src/NuGetFetch.Tests/PackageSourceClientTests.cs",
                ExplicitReferences: 13,
                ImplicitReferences: 2),
            new(
                "#4795",
                "src/NuGetFetch.Tests/PackageSourceCustomClientTests.cs",
                ExplicitReferences: 1,
                ImplicitReferences: 0),
            new(
                "#4795",
                "src/NuGetFetch.Tests/PackageSourceResultIdentityTests.cs",
                ExplicitReferences: 9,
                ImplicitReferences: 2),
            new(
                "#4797",
                "src/DotnetInspector.Packages/NuGetCredentialScope.cs",
                ExplicitReferences: 1,
                ImplicitReferences: 0),
            new(
                "#4797",
                "src/DotnetInspector.Packages/PackagePayloadAcquisition.cs",
                ExplicitReferences: 1,
                ImplicitReferences: 0),
            new(
                "#4797",
                "src/DotnetInspector.Services.Tests/PackagePayloadAcquisitionTests.cs",
                ExplicitReferences: 2,
                ImplicitReferences: 0),
            new(
                "#4805",
                "prototypes/inspect-web/engine.Core/BrowserPackageWorkspace.cs",
                ExplicitReferences: 12,
                ImplicitReferences: 0),
            new(
                "#4805",
                "prototypes/inspect-web/engine.Tests/BrowserEngineBoundaryTests.cs",
                ExplicitReferences: 17,
                ImplicitReferences: 0),
        ];

        string? error = MigrationSetError(expected, actual);
        Assert.True(error is null, error);
    }

    [Fact]
    public void LegacyMigrationSetComparisonRejectsInventoryMutations()
    {
        var enrolled = new MigrationEntry(
            "#4795",
            "src/enrolled.cs",
            ExplicitReferences: 1,
            ImplicitReferences: 1);
        var other = new MigrationEntry(
            "#4805",
            "src/other.cs",
            ExplicitReferences: 1,
            ImplicitReferences: 0);

        Assert.Null(MigrationSetError([enrolled], [enrolled]));
        Assert.Contains(
            "unlisted",
            MigrationSetError([enrolled], [enrolled, other]),
            StringComparison.Ordinal);
        Assert.Contains(
            "stale",
            MigrationSetError([enrolled, other], [enrolled]),
            StringComparison.Ordinal);
        Assert.Contains(
            "reference drift",
            MigrationSetError(
                [enrolled],
                [enrolled with { ExplicitReferences = 2 }]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyReferenceDiscoveryIncludesImplicitFormattingAndEquality()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"legacy-source-inventory-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(root, "src");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(root, "prototypes"));
        string descriptorType = DescriptorTypeName;
        string identityMember = "." + "Identity";
        string source = $$"""
            namespace NuGetFetch
            {
                sealed class {{descriptorType}}
                {
                    public object {{IdentityPropertyName}} => new();
                }
            }
            namespace Probe
            {
                using NuGetFetch;

                sealed class Consumer
                {
                    void Read({{descriptorType}} first, {{descriptorType}} second)
                    {
                        var alias = first;
                        _ = $"{alias{{identityMember}}}";
                        _ = alias{{identityMember}} == second{{identityMember}};
                        _ = "alias.Identity";
                        // alias.Identity is not an executable reader.
                    }
                }
            }
            """;

        try
        {
            File.WriteAllText(
                Path.Combine(sourceRoot, "AliasedDescriptorReader.cs"),
                source);

            MigrationEntry reader =
                Assert.Single(DiscoverReferences(new DirectoryInfo(root)));
            Assert.Equal(0, reader.ExplicitReferences);
            Assert.Equal(3, reader.ImplicitReferences);
            Assert.Contains(
                "unlisted",
                MigrationSetError([], [reader]),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static MigrationEntry[] DiscoverReferences(DirectoryInfo root)
    {
        string[] paths =
        [
            .. new[]
            {
                Path.Combine(root.FullName, "src"),
                Path.Combine(root.FullName, "prototypes"),
            }
                .SelectMany(path =>
                    Directory.EnumerateFiles(
                        path,
                        "*.cs",
                        SearchOption.AllDirectories))
                .Where(path => !IsBuildOutput(root.FullName, path))
                .Order(StringComparer.Ordinal),
        ];
        SyntaxTree[] trees =
        [
            .. paths.Select(path =>
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    CSharpParseOptions.Default
                        .WithLanguageVersion(LanguageVersion.Preview),
                    path)),
        ];
        CSharpCompilation compilation = CreateCompilation(trees);

        return
        [
            .. trees
                .Select(tree =>
                {
                    SyntaxNode syntax = tree.GetRoot();
                    SemanticModel semantics =
                        compilation.GetSemanticModel(tree);
                    return new MigrationEntry(
                        Issue: "",
                        Path.GetRelativePath(root.FullName, tree.FilePath)
                            .Replace('\\', '/'),
                        CountExplicitReferences(syntax),
                        CountImplicitReferences(syntax, semantics));
                })
                .Where(entry =>
                    entry.ExplicitReferences != 0
                    || entry.ImplicitReferences != 0)
                .OrderBy(entry => entry.Path, StringComparer.Ordinal),
        ];
    }

    static int CountExplicitReferences(SyntaxNode syntax) =>
        syntax.DescendantTokens()
            .Count(token =>
                token.IsKind(SyntaxKind.IdentifierToken)
                && token.ValueText == LegacyTypeName);

    static int CountImplicitReferences(
        SyntaxNode syntax,
        SemanticModel semantics) =>
        syntax.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Count(access =>
                access.Name.Identifier.ValueText
                    == IdentityPropertyName
                && semantics.GetSymbolInfo(access).Symbol
                    is IPropertySymbol
                    {
                        ContainingType.Name: DescriptorTypeName,
                        ContainingNamespace.Name: "NuGetFetch",
                        ContainingNamespace.ContainingNamespace
                            .IsGlobalNamespace: true,
                    });

    static CSharpCompilation CreateCompilation(
        IReadOnlyList<SyntaxTree> trees)
    {
        string trustedPlatformAssemblies =
            Assert.IsType<string>(
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        MetadataReference[] references =
        [
            .. trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path =>
                    MetadataReference.CreateFromFile(path)),
        ];
        return CSharpCompilation.Create(
            "LegacyPackageSourceIdentityMigrationInventory",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    static string? MigrationSetError(
        IReadOnlyList<MigrationEntry> expected,
        IReadOnlyList<MigrationEntry> actual)
    {
        Dictionary<string, MigrationEntry> expectedByPath =
            expected.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        Dictionary<string, MigrationEntry> actualByPath =
            actual.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        string[] unlisted =
        [
            .. actualByPath.Keys
                .Except(expectedByPath.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] stale =
        [
            .. expectedByPath.Keys
                .Except(actualByPath.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] drift =
        [
            .. expectedByPath.Keys
                .Intersect(actualByPath.Keys, StringComparer.Ordinal)
                .Where(path =>
                    expectedByPath[path].ExplicitReferences
                        != actualByPath[path].ExplicitReferences
                    || expectedByPath[path].ImplicitReferences
                        != actualByPath[path].ImplicitReferences)
                .Order(StringComparer.Ordinal)
                .Select(path =>
                    $"{path} expected "
                    + $"{expectedByPath[path].ExplicitReferences} explicit/"
                    + $"{expectedByPath[path].ImplicitReferences} implicit "
                    + $"for {expectedByPath[path].Issue}, observed "
                    + $"{actualByPath[path].ExplicitReferences} explicit/"
                    + $"{actualByPath[path].ImplicitReferences} implicit"),
        ];

        if (unlisted.Length == 0 && stale.Length == 0 && drift.Length == 0)
            return null;

        return "Legacy package-source identity migration inventory mismatch: "
            + $"unlisted [{string.Join(", ", unlisted)}]; "
            + $"stale [{string.Join(", ", stale)}]; "
            + $"reference drift [{string.Join("; ", drift)}].";
    }

    static bool IsBuildOutput(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.Split(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment is "bin" or "obj" or "artifacts");
    }

    static DirectoryInfo FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test binary.");
    }

    sealed record MigrationEntry(
        string Issue,
        string Path,
        int ExplicitReferences,
        int ImplicitReferences);
}
