using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>
/// An explicit interface implementation's property or event record is one C#
/// declaration spelled through its interface, such as
/// <c>object IEnumerator.Current { get; }</c>, with no accessibility or
/// virtual modifiers. Its accessors' structured facts identify it.
/// </summary>
static class CSharpExplicitAccessorMembers
{
    /// <summary>
    /// Whether a property or event row is an explicit implementation, and the
    /// interface-qualified name of its first explicit accessor.
    /// </summary>
    public static bool TryGetExplicitAccessorName(
        ApiMember member,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name)
    {
        name = null;
        if (member.Kind is not ("property" or "event"))
            return false;

        string? candidate = member.SignatureModel?.Accessors.FirstOrDefault(value =>
                value.IsExplicitInterfaceImplementation == true
                && !string.IsNullOrWhiteSpace(value.Name))
            ?.Name;
        if (candidate is null
            || !candidate.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        name = candidate;
        return true;
    }

    /// <summary>
    /// Reshapes a rendering snapshot of a property or event row into its
    /// explicit implementation declaration. <paramref name="namespaces"/>
    /// names the namespaces whose prefix the interface qualifier may drop;
    /// null keeps the fully qualified interface name.
    /// </summary>
    public static void ApplyExplicitShape(
        ApiMember snapshot,
        string accessorName,
        string? typeNamespace,
        IReadOnlyCollection<string>? namespaces)
    {
        int separator = accessorName.LastIndexOf('.');
        if (separator < 0)
            throw new NotSupportedException("An explicit accessor has no interface-qualified name.");
        string leaf = snapshot.SignatureModel!.MemberName ?? snapshot.Name;
        leaf = leaf[(leaf.LastIndexOf('.') + 1)..];
        snapshot.Kind = "explicit-interface-implementation";
        string qualifier = accessorName[..separator];
        if (namespaces is not null)
        {
            string? prefix = namespaces
                .Append(typeNamespace)
                .Where(static value =>
                    !string.IsNullOrWhiteSpace(value))
                .OrderByDescending(static value => value!.Length)
                .FirstOrDefault(value =>
                    qualifier.StartsWith(
                        value + ".",
                        StringComparison.Ordinal));
            if (prefix is not null)
                qualifier = qualifier[(prefix.Length + 1)..];
        }
        snapshot.Name = qualifier + "." + leaf;
        snapshot.SignatureModel.MemberName = leaf == "this[]"
            ? leaf
            : snapshot.Name;
    }
}
