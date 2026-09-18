namespace ILInspector.Decompiler.Pipeline;

internal static class OperationMemorySafetyContract
{
    internal static bool RequiresUnsafe(
        IrNode node,
        bool callerUsesUpdatedRules,
        bool skipLocalsInit,
        List<ConsumedMemberEvidence>? consumedMembers = null,
        Func<IrNode, TypeRef?>? refBindingTargetType = null)
    {
        consumedMembers ??= [];
        consumedMembers.Clear();
        ConsumedMemberEvidence.AddFrom(node, consumedMembers);
        if (consumedMembers.Any(item =>
            item.Method is { } method
                && MethodRequiresUnsafe(method, callerUsesUpdatedRules)
            || item.Field is { } field
                && FieldMemorySafetyContract.RequiresUnsafe(
                    field,
                    callerUsesUpdatedRules,
                    legacyShapeRequiresUnsafe:
                        field.FixedBuffer is null
                        && ContainsPointer(field.Type))))
        {
            return true;
        }

        return RequiresUnsafeRefBinding(
                node,
                refBindingTargetType?.Invoke(node)
                    ?? InferredRefBindingTargetType(node))
            || RequiresUnsafeOperation(
                node,
                callerUsesUpdatedRules,
                skipLocalsInit);
    }

    static bool RequiresUnsafeOperation(
        IrNode node,
        bool callerUsesUpdatedRules,
        bool skipLocalsInit)
        => node switch
        {
            CallIndirect => true,
            StackAllocate => true,
            StackAllocArray stackAlloc =>
                skipLocalsInit
                || stackAlloc.ResultType?.Kind == TypeRefKind.Pointer,
            Call call => CallRendersPointerDereference(call),
            NewObject creation => ArgumentsRenderPointerDereference(
                creation.Arguments,
                creation.Constructor.ParameterTypes),
            Convert conversion => IsUnboxPointerConversion(conversion),
            LoadField field => IsPointerReceiver(field.Instance),
            StoreField field => IsPointerReceiver(field.Instance),
            LoadFieldAddress field => IsPointerReceiver(field.Instance),
            LoadProperty property => IsPointerReceiver(property.Instance),
            StoreProperty property => IsPointerReceiver(property.Instance),
            EventSubscription subscription => IsPointerReceiver(subscription.Instance),
            FixedBufferElementAddress => true,
            LoadIndirect { Address: FixedBufferElementAddress } => true,
            StoreIndirect { Address: FixedBufferElementAddress } => true,
            LoadIndirect load => RendersAsPointerDereference(load.Address),
            StoreIndirect store => RendersAsPointerDereference(store.Address),
            PointerElementCompoundAssignment => true,
            InitObject init => RendersAsPointerDereference(init.Address),
            LocalFunctionInvocation invocation =>
                callerUsesUpdatedRules && invocation.RequiresUnsafe
                || !callerUsesUpdatedRules
                    && SignatureRequiresUnsafe(
                        invocation.ReturnType,
                        invocation.ParameterTypes)
                || ArgumentsRenderPointerDereference(
                    invocation.Arguments,
                    invocation.ParameterTypes),
            NullCoalescingFieldAssignment assignment =>
                IsPointerReceiver(assignment.Instance),
            NullCoalescingFieldAssignmentExpression assignment =>
                IsPointerReceiver(assignment.Instance),
            NullCoalescingPropertyAssignment assignment =>
                IsPointerReceiver(assignment.Instance),
            DeconstructionAssignment assignment =>
                assignment.Targets.Any(target =>
                    target is
                    {
                        Kind: DeconstructionTargetKind.Property,
                        Instance: { } instance,
                    }
                    && IsPointerReceiver(instance)),
            DelegateCreation creation => IsPointerReceiver(creation.Target),
            _ => false,
        };

    internal static bool RequiresUnsafeRefBinding(
        IrNode node,
        TypeRef? targetType)
        => targetType?.Kind == TypeRefKind.ByRef
            && node switch
            {
                StoreLocal store => RendersAsPointerDereference(store.Value),
                StoreStackSlot store => RendersAsPointerDereference(store.Value),
                Return { Value: { } value } => RendersAsPointerDereference(value),
                _ => false,
            };

    internal static bool MethodRequiresUnsafe(
        MethodRef method,
        bool callerUsesUpdatedRules)
        => MethodMemorySafetyContract.RequiresUnsafe(
            method,
            callerUsesUpdatedRules,
            SignatureRequiresUnsafe(method.ReturnType, method.ParameterTypes));

    internal static bool IsLegacyPointerOperation(IrNode node) => node switch
    {
        StoreLocal store => ContainsPointer(store.Type),
        StoreStackSlot store => ContainsPointer(store.Value.ResultType),
        Fixed => true,
        Lambda lambda => !lambda.ParameterRefKinds.IsDefaultOrEmpty
            && lambda.Parameters.Any(parameter => ContainsPointer(parameter.Type)),
        LocalFunctionStatement localFunction => ContainsPointer(localFunction.ReturnType)
            || localFunction.Parameters.Any(parameter => ContainsPointer(parameter.Type)),
        ForeachStatement foreachStatement => ContainsPointer(foreachStatement.LocalType),
        LoadField field => ContainsPointer(field.Field.Type),
        StoreField field => ContainsPointer(field.Field.Type),
        LoadFieldAddress field => ContainsPointer(field.Field.Type),
        AddressOfMethod => true,
        Convert
        {
            Operand: LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress
                or FixedBufferElementAddress or LoadElementAddress,
        } => true,
        SizeOf sizeOf => ContainsPointer(sizeOf.Type),
        Binary binary => binary.Kind is BinaryKind.Add or BinaryKind.Subtract
            && (binary.Left.ResultType is { Kind: TypeRefKind.Pointer }
                || binary.Right.ResultType is { Kind: TypeRefKind.Pointer }),
        Comparison comparison =>
            comparison.Left.ResultType is { Kind: TypeRefKind.Pointer }
            || comparison.Right.ResultType is { Kind: TypeRefKind.Pointer },
        IncrementDecrement increment =>
            increment.Target.ResultType is { Kind: TypeRefKind.Pointer },
        _ => false,
    };

    internal static bool ContainsPointer(TypeRef? type)
        => type is not null
            && (type.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));

    internal static bool RendersAsPointerDereference(IrExpression address)
        => address switch
        {
            LoadArgument { Index: 0, Name: "this" } => false,
            LoadLocalAddress => false,
            LoadArgumentAddress => false,
            LoadFieldAddress => false,
            FixedBufferElementAddress => false,
            LoadElementAddress => false,
            Conditional { ResultType.Kind: TypeRefKind.ByRef } conditional
                when conditional.WhenTrue.ResultType?.Kind == TypeRefKind.ByRef
                    && conditional.WhenFalse.ResultType?.Kind == TypeRefKind.ByRef => false,
            { ResultType.Kind: TypeRefKind.ByRef } => false,
            _ => true,
        };

    internal static bool IsUnboxPointerConversion(Convert conversion)
        => conversion.Target is
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary,
                Namespace: "System",
                Name: "IntPtr" or "UIntPtr",
            }
            && conversion.Operand is Unbox;

    internal static bool IsPointerReceiver(IrExpression? receiver)
        => receiver?.ResultType is { Kind: TypeRefKind.Pointer };

    static TypeRef? InferredRefBindingTargetType(IrNode node)
        => node switch
        {
            StoreLocal store => store.Type,
            StoreStackSlot store => InferredStackSlotTargetType(store),
            Return returnStatement => EnclosingReturnType(returnStatement),
            _ => null,
        };

    static TypeRef? InferredStackSlotTargetType(StoreStackSlot store)
        => EnclosingScopeNodes(store)
            .OfType<LoadStackSlot>()
            .FirstOrDefault(load =>
                load.Slot == store.Slot
                && load.Type?.Kind == TypeRefKind.ByRef)
            ?.Type;

    static IEnumerable<IrNode> EnclosingScopeNodes(IrNode node)
    {
        for (IrNode? current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case Lambda lambda:
                    return lambda.Body.DescendantsAndSelfOutsideNestedFunctions;
                case LocalFunctionStatement localFunction:
                    return localFunction.Body.DescendantsAndSelfOutsideNestedFunctions;
                case IrFunction function:
                    return function.Body.DescendantsAndSelfOutsideNestedFunctions;
            }
        }
        return [];
    }

    static TypeRef? EnclosingReturnType(Return returnStatement)
    {
        for (IrNode? current = returnStatement.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case Lambda:
                    return null;
                case LocalFunctionStatement localFunction:
                    return localFunction.ReturnType;
                case IrFunction function:
                    return function.Signature.ReturnType;
            }
        }
        return null;
    }

    static bool CallRendersPointerDereference(Call call)
    {
        if (!call.Callee.HasThis)
            return ArgumentsRenderPointerDereference(
                call.Arguments,
                call.Callee.ParameterTypes);
        if (call.Arguments is not [var receiver, ..])
            return false;
        return IsPointerReceiver(receiver)
            || ArgumentsRenderPointerDereference(
                [.. call.Arguments.Skip(1)],
                call.Callee.ParameterTypes);
    }

    static bool ArgumentsRenderPointerDereference(
        IReadOnlyList<IrExpression> arguments,
        IReadOnlyList<TypeRef> parameterTypes)
        => arguments
            .Select((argument, index) => (argument, index))
            .Any(pair =>
                pair.index < parameterTypes.Count
                && pair.argument.ResultType is
                {
                    Kind: TypeRefKind.Pointer,
                    ElementType: { } pointee,
                }
                && parameterTypes[pair.index] is
                {
                    Kind: TypeRefKind.ByRef,
                    ElementType: { } byRefTarget,
                }
                && pointee.Equals(byRefTarget));

    static bool SignatureRequiresUnsafe(
        TypeRef returnType,
        IEnumerable<TypeRef> parameterTypes)
        => ContainsPointer(returnType) || parameterTypes.Any(ContainsPointer);
}
