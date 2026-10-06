using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Sections;

using ILInspector.Metadata;
using ILInspector.Research;

using QuerySpace.Composition;

namespace DotnetInspector.ResearchSections;

public static class LibraryNameFamilyInspection
{
    public static LibraryNameFamilyQueryResult Execute(
        ResolvedAssemblyReference assembly,
        AssemblyInspectionSession session,
        PdbSourceProvenanceOutcome? provenance,
        LibraryNameFamilyQueryPlan operation,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return LibraryNameFamilySummary.Execute(
                assembly,
                session,
                provenance: provenance) switch
            {
                LibraryNameFamilySummaryOutcome.Available available =>
                    ExecuteAvailable(
                        available.Document,
                        operation,
                        request),
                LibraryNameFamilySummaryOutcome.Unavailable unavailable =>
                    new LibraryNameFamilyQueryResult.Unavailable(
                        LibraryNameFamilyQueryUnavailableReason
                            .ProducerUnavailable,
                        unavailable.Detail,
                        Producer: unavailable),
                LibraryNameFamilySummaryOutcome.Rejected rejected =>
                    new LibraryNameFamilyQueryResult.Rejected(rejected),
                _ => throw new InvalidOperationException(
                    "Unknown Library name-family summary outcome."),
            };
        }
        catch (Exception error)
        {
            return new LibraryNameFamilyQueryResult.Failed(error);
        }
    }

    public static InspectionEnvelope<LibraryNameFamilyDocument> Envelope(
        LibraryNameFamilyQueryResult.Available available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return new(
            available.Document,
            new InspectionShare.NonProjectable(
                "library-name-families/share",
                "Inspect Web cannot yet restore an exact Library "
                    + "name-family inspection."),
            []);
    }

    private static LibraryNameFamilyQueryResult ExecuteAvailable(
        LibraryNameFamilyDocument document,
        LibraryNameFamilyQueryPlan operation,
        QuerySpaceRequest request)
    {
        LibraryNameFamilyPopulation? population =
            document.Populations.SingleOrDefault(
                candidate => candidate.Kind == operation.Population);
        if (population is null)
        {
            return new LibraryNameFamilyQueryResult.Unavailable(
                LibraryNameFamilyQueryUnavailableReason
                    .PopulationUnavailable,
                $"The '{LibraryNameFamilyQuery.PopulationToken(operation.Population)}' "
                    + "population is unavailable because source provenance "
                    + $"is {document.Provenance.State}.",
                Document: document,
                RequestedPopulation: operation.Population);
        }

        ImmutableArray<LibraryNameFamilyTypeRow> populationTypes =
            SelectTypes(document, population.Kind);
        if (populationTypes.Length != population.TypeCount)
        {
            throw new InvalidOperationException(
                "The selected Library name-family population and its "
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
                LibraryNameFamilyQuery.FamilyRowsScopeIdentity,
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
                LibraryNameFamilyQuery.TypeRowsScopeIdentity,
                StringComparison.Ordinal))
        {
            return Apply(
                TypeRows.Resolve(request, projection),
                projection,
                request,
                population,
                familyScope: false);
        }

        return new LibraryNameFamilyQueryResult.SelectionFailed(
            $"Unknown Library name-family row scope '{scope}'.");
    }

    private static LibraryNameFamilyQueryResult Apply(
        QuerySpaceSectionRowResolutionResult<Projection> resolution,
        Projection projection,
        QuerySpaceRequest request,
        LibraryNameFamilyPopulation population,
        bool familyScope)
    {
        if (!resolution.IsSuccess)
        {
            return new LibraryNameFamilyQueryResult.SelectionFailed(
                "The Library name-family row query could not be resolved.",
                resolution.Failure!.RowQueryFailure);
        }

        if (request.Terminal == QuerySpaceTerminalRequirement.Count)
        {
            SectionCountOutcome<string, string> count =
                QuerySpaceSectionRowExecutor.ApplyCount<
                    Projection,
                    string>(resolution.Request!);
            return count switch
            {
                SectionCountOutcome<string, string>.Completed completed =>
                    new LibraryNameFamilyQueryResult.Available(
                        projection.Document,
                        population,
                        request,
                        [],
                        [],
                        completed.Counts.Single().Value),
                SectionCountOutcome<string, string>.Semantic semantic =>
                    new LibraryNameFamilyQueryResult.SelectionFailed(
                        "The Library name-family Count selection exceeded "
                            + "the available rows.",
                        SemanticFailure:
                            new LibraryNameFamilySemanticSelectionFailure(
                            semantic.StageNumber,
                            semantic.RequiredPosition,
                            semantic.AvailableCount)),
                _ => throw new InvalidOperationException(
                    "Library name-family Count did not produce an exact "
                        + "completed or semantic outcome."),
            };
        }

        SectionRowsOutcome<string, Projection> rows =
            QuerySpaceSectionRowExecutor.ApplyRows(
                resolution.Request!);
        if (!rows.IsSuccess)
        {
            return new LibraryNameFamilyQueryResult.SelectionFailed(
                "The Library name-family row selection exceeded the "
                    + "available rows.",
                SemanticFailure:
                    new LibraryNameFamilySemanticSelectionFailure(
                        rows.Failure!.Failure.StageNumber,
                        rows.Failure.Failure.RequiredPosition,
                        rows.Failure.Failure.AvailableCount));
        }

        Projection selected = rows.Rebind(projection);
        return new LibraryNameFamilyQueryResult.Available(
            selected.Document,
            population,
            request,
            familyScope ? selected.Families : [],
            familyScope ? [] : selected.Types,
            Count: null);
    }

    private static ImmutableArray<LibraryNameFamilyTypeRow> SelectTypes(
        LibraryNameFamilyDocument document,
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

    private static ImmutableArray<LibraryNameFamilyTypeRow> SelectTypes(
        LibraryNameFamilyDocument document,
        PdbTypeSourceDisposition disposition) =>
        [
            .. document.Types.Where(
                row => row.SourceEvidence?.Disposition == disposition),
        ];

    private sealed record Projection(
        LibraryNameFamilyDocument Document,
        LibraryNameFamilyPopulation Population,
        ImmutableArray<LibraryNameFamilyRow> Families,
        ImmutableArray<LibraryNameFamilyTypeRow> Types);

    private static class FamilyRows
    {
        private static readonly SectionRowSchemaIdentity<
            LibraryNameFamilyRow> Schema =
                SectionRowSchemaIdentity<
                    LibraryNameFamilyRow>.Create();

        internal static QuerySpaceSectionRowResolutionResult<Projection>
            Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    LibraryNameFamilyRow>(
                        LibraryNameFamilyQuery.FamilyRowsRowSet,
                        Schema,
                        projection.Families,
                        static (current, rows) =>
                            current with
                            {
                                Families = [.. rows],
                            });
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryNameFamilyQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<
                    LibraryNameFamilyRow>(
                        LibraryNameFamilyQuery.FamilyRowsScope,
                        Schema));
        }
    }

    private static class TypeRows
    {
        private static readonly SectionRowSchemaIdentity<
            LibraryNameFamilyTypeRow> Schema =
                SectionRowSchemaIdentity<
                    LibraryNameFamilyTypeRow>.Create();

        internal static QuerySpaceSectionRowResolutionResult<Projection>
            Resolve(
                QuerySpaceRequest request,
                Projection projection)
        {
            var declaration =
                new SectionRowSetDeclaration<
                    string,
                    Projection,
                    LibraryNameFamilyTypeRow>(
                        LibraryNameFamilyQuery.TypeRowsRowSet,
                        Schema,
                        projection.Types,
                        static (current, rows) =>
                            current with
                            {
                                Types = [.. rows],
                            });
            return QuerySpaceSectionRowResolver.Resolve<Projection>(
                LibraryNameFamilyQuery.QuerySpace,
                request,
                [declaration],
                new SectionQuerySpaceRowScopeBinding<
                    LibraryNameFamilyTypeRow>(
                        LibraryNameFamilyQuery.TypeRowsScope,
                        Schema));
        }
    }
}
