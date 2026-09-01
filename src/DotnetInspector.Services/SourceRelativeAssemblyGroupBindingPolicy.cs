using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Routes one frozen assembly group's binding requests through the policy that
/// owns the requesting assembly while preferring the group's canonical
/// descriptors for exact in-group identities.
/// </summary>
public sealed class SourceRelativeAssemblyGroupBindingPolicy :
    IAssemblyBindingPolicy
{
    readonly object _gate = new();
    readonly ImmutableArray<ResolvedAssemblyReference> _roots;
    readonly IAssemblyBindingPolicy _default;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        IAssemblyBindingPolicy> _byOrigin =
            new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        ResolvedAssemblyReference> _assemblyByOrigin =
            new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<
        (AssemblyAcquisitionRegistration Origin,
            AssemblyResolutionScope Scope),
        Lazy<AssemblyBindingSelection>> _intrinsicSelections = [];

    public SourceRelativeAssemblyGroupBindingPolicy(
        IEnumerable<(
            ResolvedAssemblyReference Assembly,
            IAssemblyBindingPolicy Policy)> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var roots = ImmutableArray.CreateBuilder<
            ResolvedAssemblyReference>();
        foreach ((ResolvedAssemblyReference assembly,
            IAssemblyBindingPolicy policy) in participants)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(policy);
            roots.Add(assembly);
            _byOrigin.Add(assembly.Registration, policy);
            _assemblyByOrigin.Add(assembly.Registration, assembly);
        }

        if (roots.Count == 0)
        {
            throw new ArgumentException(
                "At least one assembly-group participant is required.",
                nameof(participants));
        }

        _roots = roots.ToImmutable();
        _default = _byOrigin[_roots[0].Registration];
    }

    public AssemblyBindingPolicyVersion Version { get; } = new();

    public AssemblyBindingSelection Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        IAssemblyBindingPolicy policy = PolicyFor(request.Origin);
        if (request.Target is AssemblyBindingTarget.IntrinsicCoreLibrary
            && request.Origin
                is AssemblyBindingOrigin.RequestingAssembly requesting
            && AssemblyFor(requesting.Registration)
                is { } requestingAssembly)
        {
            Lazy<AssemblyBindingSelection> intrinsicSelection;
            lock (_gate)
            {
                intrinsicSelection = _intrinsicSelections.GetValueOrDefault(
                    (requesting.Registration, request.Scope))
                    ?? AddIntrinsicSelection(
                        requesting.Registration,
                        request.Scope,
                        requestingAssembly,
                        request.Origin);
            }

            return AssemblyBindingSelection.ValidateForRequest(
                request,
                intrinsicSelection.Value);
        }

        AssemblyBindingTarget.AssemblyReference? reference =
            request.Target as AssemblyBindingTarget.AssemblyReference;
        AssemblyBindingSelection? pendingDesignated = null;
        if (reference is not null)
        {
            bool nonEntitledNameOwner =
                request.Scope == AssemblyResolutionScope.Any
                && _roots.Any(root =>
                    root.Provenance
                        is not (
                            AssemblyResolutionProvenance.DesignatedAsset
                            or AssemblyResolutionProvenance.PlatformAsset)
                    && string.Equals(
                        root.Identity.Name,
                        reference.Identity.Name,
                        StringComparison.OrdinalIgnoreCase));
            if (!nonEntitledNameOwner
                && DesignatedAssemblyBindingPrecedence.TrySelect(
                        reference.Identity,
                        _roots)
                    is { } precedenceSelection)
            {
                pendingDesignated = precedenceSelection;
            }

            if (pendingDesignated is null)
            {
                ImmutableArray<ResolvedAssemblyReference> matches =
                [
                    .. _roots.Where(
                        root => SameIdentity(
                            root.Identity,
                            reference.Identity)),
                ];
                if (matches.Length == 1)
                    return AssemblyBindingSelection.Found(matches[0]);
                if (matches.Length > 1)
                    return AssemblyBindingSelection.Multiple(matches);
            }
        }

        AssemblyBindingSelection selection =
            AssemblyBindingSelection.ValidateForRequest(
                request,
                policy.Select(request));
        if (reference is not null
            && pendingDesignated is not null)
        {
            selection = ComposePendingDesignated(
                reference.Identity,
                pendingDesignated,
                selection);
        }
        if (reference is not null
            && (selection
                    is AssemblyBindingSelection.Missing
                    {
                        Disposition:
                            AssemblyBindingMissDisposition.NoNameOwner,
                    }
                || selection
                    is AssemblyBindingSelection.Selected)
            && IdentityMismatchSelection(
                reference.Identity,
                (selection as AssemblyBindingSelection.Selected)
                    ?.Assembly)
                    is { } mismatch)
        {
            selection = mismatch;
        }

        switch (selection)
        {
            case AssemblyBindingSelection.Selected selected:
                Register(selected.Assembly, policy);
                break;
            case AssemblyBindingSelection.Ambiguous ambiguous:
                foreach (ResolvedAssemblyReference assembly
                    in ambiguous.Assemblies)
                {
                    Register(assembly, policy);
                }
                break;
        }

        return selection;
    }

    static AssemblyBindingSelection ComposePendingDesignated(
        AssemblyReferenceIdentity requested,
        AssemblyBindingSelection designated,
        AssemblyBindingSelection policySelection)
    {
        if (policySelection
            is AssemblyBindingSelection.Missing
            {
                Disposition:
                    AssemblyBindingMissDisposition.NoNameOwner,
            })
        {
            return designated;
        }

        if (policySelection
            is AssemblyBindingSelection.Unavailable
                or AssemblyBindingSelection.Rejected)
        {
            return policySelection;
        }

        ImmutableArray<ResolvedAssemblyReference> policyCandidates =
            policySelection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    [selected.Assembly],
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    ambiguous.Assemblies,
                _ => [],
            };
        if (policyCandidates.IsEmpty
            || policyCandidates.Any(candidate =>
                !IsCompatibleEntitled(requested, candidate)))
        {
            return policySelection;
        }

        ImmutableArray<ResolvedAssemblyReference> designatedCandidates =
            DistinctRegistrations(
                DesignatedCandidates(designated)
                    .Concat(policyCandidates.Where(candidate =>
                        candidate.Provenance
                            is AssemblyResolutionProvenance
                                .DesignatedAsset)));
        if (designatedCandidates.Length > 1)
        {
            return AssemblyBindingSelection.Multiple(
                designatedCandidates);
        }

        IEnumerable<ResolvedAssemblyReference> policyShadows =
            policySelection
                is AssemblyBindingSelection.Selected selectedWithShadows
                    ? selectedWithShadows.ShadowedAssemblies
                    : [];
        IEnumerable<ResolvedAssemblyReference> designatedShadows =
            designated
                is AssemblyBindingSelection.Selected selectedDesignated
                    ? selectedDesignated.ShadowedAssemblies
                    : [];
        ImmutableArray<ResolvedAssemblyReference> platforms =
            DistinctRegistrations(
                designatedShadows
                    .Concat(policyCandidates)
                    .Concat(policyShadows)
                    .Where(candidate => IsCompatiblePlatform(
                        requested,
                        candidate,
                        ignoreVersion: true)));
        return AssemblyBindingSelection.Found(
            designatedCandidates[0],
            platforms);
    }

    static ImmutableArray<ResolvedAssemblyReference> DesignatedCandidates(
        AssemblyBindingSelection selection) =>
        selection switch
        {
            AssemblyBindingSelection.Selected selected
                when selected.Assembly.Provenance
                    is AssemblyResolutionProvenance.DesignatedAsset =>
                [selected.Assembly],
            AssemblyBindingSelection.Ambiguous ambiguous =>
                [
                    .. ambiguous.Assemblies.Where(candidate =>
                        candidate.Provenance
                            is AssemblyResolutionProvenance
                                .DesignatedAsset),
                ],
            _ => [],
        };

    static ImmutableArray<ResolvedAssemblyReference> DistinctRegistrations(
        IEnumerable<ResolvedAssemblyReference> candidates)
    {
        var seen = new HashSet<AssemblyAcquisitionRegistration>(
            ReferenceEqualityComparer.Instance);
        return
        [
            .. candidates.Where(candidate =>
                seen.Add(candidate.Registration)),
        ];
    }

    static bool IsCompatibleEntitled(
        AssemblyReferenceIdentity requested,
        ResolvedAssemblyReference candidate) =>
        candidate.Provenance is (
            AssemblyResolutionProvenance.DesignatedAsset
            or AssemblyResolutionProvenance.PlatformAsset)
        && requested.MatchesCandidate(
            candidate.Identity,
            allowVersionRollForward: false,
            ignoreVersion: true);

    static bool IsCompatiblePlatform(
        AssemblyReferenceIdentity requested,
        ResolvedAssemblyReference candidate,
        bool ignoreVersion) =>
        candidate.Provenance
            is AssemblyResolutionProvenance.PlatformAsset
        && requested.MatchesCandidate(
            candidate.Identity,
            allowVersionRollForward: false,
            ignoreVersion);

    AssemblyBindingSelection? IdentityMismatchSelection(
        AssemblyReferenceIdentity requested,
        ResolvedAssemblyReference? selected)
    {
        ImmutableArray<ResolvedAssemblyReference> candidates =
        [
            .. _roots.Where(root =>
                string.Equals(
                    root.Identity.Name,
                    requested.Name,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (selected is not null
            && (!string.Equals(
                    selected.Identity.Name,
                    requested.Name,
                    StringComparison.OrdinalIgnoreCase)
                || candidates.Any(candidate =>
                    SameIdentity(
                        candidate.Identity,
                        selected.Identity))))
        {
            return null;
        }

        return candidates.Length switch
        {
            0 => null,
            1 => AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.IdentityPolicyRequired)),
            _ => AssemblyBindingSelection.Multiple(candidates),
        };
    }

    static bool SameIdentity(
        AssemblyReferenceIdentity left,
        AssemblyReferenceIdentity right) =>
        string.Equals(
            left.Name,
            right.Name,
            StringComparison.OrdinalIgnoreCase)
        && left.Version == right.Version
        && string.Equals(
            NormalizeCulture(left.Culture),
            NormalizeCulture(right.Culture),
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            NormalizeOptional(left.PublicKeyToken),
            NormalizeOptional(right.PublicKeyToken),
            StringComparison.OrdinalIgnoreCase);

    static string NormalizeCulture(string? culture) =>
        string.IsNullOrEmpty(culture)
            || culture.Equals(
                "neutral",
                StringComparison.OrdinalIgnoreCase)
                ? ""
                : culture;

    static string NormalizeOptional(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value;

    Lazy<AssemblyBindingSelection> AddIntrinsicSelection(
        AssemblyAcquisitionRegistration registration,
        AssemblyResolutionScope scope,
        ResolvedAssemblyReference requestingAssembly,
        AssemblyBindingOrigin origin)
    {
        var selection = new Lazy<AssemblyBindingSelection>(
            () => SelectIntrinsicCoreLibrary(
                requestingAssembly,
                origin,
                scope),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _intrinsicSelections.Add((registration, scope), selection);
        return selection;
    }

    AssemblyBindingSelection SelectIntrinsicCoreLibrary(
        ResolvedAssemblyReference requestingAssembly,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope)
        => IntrinsicCoreLibraryBinding.Select(
            requestingAssembly,
            facade => Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(facade),
                    origin,
                    scope)));

    IAssemblyBindingPolicy PolicyFor(AssemblyBindingOrigin origin)
    {
        if (origin is not AssemblyBindingOrigin.RequestingAssembly requesting)
            return _default;

        lock (_gate)
        {
            return _byOrigin.TryGetValue(
                requesting.Registration,
                out IAssemblyBindingPolicy? policy)
                    ? policy
                    : _default;
        }

    }

    ResolvedAssemblyReference? AssemblyFor(
        AssemblyAcquisitionRegistration registration)
    {
        lock (_gate)
        {
            return _assemblyByOrigin.GetValueOrDefault(registration);
        }
    }

    void Register(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy policy)
    {
        lock (_gate)
        {
            _byOrigin.TryAdd(assembly.Registration, policy);
            _assemblyByOrigin.TryAdd(
                assembly.Registration,
                assembly);
        }
    }
}
