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

            return intrinsicSelection.Value;
        }

        if (request.Target
            is AssemblyBindingTarget.AssemblyReference reference)
        {
            ImmutableArray<ResolvedAssemblyReference> matches =
            [
                .. _roots.Where(
                    root => root.Identity == reference.Identity),
            ];
            if (matches.Length == 1)
                return AssemblyBindingSelection.Found(matches[0]);
            if (matches.Length > 1)
                return AssemblyBindingSelection.Multiple(matches);
        }

        AssemblyBindingSelection selection = policy.Select(request);
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
