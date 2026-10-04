using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(AutoFields = false)]
public sealed class PackagePairCallUseView
{
    [MarkoutSection(Name = "Direct Use Clusters")]
    public List<PackagePairDirectUseClusterViewRow> DirectUseClusters
    {
        get;
        init;
    } = [];

    [MarkoutSection(Name = "Library Pairs")]
    public List<PackagePairLibraryPairViewRow> LibraryPairs
    {
        get;
        init;
    } = [];

    [MarkoutSection(Name = "Call Sites")]
    public List<PackagePairCallSiteViewRow> CallSites { get; init; } = [];
}

[MarkoutSerializable]
public sealed class PackagePairDirectUseClusterViewRow
{
    public int Cluster { get; init; }
    public int LibraryPair { get; init; }
    public required string SourcePackage { get; init; }
    public required string SourceLibrary { get; init; }
    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid { get; init; }
    public required string AnchorSourceToken { get; init; }
    public required string TargetPackage { get; init; }
    public required string TargetLibrary { get; init; }
    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid { get; init; }
    public required string AnchorTargetToken { get; init; }
    public int SourceMembers { get; init; }
    public int ProviderTypes { get; init; }
    public int TargetMembers { get; init; }
    public int ExtensionMethods { get; init; }
    public int CallSites { get; init; }
}

[MarkoutSerializable]
public sealed class PackagePairLibraryPairViewRow
{
    public int LibraryPair { get; init; }
    public required string FirstPackage { get; init; }
    public required string FirstLibrary { get; init; }
    public required string SecondPackage { get; init; }
    public required string SecondLibrary { get; init; }
    public int Clusters { get; init; }
    public int CallSites { get; init; }
    public bool Complete { get; init; }
}

[MarkoutSerializable]
public sealed class PackagePairCallSiteViewRow
{
    public int LibraryPair { get; init; }
    public int Cluster { get; init; }
    public required string SourcePackage { get; init; }
    public required string SourceLibrary { get; init; }
    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid { get; init; }
    public required string SourceMember { get; init; }
    public required string SourceToken { get; init; }
    public required string TargetPackage { get; init; }
    public required string TargetLibrary { get; init; }
    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid { get; init; }
    public required string TargetMember { get; init; }
    public required string TargetToken { get; init; }
    public required string Call { get; init; }
    public required string EvidenceMethod { get; init; }
    public required string EvidenceToken { get; init; }
    [MarkoutPropertyName("IL Offset")]
    public required string IlOffset { get; init; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(PackagePairCallUseView))]
[MarkoutContext(typeof(PackagePairDirectUseClusterViewRow))]
[MarkoutContext(typeof(PackagePairLibraryPairViewRow))]
[MarkoutContext(typeof(PackagePairCallSiteViewRow))]
public partial class PackagePairCallUseViewContext : MarkoutSerializerContext
{
}
