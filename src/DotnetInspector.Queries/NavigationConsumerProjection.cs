using System.Collections.Immutable;
using System.Globalization;

namespace DotnetInspector.Queries;

internal sealed record NavigationActionTarget(
    NavigationAction Action,
    StructuralSubjectIdentity Source,
    StructuralSubjectIdentity Subject,
    NavigationLensIdentity? Lens,
    bool Advertised = true);

internal sealed record NavigationProjectionState(
    string Session,
    long NextToken,
    ImmutableDictionary<StructuralSubjectIdentity, string> Subjects,
    ImmutableDictionary<NavigationLensIdentity, string> Lenses);

/// <summary>Operation-local builder; only its immutable data leaves the operation.</summary>
internal sealed class NavigationConsumerProjection(NavigationProjectionState state)
{
    readonly Dictionary<StructuralSubjectIdentity, string> _subjects = new(state.Subjects);
    readonly Dictionary<NavigationLensIdentity, string> _lenses = new(state.Lenses);
    long _nextToken = state.NextToken;

    internal NavigationProjectionState Freeze() =>
        new(state.Session, _nextToken, _subjects.ToImmutableDictionary(), _lenses.ToImmutableDictionary());

    internal string SubjectId(StructuralSubjectIdentity subject)
    {
        if (!_subjects.TryGetValue(subject, out string? id))
            _subjects.Add(subject, id = Token());
        return id;
    }

    internal string Token() =>
        $"{state.Session}:n:{checked(++_nextToken).ToString(CultureInfo.InvariantCulture)}";

    internal NavigationConsumerSubject Subject(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity subject)
    {
        StructuralSubjectIdentity? parent = subject switch
        {
            StructuralSubjectIdentity.PackageSubject => snapshot.Workspace,
            StructuralSubjectIdentity.AllLibrariesSubject all => all.Package,
            StructuralSubjectIdentity.LibrarySubject library => library.Package,
            StructuralSubjectIdentity.TypeSubject type => type.Library,
            StructuralSubjectIdentity.MemberSubject member => member.DeclaringType,
            _ => null,
        };
        (string label, string? summary) = subject switch
        {
            StructuralSubjectIdentity.WorkspaceSubject =>
                ("Workspace", "Retained packages in this Workspace."),
            StructuralSubjectIdentity.PackageSubject package =>
                (package.Occurrence.Package.PackageId,
                    $"{package.Occurrence.Package.PackageVersion} / {package.Occurrence.Package.TargetFramework}"),
            StructuralSubjectIdentity.AllLibrariesSubject =>
                ("All libraries", "All admitted libraries in this Package."),
            StructuralSubjectIdentity.LibrarySubject library =>
                (snapshot.Libraries.First(row => row.Subject == library).Asset!.Path, null),
            StructuralSubjectIdentity.TypeSubject type =>
                TypeLabel(snapshot.Types.First(row => row.Row.Subject == type)),
            StructuralSubjectIdentity.MemberSubject member =>
                MemberLabel(snapshot.Members.First(row => row.Row.Subject == member)),
            _ => throw new InvalidOperationException("Unknown Navigation subject."),
        };
        return new(SubjectId(subject), subject.Kind, label, summary,
            parent is null ? null : SubjectId(parent));
    }

    static (string, string?) TypeLabel(NavigationTypeDescriptor row) =>
        (row.Row.ProducerRow.FullName, row.Row.ProducerRow.Documentation.Summary);

    static (string, string?) MemberLabel(NavigationMemberDescriptor row) =>
        (row.Row.ProducerRow.Name, row.Row.ProducerRow.Documentation.Summary);

    internal NavigationConsumerLens Lens(
        NavigationWorkspaceSnapshot snapshot,
        NavigationLensIdentity lens) =>
        Lens(Subject(snapshot, lens.Subject), lens);

    NavigationConsumerLens Lens(
        NavigationConsumerSubject subject,
        NavigationLensIdentity lens)
    {
        if (!_lenses.TryGetValue(lens, out string? id))
            _lenses.Add(lens, id = Token());
        return new(id, subject, lens.Facet.Value);
    }

    internal NavigationConsumerRequest Request(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity destination,
        NavigationLensIdentity? lens)
    {
        bool known = NavigationWorkspaceSnapshotEvaluation.SubjectExists(snapshot, destination)
            || destination is StructuralSubjectIdentity.PackageSubject package
                && snapshot.Packages.Any(row => row.Occurrence == package.Occurrence);
        // A rejected foreign request has no descriptor in this Workspace. Retain
        // its exact opaque identity without fabricating labels or local ancestry.
        NavigationConsumerSubject subject = known
            ? Subject(snapshot, destination)
            : new(SubjectId(destination), destination.Kind, destination.Kind.ToString(), null, null);
        return new(
            Subject(snapshot, snapshot.ActiveSubject), subject,
            lens is null ? null : Lens(subject, lens));
    }

    internal static NavigationConsumerFacet Facet(ViewFacetDescriptor descriptor) =>
        new(descriptor.Id.Value, descriptor.Kind, descriptor.Title,
            descriptor.Summary, descriptor.Order, descriptor.Role);

    internal static NavigationConsumerResolution Resolution(ViewFacetResolution resolution) =>
        resolution switch
        {
            ViewFacetResolution.Available result =>
                new(NavigationResolutionKind.Available, Facet(result.Descriptor), null, null),
            ViewFacetResolution.Unavailable result =>
                new(NavigationResolutionKind.Unavailable, Facet(result.Descriptor),
                    result.Reason.Kind, result.Reason.Message),
            ViewFacetResolution.Failed result =>
                new(NavigationResolutionKind.Failed, Facet(result.Descriptor), null, result.Message),
            ViewFacetResolution.Inapplicable result =>
                new(NavigationResolutionKind.Inapplicable, Facet(result.Descriptor), null, null),
            ViewFacetResolution.Unknown =>
                new(NavigationResolutionKind.Unknown, null, null, null),
            _ => throw new InvalidOperationException("Unknown Registry resolution."),
        };

    internal (NavigationConsumerSnapshot Snapshot, Dictionary<string, NavigationActionTarget> Actions) Build(
        NavigationWorkspaceSnapshot snapshot,
        string session,
        NavigationConsumerScopeStatus? scope = null,
        bool actionsEnabled = true)
    {
        string generation = Token();
        var actions = new Dictionary<string, NavigationActionTarget>(StringComparer.Ordinal);
        string source = SubjectId(snapshot.ActiveSubject);

        NavigationAction? Action(
            NavigationOperationKind kind,
            StructuralSubjectIdentity subject,
            NavigationLensIdentity? lens = null)
        {
            if (!actionsEnabled)
                return null;
            var action = new NavigationAction(session, generation, Token(), source, kind);
            actions.Add(action.Id, new(action, snapshot.ActiveSubject, subject, lens));
            return action;
        }

        NavigationConsumerSubjectDescriptor Descriptor(
            StructuralSubjectKind kind,
            StructuralSubjectIdentity? subject,
            NavigationDescriptorState state,
            bool active,
            bool retained,
            ImmutableArray<NavigationInventoryEvidence> evidence = default) =>
            new(kind, kind.ToString(), subject is null ? null : Subject(snapshot, subject),
                state, active, retained,
                evidence.IsDefaultOrEmpty
                    ? []
                    : [.. evidence.Select(Diagnostic)],
                state == NavigationDescriptorState.Available && subject is not null && !active
                    ? Action(NavigationOperationKind.Subject, subject)
                    : null);

        ImmutableArray<NavigationInventoryEvidence> HierarchyEvidence(
            NavigationHierarchyDescriptor descriptor)
        {
            if (descriptor.State != NavigationDescriptorState.Failed
                || snapshot.Inventory is null)
            {
                return [];
            }

            return descriptor.Kind switch
            {
                StructuralSubjectKind.Type =>
                    snapshot.TypeInventoryLibraryContext switch
                    {
                        StructuralSubjectIdentity.AllLibrariesSubject =>
                            snapshot.Inventory.Types.Evidence,
                        StructuralSubjectIdentity.LibrarySubject library =>
                            snapshot.Inventory.Libraries.Single(candidate =>
                                candidate.Subject == library).Types.Evidence,
                        _ => [],
                    },
                StructuralSubjectKind.Member
                    when snapshot.RetainedContext?.Type is { } type =>
                    snapshot.Inventory.Libraries.Single(candidate =>
                        candidate.Subject == type.Library).Types.Evidence,
                _ => [],
            };
        }

        NavigationConsumerLensDescriptor LensDescriptor(
            StructuralSubjectIdentity subject,
            ViewFacetOption option,
            bool current,
            NavigationOperationKind kind,
            NavigationDescriptorState? retainedState = null)
        {
            NavigationDescriptorState state = retainedState ?? (option.Availability switch
            {
                ViewFacetAvailability.Available => NavigationDescriptorState.Available,
                ViewFacetAvailability.Unavailable => NavigationDescriptorState.Unavailable,
                ViewFacetAvailability.Failed => NavigationDescriptorState.Failed,
                _ => throw new InvalidOperationException("Unknown Registry availability."),
            });
            var lens = new NavigationLensIdentity(subject, option.Descriptor.Id);
            return new(
                Facet(option.Descriptor), state, current,
                state == NavigationDescriptorState.Available ? Lens(snapshot, lens) : null,
                (option.Availability as ViewFacetAvailability.Unavailable)?.Reason.Kind,
                option.Availability switch
                {
                    ViewFacetAvailability.Unavailable unavailable => unavailable.Reason.Message,
                    ViewFacetAvailability.Failed failed => failed.Message,
                    _ => null,
                },
                state == NavigationDescriptorState.Available && !current
                    ? Action(kind, subject, lens)
                    : null);
        }

        ImmutableArray<NavigationConsumerLensDescriptor> Descendants(StructuralSubjectIdentity subject)
        {
            return
            [
                .. snapshot.DescendantLenses.Where(row => row.Request.Destination.Subject == subject)
                    .Select(row => LensDescriptor(
                        subject, row.Option, false, NavigationOperationKind.DescendantLens)),
            ];
        }

        NavigationLensEvaluationBasis basis = snapshot.LensOutcome.Basis;
        var exact = basis as NavigationLensEvaluationBasis.ExactRequest;
        var recommendation = basis as NavigationLensEvaluationBasis.Recommendation;
        var lensOutcome = new NavigationConsumerLensOutcome(
            OutcomeKind(snapshot.LensOutcome),
            exact is null ? NavigationLensBasisKind.Recommendation : NavigationLensBasisKind.ExactRequest,
            Subject(snapshot, basis.Subject),
            snapshot.LensOutcome.EffectiveLens is { } effective ? Lens(snapshot, effective) : null,
            exact is null ? null : Lens(snapshot, exact.Request),
            recommendation?.PreferredRole,
            (snapshot.LensOutcome as NavigationLensOutcome.Failed)?.Failure
                is NavigationLensFailure.Policy policy ? policy.Kind : null,
            exact is null ? null : Resolution(exact.Result))
        {
            Suspension = snapshot.LensOutcome is NavigationLensOutcome.Suspended suspended
                ? new(
                    suspended.Realization is ArtifactRootRealizationStatus.Pending
                        ? NavigationRealizationKind.Pending : NavigationRealizationKind.Failed,
                    (suspended.Realization as ArtifactRootRealizationStatus.Failed)?.Failure)
                : null,
        };

        var result = new NavigationConsumerSnapshot(
            generation,
            scope ?? new(NavigationScopeSnapshotKind.Current),
            Subject(snapshot, snapshot.Workspace),
            snapshot.ActiveOccurrence is { } occurrence
                ? SubjectId(StructuralSubjectIdentity.ForPackage(snapshot.Workspace, occurrence)) : null,
            Subject(snapshot, snapshot.ActiveSubject),
            snapshot.TypeInventoryLibraryContext is { } context ? Subject(snapshot, context) : null,
            [
                .. snapshot.Packages.Select(row =>
                {
                    var subject = StructuralSubjectIdentity.ForPackage(snapshot.Workspace, row.Occurrence);
                    WorkspacePackageDescriptor package = row.Occurrence.Package;
                    bool current = row.Occurrence == snapshot.ActiveOccurrence;
                    return new NavigationConsumerPackageDescriptor(
                        row.Order, Subject(snapshot, subject), package.PackageId, package.PackageVersion,
                        package.TargetFramework, package.RuntimeIdentifier,
                        row.Realization switch
                        {
                            ArtifactRootRealizationStatus.Ready => NavigationRealizationKind.Ready,
                            ArtifactRootRealizationStatus.Pending => NavigationRealizationKind.Pending,
                            ArtifactRootRealizationStatus.Failed => NavigationRealizationKind.Failed,
                            _ => throw new InvalidOperationException("Unknown Package realization status."),
                        },
                        (row.Realization as ArtifactRootRealizationStatus.Failed)?.Failure,
                        row.State, current,
                        row.State == NavigationDescriptorState.Available && !current
                            ? Action(NavigationOperationKind.Package, subject) : null);
                }),
            ],
            [
                .. snapshot.Hierarchy.Select(row => Descriptor(
                    row.Kind, row.Subject, row.State, row.IsActive,
                    row.Subject is not null, HierarchyEvidence(row))),
            ],
            [
                .. snapshot.Libraries.Select(row => new NavigationConsumerLibraryDescriptor(
                    Descriptor(StructuralSubjectKind.Library, row.Subject, row.State, row.IsActive, row.IsRetained),
                    row.Asset?.Id, row.Subject is StructuralSubjectIdentity.AllLibrariesSubject, row.IsPrimary)),
            ],
            [
                .. snapshot.Types.Select(row => new NavigationConsumerTypeDescriptor(
                    Descriptor(StructuralSubjectKind.Type, row.Row.Subject, row.State, row.IsActive, row.IsRetained),
                    SubjectId(row.Row.Subject.Library), row.Row.ProducerRow.Accessibility,
                    row.Row.ProducerRow.Kind, Descendants(row.Row.Subject))),
            ],
            [
                .. snapshot.Members.Select(row => new NavigationConsumerMemberDescriptor(
                    Descriptor(StructuralSubjectKind.Member, row.Row.Subject, row.State,
                        row.Row.Subject == snapshot.ActiveSubject, row.IsRetained),
                    SubjectId(row.Row.Subject.DeclaringType.Library), SubjectId(row.Row.ContainingType),
                    SubjectId(row.Row.Subject.DeclaringType), row.Row.ProducerRow.Accessibility,
                    row.Row.ProducerRow.Kind, row.Row.ProducerRow.Signature, Descendants(row.Row.Subject))),
            ],
            [
                .. snapshot.Lenses.Select(row => LensDescriptor(
                    snapshot.ActiveSubject, row.Option, row.IsEffective, NavigationOperationKind.Lens, row.State)),
            ],
            lensOutcome,
            [
                .. (snapshot.Inventory?.Types.Evidence ?? []).Select(Diagnostic),
            ]);
        return (result, actions);
    }

    internal static NavigationOutcomeKind OutcomeKind(NavigationLensOutcome outcome) =>
        outcome switch
        {
            NavigationLensOutcome.Effective => NavigationOutcomeKind.Applied,
            NavigationLensOutcome.Unavailable => NavigationOutcomeKind.Unavailable,
            NavigationLensOutcome.Failed => NavigationOutcomeKind.Failed,
            NavigationLensOutcome.Suspended suspended =>
                suspended.Realization is ArtifactRootRealizationStatus.Failed
                    ? NavigationOutcomeKind.Failed : NavigationOutcomeKind.Unavailable,
            _ => throw new InvalidOperationException("Unknown Navigation lens outcome."),
        };

    internal NavigationConsumerDiagnostic Diagnostic(NavigationInventoryEvidence evidence)
    {
        (NavigationDiagnosticKind kind, string message) = evidence switch
        {
            NavigationInventoryEvidence.ParticipantRejected item =>
                (NavigationDiagnosticKind.ParticipantRejected, item.Failure.Detail),
            NavigationInventoryEvidence.ParticipantFailed item =>
                (NavigationDiagnosticKind.ParticipantFailed, item.Error.Message),
            NavigationInventoryEvidence.DetachedParticipantRejected item =>
                (NavigationDiagnosticKind.ParticipantRejected, item.Failure.Detail),
            NavigationInventoryEvidence.DetachedParticipantFailed item =>
                (NavigationDiagnosticKind.ParticipantFailed, item.Error.Message),
            NavigationInventoryEvidence.InspectionFailed item =>
                (NavigationDiagnosticKind.InspectionFailed, $"{item.Failure.Operation}: {item.Failure.Detail}"),
            NavigationInventoryEvidence.TypeIdentityMissing item =>
                (NavigationDiagnosticKind.TypeIdentityMissing, item.ProducerRow.FullName),
            NavigationInventoryEvidence.ProjectedMemberIdentityFailure item =>
                (NavigationDiagnosticKind.ProjectedMemberIdentityFailure,
                    $"{item.Kind}: {item.ContainingType.FullName}.{item.ProducerRow.Name}"),
            NavigationInventoryEvidence.ProjectionOmitted item =>
                (NavigationDiagnosticKind.ProjectionOmitted,
                    $"{item.Truncation.Limit}: {item.Truncation.OmittedParticipants} omitted participants"),
            _ => throw new InvalidOperationException("Unknown Navigation inventory evidence."),
        };
        return new(kind, SubjectId(evidence.Library), message);
    }

    internal (NavigationConsumerSnapshot Snapshot, Dictionary<string, NavigationActionTarget> Actions) RenewActions(
        NavigationConsumerSnapshot snapshot,
        ImmutableDictionary<string, NavigationActionTarget> prior)
    {
        string generation = Token();
        var actions = new Dictionary<string, NavigationActionTarget>(StringComparer.Ordinal);
        NavigationAction? Renew(NavigationAction? action)
        {
            if (action is null)
                return null;
            NavigationAction replacement = action with { Id = Token(), Generation = generation };
            actions.Add(replacement.Id, prior[action.Id] with { Action = replacement });
            return replacement;
        }
        NavigationConsumerSubjectDescriptor Subject(NavigationConsumerSubjectDescriptor row) =>
            row with { Action = Renew(row.Action) };
        ImmutableArray<NavigationConsumerLensDescriptor> Lenses(
            ImmutableArray<NavigationConsumerLensDescriptor> rows) =>
            [.. rows.Select(row => row with { Action = Renew(row.Action) })];

        NavigationConsumerSnapshot renewed = snapshot with
        {
            Generation = generation,
            Packages = [.. snapshot.Packages.Select(row => row with { Action = Renew(row.Action) })],
            Hierarchy = [.. snapshot.Hierarchy.Select(Subject)],
            Libraries = [.. snapshot.Libraries.Select(row => row with { Navigation = Subject(row.Navigation) })],
            Types = [.. snapshot.Types.Select(row => row with
            {
                Navigation = Subject(row.Navigation),
                DescendantLenses = Lenses(row.DescendantLenses),
            })],
            Members = [.. snapshot.Members.Select(row => row with
            {
                Navigation = Subject(row.Navigation),
                DescendantLenses = Lenses(row.DescendantLenses),
            })],
            Lenses = Lenses(snapshot.Lenses),
        };
        return (renewed, actions);
    }
}
