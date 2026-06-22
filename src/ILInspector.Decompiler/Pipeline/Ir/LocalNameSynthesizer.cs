namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Synthesizes a readable identifier for a local that has no usable PDB source
/// name, used only by the opt-in readable-names mode (see
/// <c>docs/design/readable-local-names.md</c>). It names from evidence the IR
/// already carries — the local's type and whether it is a loop counter — and
/// resolves collisions against the names already taken (parameters, source-named
/// locals, and earlier synthesized names).
///
/// It never invents a name with no evidence: when the type carries no readable
/// name it returns <c>null</c> and the caller keeps the <c>V_index</c> fallback,
/// preserving the pipeline's honest-degradation contract. The mode is opt-in and
/// off by default, so this never changes shipped output.
/// </summary>
static class LocalNameSynthesizer
{
    /// <summary>
    /// A readable name for a local of <paramref name="type"/>, or <c>null</c>
    /// when no honest readable name applies (the caller then keeps <c>V_index</c>).
    /// <paramref name="isLoopCounter"/> marks an integer loop-induction local, the
    /// one role worth the conventional <c>i</c>/<c>j</c>/<c>k</c>. The result is
    /// guaranteed absent from <paramref name="taken"/>; the caller reserves it.
    /// </summary>
    public static string? Synthesize(TypeRef? type, bool isLoopCounter, IReadOnlySet<string> taken)
    {
        var bases = BaseNames(type, isLoopCounter);
        if (bases.Count == 0)
            return null;
        foreach (var name in bases)
            if (!taken.Contains(name))
                return name;
        // Every preferred base is taken; suffix the primary until one is free.
        string primary = bases[0];
        for (int n = 2; ; n++)
        {
            string candidate = primary + n;
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    static IReadOnlyList<string> BaseNames(TypeRef? type, bool isLoopCounter)
    {
        if (type is null)
            return [];
        var core = Unwrap(type);
        if (isLoopCounter && IsIntegerLike(core))
            return ["i", "j", "k", "l", "m", "n"];
        return TypeBaseName(core) is { } name ? [name] : [];
    }

    // Name from the underlying value type, not its ref/pin/pointer wrapper.
    static TypeRef Unwrap(TypeRef type)
        => type.Kind is TypeRefKind.ByRef or TypeRefKind.Pinned or TypeRefKind.Pointer && type.ElementType is { } inner
            ? Unwrap(inner)
            : type;

    static string? TypeBaseName(TypeRef type)
    {
        if (IsCore(type, "String")) return "text";
        if (IsCore(type, "Boolean")) return "flag";
        if (IsCore(type, "Char")) return "ch";
        if (IsIntegerLike(type)) return "num";
        if (IsCore(type, "Single") || IsCore(type, "Double") || IsCore(type, "Decimal")) return "value";
        if (type.Kind is TypeRefKind.SzArray or TypeRefKind.Array) return "items";
        if (type.Kind is TypeRefKind.Definition or TypeRefKind.GenericInstance)
            return CamelCase(SimpleName(type));
        return null;
    }

    static bool IsCore(TypeRef type, string name) => MemberIdentity.IsCoreLibraryType(type, "System", name);

    static bool IsIntegerLike(TypeRef type)
        => IsCore(type, "Int32") || IsCore(type, "Int64") || IsCore(type, "Int16") || IsCore(type, "SByte")
            || IsCore(type, "Byte") || IsCore(type, "UInt16") || IsCore(type, "UInt32") || IsCore(type, "UInt64")
            || IsCore(type, "IntPtr") || IsCore(type, "UIntPtr");

    // The leaf simple name, stripped of generic arity backtick and nested-type
    // '+' segments, so List`1 / Outer+Inner name as `list` / `inner`.
    static string SimpleName(TypeRef type)
    {
        string name = type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element ? element.Name : type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        int plus = name.LastIndexOf('+');
        if (plus >= 0) name = name[(plus + 1)..];
        return name;
    }

    static string? CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsLetter(name[0]))
            return null;
        string camel = char.ToLowerInvariant(name[0]) + name[1..];
        // A name that collides with a keyword (`object`, `string`) or is otherwise
        // unusable is dropped rather than escaped — V_index is the honest fallback.
        return CSharpNaming.IsUsableIdentifier(camel) ? camel : null;
    }
}
