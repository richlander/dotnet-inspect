namespace ILInspector.Metadata;

/// <summary>
/// Binding policy for inspection contexts that have no reference resolver.
/// It performs no acquisition and never selects an assembly.
/// </summary>
public sealed class NoResolverAssemblyBindingPolicy : IAssemblyBindingPolicy
{
    public static NoResolverAssemblyBindingPolicy Instance { get; } = new();

    NoResolverAssemblyBindingPolicy()
    {
    }

    public AssemblyBindingPolicyVersion Version { get; } = new();

    public AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AssemblyBindingSelectionSnapshot(
            Version,
            request.Target switch
            {
                AssemblyBindingTarget.AssemblyReference =>
                    AssemblyBindingSelection.NameNotOwned(),
                AssemblyBindingTarget.IntrinsicCoreLibrary =>
                    AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.UnsupportedScope)),
                _ => AssemblyBindingSelection.Invalid(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.InvalidPolicyResult)),
            });
    }
}
