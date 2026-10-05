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

public sealed record LibraryFamilyRoleScorecardAsset(
    LibraryFamilyRoleCompositionDocument Document,
    LibraryFamilyRolePopulation Population,
    LibraryFamilyRoleQueryPlan Operation);

public static class LibraryFamilyRolePopulationScorecard
{
    public static IReadOnlyList<
        ScorecardAsset<LibraryFamilyRoleScorecardAsset>> LoadAssets(
            IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names =
            ScorecardAssetNames.FromPaths(paths);
        var assets = new List<
            ScorecardAsset<LibraryFamilyRoleScorecardAsset>>(
                paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            LibraryFamilyRoleQueryPlan operation =
                LibraryFamilyRoleQuery.CreatePlan(
                    LibraryNameFamilyPopulationKind.AllTypes);
            ResolvedAssemblyReference assembly =
                ArtifactBackedReference(paths[i]);
            using PdbContext context =
                PdbContext.OpenMetadataOnly(assembly);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Borrow(context);
            QuerySpaceRequest request =
                LibraryFamilyRoleQuery.CreateFamilyRequest(
                    operation,
                    RowSelectionIntent<string>.Empty,
                    QuerySpaceTerminalRequirement.Rows);
            var available =
                LibraryFamilyRoleInspection.Execute(
                    assembly,
                    session,
                    provenance: null,
                    operation,
                    request)
                as LibraryFamilyRoleQueryResult.Available
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
        LibraryFamilyRoleScorecardAsset,
        LibraryFamilyRoleRow> LinqColumn(
            ScorecardShape shape) =>
        new(
            "LINQ",
            (closing, asset) =>
                LinqAnswer(shape, closing, asset));

    public static ScorecardColumn<
        LibraryFamilyRoleScorecardAsset,
        LibraryFamilyRoleRow> NLinqColumn(
            ScorecardShape shape) =>
        new(
            "NLinq",
            (closing, asset) =>
                NLinqAnswer(shape, closing, asset));

    public static ScorecardColumn<
        LibraryFamilyRoleScorecardAsset,
        LibraryFamilyRoleRow> QuerySpaceColumn(
            ScorecardShape shape) =>
        new(
            "QuerySpace",
            (closing, asset) =>
                QuerySpaceAnswer(shape, closing, asset));

    public static string RowText(
        LibraryFamilyRoleRow row) =>
        $"{row.Identity.Kind}|"
            + $"{string.Join(row.Identity.Separator, row.Identity.Words)}|"
            + $"{row.TypeCount}|{row.DistinctNamespaceCount}";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScorecardAnswer<LibraryFamilyRoleRow> LinqAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        LibraryFamilyRoleScorecardAsset asset)
    {
        IReadOnlyList<LibraryFamilyRoleRow> rows =
            asset.Population.Families;
        return closing switch
        {
            ScorecardClosing.Count =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfCount(
                    rows.Count),
            ScorecardClosing.Head =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                    [.. rows.Take(shape.N)]),
            ScorecardClosing.Tail =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                    [.. rows.TakeLast(shape.N)]),
            ScorecardClosing.Rows =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(rows),
            ScorecardClosing.Window =>
                rows.Count >= shape.WindowLast
                    ? ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                        [
                            .. rows.Skip(shape.WindowSkip)
                                .Take(shape.WindowTake),
                        ])
                    : ScorecardAnswer<LibraryFamilyRoleRow>
                        .OfWindowFailure(),
            _ => throw Unsupported(closing),
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScorecardAnswer<LibraryFamilyRoleRow> NLinqAnswer(
        ScorecardShape shape,
        ScorecardClosing closing,
        LibraryFamilyRoleScorecardAsset asset)
    {
        IReadOnlyList<LibraryFamilyRoleRow> rows =
            asset.Population.Families;
        return closing switch
        {
            ScorecardClosing.Count =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfCount(
                    CountNLinq(rows)),
            ScorecardClosing.Head =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                    HeadNLinq(rows, shape.N)),
            ScorecardClosing.Tail =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                    TailNLinq(rows, shape.N)),
            ScorecardClosing.Rows =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                    RowsNLinq(rows)),
            ScorecardClosing.Window =>
                WindowNLinq(
                    rows,
                    shape.WindowSkip,
                    shape.WindowTake,
                    out List<LibraryFamilyRoleRow> window)
                    ? ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                        window)
                    : ScorecardAnswer<LibraryFamilyRoleRow>
                        .OfWindowFailure(),
            _ => throw Unsupported(closing),
        };
    }

    private static int CountNLinq(
        IReadOnlyList<LibraryFamilyRoleRow> rows) =>
        NLinqExtensions.Count<
            ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow>(rows.AsNLinq());

    private static List<LibraryFamilyRoleRow> HeadNLinq(
        IReadOnlyList<LibraryFamilyRoleRow> rows,
        int count)
    {
        TakeEnumerator<
            ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow> taken =
                OracleOperators.Take<
                    ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
                    LibraryFamilyRoleRow>(
                        rows.AsNLinq(),
                        count);
        return NLinqExtensions.ToList<
            TakeEnumerator<
                ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
                LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow>(taken);
    }

    private static List<LibraryFamilyRoleRow> TailNLinq(
        IReadOnlyList<LibraryFamilyRoleRow> rows,
        int count) =>
        OracleOperators.TakeLast<
            ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow>(
                rows.AsNLinq(),
                count);

    private static List<LibraryFamilyRoleRow> RowsNLinq(
        IReadOnlyList<LibraryFamilyRoleRow> rows) =>
        NLinqExtensions.ToList<
            ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow>(rows.AsNLinq());

    private static bool WindowNLinq(
        IReadOnlyList<LibraryFamilyRoleRow> rows,
        int skip,
        int take,
        out List<LibraryFamilyRoleRow> window)
    {
        SkipEnumerator<
            ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow> skipped =
                OracleOperators.Skip<
                    ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
                    LibraryFamilyRoleRow>(
                        rows.AsNLinq(),
                        skip);
        return OracleOperators.TryTakeExactly<
            SkipEnumerator<
                ReadOnlyListEnumerator<LibraryFamilyRoleRow>,
                LibraryFamilyRoleRow>,
            LibraryFamilyRoleRow>(
                skipped,
                take,
                out window);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScorecardAnswer<LibraryFamilyRoleRow>
        QuerySpaceAnswer(
            ScorecardShape shape,
            ScorecardClosing closing,
            LibraryFamilyRoleScorecardAsset asset)
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
            LibraryFamilyRoleQuery.CreateFamilyRequest(
                asset.Operation,
                rows,
                terminal);
        return LibraryFamilyRoleInspection.Execute(
            asset.Document,
            asset.Operation,
            request) switch
        {
            LibraryFamilyRoleQueryResult.Available available
                when available.Count is int count =>
                    ScorecardAnswer<LibraryFamilyRoleRow>
                        .OfCount(count),
            LibraryFamilyRoleQueryResult.Available available =>
                ScorecardAnswer<LibraryFamilyRoleRow>.OfRows(
                    available.FamilyRows),
            LibraryFamilyRoleQueryResult.SelectionFailed
                {
                    SemanticFailure: not null,
                } when closing == ScorecardClosing.Window =>
                    ScorecardAnswer<LibraryFamilyRoleRow>
                        .OfWindowFailure(),
            LibraryFamilyRoleQueryResult result =>
                throw new InvalidOperationException(
                    "QuerySpace family-role scorecard failed with "
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
                    "family-role-scorecard"))
            ?? throw new BadImageFormatException(
                $"'{path}' has no managed metadata.");
    }

    private static ArgumentOutOfRangeException Unsupported(
        ScorecardClosing closing) =>
        new(
            nameof(closing),
            closing,
            "The family-role operation does not expose this terminal.");

    private sealed class ScorecardArtifactProvenance
        : IArtifactProvenance
    {
        internal static ScorecardArtifactProvenance Instance
        { get; } = new();
    }
}
