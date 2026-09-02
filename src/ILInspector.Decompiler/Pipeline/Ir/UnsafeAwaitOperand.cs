namespace ILInspector.Decompiler.Pipeline;

internal static class UnsafeAwaitOperand
{
    public static bool ContainsAwait(IrNode root)
        => root is AwaitExpression || root.Descendants.OfType<AwaitExpression>().Any();

    public static bool WouldPlaceAwaitInUnsafeContext(IrNode root)
        => ContainsAwait(root) && RequiresUnsafeContext(root);

    public static bool RequiresUnsafeContext(IrNode root)
    {
        var evidence = new List<ConsumedMemberEvidence>();
        foreach (var node in root.Descendants.Prepend(root))
        {
            if (node is CallIndirect
                or StackAllocate
                or FixedBufferElementAddress
                || node is LoadIndirect load && RendersAsPointerDereference(load.Address)
                || node is StoreIndirect store && RendersAsPointerDereference(store.Address)
                || node is InitObject init && RendersAsPointerDereference(init.Address)
                || node is LoadField field && IsPointerReceiver(field.Instance)
                || node is StoreField fieldStore && IsPointerReceiver(fieldStore.Instance)
                || node is LoadFieldAddress fieldAddress && IsPointerReceiver(fieldAddress.Instance)
                || node is LocalFunctionInvocation invocation
                    && (invocation.RequiresUnsafe
                        || ContainsPointer(invocation.ReturnType)
                        || invocation.ParameterTypes.Any(ContainsPointer)))
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

    static bool IsPointerReceiver(IrExpression? receiver)
        => receiver?.ResultType?.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer;

    static bool RendersAsPointerDereference(IrExpression address)
        => address switch
        {
            LoadArgument { Index: 0, Name: "this" } => false,
            LoadLocalAddress => false,
            LoadArgumentAddress => false,
            LoadFieldAddress => false,
            FixedBufferElementAddress => false,
            LoadElementAddress => false,
            { ResultType.Kind: TypeRefKind.ByRef } => false,
            _ => true,
        };

    static bool ContainsPointer(TypeRef? type)
        => type is not null
            && (type.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));
}
