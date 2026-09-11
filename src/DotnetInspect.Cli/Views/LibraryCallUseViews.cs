using Markout;

namespace DotnetInspect.Cli.Views;

internal static class LibraryCallUseViewSections
{
    internal const string ConsumerUseSites = "Consumer Use Sites";
    internal const string ProviderApiTypes = "Provider API Types";
    internal const string CallSites = "Call Sites";
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseHeaderView
{
    [MarkoutIgnore]
    public string Title { get; init; } = "Library Call Use";

    [MarkoutIgnore]
    public required string Description { get; init; }

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
    public string Title { get; init; } =
        LibraryCallUseViewSections.ConsumerUseSites;

    [MarkoutIgnore]
    public required string Description { get; init; }

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
    public string Title { get; init; } =
        LibraryCallUseViewSections.ProviderApiTypes;

    [MarkoutIgnore]
    public required string Description { get; init; }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.ProviderApiTypes,
        Headless = true)]
    public List<LibraryCallUseProviderApiTypeRow>? Rows { get; init; }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public sealed class LibraryCallUseCallSitesView
{
    [MarkoutIgnore]
    public required string Title { get; init; }

    [MarkoutIgnore]
    public required string Description { get; init; }

    [MarkoutSection(
        Name = LibraryCallUseViewSections.CallSites,
        Headless = true)]
    public List<LibraryCallUseCallSiteRow>? Rows { get; init; }
}

[MarkoutSerializable(AutoFields = false)]
public sealed class LibraryCallUseSelectedView
{
    [MarkoutSection(Name = LibraryCallUseViewSections.ConsumerUseSites)]
    public List<LibraryCallUseConsumerUseSiteRow> ConsumerUseSites { get; init; } = [];

    [MarkoutSection(Name = LibraryCallUseViewSections.ProviderApiTypes)]
    public List<LibraryCallUseProviderApiTypeRow> ProviderApiTypes { get; init; } = [];

    [MarkoutSection(Name = LibraryCallUseViewSections.CallSites)]
    public List<LibraryCallUseCallSiteRow> CallSites { get; init; } = [];
}

[MarkoutSerializable]
public sealed class LibraryCallUseConsumerUseSiteRow
{
    public required string SourceLibrary { get; init; }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid { get; init; }

    public required string SourceMember { get; init; }
    public required string SourceToken { get; init; }
    public required string TargetLibrary { get; init; }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid { get; init; }

    public int ProviderTypes { get; init; }
    public int TargetMembers { get; init; }
    public int CallSites { get; init; }
    public required string CallSiteRows { get; init; }
}

[MarkoutSerializable]
public sealed class LibraryCallUseProviderApiTypeRow
{
    public required string SourceLibrary { get; init; }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid { get; init; }

    public required string TargetLibrary { get; init; }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid { get; init; }

    public required string TargetType { get; init; }
    public int SourceMembers { get; init; }
    public int TargetMembers { get; init; }
    public int CallSites { get; init; }
    public required string CallSiteRows { get; init; }
}

[MarkoutSerializable]
public sealed class LibraryCallUseCallSiteRow
{
    public required string SourceLibrary { get; init; }

    [MarkoutPropertyName("Source MVID")]
    public required string SourceMvid { get; init; }

    public required string SourceMember { get; init; }
    public required string SourceToken { get; init; }
    public required string TargetLibrary { get; init; }

    [MarkoutPropertyName("Target MVID")]
    public required string TargetMvid { get; init; }

    public required string TargetMember { get; init; }
    public required string TargetToken { get; init; }
    public required string Call { get; init; }
    public required string EvidenceMethod { get; init; }

    [MarkoutPropertyName("Evidence MVID")]
    public required string EvidenceMvid { get; init; }

    public required string EvidenceToken { get; init; }

    [MarkoutPropertyName("IL Offset")]
    public required string IlOffset { get; init; }

    public required string OperandToken { get; init; }
    public required string ExactTarget { get; init; }
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(LibraryCallUseHeaderView))]
[MarkoutContext(typeof(LibraryCallUseConsumerUseSitesView))]
[MarkoutContext(typeof(LibraryCallUseProviderApiTypesView))]
[MarkoutContext(typeof(LibraryCallUseCallSitesView))]
[MarkoutContext(typeof(LibraryCallUseSelectedView))]
[MarkoutContext(typeof(LibraryCallUseConsumerUseSiteRow))]
[MarkoutContext(typeof(LibraryCallUseProviderApiTypeRow))]
[MarkoutContext(typeof(LibraryCallUseCallSiteRow))]
public partial class LibraryCallUseViewContext : MarkoutSerializerContext
{
}
