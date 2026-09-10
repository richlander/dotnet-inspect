using DotnetInspector.Sections;
using InertText;
using Markout;

namespace DotnetInspector.Views;

/// <summary>The Markout document for <c>ecosystem</c>.</summary>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class EcosystemView
{
    [MarkoutIgnore]
    public string Title => "Ecosystems";

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description
    {
        get => field;
        init => field = EcosystemViewText.Optional(value)?.ToString();
    }

    [MarkoutPropertyName("Ecosystems")]
    public int EcosystemCount { get; init; }

    [MarkoutPropertyName("Platform Target")]
    [MarkoutSkipNull]
    public string? PlatformTarget
    {
        get => field;
        init => field = EcosystemViewText.Optional(value)?.ToString();
    }

    [MarkoutPropertyName("Subsumed Packages")]
    [MarkoutSkipDefault]
    public int SubsumedPackageCount { get; init; }

    [MarkoutSection(Name = EcosystemSections.Ecosystems)]
    public List<EcosystemEntryView>? Ecosystems { get; init; }

    [MarkoutSection(Name = EcosystemSections.Pruning)]
    public List<EcosystemPruningView>? Pruning { get; init; }
}

/// <summary>
/// The table-only projection of the same row views.
/// </summary>
/// <remarks>
/// <c>--table</c>, <c>--tsv</c>, and <c>--jsonl</c> carry exactly one row schema, so this wrapper
/// exposes the sections without the document summary fields.
/// </remarks>
[MarkoutSerializable]
public sealed class EcosystemTableView
{
    [MarkoutSection(Name = EcosystemSections.Ecosystems)]
    public List<EcosystemEntryView>? Ecosystems { get; init; }

    [MarkoutSection(Name = EcosystemSections.Pruning)]
    public List<EcosystemPruningView>? Pruning { get; init; }
}

/// <summary>Containment for values these views compose rather than read.</summary>
internal static class EcosystemViewText
{
    public static InertString Field(string? value) => new(TextPolicy.Field, value ?? "");

    public static InertString? Optional(string? value) => value is null ? null : Field(value);
}

/// <summary>One registered ecosystem.</summary>
[MarkoutSerializable]
public sealed class EcosystemEntryView
{
    public EcosystemEntryView(
        InertString id,
        InertString title,
        InertString summary,
        InertString packageSet,
        int corePackages,
        int toolPackages,
        int namespaceRoots,
        bool scanner,
        int demos)
    {
        IdText = id;
        TitleText = title;
        SummaryText = summary;
        PackageSetText = packageSet;
        CorePackages = corePackages;
        ToolPackages = toolPackages;
        NamespaceRoots = namespaceRoots;
        Scanner = scanner;
        Demos = demos;
    }

    internal static EcosystemEntryView From(EcosystemRow row) =>
        new(
            EcosystemViewText.Field(row.Id),
            EcosystemViewText.Field(row.Title),
            EcosystemViewText.Field(row.Summary),
            EcosystemViewText.Field(row.PackageSet ?? ""),
            row.CorePackages,
            row.ToolPackages,
            row.NamespaceRoots,
            row.HasScanner,
            row.Demos);

    [MarkoutIgnore] public InertString IdText { get; }
    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString SummaryText { get; }
    [MarkoutIgnore] public InertString PackageSetText { get; }

    public string Id => IdText.ToString();

    public string Title => TitleText.ToString();

    public string Summary => SummaryText.ToString();

    [MarkoutPropertyName("Package Set")]
    public string PackageSet => PackageSetText.ToString();

    [MarkoutPropertyName("Core")]
    public int CorePackages { get; }

    [MarkoutPropertyName("Tools")]
    public int ToolPackages { get; }

    [MarkoutPropertyName("Namespaces")]
    public int NamespaceRoots { get; }

    public bool Scanner { get; }

    public int Demos { get; }
}

/// <summary>One package identity the selected platform target subsumes.</summary>
[MarkoutSerializable]
public sealed class EcosystemPruningView
{
    public EcosystemPruningView(
        InertString package,
        InertString family,
        InertString supplied,
        InertString kind)
    {
        PackageText = package;
        FamilyText = family;
        SuppliedText = supplied;
        KindText = kind;
    }

    internal static EcosystemPruningView From(EcosystemPruningRow row) =>
        new(
            EcosystemViewText.Field(row.PackageId),
            EcosystemViewText.Field(row.Family),
            EcosystemViewText.Field(row.SuppliedVersion),
            // Live and Frozen name the two populations the design distinguishes: a frozen entry
            // is subsumed for any plausible request, a live one turns on the comparison.
            EcosystemViewText.Field(row.Live ? "Live" : "Frozen"));

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString FamilyText { get; }
    [MarkoutIgnore] public InertString SuppliedText { get; }
    [MarkoutIgnore] public InertString KindText { get; }

    public string Package => PackageText.ToString();

    public string Family => FamilyText.ToString();

    public string Supplied => SuppliedText.ToString();

    public string Kind => KindText.ToString();
}

[MarkoutContext(typeof(EcosystemView))]
[MarkoutContext(typeof(EcosystemTableView))]
[MarkoutContext(typeof(EcosystemEntryView))]
[MarkoutContext(typeof(EcosystemPruningView))]
public partial class EcosystemViewContext : MarkoutSerializerContext
{
}
