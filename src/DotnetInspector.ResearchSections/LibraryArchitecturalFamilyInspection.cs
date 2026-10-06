using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Sections;

using ILInspector.Metadata;
using ILInspector.Research;

using QuerySpace.Composition;

namespace DotnetInspector.ResearchSections;

public static class LibraryArchitecturalFamilyInspection
{
    public static LibraryArchitecturalFamilyQueryResult Execute(
        ResolvedAssemblyReference assembly,
        AssemblyInspectionSession session,
        PdbSourceProvenanceOutcome? provenance,
        LibraryArchitecturalFamilyQueryPlan operation,
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
                return new LibraryArchitecturalFamilyQueryResult
                    .NameFamiliesUnavailable(unavailable);
            }
            if (names
                is LibraryNameFamilySummaryOutcome.Rejected rejected)
            {
                return new LibraryArchitecturalFamilyQueryResult
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
                return new LibraryArchitecturalFamilyQueryResult
                    .StructuralRejected(structuralRejected);
            }
            LibraryStructuralSalienceDocument structuralDocument =
                ((LibrarySurfaceLeverageResult.AvailableExhaustive)
                    structure).Document;

            LibraryArchitecturalFamilyCompositionOutcome composition =
                LibraryArchitecturalFamilyComposition.Execute(
                    new(
                        nameDocument.Binding.Artifact,
                        nameDocument.Binding.Assembly,
                        nameDocument.Binding.ModuleVersionId),
                    nameDocument,
                    structuralDocument);
            if (composition
                is LibraryArchitecturalFamilyCompositionOutcome.Rejected
                    compositionRejected)
            {
                return new LibraryArchitecturalFamilyQueryResult
                    .CompositionRejected(compositionRejected);
            }

            return Execute(
                ((LibraryArchitecturalFamilyCompositionOutcome.Available)
                    composition).Document,
                operation,
                request);
        }
        catch (Exception error)
        {
            return new LibraryArchitecturalFamilyQueryResult.Failed(error);
        }
    }

    public static InspectionEnvelope<
        LibraryArchitecturalFamilyCompositionDocument> Envelope(
            LibraryArchitecturalFamilyQueryResult.Available available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return new(
            available.Document,
            new InspectionShare.NonProjectable(
                "architectural-families/share",
                "Inspect Web cannot yet restore an exact Library "
                    + "Architectural Families inspection."),
            []);
    }

    public static LibraryArchitecturalFamilyQueryResult Execute(
        LibraryArchitecturalFamilyCompositionDocument document,
        LibraryArchitecturalFamilyQueryPlan operation,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);

        LibraryArchitecturalFamilyPopulation? population =
            document.Populations.SingleOrDefault(
                candidate => candidate.Kind == operation.Population);
        if (population is null)
        {
            return new LibraryArchitecturalFamilyQueryResult.PopulationUnavailable(
                document,
                operation.Population);
        }

        ImmutableArray<LibraryArchitecturalFamilyTypeRow> populationTypes =
            SelectTypes(document, population.Kind);
        if (populationTypes.Length != population.TypeCount)
        {
            throw new InvalidOperationException(
                "The selected Architectural Families population and its "
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
                LibraryArchitecturalFamilyQuery.FamilyRowsScopeIdentity,
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
                LibraryArchitecturalFamilyQuery.TypeRowsScopeIdentity,
                StringComparison.Ordinal))
        {
            return Apply(
                TypeRows.Resolve(request, projection),
                projection,
                request,
                population,
                familyScope: false);
        }

        return new LibraryArchitecturalFamilyQueryResult.SelectionFailed(
            $"Unknown Architectural Families row scope '{scope}'.");
    }

    private static LibraryArchitecturalFamilyQueryResult Apply(
        QuerySpaceSectionRowResolutionResult<Projection> resolution,
        Projection projection,
        QuerySpaceRequest request,
        LibraryArchitecturalFamilyPopulation population,
        bool familyScope)
    {
        if (!resolution.IsSuccess)
        {
            return new LibraryArchitecturalFamilyQueryResult.SelectionFailed(
                "The Architectural Families row query could not be resolved.",
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
                    new LibraryArchitecturalFamilyQueryResult.SelectionFailed(
                        "The Architectural Families Count selection exceeded "
                            + "the available rows.",
                        SemanticFailure:
                            new LibraryArchitecturalFamilySemanticSelectionFailure(
                            semantic.StageNumber,
                            semantic.RequiredPosition,
                            semantic.AvailableCount)),
                _ => throw new InvalidOperationException(
                    "Architectural Families Count did not produce an exact "
                        + "completed or semantic outcome."),
            };
        }

        SectionRowsOutcome<string, Projection> rows =
            QuerySpaceSectionRowExecutor.ApplyRows(
                resolution.Request!);
        if (!rows.IsSuccess)
        {
            return new LibraryArchitecturalFamilyQueryResult.SelectionFailed(
                "The Architectural Families row selection exceeded the "
                    + "available rows.",
                SemanticFailure:
                    new LibraryArchitecturalFamilySemanticSelectionFailure(
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

    private static LibraryArchitecturalFamilyQueryResult.Available Available(
        Projection projection,
        LibraryArchitecturalFamilyPopulation population,
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

    private static ImmutableArray<LibraryArchitecturalFamilyTypeRow> SelectTypes(
        LibraryArchitecturalFamilyCompositionDocument document,
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

    private static ImmutableArray<LibraryArchitecturalFamilyTypeRow> SelectTypes(
        LibraryArchitecturalFamilyCompositionDocument document,
        PdbTypeSourceDisposition disposition) =>
        [
            .. document.Types.Where(
                row => row.SourceDisposition == disposition),
        ];

    private sealed record Projection(
        LibraryArchitecturalFamilyCompositionDocument Document,
        LibraryArchitecturalFamilyPopulation Population,
        ImmutableArray<LibraryArchitecturalFamilyRow> Families,
        ImmutableArray<LibraryArchitecturalFamilyTypeRow> Types);

    private static class FamilyRows
    {
        private static readonly SectionRowSchemaIdentity<
            LibraryArchitecturalFamilyRow> Schema =
                SectionRowSchemaIdentity<
                    LibraryArchitecturalFamilyRow>.Create();

        internal static QuerySpaceSectionRowResolutionResult<Projection>
            Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    LibraryArchitecturalFamilyRow>(
                        LibraryArchitecturalFamilyQuery.FamilyRowsRowSet,
                        Schema,
                        projection.Families,
                        static (current, rows) =>
                            current with
                            {
                                Families = [.. rows],
                            });
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryArchitecturalFamilyQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<
                    LibraryArchitecturalFamilyRow>(
                        LibraryArchitecturalFamilyQuery.FamilyRowsScope,
                        Schema));
        }
    }

    private static class TypeRows
    {
        private static readonly SectionRowSchemaIdentity<
            LibraryArchitecturalFamilyTypeRow> Schema =
                SectionRowSchemaIdentity<
                    LibraryArchitecturalFamilyTypeRow>.Create();

        internal static QuerySpaceSectionRowResolutionResult<Projection>
            Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    LibraryArchitecturalFamilyTypeRow>(
                        LibraryArchitecturalFamilyQuery.TypeRowsRowSet,
                        Schema,
                        projection.Types,
                        static (current, rows) =>
                            current with
                            {
                                Types = [.. rows],
                            });
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryArchitecturalFamilyQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<
                    LibraryArchitecturalFamilyTypeRow>(
                        LibraryArchitecturalFamilyQuery.TypeRowsScope,
                        Schema));
        }
    }
}
