namespace ILInspector.Decompiler.Pipeline;

internal static class UnsafeAwaitOperand
{
    public static bool ContainsAwait(IrNode root)
        => root is AwaitExpression || root.Descendants.OfType<AwaitExpression>().Any();

    public static bool WouldPlaceAwaitInUnsafeContext(
        IrNode root,
        bool usesUpdatedMemorySafetyRules)
        => ContainsAwait(root)
            && RequiresUnsafeContext(root, usesUpdatedMemorySafetyRules);

    public static bool RequiresUnsafeContext(
        IrNode root,
        bool usesUpdatedMemorySafetyRules)
        => ContainsUnsafeOperation(root, usesUpdatedMemorySafetyRules)
            || !usesUpdatedMemorySafetyRules && ContainsPointerSyntax(root);

    static bool ContainsUnsafeOperation(
        IrNode root,
        bool usesUpdatedMemorySafetyRules)
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
                || node is Call call && CallRendersPointerDereference(call)
                || node is NewObject creation
                    && ArgumentsRenderPointerDereference(
                        creation.Arguments,
                        creation.Constructor.ParameterTypes)
                || node is LoadProperty property && IsPointerReceiver(property.Instance)
                || node is StoreProperty propertyStore && IsPointerReceiver(propertyStore.Instance)
                || node is EventSubscription subscription && IsPointerReceiver(subscription.Instance)
                || node is LocalFunctionInvocation invocation
                    && (invocation.RequiresUnsafe
                        || !usesUpdatedMemorySafetyRules
                            && (ContainsPointer(invocation.ReturnType)
                                || invocation.ParameterTypes.Any(ContainsPointer))
                        || ArgumentsRenderPointerDereference(
                            invocation.Arguments,
                            invocation.ParameterTypes)))
            {
                return true;
            }

            evidence.Clear();
            ConsumedMemberEvidence.AddFrom(node, evidence);
            if (evidence.Any(item => item.Method is { } method
                && MethodRequiresUnsafe(method, usesUpdatedMemorySafetyRules)))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool MethodRequiresUnsafe(
        MethodRef method,
        bool usesUpdatedMemorySafetyRules)
        => method.RequiresUnsafe
            || method.RequiresUnsafeFact == MetadataFactState.Yes
            || (!usesUpdatedMemorySafetyRules
                || method.RequiresUnsafeFact == MetadataFactState.Unknown)
            && (ContainsPointer(method.ReturnType)
                || method.ParameterTypes.Any(ContainsPointer));

    static bool CallRendersPointerDereference(Call call)
    {
        IReadOnlyList<IrExpression> arguments = call.Arguments;
        if (call.Callee.HasThis)
        {
            if (arguments is not [var receiver, ..])
                return false;
            if (IsPointerReceiver(receiver))
                return true;
            arguments = [.. arguments.Skip(1)];
        }
        return ArgumentsRenderPointerDereference(
            arguments,
            call.Callee.ParameterTypes);
    }

    static bool ArgumentsRenderPointerDereference(
        IReadOnlyList<IrExpression> arguments,
        IReadOnlyList<TypeRef> parameterTypes)
        => arguments
            .Select((argument, index) => (argument, index))
            .Any(pair => pair.index < parameterTypes.Count
                && parameterTypes[pair.index].Kind == TypeRefKind.ByRef
                && pair.argument.ResultType?.Kind == TypeRefKind.Pointer);

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

    static bool ContainsPointerSyntax(IrNode root)
        => root.Descendants.Prepend(root).Any(node =>
            node is Fixed or StackAllocate
            || node.DirectTypes.Any(ContainsPointer)
            || node is IrExpression expression
                && ContainsPointer(expression.ResultType));
}
