using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web;

internal static class BrowserExternalPackageWorkspaceRequestFactory
{
    const int SchemaVersion = InspectionDefinitionSchema.Version3;
    const string WorkspaceId = "external-package-workspace";
    const string ContextId = "external-package-context";
    const string NavigationId = "external-package-navigation";
    const string PackageNavigationId = "external-package";
    const string ViewId = "external-package-view";
    const string ScenarioId = "external-package-scenario";

    internal static BrowserRetainedWorkspaceActivationRequest Create(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        PackageSourceCoordinate package,
        WorkspacePlan curatedPlan)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(curatedPlan);
        if (!curatedPlan.Contexts.IsEmpty)
        {
            throw new ArgumentException(
                "The external Package curated plan must contain registrations only.",
                nameof(curatedPlan));
        }
        if (!ReferenceEquals(
                curatedPlan.TraversalTargetPolicy,
                TraversalTargetFrameworkPolicy.ProductDefault))
        {
            throw new ArgumentException(
                "The external Package curated plan must retain the product-default traversal target policy.",
                nameof(curatedPlan));
        }

        string framework =
            curatedPlan.TraversalTargetPolicy.TargetFramework;
        var coordinate = new DefinitionMemberCoordinate.PackageCoordinate(
            package.PackageId,
            package.Version,
            framework);
        InspectionDefinitionRecord[] records =
        [
            new WorkspaceDefinition(
                SchemaVersion,
                WorkspaceId,
                [
                    new WorkspaceContextDefinition(
                        ContextId,
                        framework,
                        members: [coordinate]),
                ],
                title: label,
                registrations: curatedPlan.Registrations),
            new CommittedNavigationDefinition(
                SchemaVersion,
                NavigationId,
                [
                    new NavigationTabDefinition(
                        PackageNavigationId,
                        coordinate: coordinate),
                ],
                focus: PackageNavigationId),
            new CommittedViewDefinition(
                SchemaVersion,
                ViewId,
                [
                    new CommittedViewStateDefinition(
                        navigation: null,
                        new PortableSubjectRequest.Workspace()),
                    new CommittedViewStateDefinition(
                        PackageNavigationId,
                        new PortableSubjectRequest.Package(),
                        new PortableRetainedSubjectContext.Package()),
                ]),
            new ScenarioDefinition(
                SchemaVersion,
                ScenarioId,
                workspace: WorkspaceId,
                context: ContextId,
                view: ViewId,
                navigation: NavigationId),
        ];
        var restoration = new CompleteRestorationRequestBasis.DefinitionInput(
            ScenarioId,
            records);
        return new BrowserRetainedWorkspaceActivationRequest(
            retainedDefinitionId,
            label,
            canonicalLocation,
            restoration);
    }
}
