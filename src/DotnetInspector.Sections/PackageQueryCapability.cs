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

    private static readonly string[] DependencyContinuationTerms =
    [
        PackageQuery.DependenciesTermKey,
        PackageQuery.DependencyDepthTermKey,
        PackageQuery.DependencyTargetTermKey,
        PackageQuery.DependsTermKey,
        PackageQuery.DependsTransitiveTermKey,
        PackageQuery.DependsEcosystemTermKey,
    ];

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
                        InspectionQueryTermRelationshipKind.RequiredContext,
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.DependsTransitiveTermKey),
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.DependencyTargetTermKey)),
                    new(
                        InspectionQueryTermRelationshipKind.RequiredContext,
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.DependsTransitiveTermKey),
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.DependencyDepthTermKey)),
                    new(
                        InspectionQueryTermRelationshipKind.RequiredContext,
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.DependencyDepthTermKey),
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.DependencyTargetTermKey)),
                    new(
                        InspectionQueryTermRelationshipKind
                            .RequiredContext,
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.LibraryLiteralTermKey),
                        PackageQuery.TermBindingIdentity(
                            PackageQuery.LibraryTargetTermKey)),
                ],
                queryTermRelatedOperations:
                [
                    .. DependencyContinuationTerms.Select(term =>
                        new InspectionQueryTermRelatedOperation(
                            PackageQuery.TermBindingIdentity(term),
                            PackageRelatedOperationAffordances
                                .InspectDependencies)),
                ],
                queryTermPresentations:
                [
                    .. PackageQuery.RegisteredTerms.Select((term, order) =>
                        new InspectionQueryTermPresentation(
                            PackageQuery.TermBindingIdentity(
                                term.Descriptor.Key),
                            order,
                            ControlKind(term.Descriptor.ControlKind),
                            PresentationKind(
                                term.Descriptor.PresentationKind),
                            PackageQuery.ExecutionClassIdentity(
                                term.Descriptor.ExecutionClass),
                            term.Descriptor.Options.Select(option =>
                                new InspectionQueryTermOption(
                                    option.Value,
                                    option.Label,
                                    option.Summary)),
                            term.Descriptor.SelectionGroupId,
                            term.Descriptor.CombinesWithinSelectionGroup,
                            term.Descriptor.ReplacementGroupId,
                            term.Descriptor.OptionSourceId)),
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
            ],
            routingDeclarations: static () => PackageQueryRoutingDeclarations.Declarations);

    private static string ControlKind(
        PackageQueryTermControlKind kind) =>
        kind switch
        {
            PackageQueryTermControlKind.Input => "input",
            PackageQueryTermControlKind.MultilineInput => "multiline-input",
            PackageQueryTermControlKind.Toggle => "toggle",
            PackageQueryTermControlKind.Choice => "choice",
            _ => throw new InvalidOperationException(
                "Unknown Package Query control kind."),
        };

    private static string PresentationKind(
        PackageQueryTermPresentationKind kind) =>
        kind switch
        {
            PackageQueryTermPresentationKind.Fact => "fact",
            PackageQueryTermPresentationKind.ValueQuery => "value-query",
            _ => throw new InvalidOperationException(
                "Unknown Package Query presentation kind."),
        };
}
