using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal abstract class AssemblyBindingPolicyFacade(
    IAssemblyBindingPolicy inner) : IAssemblyBindingPolicy
{
    BindingState _state = new(inner.Version);

    public AssemblyBindingPolicyVersion Version => CurrentState().Version;

    public virtual AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BindingState state = CurrentState();
        AssemblyBindingRequest? delegatedRequest = DelegateRequest(
            state,
            request);
        if (delegatedRequest is null)
        {
            return new AssemblyBindingSelectionSnapshot(
                state.Version,
                AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidBindingOrigin)));
        }

        AssemblyBindingSelectionSnapshot? snapshot =
            inner.Select(delegatedRequest);
        if (snapshot is null)
            return null!;
        if (!ReferenceEquals(snapshot.Version, state.DelegateVersion))
        {
            Interlocked.CompareExchange(
                ref _state,
                new BindingState(inner.Version),
                state);
            ObserveForeignSnapshot();
            return snapshot;
        }

        AssemblyBindingSelection selection =
            AssemblyBindingSelection.ValidateForRequest(
                delegatedRequest,
                snapshot.Selection);
        AssemblyBindingSelection transformed = TransformSelection(selection);
        if (selection is AssemblyBindingSelection.Selected selected
            && transformed is AssemblyBindingSelection.Selected adapted)
        {
            // Adapt the descriptor without replacing delegated context with seed routing.
            var lineage = new FacadeLineage(
                this,
                state,
                selected.Occurrence);
            transformed = AssemblyBindingSelection.FoundOccurrence(
                lineage.Issue(adapted.Assembly),
                adapted.ShadowedAssemblies);
        }

        return new AssemblyBindingSelectionSnapshot(
            state.Version,
            transformed);
    }

    protected virtual AssemblyBindingRequest SeedRequest(
        AssemblyBindingRequest request) => request;

    protected abstract AssemblyBindingSelection TransformSelection(
        AssemblyBindingSelection selection);

    protected virtual void ObserveForeignSnapshot()
    {
    }

    AssemblyBindingRequest? DelegateRequest(
        BindingState state,
        AssemblyBindingRequest request)
    {
        if (request.Origin
                is not AssemblyBindingOrigin.RequestingAssembly requesting
            || requesting.Lineage is null
            || requesting.Lineage == AssemblyBindingLineage.Seed)
        {
            return SeedRequest(request);
        }

        if (requesting.Lineage is not FacadeLineage lineage
            || !ReferenceEquals(lineage.Issuer, this)
            || !ReferenceEquals(lineage.State, state))
        {
            return null;
        }

        return new AssemblyBindingRequest(
            request.Target,
            AssemblyBindingOrigin.FromOccurrence(lineage.DelegatedOccurrence),
            request.Scope);
    }

    BindingState CurrentState()
    {
        while (true)
        {
            BindingState state = Volatile.Read(ref _state);
            AssemblyBindingPolicyVersion version = inner.Version;
            if (ReferenceEquals(state.DelegateVersion, version))
                return state;

            Interlocked.CompareExchange(
                ref _state,
                new BindingState(version),
                state);
        }
    }

    sealed class BindingState(AssemblyBindingPolicyVersion delegateVersion)
    {
        internal AssemblyBindingPolicyVersion Version { get; } = new();
        internal AssemblyBindingPolicyVersion DelegateVersion { get; } =
            delegateVersion;
    }

    sealed record FacadeLineage : AssemblyBindingLineage
    {
        internal FacadeLineage(
            AssemblyBindingPolicyFacade issuer,
            BindingState state,
            AssemblyBindingOccurrence delegatedOccurrence)
            : base(state.Version)
        {
            Issuer = issuer;
            State = state;
            DelegatedOccurrence = delegatedOccurrence;
        }

        internal AssemblyBindingPolicyFacade Issuer { get; }
        internal BindingState State { get; }
        internal AssemblyBindingOccurrence DelegatedOccurrence { get; }

        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) => CreateOccurrence(assembly);
    }
}
