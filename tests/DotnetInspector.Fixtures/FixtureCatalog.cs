namespace DotnetInspector.Fixtures;

public sealed record FixtureDefinition(
    string Id,
    string ProjectName,
    string AssemblyFileName,
    IReadOnlyList<string> Tags)
{
    public string AssemblyPath() => FixtureCatalog.AssemblyPath(Id);
}

public sealed record FixturePair(string Id, FixtureDefinition Old, FixtureDefinition New)
{
    public string OldAssemblyPath() => Old.AssemblyPath();
    public string NewAssemblyPath() => New.AssemblyPath();
}

public sealed record FixtureGroup(string Id, IReadOnlyList<FixtureDefinition> Fixtures);

public static class FixtureIds
{
    public const string DiffV1 = "diff.v1";
    public const string DiffV2 = "diff.v2";
    public const string DecompilerClassicAsync = "decompiler.classic-async";
}

public static class FixtureCatalog
{
    public static readonly FixtureDefinition DiffV1 = new(
        FixtureIds.DiffV1,
        "DiffFixtures.V1",
        "DiffFixtureSample.dll",
        ["diff", "version-pair", "analysis", "decompiler", "rts-candidate"]);

    public static readonly FixtureDefinition DiffV2 = new(
        FixtureIds.DiffV2,
        "DiffFixtures.V2",
        "DiffFixtureSample.dll",
        ["diff", "version-pair", "analysis", "decompiler", "rts-candidate"]);

    public static readonly FixtureDefinition DecompilerClassicAsync = new(
        FixtureIds.DecompilerClassicAsync,
        "ILInspector.Decompiler.Fixtures.ClassicAsync",
        "ILInspector.Decompiler.Fixtures.ClassicAsync.dll",
        ["decompiler", "async", "classic-async", "rts-candidate"]);

    public static readonly FixturePair DiffPair = new("diff", DiffV1, DiffV2);

    public static readonly FixtureGroup ReturnToSenderCandidates = new(
        "rts.candidates",
        [DiffV1, DiffV2, DecompilerClassicAsync]);

    static readonly Dictionary<string, FixtureDefinition> s_byId = new(StringComparer.Ordinal)
    {
        [DiffV1.Id] = DiffV1,
        [DiffV2.Id] = DiffV2,
        [DecompilerClassicAsync.Id] = DecompilerClassicAsync,
    };

    public static FixtureDefinition Get(string id)
        => s_byId.TryGetValue(id, out var fixture)
            ? fixture
            : throw new ArgumentException($"Unknown fixture id '{id}'.", nameof(id));

    public static string AssemblyPath(string id)
    {
        var fixture = Get(id);
        string configuration = CurrentConfiguration();
        string root = RepositoryRoot();
        string path = Path.Combine(
            root,
            "artifacts",
            "bin",
            fixture.ProjectName,
            configuration,
            fixture.AssemblyFileName);

        if (File.Exists(path))
            return path;

        throw new FileNotFoundException(
            $"Expected built fixture '{fixture.Id}' at {path}. Run 'dotnet build dotnet-inspect.slnx -c Release' before running harnesses or tests that consume fixture binaries.",
            path);
    }

    static string CurrentConfiguration()
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return outputDirectory.Name;
    }

    static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'. Run tests from a dotnet-inspect checkout after building 'dotnet-inspect.slnx'.");
    }
}
