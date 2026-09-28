using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Sections;

public sealed record PackageQueryInspectionRequest
{
    public PackageQueryInspectionRequest(
        IPackageSourceClient source,
        PackageQueryPlan plan,
        IPackageQueryContentProvider? contentProvider = null,
        PackageQueryDependencyTraversalServices? dependencyTraversalServices =
            null,
        PackageQueryAssemblySemanticExecution? assemblySemanticExecution =
            null,
        IPackageQueryNonterminalSink? nonterminalSink = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ContentProvider = contentProvider;
        DependencyTraversalServices = dependencyTraversalServices;
        AssemblySemanticExecution = assemblySemanticExecution;
        NonterminalSink = nonterminalSink;
    }

    public IPackageSourceClient Source { get; }

    public PackageQueryPlan Plan { get; }

    public IPackageQueryContentProvider? ContentProvider { get; }

    public PackageQueryDependencyTraversalServices?
        DependencyTraversalServices { get; }

    public PackageQueryAssemblySemanticExecution?
        AssemblySemanticExecution { get; }

    public IPackageQueryNonterminalSink? NonterminalSink { get; }
}

public static class PackageQueryCapability
{
    public const string DocumentIdentity =
        "package-query/document";
    public const string RouteIdentity =
        "package-query/route/default";
    public const string ProductModuleIdentity =
        "package-query/product";

    public static InspectionDocumentRegistration<PackageQueryDocument>
        Document { get; } =
            new(
                new(
                    DocumentIdentity,
                    "Package Query document",
                    "A completed package candidate query with matches, "
                    + "failures, and completion summary.",
                    PackageQuery.ResultContractIdentity));

    public static InspectionRouteRegistration<
        PackageQueryInspectionRequest,
        PackageQueryDocument> Route { get; } =
            new(
                new(
                    RouteIdentity,
                    "Package Query",
                    "Qualifies a package population through the executable "
                    + "Package Query space and returns one completed document."),
                Document,
                PackageQuery.QuerySpace,
                static (request, cancellationToken) =>
                    PackageQueryInspection.ExecuteAsync(
                        request.Source,
                        request.Plan,
                        request.ContentProvider,
                        request.DependencyTraversalServices,
                        request.AssemblySemanticExecution,
                        request.NonterminalSink,
                        cancellationToken),
                queryTermRelationships:
                [
                    new(
                        InspectionQueryTermRelationshipKind
                            .RequiredContext,
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.LibraryLiteralTermKey),
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.LibraryTargetTermKey)),
                ]);

    public static InspectionCapabilityModule ProductModule { get; } =
        new(
            ProductModuleIdentity,
            documents: [Document],
            routes: [Route],
            adoptionRequirements:
            [
                new(Route, InspectionConsumerKind.Cli),
                new(Route, InspectionConsumerKind.Browser),
            ]);
}
