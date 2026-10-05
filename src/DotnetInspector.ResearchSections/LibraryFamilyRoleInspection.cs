using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Sections;

using ILInspector.Metadata;
using ILInspector.Research;

using QuerySpace.Composition;

namespace DotnetInspector.ResearchSections;

public static class LibraryFamilyRoleInspection
{
    public static LibraryFamilyRoleQueryResult Execute(
        ResolvedAssemblyReference assembly,
        AssemblyInspectionSession session,
        PdbSourceProvenanceOutcome? provenance,
        LibraryFamilyRoleQueryPlan operation,
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            LibraryNameFamilySummaryOutcome names =
                LibraryNameFamilySummary.Execute(
                    assembly,
                    session,
                    provenance: provenance);
            if (names
                is LibraryNameFamilySummaryOutcome.Unavailable unavailable)
            {
                return new LibraryFamilyRoleQueryResult
                    .NameFamiliesUnavailable(unavailable);
            }
            if (names
                is LibraryNameFamilySummaryOutcome.Rejected rejected)
            {
                return new LibraryFamilyRoleQueryResult
                    .NameFamiliesRejected(rejected);
            }
            LibraryNameFamilyDocument nameDocument =
                ((LibraryNameFamilySummaryOutcome.Available)names).Document;

            LibrarySurfaceLeverageResult structure =
                AssemblyContextLibrarySurfaceLeverageQuery
                    .ExecuteExhaustive(
                        session,
                        cancellationToken);
            if (structure
                is LibrarySurfaceLeverageResult.Rejected
                    structuralRejected)
            {
                return new LibraryFamilyRoleQueryResult
                    .StructuralRejected(structuralRejected);
            }
            LibraryStructuralSalienceDocument structuralDocument =
                ((LibrarySurfaceLeverageResult.AvailableExhaustive)
                    structure).Document;

            LibraryFamilyRoleCompositionOutcome composition =
                LibraryFamilyRoleComposition.Execute(
                    new(
                        nameDocument.Binding.Artifact,
                        nameDocument.Binding.Assembly,
                        nameDocument.Binding.ModuleVersionId),
                    nameDocument,
                    structuralDocument);
            if (composition
                is LibraryFamilyRoleCompositionOutcome.Rejected
                    compositionRejected)
            {
                return new LibraryFamilyRoleQueryResult
                    .CompositionRejected(compositionRejected);
            }

            return Execute(
                ((LibraryFamilyRoleCompositionOutcome.Available)
                    composition).Document,
                operation,
                request);
        }
        catch (Exception error)
        {
            return new LibraryFamilyRoleQueryResult.Failed(error);
        }
    }

    public static InspectionEnvelope<
        LibraryFamilyRoleCompositionDocument> Envelope(
            LibraryFamilyRoleQueryResult.Available available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return new(
            available.Document,
            new InspectionShare.NonProjectable(
                "library-family-roles/share",
                "Inspect Web cannot yet restore an exact Library "
                    + "family-role inspection."),
            []);
    }

    public static LibraryFamilyRoleQueryResult Execute(
        LibraryFamilyRoleCompositionDocument document,
        LibraryFamilyRoleQueryPlan operation,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);

        LibraryFamilyRolePopulation? population =
            document.Populations.SingleOrDefault(
                candidate => candidate.Kind == operation.Population);
        if (population is null)
        {
            return new LibraryFamilyRoleQueryResult.PopulationUnavailable(
                document,
                operation.Population);
        }

        ImmutableArray<LibraryFamilyRoleTypeRow> populationTypes =
            SelectTypes(document, population.Kind);
        if (populationTypes.Length != population.TypeCount)
        {
            throw new InvalidOperationException(
                "The selected Library family-role population and its "
                    + "Type rows disagree.");
        }

        var projection = new Projection(
            document,
            population,
            population.Families,
            populationTypes);
        string scope = request.RowIntents.Single().Scope;
        if (string.Equals(
                scope,
                LibraryFamilyRoleQuery.FamilyRowsScopeIdentity,
                StringComparison.Ordinal))
        {
            return Apply(
                FamilyRows.Resolve(request, projection),
                projection,
                request,
                population,
                familyScope: true);
        }
        if (string.Equals(
                scope,
                LibraryFamilyRoleQuery.TypeRowsScopeIdentity,
                StringComparison.Ordinal))
        {
            return Apply(
                TypeRows.Resolve(request, projection),
                projection,
                request,
                population,
                familyScope: false);
        }

        return new LibraryFamilyRoleQueryResult.SelectionFailed(
            $"Unknown Library family-role row scope '{scope}'.");
    }

    private static LibraryFamilyRoleQueryResult Apply(
        QuerySpaceSectionRowResolutionResult<Projection> resolution,
        Projection projection,
        QuerySpaceRequest request,
        LibraryFamilyRolePopulation population,
        bool familyScope)
    {
        if (!resolution.IsSuccess)
        {
            return new LibraryFamilyRoleQueryResult.SelectionFailed(
                "The Library family-role row query could not be resolved.",
                resolution.Failure!.RowQueryFailure);
        }

        int total = familyScope
            ? projection.Families.Length
            : projection.Types.Length;
        if (request.Terminal == QuerySpaceTerminalRequirement.Count)
        {
            SectionCountOutcome<string, string> count =
                QuerySpaceSectionRowExecutor.ApplyCount<
                    Projection,
                    string>(resolution.Request!);
            return count switch
            {
                SectionCountOutcome<string, string>.Completed completed =>
                    Available(
                        familyScope
                            ? projection with { Families = [] }
                            : projection with { Types = [] },
                        population,
                        request,
                        familyScope,
                        total,
                        completed.Counts.Single().Value,
                        completed.Counts.Single().Value),
                SectionCountOutcome<string, string>.Semantic semantic =>
                    new LibraryFamilyRoleQueryResult.SelectionFailed(
                        "The Library family-role Count selection exceeded "
                            + "the available rows.",
                        SemanticFailure:
                            new LibraryFamilyRoleSemanticSelectionFailure(
                            semantic.StageNumber,
                            semantic.RequiredPosition,
                            semantic.AvailableCount)),
                _ => throw new InvalidOperationException(
                    "Library family-role Count did not produce an exact "
                        + "completed or semantic outcome."),
            };
        }

        SectionRowsOutcome<string, Projection> rows =
            QuerySpaceSectionRowExecutor.ApplyRows(
                resolution.Request!);
        if (!rows.IsSuccess)
        {
            return new LibraryFamilyRoleQueryResult.SelectionFailed(
                "The Library family-role row selection exceeded the "
                    + "available rows.",
                SemanticFailure:
                    new LibraryFamilyRoleSemanticSelectionFailure(
                        rows.Failure!.Failure.StageNumber,
                        rows.Failure.Failure.RequiredPosition,
                        rows.Failure.Failure.AvailableCount));
        }

        Projection selected = rows.Rebind(projection);
        int selectedCount = familyScope
            ? selected.Families.Length
            : selected.Types.Length;
        return Available(
            selected,
            population,
            request,
            familyScope,
            total,
            selectedCount,
            Count: null);
    }

    private static LibraryFamilyRoleQueryResult.Available Available(
        Projection projection,
        LibraryFamilyRolePopulation population,
        QuerySpaceRequest request,
        bool familyScope,
        int total,
        int selected,
        int? Count) =>
        new(
            projection.Document,
            population,
            request,
            familyScope ? projection.Families : [],
            familyScope ? [] : projection.Types,
            total,
            selected,
            Count);

    private static ImmutableArray<LibraryFamilyRoleTypeRow> SelectTypes(
        LibraryFamilyRoleCompositionDocument document,
        LibraryNameFamilyPopulationKind population) =>
        population switch
        {
            LibraryNameFamilyPopulationKind.AllTypes => document.Types,
            LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly =>
                SelectTypes(
                    document,
                    PdbTypeSourceDisposition.OrdinaryEvidenceOnly),
            LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly =>
                SelectTypes(
                    document,
                    PdbTypeSourceDisposition.GeneratedEvidenceOnly),
            LibraryNameFamilyPopulationKind.MixedEvidence =>
                SelectTypes(
                    document,
                    PdbTypeSourceDisposition.MixedEvidence),
            LibraryNameFamilyPopulationKind.Unknown =>
                SelectTypes(
                    document,
                    PdbTypeSourceDisposition.Unknown),
            _ => throw new ArgumentOutOfRangeException(
                nameof(population)),
        };

    private static ImmutableArray<LibraryFamilyRoleTypeRow> SelectTypes(
        LibraryFamilyRoleCompositionDocument document,
        PdbTypeSourceDisposition disposition) =>
        [
            .. document.Types.Where(
                row => row.SourceDisposition == disposition),
        ];

    private sealed record Projection(
        LibraryFamilyRoleCompositionDocument Document,
        LibraryFamilyRolePopulation Population,
        ImmutableArray<LibraryFamilyRoleRow> Families,
        ImmutableArray<LibraryFamilyRoleTypeRow> Types);

    private static class FamilyRows
    {
        private static readonly SectionRowSchemaIdentity<
            LibraryFamilyRoleRow> Schema =
                SectionRowSchemaIdentity<
                    LibraryFamilyRoleRow>.Create();

        internal static QuerySpaceSectionRowResolutionResult<Projection>
            Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    LibraryFamilyRoleRow>(
                        LibraryFamilyRoleQuery.FamilyRowsRowSet,
                        Schema,
                        projection.Families,
                        static (current, rows) =>
                            current with
                            {
                                Families = [.. rows],
                            });
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryFamilyRoleQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<
                    LibraryFamilyRoleRow>(
                        LibraryFamilyRoleQuery.FamilyRowsScope,
                        Schema));
        }
    }

    private static class TypeRows
    {
        private static readonly SectionRowSchemaIdentity<
            LibraryFamilyRoleTypeRow> Schema =
                SectionRowSchemaIdentity<
                    LibraryFamilyRoleTypeRow>.Create();

        internal static QuerySpaceSectionRowResolutionResult<Projection>
            Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    LibraryFamilyRoleTypeRow>(
                        LibraryFamilyRoleQuery.TypeRowsRowSet,
                        Schema,
                        projection.Types,
                        static (current, rows) =>
                            current with
                            {
                                Types = [.. rows],
                            });
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryFamilyRoleQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<
                    LibraryFamilyRoleTypeRow>(
                        LibraryFamilyRoleQuery.TypeRowsScope,
                        Schema));
        }
    }
}
