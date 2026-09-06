using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

public enum SearchPlatformFramework
{
    Runtime,
    AspNetCore,
    NetStandard,
}

public sealed class SearchSourceSelection
{
    internal SearchSourceSelection(
        SourceIntent intent,
        bool usesImplicitPlatform,
        SearchPlatformFramework[] frameworks,
        PackageCoordinate[] packages,
        SourceSelector[] otherSources)
    {
        Intent = intent;
        UsesImplicitPlatform = usesImplicitPlatform;
        Frameworks = Array.AsReadOnly(frameworks);
        Packages = Array.AsReadOnly(packages);
        OtherSources = Array.AsReadOnly(otherSources);
    }

    public SourceIntent Intent { get; }
    public bool UsesImplicitPlatform { get; }
    public IReadOnlyList<SearchPlatformFramework> Frameworks { get; }
    public IReadOnlyList<PackageCoordinate> Packages { get; }
    public IReadOnlyList<SourceSelector> OtherSources { get; }
}
