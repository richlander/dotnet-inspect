using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Pure (Roslyn-free) helpers that decide whether a validity-shell diagnostic is
/// a sibling-free-shell artifact rather than a decompiler defect. Kept separate
/// from <see cref="ValidityCheck"/> so the discriminator can be linked into the
/// test project and unit-tested WITHOUT dragging in the shell/Roslyn machinery
/// (which is also why a stray fidelity-gate regression earlier — the gate
/// reconstructs every type in the test assembly into a skeleton, so only a
/// trivially-reconstructable static class may be linked).
/// </summary>
internal static class ShellNoise
{
    /// <summary>
    /// Does the decompiler's own model for <paramref name="function"/> reference a
    /// generic type whose simple name is <paramref name="simpleName"/>? Used to tell
    /// a bare-name import collision (the model holds a NON-generic type, or no type
    /// at all, of that name — sibling-free-shell noise) apart from a genuine
    /// dropped-type-argument render (the model holds a generic of that name, which a
    /// correct printer would have spelled with its arguments — a real defect that
    /// must stay reported).
    /// </summary>
    internal static bool ReferencesGenericTypeNamed(IrFunction function, string simpleName)
    {
        foreach (var node in function.Descendants.Prepend(function))
            foreach (var type in node.DirectTypes)
                if (ContainsGenericTypeNamed(type, simpleName))
                    return true;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> — or any type nested inside it (array element,
    /// by-ref/pointer pointee, or a type argument) — is a generic type whose simple
    /// name is <paramref name="simpleName"/>. Both a generic instantiation
    /// (<c>List&lt;int&gt;</c>) and a bare generic definition reference
    /// (<c>List`1</c> with no arguments — the shape a dropped-argument bug would
    /// render) count.
    /// </summary>
    internal static bool ContainsGenericTypeNamed(TypeRef type, string simpleName)
    {
        if (IsGenericNamed(type, simpleName))
            return true;
        if (type.ElementType is { } element && ContainsGenericTypeNamed(element, simpleName))
            return true;
        foreach (var argument in type.TypeArguments)
            if (ContainsGenericTypeNamed(argument, simpleName))
                return true;
        return false;
    }

    static bool IsGenericNamed(TypeRef type, string simpleName) => type.Kind switch
    {
        // A generic instantiation: the simple name lives on the open definition.
        TypeRefKind.GenericInstance =>
            type.ElementType is { } definition && SimpleName(definition.Name) == simpleName,
        // A bare definition is generic only when its metadata name carries an
        // arity suffix (`N); a plain non-generic type (the collision case) is not.
        TypeRefKind.Definition =>
            HasArity(type.Name) && SimpleName(type.Name) == simpleName,
        _ => false,
    };

    /// <summary>The C# simple name of a metadata type name: innermost nested
    /// segment, with the generic-arity suffix stripped (<c>Outer+List`1</c> → <c>List</c>).</summary>
    static string SimpleName(string metadataName)
    {
        int nested = metadataName.LastIndexOf('+');
        string innermost = nested < 0 ? metadataName : metadataName[(nested + 1)..];
        int tick = innermost.IndexOf('`');
        return tick < 0 ? innermost : innermost[..tick];
    }

    static bool HasArity(string metadataName)
    {
        int nested = metadataName.LastIndexOf('+');
        string innermost = nested < 0 ? metadataName : metadataName[(nested + 1)..];
        int tick = innermost.IndexOf('`');
        return tick >= 0 && int.TryParse(innermost[(tick + 1)..], out int arity) && arity > 0;
    }
}
