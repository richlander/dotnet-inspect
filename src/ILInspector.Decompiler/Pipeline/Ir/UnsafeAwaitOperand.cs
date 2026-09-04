namespace ILInspector.Decompiler.Pipeline;

internal static class UnsafeAwaitOperand
{
    public static bool ContainsAwait(IrNode root)
        => IsAwaitSyntax(root) || root.Descendants.Any(IsAwaitSyntax);

    static bool IsAwaitSyntax(IrNode node)
        => node is AwaitExpression
            or UsingStatement { IsAwait: true }
            or ForeachStatement { IsAwait: true };

    public static bool WouldPlaceAwaitInUnsafeContext(
        IrNode root,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit = false)
        => ContainsAwait(root)
            && RequiresUnsafeContext(
                root,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit);

    public static bool RequiresUnsafeContext(
        IrNode root,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit = false)
        => ContainsUnsafeOperation(
            root,
            usesUpdatedMemorySafetyRules,
            skipLocalsInit);

    static bool ContainsUnsafeOperation(
        IrNode root,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit)
    {
        var evidence = new List<ConsumedMemberEvidence>();
        foreach (var node in root.Descendants.Prepend(root))
        {
            if (node is CallIndirect
                or StackAllocate
                or FixedBufferElementAddress
                || node is StackAllocArray stackAlloc
                    && (skipLocalsInit
                        || stackAlloc.ResultType?.Kind == TypeRefKind.Pointer)
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
                    && ((usesUpdatedMemorySafetyRules
                            && invocation.RequiresUnsafe)
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
            ? usesUpdatedMemorySafetyRules
            : method.RequiresUnsafeFact == MetadataFactState.Yes
                || method.RequiresUnsafeFact == MetadataFactState.Unknown
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

}
