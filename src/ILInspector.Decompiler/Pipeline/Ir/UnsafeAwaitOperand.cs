namespace ILInspector.Decompiler.Pipeline;

internal static class UnsafeAwaitOperand
{
    public static bool RequiresUnsafeContext(IrNode root)
    {
        var evidence = new List<ConsumedMemberEvidence>();
        foreach (var node in root.Descendants.Prepend(root))
        {
            if (node is IrExpression
                {
                    ResultType.Kind: TypeRefKind.Pointer or TypeRefKind.FunctionPointer,
                }
                or CallIndirect)
            {
                return true;
            }

            evidence.Clear();
            ConsumedMemberEvidence.AddFrom(node, evidence);
            if (evidence.Any(item => item.Method is { } method
                && (method.RequiresUnsafe
                    || ContainsPointer(method.ReturnType)
                    || method.ParameterTypes.Any(ContainsPointer))))
            {
                return true;
            }
        }
        return false;
    }

    static bool ContainsPointer(TypeRef? type)
        => type is not null
            && (type.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));
}
