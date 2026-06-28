using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Analysis;

/// <summary>
/// Canonicalizes generic member identity for <em>cross-assembly</em> structural
/// matching (the <c>Callers</c> table and the cross-assembly <c>Caller Graph</c>).
///
/// Same-assembly matching resolves through metadata tokens
/// (<see cref="DirectCall.CalleeDefinitionToken"/>), so it sees a constructed call
/// site and its open definition as the same member already. Cross-assembly matching
/// has no shared token space and must compare structurally, where the two sides
/// disagree on generics: a call site keys on its instantiation
/// (<c>List&lt;int&gt;.Add(int)</c> / <c>Id&lt;int&gt;(int)</c>) while the selected
/// target keys on the open definition (<c>List&lt;T&gt;.Add(T)</c> /
/// <c>Id&lt;T&gt;(T)</c>), so they never match and the target under-reports external
/// callers (#1339).
///
/// The normalization reduces a generic member to its open declaring definition plus
/// name plus parameter arity ("arity + erased shape"). It is deliberately symmetric:
/// the open-definition side and the constructed-call-site side each compute the same
/// erased identity from the data they carry, so they collapse onto one key. Distinct
/// generic overloads that share a name and parameter arity on the same generic type
/// are an accepted coarsening, far preferable to the prior zero-match under-report.
/// Non-generic members are left on their exact instantiated signature.
/// </summary>
public static class GenericMemberIdentity
{
    /// <summary>
    /// The open named definition of a (possibly constructed) declaring type, so a
    /// constructed generic type matches its open definition. A non-generic or
    /// already-open declaring type is returned unchanged.
    /// </summary>
    public static TypeRef OpenDeclaringType(TypeRef declaringType)
        => declaringType.Kind == TypeRefKind.GenericInstance && declaringType.ElementType is { } element
            ? element
            : declaringType;

    /// <summary>
    /// Whether a declaring type is generic — either a constructed instantiation
    /// (call site) or an open generic definition (target side, whose metadata name
    /// carries an arity backtick).
    /// </summary>
    public static bool IsGenericType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            || (type.Kind == TypeRefKind.Definition && HasArity(type.Name));

    /// <summary>A metadata type name carries a generic arity suffix (e.g. <c>List`1</c>).</summary>
    public static bool HasArity(string name) => name.IndexOf('`') >= 0;

    /// <summary>
    /// Whether a type mentions a generic parameter anywhere — the open-definition
    /// marker for a generic method (its signature still references <c>!T</c>/<c>!!T</c>
    /// where the call site carries concrete instantiations).
    /// </summary>
    public static bool ContainsGenericParameter(TypeRef type)
    {
        if (type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter)
            return true;
        if (type.ElementType is { } element && ContainsGenericParameter(element))
            return true;
        return type.TypeArguments.Any(ContainsGenericParameter);
    }

    /// <summary>
    /// Whether a member should be matched on erased generic shape rather than its
    /// exact instantiated signature. True when the declaring type is generic (open or
    /// constructed), the member carries method type arguments (a constructed generic
    /// method call), or its signature mentions a generic parameter (an open generic
    /// method definition).
    /// </summary>
    public static bool ShouldErase(
        TypeRef declaringType,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        ImmutableArray<TypeRef> typeArguments)
        => IsGenericType(declaringType)
            || typeArguments.Length > 0
            || ContainsGenericParameter(returnType)
            || parameterTypes.Any(ContainsGenericParameter);

    /// <summary>
    /// A cross-assembly-stable parameter shape for an erased generic member, computed
    /// from the <em>open</em> signature (VAR/MVAR markers preserved): a type generic
    /// parameter renders as <c>!i</c>, a method generic parameter as <c>!!i</c>, generic
    /// instances and array/byref/pointer wrappers recurse, and any other type uses its
    /// namespace-qualified display. The open definition and a constructed call site both
    /// carry the same markers, so they collapse onto one key, while same-arity overloads
    /// with a different parameter structure — <c>Box&lt;T&gt;.Store(T)</c> vs
    /// <c>Box&lt;T&gt;.Store(List&lt;T&gt;)</c>, or a type-parameter vs a literal of the
    /// same instantiated type — stay distinct (#1731). The earlier erasure kept only the
    /// parameter count.
    /// </summary>
    public static string ErasedParameterShape(ImmutableArray<TypeRef> openParameterTypes)
        => string.Join(",", openParameterTypes.Select(KeyFragment));

    /// <summary>
    /// An assembly-qualified, cross-assembly-stable key fragment for a type. Generic
    /// parameters render as <c>!i</c>/<c>!!i</c>; generic instances and
    /// array/byref/pointer wrappers recurse; a named type renders as
    /// <c>assembly|namespace.name</c>. Including the assembly keeps same-FQN types from
    /// different assemblies distinct in string keys (<see cref="TypeRef.Equals"/> already
    /// distinguishes them, but the display strings did not) (#1741), and the namespace
    /// keeps same-name types from different namespaces distinct (#1731).
    /// </summary>
    public static string KeyFragment(TypeRef type) => type.Kind switch
    {
        TypeRefKind.GenericParameter => $"!{type.GenericParameterIndex}",
        TypeRefKind.MethodGenericParameter => $"!!{type.GenericParameterIndex}",
        TypeRefKind.GenericInstance when type.ElementType is { } definition
            => $"{KeyFragment(definition)}<{string.Join(",", type.TypeArguments.Select(KeyFragment))}>",
        TypeRefKind.SzArray when type.ElementType is { } element
            => $"{KeyFragment(element)}[]",
        TypeRefKind.Array when type.ElementType is { } element
            => $"{KeyFragment(element)}[{new string(',', type.Rank > 0 ? type.Rank - 1 : 0)}]",
        TypeRefKind.ByRef when type.ElementType is { } element
            => $"ref {KeyFragment(element)}",
        TypeRefKind.Pointer when type.ElementType is { } element
            => $"{KeyFragment(element)}*",
        TypeRefKind.Definition => $"{type.Assembly}|{type.Namespace}.{type.Name}",
        _ => type.ToQualifiedDisplayString(),
    };
}
