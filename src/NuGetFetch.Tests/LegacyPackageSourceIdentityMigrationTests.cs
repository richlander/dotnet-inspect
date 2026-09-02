using System.Text.RegularExpressions;

namespace NuGetFetch.Tests;

public sealed class LegacyPackageSourceIdentityMigrationTests
{
    const string LegacyTypeName = "PackageSource" + "Identity";
    static readonly Regex LegacyTypeReference = new(
        $@"\b{LegacyTypeName}\b",
        RegexOptions.CultureInvariant);
    static readonly Regex DescriptorDeclaration = new(
        @"\bPackageSourceDescriptor\s*\??\s+([A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);
    static readonly Regex InferredDescriptorDeclaration = new(
        @"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*PackageSourceDescriptor\s*\.",
        RegexOptions.CultureInvariant);
    static readonly Regex DescriptorTypeReference = new(
        @"\bPackageSourceDescriptor\b",
        RegexOptions.CultureInvariant);
    static readonly Regex IdentityAccess = new(
        @"\.\s*Identity\b",
        RegexOptions.CultureInvariant);

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
                ExplicitReferences: 11,
                ImplicitReferences: 0),
            new(
                "#4805",
                "prototypes/inspect-web/engine.Tests/BrowserEngineBoundaryTests.cs",
                ExplicitReferences: 15,
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
        string descriptorType = "PackageSource" + "Descriptor";
        string identityMember = "." + "Identity";
        string source = $$"""
            {{descriptorType}} first = Create();
            {{descriptorType}} second = Create();
            _ = $"{first{{identityMember}}}";
            _ = first{{identityMember}} == second{{identityMember}};
            """;

        Assert.Equal(3, CountImplicitReferences(source));
    }

    static MigrationEntry[] DiscoverReferences(DirectoryInfo root)
    {
        string[] sourceRoots =
        [
            Path.Combine(root.FullName, "src"),
            Path.Combine(root.FullName, "prototypes"),
        ];

        return
        [
            .. sourceRoots
                .SelectMany(path =>
                    Directory.EnumerateFiles(
                        path,
                        "*.cs",
                        SearchOption.AllDirectories))
                .Where(path => !IsBuildOutput(root.FullName, path))
                .Select(path =>
                {
                    string source = File.ReadAllText(path);
                    return new MigrationEntry(
                        Issue: "",
                        Path.GetRelativePath(root.FullName, path)
                            .Replace('\\', '/'),
                        LegacyTypeReference.Matches(source).Count,
                        CountImplicitReferences(source));
                })
                .Where(entry =>
                    entry.ExplicitReferences != 0
                    || entry.ImplicitReferences != 0)
                .OrderBy(entry => entry.Path, StringComparer.Ordinal),
        ];
    }

    static int CountImplicitReferences(string source)
    {
        var positions = new HashSet<int>();
        for (int start = 0; start < source.Length;)
        {
            int terminator = source.IndexOf(';', start);
            int length = terminator < 0
                ? source.Length - start
                : terminator - start + 1;
            string statement = source.Substring(start, length);
            if (DescriptorTypeReference.IsMatch(statement))
            {
                foreach (Match access in IdentityAccess.Matches(statement))
                    positions.Add(start + access.Index);
            }
            start += length;
        }

        var descriptorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match declaration in DescriptorDeclaration.Matches(source))
            descriptorNames.Add(declaration.Groups[1].Value);
        foreach (Match declaration in
                 InferredDescriptorDeclaration.Matches(source))
        {
            descriptorNames.Add(declaration.Groups[1].Value);
        }

        foreach (string name in descriptorNames)
        {
            var access = new Regex(
                $@"\b{Regex.Escape(name)}\s*\.\s*Identity\b",
                RegexOptions.CultureInvariant);
            foreach (Match match in access.Matches(source))
                positions.Add(match.Index + match.Value.IndexOf('.'));
        }

        return positions.Count;
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
