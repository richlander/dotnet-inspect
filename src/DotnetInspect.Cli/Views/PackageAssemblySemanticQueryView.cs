using DotnetInspector.PackageQueries;
using ILInspector.Analysis;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description))]
public sealed class PackageAssemblySemanticQueryView
{
    [MarkoutIgnore] public required InertString TitleText { get; init; }
    [MarkoutIgnore] public required InertString DescriptionText { get; init; }
    [MarkoutIgnore] public string Title => TitleText.ToString();
    [MarkoutIgnore] public string Description => DescriptionText.ToString();

    [MarkoutSection(Name = "Packages")]
    public required List<PackageAssemblySemanticQueryRow> Results { get; init; }
}

[MarkoutSerializable]
public sealed class PackageAssemblySemanticQueryRow
{
    public PackageAssemblySemanticQueryRow(
        PackageAssemblySemanticQueryResult result)
        : this(
            new InertString(
                TextPolicy.Field,
                result.Coordinate.PackageId),
            new InertString(
                TextPolicy.Field,
                result.Coordinate.Version),
            result.SelectedAsset.Asset.Path,
            result.Occurrences.Length,
            new InertString(
                TextPolicy.Field,
                string.Join(
                    ", ",
                    result.Occurrences
                        .Take(3)
                        .Select(Describe))),
            new InertString(
                TextPolicy.Field,
                result.RootRequest.Encode()))
    {
    }

    public PackageAssemblySemanticQueryRow(
        InertString package,
        InertString version,
        InertString library,
        int occurrenceCount,
        InertString evidence,
        InertString root)
    {
        if (occurrenceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(occurrenceCount));

        PackageText = package;
        VersionText = version;
        LibraryText = library;
        OccurrenceCount = occurrenceCount;
        EvidenceText = evidence;
        RootText = root;
    }

    [MarkoutIgnore] public InertString PackageText { get; }
    [MarkoutIgnore] public InertString VersionText { get; }
    [MarkoutIgnore] public InertString LibraryText { get; }
    [MarkoutIgnore] public InertString EvidenceText { get; }
    [MarkoutIgnore] public InertString RootText { get; }
    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Library => LibraryText.ToString();
    public int Occurrences => OccurrenceCount;
    [MarkoutIgnore] public int OccurrenceCount { get; }
    public string Evidence => EvidenceText.ToString();
    public string Root => RootText.ToString();

    private static string Describe(StringLiteralUseOccurrence occurrence) =>
        $"0x{occurrence.Address.MethodDefinitionToken:X8}/IL_{occurrence.Address.ILOffset:X4}";
}
