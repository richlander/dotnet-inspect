using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public abstract record AnnotatedTypeRelationsCallInspectionResult
{
    private AnnotatedTypeRelationsCallInspectionResult()
    {
    }

    public sealed record Available(
        AnnotatedTypeRelationsRequestResult.Accepted Resolution,
        InspectionEnvelope<AnnotatedTypeRelationsDocument> Inspection)
        : AnnotatedTypeRelationsCallInspectionResult;

    public sealed record Rejected(
        AnnotatedTypeRelationsRequestFailure Failure)
        : AnnotatedTypeRelationsCallInspectionResult;
}

public static class AnnotatedTypeRelationsCallInspection
{
    private static readonly AnnotatedTypeRelationsCatalog RelationsCatalog =
        AnnotatedTypeRelationsCallAdoption.Register(
            new AnnotatedTypeRelationsRegistry()).Compile();

    private static readonly InspectionQueryCatalog<
        AnnotatedTypeRelationsCallRequest> QueryCatalog =
            new InspectionQueryRegistry<AnnotatedTypeRelationsCallRequest>()
                .Add(
                    AnnotatedTypeRelationsCallAdoption.RowsQuery,
                    AnnotatedTypeRelationsCallAdoption.Rows)
                .Add(
                    AnnotatedTypeRelationsCallAdoption.CountQuery,
                    AnnotatedTypeRelationsCallAdoption.Count)
                .Add(
                    AnnotatedTypeRelationsCallAdoption.ExistsQuery,
                    AnnotatedTypeRelationsCallAdoption.Exists)
                .Compile();

    public static AnnotatedTypeRelationsCallInspectionResult Execute(
        AssemblyContextGroup group,
        AssemblyAcquisitionRegistration focusAssembly,
        MetadataTypeDefinitionName focusType,
        SubjectRelationPopulationAuthority population,
        AnnotatedTypeRelationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(focusAssembly);
        ArgumentNullException.ThrowIfNull(focusType);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        AnnotatedTypeRelationsRequestResult resolution =
            AnnotatedTypeRelationsRequestResolver.Resolve(
                RelationsCatalog,
                request,
                QueryCatalog);
        if (resolution is AnnotatedTypeRelationsRequestResult.Rejected rejected)
        {
            return new AnnotatedTypeRelationsCallInspectionResult.Rejected(
                rejected.Failure);
        }

        var accepted =
            (AnnotatedTypeRelationsRequestResult.Accepted)resolution;
        AssemblyContextTypeCallCompatibilityResult evidence =
            AssemblyContextTypeCallCompatibilityQuery.Execute(
                group,
                focusAssembly,
                focusType,
                cancellationToken);
        var callRequest = new AnnotatedTypeRelationsCallRequest(
            request.Focus,
            population,
            evidence,
            accepted.Plan.Colors.Select(
                registration => registration.Color),
            accepted.Plan.Request.RequiredColors);
        InspectionQueryResults results =
            QueryCatalog.Run(accepted.Plan.Queries, callRequest);
        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                accepted.Plan,
                results);
        var inspection =
            new InspectionEnvelope<AnnotatedTypeRelationsDocument>(
                document,
                new InspectionShare.NonProjectable(
                    "annotated-type-relations/share",
                    "Annotated Type Relations requests do not yet have a "
                        + "canonical Workspace Share projection."));
        return new AnnotatedTypeRelationsCallInspectionResult.Available(
            accepted,
            inspection);
    }
}
