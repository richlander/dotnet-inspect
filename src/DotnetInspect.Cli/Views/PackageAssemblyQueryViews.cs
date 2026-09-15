using InertText;
using Markout;

namespace DotnetInspect.Cli.Views;

/// <summary>
/// The rendered result of one assembly-semantic decoded-literal Find
/// operation over a bounded package population.
/// </summary>
/// <remarks>
/// Matches lead in expanded output, and every candidate keeps its own row so a semantic miss, an
/// inapplicable candidate, and a failure stay distinguishable from one another
/// and from a match. Each evaluated candidate carries the owner-issued Root
/// reopening token, which is the only value a host may hand back to
/// <c>workspace --root-request</c>.
/// </remarks>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class PackageAssemblyQueryView
{
    public PackageAssemblyQueryView(
        InertString title,
        InertString description)
    {
        TitleText = title;
        DescriptionText = description;
    }

    [MarkoutIgnore] public InertString TitleText { get; }
    [MarkoutIgnore] public InertString DescriptionText { get; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] public string Description => DescriptionText.ToString();

    [MarkoutIgnore] public int CandidateCount { get; init; }
    [MarkoutIgnore] public int MatchedCandidateCount { get; init; }
    [MarkoutIgnore] public int SemanticMissCount { get; init; }
    [MarkoutIgnore] public int NotApplicableCount { get; init; }
    [MarkoutIgnore] public int FailureCount { get; init; }
    [MarkoutIgnore] public int PopulationFailureCount { get; init; }
    [MarkoutIgnore] public bool IsComplete { get; init; }

    [MarkoutSection(Name = "Matches")]
    [MarkoutSkipNull]
    public List<PackageAssemblyLiteralUseRow>? Matches { get; init; }

    [MarkoutSection(Name = "Candidates")]
    [MarkoutSkipNull]
    public List<PackageAssemblyCandidateRow>? Candidates { get; init; }

    [MarkoutSection(Name = "Population Failures")]
    [MarkoutSkipNull]
    public List<PackageAssemblyPopulationFailureRow>? PopulationFailures
        { get; init; }
}

/// <summary>One decoded <c>ldstr</c> occurrence that contains the operand.</summary>
[MarkoutSerializable]
public sealed class PackageAssemblyLiteralUseRow
{
    public PackageAssemblyLiteralUseRow(
        string package,
        string version,
        string assembly,
        string method,
        string offset,
        string literal)
        : this(
            PackageAssemblyQueryText.Cell(package),
            PackageAssemblyQueryText.Cell(version),
            PackageAssemblyQueryText.Cell(assembly),
            PackageAssemblyQueryText.Cell(method),
            PackageAssemblyQueryText.Cell(offset),
            PackageAssemblyQueryText.Cell(literal))
    {
    }

    internal PackageAssemblyLiteralUseRow(
        InertString package,
        InertString version,
        InertString assembly,
        InertString method,
        InertString offset,
        InertString literal)
    {
        PackageText = package;
        VersionText = version;
        AssemblyText = assembly;
        MethodText = method;
        OffsetText = offset;
        LiteralText = literal;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString AssemblyText { get; }
    [MarkoutIgnore] public InertString MethodText { get; }
    [MarkoutIgnore] public InertString OffsetText { get; }
    [MarkoutIgnore] public InertString LiteralText { get; }

    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Assembly => AssemblyText.ToString();
    public string Method => MethodText.ToString();
    public string Offset => OffsetText.ToString();
    public string Literal => LiteralText.ToString();
}

/// <summary>One admitted package candidate and its evaluation outcome.</summary>
[MarkoutSerializable]
public sealed class PackageAssemblyCandidateRow
{
    public PackageAssemblyCandidateRow(
        string package,
        string version,
        string outcome,
        string asset,
        string targetFramework,
        string detail,
        string root)
        : this(
            PackageAssemblyQueryText.Cell(package),
            PackageAssemblyQueryText.Cell(version),
            PackageAssemblyQueryText.Cell(outcome),
            PackageAssemblyQueryText.Cell(asset),
            PackageAssemblyQueryText.Cell(targetFramework),
            PackageAssemblyQueryText.Cell(detail),
            PackageAssemblyQueryText.Cell(root))
    {
    }

    internal PackageAssemblyCandidateRow(
        InertString package,
        InertString version,
        InertString outcome,
        InertString asset,
        InertString targetFramework,
        InertString detail,
        InertString root)
    {
        PackageText = package;
        VersionText = version;
        OutcomeText = outcome;
        AssetText = asset;
        TargetFrameworkText = targetFramework;
        DetailText = detail;
        RootText = root;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString OutcomeText { get; }
    [MarkoutIgnore] public InertString AssetText { get; }
    [MarkoutIgnore] public InertString TargetFrameworkText { get; }
    [MarkoutIgnore] public InertString DetailText { get; }
    [MarkoutIgnore] public InertString RootText { get; }

    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Outcome => OutcomeText.ToString();
    public string Asset => AssetText.ToString();
    [MarkoutPropertyName("TFM")]
    public string TargetFramework => TargetFrameworkText.ToString();
    public string Detail => DetailText.ToString();
    public string Root => RootText.ToString();
}

/// <summary>
/// One failure observed while forming the requested bounded package
/// population.
/// </summary>
[MarkoutSerializable]
public sealed class PackageAssemblyPopulationFailureRow
{
    public PackageAssemblyPopulationFailureRow(
        string candidate,
        string package,
        string version,
        string authority,
        string kind,
        string detail)
        : this(
            PackageAssemblyQueryText.Cell(candidate),
            PackageAssemblyQueryText.Cell(package),
            PackageAssemblyQueryText.Cell(version),
            PackageAssemblyQueryText.Cell(authority),
            PackageAssemblyQueryText.Cell(kind),
            PackageAssemblyQueryText.Cell(detail))
    {
    }

    internal PackageAssemblyPopulationFailureRow(
        InertString candidate,
        InertString package,
        InertString version,
        InertString authority,
        InertString kind,
        InertString detail)
    {
        CandidateText = candidate;
        PackageText = package;
        VersionText = version;
        AuthorityText = authority;
        KindText = kind;
        DetailText = detail;
    }

    [MarkoutIgnore] public InertString CandidateText { get; }
    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString AuthorityText { get; }
    [MarkoutIgnore] public InertString KindText { get; }
    [MarkoutIgnore] public InertString DetailText { get; }

    public string Candidate => CandidateText.ToString();
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Authority => AuthorityText.ToString();
    public string Kind => KindText.ToString();
    public string Detail => DetailText.ToString();
}

internal static class PackageAssemblyQueryText
{
    internal static InertString Cell(string value) =>
        new(TextPolicy.Field, value);
}
