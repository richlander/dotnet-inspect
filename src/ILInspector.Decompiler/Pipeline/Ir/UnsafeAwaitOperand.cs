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
            if (!usesUpdatedMemorySafetyRules && IsLegacyPointerOperation(node))
                return true;

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

    internal static bool CanScopeLegacyPointerLocal(
        IrFunction function,
        StoreLocal store)
        => !ReferencesLocal(store.Value, store.Index)
            && ReferencesStayInAwaitFreeStoreRange(
                function,
                store,
                candidate => candidate is StoreLocal local
                    && local.Index == store.Index
                    || candidate is LoadLocal load
                        && load.Index == store.Index
                    || candidate is LoadLocalAddress address
                        && address.Index == store.Index);

    internal static bool CanScopeLegacyPointerStackSlot(
        IrFunction function,
        StoreStackSlot store)
        => !ReferencesStackSlot(store.Value, store.Slot)
            && ReferencesStayInAwaitFreeStoreRange(
                function,
                store,
                candidate => candidate is StoreStackSlot slotStore
                    && slotStore.Slot == store.Slot
                    || candidate is LoadStackSlot load
                        && load.Slot == store.Slot);

    static bool ReferencesStayInAwaitFreeStoreRange(
        IrFunction function,
        IrNode store,
        Func<IrNode, bool> isReference)
    {
        if (store.Parent is not Block block || store.ChildIndex < 0)
            return false;

        var references = function.DescendantsOutsideNestedFunctions
            .Where(isReference)
            .ToList();
        int lastReference = store.ChildIndex;
        foreach (var reference in references)
        {
            int statementIndex = DirectChildIndex(block, reference);
            if (statementIndex < store.ChildIndex)
                return false;
            lastReference = Math.Max(lastReference, statementIndex);
        }

        var range = block.Children
            .Skip(store.ChildIndex)
            .Take(lastReference - store.ChildIndex + 1)
            .ToList();
        return !range.Any(ContainsAwait)
            && !range.SelectMany(
                    statement => statement.DescendantsAndSelfOutsideNestedFunctions)
                .Any(node => node is Branch
                    or ConditionalBranch
                    or SwitchBranch
                    or Leave);
    }

    static int DirectChildIndex(Block block, IrNode node)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, block))
                return current.ChildIndex;
        }
        return -1;
    }

    static bool ReferencesLocal(IrNode node, int index)
        => node.DescendantsAndSelfOutsideNestedFunctions.Any(candidate =>
            candidate is StoreLocal store && store.Index == index
            || candidate is LoadLocal load && load.Index == index
            || candidate is LoadLocalAddress address && address.Index == index);

    static bool ReferencesStackSlot(IrNode node, int slot)
        => node.DescendantsAndSelfOutsideNestedFunctions.Any(candidate =>
            candidate is StoreStackSlot store && store.Slot == slot
            || candidate is LoadStackSlot load && load.Slot == slot);

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

    internal static bool ContainsPointer(TypeRef? type)
        => type is not null
            && (type.Kind is TypeRefKind.Pointer or TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));

}
