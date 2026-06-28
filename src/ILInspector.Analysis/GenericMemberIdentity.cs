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
internal static class GenericMemberIdentity
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
    /// A cross-assembly-stable parameter shape for an erased generic member. Each
    /// declaring-type generic argument is mapped to a positional marker
    /// (<c>!0</c>, <c>!1</c>, …) so the open definition (whose parameters reference the
    /// type parameters as <c>!i</c>) and a constructed call site (whose parameters carry
    /// the instantiation) collapse onto the same shape, while same-arity overloads with
    /// a different parameter structure — <c>Box&lt;T&gt;.Store(T)</c> vs
    /// <c>Box&lt;T&gt;.Store(List&lt;T&gt;)</c> — stay distinct (#1731). The earlier
    /// erasure kept only the parameter count, collapsing such overloads.
    /// </summary>
    public static string ErasedParameterShape(TypeRef declaringType, ImmutableArray<TypeRef> methodTypeArguments, ImmutableArray<TypeRef> parameterTypes)
    {
        var declaringArguments = declaringType.Kind == TypeRefKind.GenericInstance
            ? declaringType.TypeArguments
            : [];
        return string.Join(",", parameterTypes.Select(parameter => NormalizeShape(parameter, declaringArguments, methodTypeArguments)));
    }

    static string NormalizeShape(TypeRef type, ImmutableArray<TypeRef> declaringArguments, ImmutableArray<TypeRef> methodArguments)
    {
        // Open-definition side: a type/method generic parameter is a positional marker.
        if (type.Kind == TypeRefKind.GenericParameter)
            return $"!{type.GenericParameterIndex}";
        if (type.Kind == TypeRefKind.MethodGenericParameter)
            return $"!!{type.GenericParameterIndex}";

        // Constructed-call-site side: a type equal to a declaring-type argument maps to
        // the matching type marker (!i), and one equal to a method type argument maps to
        // the method marker (!!i) — so List<int> under Box<int> reads as List<!0>
        // (matching open List<T> under Box<T>), and Id<int>(int) reads as !!0 (matching
        // open Id<T>(T)). Declaring args take precedence so the markers line up with the
        // open definition's parameter kinds.
        for (int i = 0; i < declaringArguments.Length; i++)
        {
            if (declaringArguments[i].Equals(type))
                return $"!{i}";
        }
        for (int i = 0; i < methodArguments.Length; i++)
        {
            if (methodArguments[i].Equals(type))
                return $"!!{i}";
        }

        return type.Kind switch
        {
            TypeRefKind.GenericInstance when type.ElementType is { } definition
                => $"{definition.Name}<{string.Join(",", type.TypeArguments.Select(argument => NormalizeShape(argument, declaringArguments, methodArguments)))}>",
            TypeRefKind.SzArray when type.ElementType is { } element
                => $"{NormalizeShape(element, declaringArguments, methodArguments)}[]",
            TypeRefKind.Array when type.ElementType is { } element
                => $"{NormalizeShape(element, declaringArguments, methodArguments)}[{new string(',', type.Rank > 0 ? type.Rank - 1 : 0)}]",
            TypeRefKind.ByRef when type.ElementType is { } element
                => $"ref {NormalizeShape(element, declaringArguments, methodArguments)}",
            TypeRefKind.Pointer when type.ElementType is { } element
                => $"{NormalizeShape(element, declaringArguments, methodArguments)}*",
            _ => type.ToQualifiedDisplayString(),
        };
    }
}
