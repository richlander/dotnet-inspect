using System.Collections.Generic;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Bounded, iterative walkers for the nested-type chains that qualify a nested type's name
/// (<c>Outer.Inner</c>): a <see cref="TypeDefinition"/>'s declaring-type chain and a
/// <see cref="TypeReference"/>'s resolution-scope chain.
///
/// These chains come from inspected (untrusted) metadata, which can be malformed so that a chain
/// cycles (a type nested in itself, a TypeReference scoped to itself) or nests pathologically deep.
/// A naive recursive climb overflows the native stack (an uncatchable
/// <see cref="System.StackOverflowException"/>); a cyclic climb that concatenates each row's
/// (arbitrarily long) name also amplifies a tiny blob into gigabytes of string. These helpers never
/// recurse and stop at <see cref="MaxDepth"/>, so callers get a finite chain to format; callers that
/// concatenate names must also stop once the accumulated length passes <see cref="MaxLength"/>.
/// </summary>
public static class NestedTypeName
{
    /// <summary>Maximum nested-type chain length before the climb is truncated. Real types nest a
    /// handful of levels; the ceiling only trips on malformed / adversarial metadata.</summary>
    public const int MaxDepth = 256;

    /// <summary>Maximum accumulated qualified-name length before a caller should stop concatenating
    /// and degrade. Real qualified names are far shorter, so this only trips on a cyclic climb over
    /// a long untrusted name (which would otherwise amplify a small blob into a huge string).</summary>
    public const int MaxLength = 8192;

    /// <summary>
    /// The declaring-type chain of <paramref name="handle"/>, outermost first (including the type
    /// itself), iterated with a depth bound so a self-nested TypeDefinition cannot recurse forever.
    /// </summary>
    public static List<TypeDefinitionHandle> DeclaringChain(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var chain = new List<TypeDefinitionHandle>();
        var current = handle;
        for (int depth = 0; depth < MaxDepth && !current.IsNil; depth++)
        {
            chain.Add(current);
            current = reader.GetTypeDefinition(current).GetDeclaringType();
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// The resolution-scope chain of a nested <paramref name="handle"/>, outermost first (including
    /// the reference itself), iterated with a depth bound so a self-scoped TypeReference cannot
    /// recurse forever. The walk stops at the first non-TypeReference scope (assembly / module /
    /// nil) or at <see cref="MaxDepth"/>.
    /// </summary>
    public static List<TypeReferenceHandle> ResolutionScopeChain(MetadataReader reader, TypeReferenceHandle handle)
    {
        var chain = new List<TypeReferenceHandle>();
        var current = handle;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            chain.Add(current);
            var typeRef = reader.GetTypeReference(current);
            if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
                break;
            current = (TypeReferenceHandle)typeRef.ResolutionScope;
        }
        chain.Reverse();
        return chain;
    }
}
