using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal sealed record WorkspaceNavigationCommandResult(
    NavigationWorkspaceSnapshot Snapshot,
    NavigationDescendantLensResult? Descendant,
    NavigationSnapshotSelectorResolution? Selector)
{
    internal bool IsSuccess =>
        (Selector is null
            or NavigationSnapshotSelectorResolution.Selected)
        && (Descendant is null
            or NavigationDescendantLensResult.Applied);
}

internal static class WorkspaceNavigationProjection
{
    internal static WorkspaceNavigationView Create(
        WorkspaceNavigationCommandResult result,
        IReadOnlyList<NavigationPackageDescriptor> packages)
    {
        NavigationWorkspaceSnapshot snapshot = result.Snapshot;
        return new WorkspaceNavigationView
        {
            Navigation =
            [
                new WorkspaceNavigationSummaryRow(
                    Text(Subject(snapshot, snapshot.ActiveSubject)),
                    snapshot.ActiveSubject.Kind.ToString(),
                    Text(snapshot.LensOutcome.EffectiveLens?.Facet.Value
                        ?? "(none)"),
                    snapshot.LensOutcome switch
                    {
                        NavigationLensOutcome.Effective => "Effective",
                        NavigationLensOutcome.Unavailable => "Unavailable",
                        NavigationLensOutcome.Failed => "Failed",
                        _ => throw new InvalidOperationException(
                            "Unknown Navigation lens outcome."),
                    },
                    Text(snapshot.TypeInventoryLibraryContext is null
                        ? "(none)"
                        : Subject(
                            snapshot,
                            snapshot.TypeInventoryLibraryContext))),
            ],
            Packages =
            [
                .. packages.Select(package =>
                    new WorkspaceNavigationPackageRow(
                        package.Order,
                        Text(package.Occurrence.Package.PackageId),
                        Text(package.Occurrence.Package.PackageVersion),
                        Text(
                            package.Occurrence.Package.TargetFramework
                            ?? ""),
                        package.State.ToString(),
                        ReferenceEquals(
                            package.Occurrence,
                            snapshot.ActiveOccurrence))),
            ],
            Hierarchy =
            [
                .. snapshot.Hierarchy.Select(slot =>
                    new WorkspaceNavigationHierarchyRow(
                        slot.Kind.ToString(),
                        Text(slot.Subject is null
                            ? "(none)"
                            : Subject(snapshot, slot.Subject)),
                        slot.State.ToString(),
                        slot.IsActive)),
            ],
            Libraries =
            [
                .. snapshot.Libraries.Select(library =>
                    new WorkspaceNavigationLibraryRow(
                        Text(Subject(snapshot, library.Subject)),
                        Text(library.Asset?.Id ?? ""),
                        Text(library.Asset?.Path ?? ""),
                        library.State.ToString(),
                        library.IsPrimary,
                        library.IsActive,
                        library.IsRetained)),
            ],
            Types =
            [
                .. snapshot.Types.Select(type =>
                    new WorkspaceNavigationTypeRow(
                        Text(
                            type.Row.Subject.Identity.Type
                                .ToEscapedFullName()),
                        Text(Subject(
                            snapshot,
                            type.Row.Subject.Library)),
                        Text(AssetId(
                            snapshot,
                            type.Row.Subject.Library)),
                        Text(type.Row.Accessibility.Label),
                        type.State.ToString(),
                        type.IsActive,
                        type.IsRetained)),
            ],
            Members =
            [
                .. snapshot.Members.Select(member =>
                    new WorkspaceNavigationMemberRow(
                        Text(member.Row.Subject.Identity.Member.MemberName),
                        Text(
                            member.Row.ContainingType.Identity.Type
                                .ToEscapedFullName()),
                        Text(
                            member.Row.Subject.DeclaringType.Identity.Type
                                .ToEscapedFullName()),
                        Text(AssetId(
                            snapshot,
                            member.Row.Subject.DeclaringType.Library)),
                        Text(
                            member.Row.Subject.Identity.Member
                                .StableSelector),
                        Text(
                            member.Row.Subject.Identity.Member
                                .CanonicalSignature),
                        member.State.ToString(),
                        member.IsActive,
                        member.IsRetained)),
            ],
            Lenses =
            [
                .. snapshot.Lenses.Select(lens =>
                    new WorkspaceNavigationLensRow(
                        Text(lens.Option.Descriptor.Id.Value),
                        Text(lens.Option.Descriptor.Title),
                        lens.State.ToString(),
                        lens.IsEffective,
                        Text(LensDiagnostic(lens.Option.Availability)))),
            ],
            Diagnostics =
            [
                .. PackageDiagnostics(packages),
                .. InventoryDiagnostics(snapshot),
                .. SelectorDiagnostics(result.Selector),
                .. DescendantDiagnostics(result.Descendant),
            ],
        };
    }

    internal static WorkspaceNavigationStreamView CreateStream(
        WorkspaceNavigationView view)
    {
        var rows = new List<WorkspaceNavigationStreamRow>();
        rows.AddRange(view.Navigation.Select(row =>
            new WorkspaceNavigationStreamRow(
                "navigation",
                row.ActiveKind,
                row.ActiveSubject,
                row.TypeInventoryContext,
                row.LensState,
                row.Lens,
                active: true,
                libraryAssetId: "",
                containingType: "",
                declaringType: "")));
        rows.AddRange(view.Packages.Select(row =>
            new WorkspaceNavigationStreamRow(
                "package",
                "Package",
                $"{row.Package}@{row.Version}",
                "Workspace",
                row.State,
                row.Framework,
                row.Active,
                libraryAssetId: "",
                containingType: "",
                declaringType: "")));
        rows.AddRange(view.Hierarchy.Select(row =>
            new WorkspaceNavigationStreamRow(
                "hierarchy",
                row.Level,
                row.Subject,
                "",
                row.State,
                "",
                row.Active,
                libraryAssetId: "",
                containingType: "",
                declaringType: "")));
        rows.AddRange(view.Libraries.Select(row =>
            new WorkspaceNavigationStreamRow(
                "library",
                "Library",
                row.Library,
                "Package",
                row.State,
                row.AssetId,
                row.Active,
                row.AssetId,
                containingType: "",
                declaringType: "")));
        rows.AddRange(view.Types.Select(row =>
            new WorkspaceNavigationStreamRow(
                "type",
                "Type",
                row.Type,
                row.Library,
                row.State,
                row.Accessibility,
                row.Active,
                row.LibraryAssetId,
                row.Type,
                row.Type)));
        rows.AddRange(view.Members.Select(row =>
            new WorkspaceNavigationStreamRow(
                "member",
                "Member",
                row.Member,
                row.ContainingType,
                row.State,
                row.Selector,
                row.Active,
                row.LibraryAssetId,
                row.ContainingType,
                row.DeclaringType)));
        rows.AddRange(view.Lenses.Select(row =>
            new WorkspaceNavigationStreamRow(
                "lens",
                "Lens",
                row.Facet,
                row.Title,
                row.Availability,
                row.Diagnostic,
                row.Effective,
                libraryAssetId: "",
                containingType: "",
                declaringType: "")));
        rows.AddRange(view.Diagnostics.Select(row =>
            new WorkspaceNavigationStreamRow(
                "diagnostic",
                row.Code,
                row.Scope,
                "",
                "Failed",
                row.Message,
                active: false,
                libraryAssetId: "",
                containingType: "",
                declaringType: "")));
        return new WorkspaceNavigationStreamView
        {
            Rows = rows,
        };
    }

    static IEnumerable<WorkspaceNavigationDiagnosticRow>
        PackageDiagnostics(
            IReadOnlyList<NavigationPackageDescriptor> packages)
    {
        foreach (NavigationPackageDescriptor package in packages)
        {
            if (package.Realization
                is not ArtifactRootRealizationStatus.Failed failed)
            {
                continue;
            }

            yield return Diagnostic(
                $"Package {package.Order}",
                "artifact-root-failed",
                failed.Failure.ToString());
        }
    }

    static IEnumerable<WorkspaceNavigationDiagnosticRow>
        InventoryDiagnostics(NavigationWorkspaceSnapshot snapshot)
    {
        if (snapshot.Inventory is null)
            yield break;

        foreach (NavigationInventoryEvidence evidence
            in snapshot.Inventory.Types.Evidence)
        {
            yield return evidence switch
            {
                NavigationInventoryEvidence.ParticipantRejected rejected =>
                    Diagnostic(
                        Subject(snapshot, rejected.Library),
                        "participant-rejected",
                        $"{rejected.Failure.Kind}: "
                            + rejected.Failure.Detail),
                NavigationInventoryEvidence.ParticipantFailed failed =>
                    Diagnostic(
                        Subject(snapshot, failed.Library),
                        "participant-failed",
                        failed.Error.Message),
                NavigationInventoryEvidence.InspectionFailed failed =>
                    Diagnostic(
                        Subject(snapshot, failed.Library),
                        "inspection-failed",
                        $"{failed.Failure.Operation}: "
                            + failed.Failure.Detail),
                NavigationInventoryEvidence.TypeIdentityMissing missing =>
                    Diagnostic(
                        Subject(snapshot, missing.Library),
                        "type-identity-missing",
                        missing.ProducerRow.FullName),
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure
                    failure =>
                    Diagnostic(
                        Subject(snapshot, failure.Library),
                        "member-identity-failed",
                        $"{failure.Kind}: "
                            + failure.ProducerRow.Name),
                NavigationInventoryEvidence.ProjectionOmitted omitted =>
                    Diagnostic(
                        Subject(snapshot, omitted.Library),
                        "inventory-truncated",
                        $"{omitted.Truncation.Limit} "
                            + $"bound {omitted.Truncation.Bound} omitted "
                            + $"{omitted.Truncation.OmittedParticipants} "
                            + "Library participant(s)."),
                _ => throw new InvalidOperationException(
                    "Unknown Navigation inventory evidence."),
            };
        }
    }

    static IEnumerable<WorkspaceNavigationDiagnosticRow>
        SelectorDiagnostics(NavigationSnapshotSelectorResolution? result)
    {
        switch (result)
        {
            case null:
            case NavigationSnapshotSelectorResolution.Selected:
                yield break;
            case NavigationSnapshotSelectorResolution.NotPresent missing:
                yield return Diagnostic(
                    "Navigation selector",
                    "selector-not-present",
                    $"'{missing.Selector}' is not present in the complete "
                        + "scoped inventory.");
                yield break;
            case NavigationSnapshotSelectorResolution.Ambiguous ambiguous:
                yield return Diagnostic(
                    "Navigation selector",
                    "selector-ambiguous",
                    $"'{ambiguous.Selector}' identifies multiple scoped rows.");
                yield break;
            case NavigationSnapshotSelectorResolution.Incomplete incomplete:
                yield return Diagnostic(
                    "Navigation selector",
                    "selector-incomplete",
                    $"The scoped inventory could not establish whether "
                        + $"'{incomplete.Selector}' is present.");
                yield break;
            case NavigationSnapshotSelectorResolution.Unavailable unavailable:
                yield return Diagnostic(
                    "Navigation selector",
                    "selector-unavailable",
                    $"'{unavailable.Selector}' has no subject in this Package.");
                yield break;
            case NavigationSnapshotSelectorResolution.Invalid invalid:
                yield return Diagnostic(
                    "Navigation selector",
                    "selector-invalid",
                    invalid.Selector);
                yield break;
            default:
                throw new InvalidOperationException(
                    "Unknown Navigation selector resolution.");
        }
    }

    static IEnumerable<WorkspaceNavigationDiagnosticRow>
        DescendantDiagnostics(NavigationDescendantLensResult? result)
    {
        switch (result)
        {
            case null:
            case NavigationDescendantLensResult.Applied:
                yield break;
            case NavigationDescendantLensResult.Unavailable unavailable:
                ViewFacetResolution.Unavailable unavailableResult =
                    AssertExact<ViewFacetResolution.Unavailable>(
                        unavailable.Activation.Outcome.Basis);
                yield return Diagnostic(
                    "Destination lens",
                    "lens-unavailable",
                    unavailableResult.Reason.Message);
                yield break;
            case NavigationDescendantLensResult.Failed failed:
                ViewFacetResolution.Failed failedResult =
                    AssertExact<ViewFacetResolution.Failed>(
                        failed.Activation.Outcome.Basis);
                yield return Diagnostic(
                    "Destination lens",
                    "lens-failed",
                    failedResult.Message);
                yield break;
            case NavigationDescendantLensResult.Rejected
                {
                    Validation: { } validation,
                }:
                yield return Diagnostic(
                    "Descendant request",
                    "request-rejected",
                    validation.ToString());
                yield break;
            case NavigationDescendantLensResult.Rejected
                {
                    Activation.Rejection:
                        NavigationLensRejection.Registry registry,
                }:
                yield return Diagnostic(
                    "Destination lens",
                    registry.Result is ViewFacetResolution.Unknown
                        ? "lens-unknown"
                        : "lens-inapplicable",
                    registry.Result is ViewFacetResolution.Unknown
                        ? "The requested facet is unknown."
                        : "The requested facet is not applicable to the destination subject.");
                yield break;
            default:
                throw new InvalidOperationException(
                    "Unknown descendant lens result.");
        }
    }

    static TResult AssertExact<TResult>(
        NavigationLensEvaluationBasis basis)
        where TResult : ViewFacetResolution =>
        ((NavigationLensEvaluationBasis.ExactRequest)basis).Result
            is TResult result
                ? result
                : throw new InvalidOperationException(
                    "The exact lens result did not retain the expected Registry evidence.");

    static string LensDiagnostic(
        ViewFacetAvailability availability) =>
        availability switch
        {
            ViewFacetAvailability.Available => "",
            ViewFacetAvailability.Unavailable unavailable =>
                unavailable.Reason.Message,
            ViewFacetAvailability.Failed failed =>
                failed.Message,
            _ => throw new InvalidOperationException(
                "Unknown view-facet availability."),
        };

    static WorkspaceNavigationDiagnosticRow Diagnostic(
        string scope,
        string code,
        string message) =>
        new(Text(scope), code, Text(message));

    static string Subject(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.WorkspaceSubject =>
                "Workspace",
            StructuralSubjectIdentity.PackageSubject package =>
                $"{package.Descriptor.PackageId}"
                    + $"@{package.Descriptor.PackageVersion}",
            StructuralSubjectIdentity.AllLibrariesSubject =>
                "All libraries",
            StructuralSubjectIdentity.LibrarySubject library =>
                snapshot.Libraries.FirstOrDefault(
                    descriptor => descriptor.Subject == library)
                    ?.Asset?.AssemblyName
                ?? "(unknown library)",
            StructuralSubjectIdentity.TypeSubject type =>
                type.Identity.Type.ToEscapedFullName(),
            StructuralSubjectIdentity.MemberSubject member =>
                member.Identity.Member.Format(
                    ILInspector.MetadataPrimitives.MemberAnchorFormat
                        .Qualified),
            _ => throw new InvalidOperationException(
                "Unknown structural subject."),
        };

    static string AssetId(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity.LibrarySubject library) =>
        snapshot.Libraries.Single(descriptor =>
            descriptor.Subject == library).Asset?.Id
        ?? throw new InvalidOperationException(
            "An exact Library subject must retain its portable asset id.");

    static string Text(string value) =>
        LibraryViewText.Contain(value) ?? "";
}
