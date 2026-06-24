using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

static class CorpusMethodIdentity
{
    public static string SignatureText(MethodSignature signature)
    {
        string arity = signature.GenericParameterCount == 0 ? "" : $"`{signature.GenericParameterCount}";
        return $"{arity}({string.Join(", ", signature.Parameters.Select(p => TypeText(p.Type)))}) -> {TypeText(signature.ReturnType)}";
    }

    static string TypeText(TypeRef type) => type.Kind switch
    {
        TypeRefKind.Definition => NamedTypeText(type),
        TypeRefKind.GenericInstance => $"{TypeText(type.ElementType!)}<{string.Join(", ", type.TypeArguments.Select(TypeText))}>",
        TypeRefKind.SzArray => $"{TypeText(type.ElementType!)}[]",
        TypeRefKind.Array => $"{TypeText(type.ElementType!)}[{new string(',', type.Rank - 1)}]",
        TypeRefKind.ByRef => $"ref {TypeText(type.ElementType!)}",
        TypeRefKind.Pointer => $"{TypeText(type.ElementType!)}*",
        TypeRefKind.Pinned => $"pinned {TypeText(type.ElementType!)}",
        TypeRefKind.GenericParameter => $"!{type.GenericParameterIndex}",
        TypeRefKind.MethodGenericParameter => $"!!{type.GenericParameterIndex}",
        TypeRefKind.FunctionPointer => FunctionPointerText(type),
        TypeRefKind.Unsupported => $"<unsupported:{type.UnsupportedReason}>",
        _ => type.ToDisplayString(),
    };

    static string NamedTypeText(TypeRef type)
    {
        string ns = type.Namespace.Length == 0 ? "" : $"{type.Namespace}.";
        return $"{type.Assembly}:{ns}{type.Name}";
    }

    static string FunctionPointerText(TypeRef type)
    {
        var parts = type.TypeArguments.Select(TypeText).Append(TypeText(type.ElementType!));
        string convention = type.CallingConvention.Length == 0 ? "" : $" {type.CallingConvention}";
        return $"delegate*{convention}<{string.Join(", ", parts)}>";
    }
}
