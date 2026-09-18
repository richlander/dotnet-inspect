using Markout;

namespace DotnetInspect.Cli.Views;

internal static class LibraryCallUseViewSections
{
    internal const string ConsumerUseSites = "Consumer Use Sites";
    internal const string ProviderApiTypes = "Provider API Types";
    internal const string DirectUseClusters = "Direct Use Clusters";
    internal const string CallSites = "Call Sites";
    internal const string PublicRootPaths = "Public Root Paths";
}

internal static class LibraryCallUseViewText
{
    internal static string Contain(string value) =>
        LibraryViewText.Contain(value) ?? "";

    internal static string ContainDescription(string value) =>
        string.Join(
            "\n",
            value.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(Contain));
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseHeaderView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    } = LibraryCallUseViewText.Contain("Library Call Use");

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = LibraryCallUseViewText.ContainDescription(value);
    }

    [MarkoutSection(Name = "Header", Headless = true)]
    public List<MarkoutField>? Content => null;
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseConsumerUseSitesView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    } = LibraryCallUseViewText.Contain(
        LibraryCallUseViewSections.ConsumerUseSites);

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = LibraryCallUseViewText.ContainDescription(value);
    }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.ConsumerUseSites,
        Headless = true)]
    public List<LibraryCallUseConsumerUseSiteRow>? Rows { get; init; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseProviderApiTypesView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    } = LibraryCallUseViewText.Contain(
        LibraryCallUseViewSections.ProviderApiTypes);

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = LibraryCallUseViewText.ContainDescription(value);
    }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.ProviderApiTypes,
        Headless = true)]
    public List<LibraryCallUseProviderApiTypeRow>? Rows { get; init; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseDirectUseClustersView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    } = LibraryCallUseViewText.Contain(
        LibraryCallUseViewSections.DirectUseClusters);

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = LibraryCallUseViewText.ContainDescription(value);
    }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.DirectUseClusters,
        Headless = true)]
    public List<LibraryCallUseDirectUseClusterRow>? Rows { get; init; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseCallSitesView
{
    [MarkoutIgnore]
    public required string Title
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = LibraryCallUseViewText.ContainDescription(value);
    }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.CallSites,
        Headless = true)]
    public List<LibraryCallUseCallSiteRow>? Rows { get; init; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUsePublicRootPathsView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    } = LibraryCallUseViewText.Contain(
        LibraryCallUseViewSections.PublicRootPaths);

    [MarkoutIgnore]
    public required string Description
    {
        get => field;
        init => field = LibraryCallUseViewText.ContainDescription(value);
    }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.PublicRootPaths,
        Headless = true)]
    public List<LibraryCallUsePublicRootPathRow>? Rows { get; init; }
}

[MarkoutSerializable(AutoFields = false)]
public sealed class LibraryCallUseSelectedView
{
    [MarkoutSection(Name = LibraryCallUseViewSections.ConsumerUseSites)]
    public List<LibraryCallUseConsumerUseSiteRow> ConsumerUseSites { get; init; } = [];

    [MarkoutSection(Name = LibraryCallUseViewSections.ProviderApiTypes)]
    public List<LibraryCallUseProviderApiTypeRow> ProviderApiTypes { get; init; } = [];

    [MarkoutSection(Name = LibraryCallUseViewSections.DirectUseClusters)]
    public List<LibraryCallUseDirectUseClusterRow> DirectUseClusters { get; init; } = [];

    [MarkoutSection(Name = LibraryCallUseViewSections.CallSites)]
    public List<LibraryCallUseCallSiteRow> CallSites { get; init; } = [];

    [MarkoutSection(Name = LibraryCallUseViewSections.PublicRootPaths)]
    public List<LibraryCallUsePublicRootPathRow> PublicRootPaths { get; init; } = [];
}

[MarkoutSerializable]
public sealed class LibraryCallUseConsumerUseSiteRow
{
    public required string SourceLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string SourceMember
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string SourceToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public int ProviderTypes { get; init; }
    public int TargetMembers { get; init; }
    public int CallSites { get; init; }
    public required string CallSiteRows
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }
}

[MarkoutSerializable]
public sealed class LibraryCallUseProviderApiTypeRow
{
    public required string SourceLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetType
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public int SourceMembers { get; init; }
    public int TargetMembers { get; init; }
    public int CallSites { get; init; }
    public required string CallSiteRows
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }
}

[MarkoutSerializable]
public sealed class LibraryCallUseDirectUseClusterRow
{
    public required string SourceLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public int Cluster { get; init; }

    public required string Derivation
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string AnchorSourceToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string AnchorTargetToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public int SourceMembers { get; init; }
    public int ProviderTypes { get; init; }
    public int TargetMembers { get; init; }
    public int ExtensionMethods { get; init; }
    public int CallSites { get; init; }
    public required string CallSiteRows
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }
}

[MarkoutSerializable]
public sealed class LibraryCallUseCallSiteRow
{
    public required string SourceLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string SourceMember
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string SourceToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetMember
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string TargetToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string Call
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string EvidenceMethod
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("Evidence MVID")]
    public required string EvidenceMvid
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string EvidenceToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    [MarkoutPropertyName("IL Offset")]
    public required string IlOffset
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string OperandToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string ExactTarget
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }
}

[MarkoutSerializable]
public sealed class LibraryCallUsePublicRootPathRow
{
    public required string SourceLibrary
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public int Cluster { get; init; }

    public required string PublicRoot
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string PublicRootToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string DirectUseDestination
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string DestinationToken
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public int Depth { get; init; }

    public required string MethodPath
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }

    public required string PhysicalReceipts
    {
        get => field;
        init => field = LibraryCallUseViewText.Contain(value);
    }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(LibraryCallUseHeaderView))]
[MarkoutContext(typeof(LibraryCallUseConsumerUseSitesView))]
[MarkoutContext(typeof(LibraryCallUseProviderApiTypesView))]
[MarkoutContext(typeof(LibraryCallUseDirectUseClustersView))]
[MarkoutContext(typeof(LibraryCallUseCallSitesView))]
[MarkoutContext(typeof(LibraryCallUsePublicRootPathsView))]
[MarkoutContext(typeof(LibraryCallUseSelectedView))]
[MarkoutContext(typeof(LibraryCallUseConsumerUseSiteRow))]
[MarkoutContext(typeof(LibraryCallUseProviderApiTypeRow))]
[MarkoutContext(typeof(LibraryCallUseDirectUseClusterRow))]
[MarkoutContext(typeof(LibraryCallUseCallSiteRow))]
[MarkoutContext(typeof(LibraryCallUsePublicRootPathRow))]
public partial class LibraryCallUseViewContext : MarkoutSerializerContext
{
}
