using System.Runtime.Versioning;
using DotnetInspector.Queries;

namespace DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
internal static partial class BrowserCatalogWireProjection
{
    internal static BrowserRetainedNavigationResult Project(
        NavigationConsumerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.Operation.ToString(),
            result.Request,
            Project(result.Snapshot),
            Project(result.Outcome),
            result.Synchronization.ToString(),
            Project(result.Authority));
    }

    static BrowserRetainedNavigationSnapshot Project(
        NavigationConsumerSnapshot snapshot) =>
        new(
            snapshot.Generation,
            new(
                snapshot.Scope.Kind.ToString(),
                snapshot.Scope.RuntimeFailure?.ToString()),
            Project(snapshot.Workspace),
            snapshot.ActivePackage,
            Project(snapshot.ActiveSubject),
            snapshot.TypeInventoryLibraryContext is null
                ? null
                : Project(snapshot.TypeInventoryLibraryContext),
            [.. snapshot.Packages.Select(Project)],
            [.. snapshot.Hierarchy.Select(Project)],
            [.. snapshot.Libraries.Select(Project)],
            [.. snapshot.Types.Select(Project)],
            [.. snapshot.Members.Select(Project)],
            [.. snapshot.Lenses.Select(Project)],
            Project(snapshot.LensOutcome),
            [.. snapshot.Diagnostics.Select(Project)]);

    static BrowserRetainedNavigationOutcome Project(
        NavigationConsumerOutcome outcome) =>
        new(
            outcome.Kind.ToString(),
            outcome.Rejection?.ToString(),
            outcome.FailureSource?.ToString(),
            outcome.Message,
            outcome.Request is null ? null : Project(outcome.Request),
            outcome.Resolution is null ? null : Project(outcome.Resolution),
            outcome.Scope is null ? null : Project(outcome.Scope),
            [.. outcome.Diagnostics.Select(Project)],
            outcome.CoordinateRetention is null
                ? null
                : Project(outcome.CoordinateRetention));

    static BrowserRetainedNavigationSubject Project(
        NavigationConsumerSubject subject) =>
        new(
            subject.Id,
            subject.Kind.ToString(),
            subject.Label,
            subject.Summary,
            subject.Parent);

    static BrowserRetainedNavigationSubjectDescriptor Project(
        NavigationConsumerSubjectDescriptor descriptor) =>
        new(
            descriptor.Kind.ToString(),
            descriptor.Label,
            descriptor.Subject is null
                ? null
                : Project(descriptor.Subject),
            descriptor.State.ToString(),
            descriptor.IsActive,
            descriptor.IsRetained,
            [.. descriptor.Evidence.Select(Project)],
            Project(descriptor.Action));

    static BrowserRetainedNavigationPackageDescriptor Project(
        NavigationConsumerPackageDescriptor descriptor) =>
        new(
            descriptor.Order,
            Project(descriptor.Subject),
            descriptor.PackageId,
            descriptor.Version,
            descriptor.Framework,
            descriptor.RuntimeIdentifier,
            descriptor.Realization.ToString(),
            descriptor.RealizationFailure?.ToString(),
            descriptor.State.ToString(),
            descriptor.IsCurrent,
            Project(descriptor.Action));

    static BrowserRetainedNavigationLibraryDescriptor Project(
        NavigationConsumerLibraryDescriptor descriptor) =>
        new(
            Project(descriptor.Navigation),
            descriptor.AssetId,
            descriptor.IsAggregate,
            descriptor.IsPrimary);

    static BrowserRetainedNavigationTypeDescriptor Project(
        NavigationConsumerTypeDescriptor descriptor) =>
        new(
            Project(descriptor.Navigation),
            descriptor.Library,
            descriptor.Accessibility,
            descriptor.TypeKind,
            [.. descriptor.DescendantLenses.Select(Project)]);

    static BrowserRetainedNavigationMemberDescriptor Project(
        NavigationConsumerMemberDescriptor descriptor) =>
        new(
            Project(descriptor.Navigation),
            descriptor.Library,
            descriptor.ContainingType,
            descriptor.DeclaringType,
            descriptor.Accessibility,
            descriptor.MemberKind,
            descriptor.Signature,
            [.. descriptor.DescendantLenses.Select(Project)]);

    static BrowserRetainedNavigationLensDescriptor Project(
        NavigationConsumerLensDescriptor descriptor) =>
        new(
            Project(descriptor.Facet),
            descriptor.State.ToString(),
            descriptor.IsCurrent,
            descriptor.Target is null ? null : Project(descriptor.Target),
            descriptor.Unavailability?.ToString(),
            descriptor.Message,
            Project(descriptor.Action));

    static BrowserRetainedNavigationFacet Project(
        NavigationConsumerFacet facet) =>
        new(
            facet.Id,
            facet.Kind.ToString(),
            facet.Title,
            facet.Summary,
            facet.Order,
            facet.Role?.ToString());

    static BrowserRetainedNavigationLens Project(
        NavigationConsumerLens lens) =>
        new(lens.Id, Project(lens.Subject), lens.Facet);

    static BrowserRetainedNavigationLensOutcome Project(
        NavigationConsumerLensOutcome outcome) =>
        new(
            outcome.Kind.ToString(),
            outcome.Basis.ToString(),
            Project(outcome.Subject),
            outcome.EffectiveLens is null
                ? null
                : Project(outcome.EffectiveLens),
            outcome.Request is null ? null : Project(outcome.Request),
            outcome.PreferredRole?.ToString(),
            outcome.PolicyFailure?.ToString(),
            outcome.Resolution is null ? null : Project(outcome.Resolution),
            outcome.Suspension is null ? null : Project(outcome.Suspension));

    static BrowserRetainedNavigationResolution Project(
        NavigationConsumerResolution resolution) =>
        new(
            resolution.Kind.ToString(),
            resolution.Descriptor is null
                ? null
                : Project(resolution.Descriptor),
            resolution.Unavailability?.ToString(),
            resolution.Message);

    static BrowserRetainedNavigationRealization Project(
        NavigationConsumerRealization realization) =>
        new(realization.Kind.ToString(), realization.Failure?.ToString());

    static BrowserRetainedNavigationDiagnostic Project(
        NavigationConsumerDiagnostic diagnostic) =>
        new(
            diagnostic.Kind.ToString(),
            diagnostic.Library,
            diagnostic.Message);

    static BrowserRetainedNavigationRequest Project(
        NavigationConsumerRequest request) =>
        new(
            Project(request.Source),
            Project(request.Destination),
            request.Lens is null ? null : Project(request.Lens));

    static BrowserRetainedNavigationScopeOutcome Project(
        NavigationConsumerScopeOutcome outcome) =>
        new(
            outcome.Kind.ToString(),
            outcome.Operation.ToString(),
            outcome.Rejection?.ToString(),
            outcome.Failure?.ToString());

    static BrowserRetainedNavigationCoordinateOutcome Project(
        NavigationConsumerCoordinateOutcome outcome) =>
        new(
            outcome.Disposition.ToString(),
            outcome.Detail,
            outcome.LibraryPairing?.ToString(),
            outcome.TypeCorrespondence?.ToString(),
            outcome.MemberCorrespondence?.ToString());

    static BrowserRetainedNavigationAction? Project(
        NavigationAction? action) =>
        action is null
            ? null
            : new(
                action.Session,
                action.Generation,
                action.Id,
                action.Source,
                action.Kind.ToString());

    static BrowserRetainedNavigationAuthority? Project(
        NavigationEffectAuthority? authority) =>
        authority is null
            ? null
            : new(
                authority.Session,
                authority.Revision,
                authority.Intent,
                authority.Epoch);
}
