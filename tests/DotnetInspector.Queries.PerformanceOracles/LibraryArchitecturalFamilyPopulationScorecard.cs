using System.Runtime.CompilerServices;

using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;

using ILInspector.Metadata;
using ILInspector.Research;

using Inspector.Artifacts;

using NLinq;

using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.PerformanceOracles;

public sealed record LibraryArchitecturalFamilyScorecardAsset(
    LibraryArchitecturalFamilyCompositionDocument Document,
    LibraryArchitecturalFamilyPopulation Population,
    LibraryArchitecturalFamilyQueryPlan Operation);

public static class LibraryArchitecturalFamilyPopulationScorecard
{
    public static IReadOnlyList<
        ScorecardAsset<LibraryArchitecturalFamilyScorecardAsset>> LoadAssets(
            IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names =
            ScorecardAssetNames.FromPaths(paths);
        var assets = new List<
            ScorecardAsset<LibraryArchitecturalFamilyScorecardAsset>>(
                paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            LibraryArchitecturalFamilyQueryPlan operation =
                LibraryArchitecturalFamilyQuery.CreatePlan(
                    LibraryNameFamilyPopulationKind.AllTypes);
            ResolvedAssemblyReference assembly =
                ArtifactBackedReference(paths[i]);
            using PdbContext context =
                PdbContext.OpenMetadataOnly(assembly);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Borrow(context);
            QuerySpaceRequest request =
                LibraryArchitecturalFamilyQuery.CreateFamilyRequest(
                    operation,
                    RowSelectionIntent<string>.Empty,
                    QuerySpaceTerminalRequirement.Rows);
            var available =
                LibraryArchitecturalFamilyInspection.Execute(
                    assembly,
                    session,
                    provenance: null,
                    operation,
                    request)
                as LibraryArchitecturalFamilyQueryResult.Available
                ?? throw new InvalidOperationException(
                    $"Family-role composition failed for '{paths[i]}'.");
            assets.Add(
                new(
                    names[i],
                    new(
                        available.Document,
                        available.Population,
                        operation)));
        }

        return assets;
    }

    public static ScorecardColumn<
        LibraryArchitecturalFamilyScorecardAsset,
        LibraryArchitecturalFamilyRow> LinqColumn(
            ScorecardShape shape) =>
        new(
            "LINQ",
            (closing, asset) =>
                LinqAnswer(shape, closing, asset));

    public static ScorecardColumn<
        LibraryArchitecturalFamilyScorecardAsset,
        LibraryArchitecturalFamilyRow> NLinqColumn(
            ScorecardShape shape) =>
        new(
            "NLinq",
            (closing, asset) =>
                NLinqAnswer(shape, closing, asset));

    public static ScorecardColumn<
        LibraryArchitecturalFamilyScorecardAsset,
        LibraryArchitecturalFamilyRow> QuerySpaceColumn(
            ScorecardShape shape) =>
        new(
            "QuerySpace",
            (closing, asset) =>
                QuerySpaceAnswer(shape, closing, asset));

    public static string RowText(
        LibraryArchitecturalFamilyRow row) =>
        $"{row.Identity.Kind}|"
            + $"{string.Join(row.Identity.Separator, row.Identity.Words)}|"
            + $"{row.TypeCount}|{row.DistinctNamespaceCount}";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScorecardAnswer<LibraryArchitecturalFamilyRow> LinqAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        LibraryArchitecturalFamilyScorecardAsset asset)
    {
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows =
            asset.Population.Families;
        return closing switch
        {
            ScorecardClosing.Count =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfCount(
                    Enumerable.Count(rows)),
            ScorecardClosing.Head =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    [.. rows.Take(shape.N)]),
            ScorecardClosing.Tail =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    [.. rows.TakeLast(shape.N)]),
            ScorecardClosing.Rows =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    Enumerable.ToArray(rows)),
            ScorecardClosing.Window =>
                rows.Count >= shape.WindowLast
                    ? ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                        [
                            .. rows.Skip(shape.WindowSkip)
                                .Take(shape.WindowTake),
                        ])
                    : ScorecardAnswer<LibraryArchitecturalFamilyRow>
                        .OfWindowFailure(),
            _ => throw Unsupported(closing),
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScorecardAnswer<LibraryArchitecturalFamilyRow> NLinqAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        LibraryArchitecturalFamilyScorecardAsset asset)
    {
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows =
            asset.Population.Families;
        return closing switch
        {
            ScorecardClosing.Count =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfCount(
                    CountNLinq(rows)),
            ScorecardClosing.Head =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    HeadNLinq(rows, shape.N)),
            ScorecardClosing.Tail =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    TailNLinq(rows, shape.N)),
            ScorecardClosing.Rows =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    RowsNLinq(rows)),
            ScorecardClosing.Window =>
                WindowNLinq(
                    rows,
                    shape.WindowSkip,
                    shape.WindowTake,
                    out List<LibraryArchitecturalFamilyRow> window)
                    ? ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                        window)
                    : ScorecardAnswer<LibraryArchitecturalFamilyRow>
                        .OfWindowFailure(),
            _ => throw Unsupported(closing),
        };
    }

    private static int CountNLinq(
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows) =>
        NLinqExtensions.Count<
            ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow>(rows.AsNLinq());

    private static List<LibraryArchitecturalFamilyRow> HeadNLinq(
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows,
        int count)
    {
        TakeEnumerator<
            ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow> taken =
                OracleOperators.Take<
                    ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
                    LibraryArchitecturalFamilyRow>(
                        rows.AsNLinq(),
                        count);
        return NLinqExtensions.ToList<
            TakeEnumerator<
                ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
                LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow>(taken);
    }

    private static List<LibraryArchitecturalFamilyRow> TailNLinq(
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows,
        int count) =>
        OracleOperators.TakeLast<
            ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow>(
                rows.AsNLinq(),
                count);

    private static List<LibraryArchitecturalFamilyRow> RowsNLinq(
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows) =>
        NLinqExtensions.ToList<
            ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow>(rows.AsNLinq());

    private static bool WindowNLinq(
        IReadOnlyList<LibraryArchitecturalFamilyRow> rows,
        int skip,
        int take,
        out List<LibraryArchitecturalFamilyRow> window)
    {
        SkipEnumerator<
            ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow> skipped =
                OracleOperators.Skip<
                    ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
                    LibraryArchitecturalFamilyRow>(
                        rows.AsNLinq(),
                        skip);
        return OracleOperators.TryTakeExactly<
            SkipEnumerator<
                ReadOnlyListEnumerator<LibraryArchitecturalFamilyRow>,
                LibraryArchitecturalFamilyRow>,
            LibraryArchitecturalFamilyRow>(
                skipped,
                take,
                out window);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScorecardAnswer<LibraryArchitecturalFamilyRow>
        QuerySpaceAnswer(
            ScorecardShape shape,
            ScorecardClosing closing,
            LibraryArchitecturalFamilyScorecardAsset asset)
    {
        QuerySpaceTerminalRequirement terminal =
            closing == ScorecardClosing.Count
                ? QuerySpaceTerminalRequirement.Count
                : QuerySpaceTerminalRequirement.Rows;
        RowSelectionIntent<string> rows = closing switch
        {
            ScorecardClosing.Count or ScorecardClosing.Rows =>
                RowSelectionIntent<string>.Empty,
            ScorecardClosing.Head =>
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Head(
                            shape.N),
                    ]),
            ScorecardClosing.Tail =>
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Tail(
                            shape.N),
                    ]),
            ScorecardClosing.Window =>
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Window(
                            shape.WindowFirst,
                            shape.WindowLast),
                    ]),
            _ => throw Unsupported(closing),
        };
        QuerySpaceRequest request =
            LibraryArchitecturalFamilyQuery.CreateFamilyRequest(
                asset.Operation,
                rows,
                terminal);
        return LibraryArchitecturalFamilyInspection.Execute(
            asset.Document,
            asset.Operation,
            request) switch
        {
            LibraryArchitecturalFamilyQueryResult.Available available
                when available.Count is int count =>
                    ScorecardAnswer<LibraryArchitecturalFamilyRow>
                        .OfCount(count),
            LibraryArchitecturalFamilyQueryResult.Available available =>
                ScorecardAnswer<LibraryArchitecturalFamilyRow>.OfRows(
                    available.FamilyRows),
            LibraryArchitecturalFamilyQueryResult.SelectionFailed
                {
                    SemanticFailure: not null,
                } when closing == ScorecardClosing.Window =>
                    ScorecardAnswer<LibraryArchitecturalFamilyRow>
                        .OfWindowFailure(),
            LibraryArchitecturalFamilyQueryResult result =>
                throw new InvalidOperationException(
                    "QuerySpace architectural-family scorecard failed with "
                        + result.GetType().Name),
        };
    }

    private static ResolvedAssemblyReference ArtifactBackedReference(
        string path)
    {
        string fullPath = Path.GetFullPath(path);
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                ScorecardArtifactProvenance.Instance,
                _ => File.OpenRead(fullPath));
        }
        authority.CreateRetainedContent(
            contribution.Registration,
            _ => File.OpenRead(fullPath));
        authority.CompleteAdmission(admission);
        return ResolvedAssemblyReference
            .CreateFromArtifactPathIfManaged(
                contribution.Registration,
                fullPath,
                () => File.OpenRead(fullPath),
                AssemblyResolutionProvenance.Local(
                    "architectural-family-scorecard"))
            ?? throw new BadImageFormatException(
                $"'{path}' has no managed metadata.");
    }

    private static ArgumentOutOfRangeException Unsupported(
        ScorecardClosing closing) =>
        new(
            nameof(closing),
            closing,
            "The architectural-family operation does not expose this terminal.");

    private sealed class ScorecardArtifactProvenance
        : IArtifactProvenance
    {
        internal static ScorecardArtifactProvenance Instance
        { get; } = new();
    }
}
