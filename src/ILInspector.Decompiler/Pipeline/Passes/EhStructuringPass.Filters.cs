namespace ILInspector.Decompiler.Pipeline;

public sealed partial class EhStructuringPass
{
    sealed record FilterInfo(TypeRef ExceptionType, int? VariableIndex, IrExpression Condition);

    static bool ValidateFilters(IrFunction function, IReadOnlyList<Block> blocks, List<Construct> all, Dictionary<int, int> offsetToIndex)
    {
        foreach (var handler in all.SelectMany(c => c.Handlers).Where(h => h.Kind == HandlerKind.Filter))
        {
            var preferredVariable = PeekHandlerEntryVariable(blocks, handler, offsetToIndex);
            if (TryBuildFilter(function, blocks, offsetToIndex, handler, allocateVariable: false, preferredVariable: preferredVariable?.Index, preferredVariableType: preferredVariable?.Type) is null)
                return false;
        }
        return true;
    }

    static (int Index, TypeRef Type)? PeekHandlerEntryVariable(IReadOnlyList<Block> blocks, HandlerRegion handler, Dictionary<int, int> offsetToIndex)
    {
        if (!offsetToIndex.TryGetValue(handler.HandlerOffset, out int handlerIndex))
            return null;
        return blocks[handlerIndex].Children is [StoreLocal { Value: CaughtException } store, ..]
            ? (store.Index, store.Type)
            : null;
    }

    static FilterInfo? TryBuildFilter(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, HandlerRegion handler)
        => TryBuildFilter(null, blocks, offsetToIndex, handler, allocateVariable: false, preferredVariable: null, preferredVariableType: null);

    static FilterInfo? TryBuildFilter(
        IrFunction? function,
        IReadOnlyList<Block> blocks,
        Dictionary<int, int> offsetToIndex,
        HandlerRegion handler,
        bool allocateVariable,
        int? preferredVariable,
        TypeRef? preferredVariableType)
    {
        if (handler.Kind != HandlerKind.Filter
            || !offsetToIndex.TryGetValue(handler.FilterOffset, out int filterStart)
            || !offsetToIndex.TryGetValue(handler.HandlerOffset, out int handlerStart))
        {
            return null;
        }

        var filterBlocks = blocks.Skip(filterStart).Take(handlerStart - filterStart).ToArray();
        if (TryBuildSimpleCatchAllFilter(filterBlocks) is { } simple)
            return simple;
        if (TryBuildExceptionCaptureFilter(filterBlocks) is { } capture)
            return capture;
        if (TryBuildGlobalExceptionHandlerFilter(filterBlocks) is { } globalHandler)
            return globalHandler;

        if (TryBuildIOExceptionFilter(filterBlocks) is { } ioFilter)
        {
            if (preferredVariable is not null && ioFilter.VariableIndex != preferredVariable)
                return null;
            if (preferredVariableType is not null && !ioFilter.ExceptionType.Equals(preferredVariableType))
                return null;
            if (ioFilter.VariableIndex is { } ioVariable
                && LocalReferencedOutsideFilterHandler(blocks, handler, ioVariable))
            {
                return null;
            }
            return ioFilter;
        }
        if (TryBuildTwoTypeExceptionFilter(filterBlocks) is { } twoTypeFilter)
        {
            if (preferredVariable is not null && twoTypeFilter.VariableIndex != preferredVariable)
                return null;
            if (preferredVariableType is not null && !twoTypeFilter.ExceptionType.Equals(preferredVariableType))
                return null;
            if (twoTypeFilter.VariableIndex is { } twoTypeVariable
                && LocalReferencedOutsideFilterHandler(blocks, handler, twoTypeVariable))
            {
                return null;
            }
            return twoTypeFilter;
        }
        if (TryBuildThreeTypeExceptionFilter(blocks, handler, filterBlocks) is { } threeTypeFilter)
        {
            if (preferredVariable is not null && threeTypeFilter.VariableIndex != preferredVariable)
                return null;
            if (preferredVariableType is not null && !threeTypeFilter.ExceptionType.Equals(preferredVariableType))
                return null;
            if (threeTypeFilter.VariableIndex is { } threeTypeVariable
                && LocalReferencedOutsideFilterHandler(blocks, handler, threeTypeVariable))
            {
                return null;
            }
            return threeTypeFilter;
        }
        if (TryBuildIoRelatedDisposeFilter(filterBlocks) is { } disposeFilter)
        {
            if (preferredVariable is not null && disposeFilter.VariableIndex != preferredVariable)
                return null;
            if (preferredVariableType is not null && !disposeFilter.ExceptionType.Equals(preferredVariableType))
                return null;
            if (disposeFilter.VariableIndex is { } disposeVariable
                && LocalReferencedOutsideFilterHandler(blocks, handler, disposeVariable))
            {
                return null;
            }
            return disposeFilter;
        }

        return TryBuildTypedExceptionFilter(
            function,
            blocks,
            filterBlocks,
            handler,
            allocateVariable,
            preferredVariable,
            preferredVariableType);
    }

    static FilterInfo? TryBuildSimpleCatchAllFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not [var filter]
            || filter.Children is not [ExpressionStatement { Expression: CaughtException }, EndFilter { Value: var value }])
        {
            return null;
        }

        if (BoolFilterCondition(value) is not { } condition)
            return null;

        return new FilterInfo(TypeRef.CoreLib("System", "Object"), null, condition);
    }

    static IrExpression? BoolFilterCondition(IrExpression value)
    {
        // csc sometimes normalizes a bool filter through `(flag == false) > false`;
        // recover the source bool so the catch filter stays readable and valid.
        if (value is Comparison
            {
                Kind: ComparisonKind.GreaterThan,
                IsUnsigned: true,
                Left: Comparison
                {
                    Kind: ComparisonKind.Equal,
                    Left: var operand,
                    Right: Constant { Value: false or 0 },
                },
                Right: Constant { Value: false or 0 },
            })
        {
            return CloneFilterValue(operand);
        }

        if (value.ResultType is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return CloneFilterValue(value);

        return null;
    }

    static IrExpression? CloneFilterValue(IrExpression value) => value switch
    {
        LoadArgument argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
        LoadLocal local => new LoadLocal(local.Index, local.Type),
        Constant constant => new Constant(constant.Value, constant.Type),
        _ => null,
    };

    static FilterInfo? TryBuildExceptionCaptureFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || !exceptionType.Equals(TypeRef.CoreLib("System", "Exception"))
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                StoreStackSlot
                {
                    Slot: var trueVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: var conditionValue,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || trueVerdictSlot != verdictSlot
            || BoolFilterCondition(conditionValue) is not { } condition)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        return new FilterInfo(exceptionType, variableIndex, condition);
    }

    static FilterInfo? TryBuildGlobalExceptionHandlerFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || !exceptionType.Equals(TypeRef.CoreLib("System", "Exception"))
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreStackSlot
                {
                    Slot: var trueVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: Call
                        {
                            Callee: var callee,
                            IsVirtual: var isVirtual,
                            Arguments: [LoadStackSlot handledException],
                        } call,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || trueVerdictSlot != verdictSlot
            || handledException.Slot != copiedExceptionSlot
            || call.ConstrainedTo is not null
            || isVirtual
            || callee.Name != "IsHandledByGlobalHandler"
            || callee.DeclaringType.Name != "ExceptionHandling"
            || !callee.ReturnType.Equals(TypeRef.CoreLib("System", "Boolean")))
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var condition = new Call(callee, isVirtual: false, [new CaughtException(exceptionType)]);
        return new FilterInfo(exceptionType, null, condition);
    }

    static FilterInfo? TryBuildIOExceptionFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var innerExceptionTest,
                var directMatch,
                var join,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                ConditionalBranch { Condition: IsInstance { Type: var testedType, Operand: LoadLocal directOperand }, TargetOffset: var directMatchOffset },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || directOperand.Index != variableIndex
            || directMatchOffset != directMatch.StartOffset)
        {
            return null;
        }

        if (innerExceptionTest.Children is not
            [
                StoreStackSlot
                {
                    Slot: var innerResultSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: IsInstance
                        {
                            Type: var innerTestedType,
                            Operand: Call { Callee: var innerExceptionGetter, IsVirtual: true, Arguments: [LoadLocal innerReceiver] },
                        },
                        Right: Constant { Value: null },
                    },
                },
                Branch { TargetOffset: var joinOffset },
            ]
            || !innerTestedType.Equals(testedType)
            || innerReceiver.Index != variableIndex
            || joinOffset != join.StartOffset)
        {
            return null;
        }

        if (directMatch.Children is not
            [
                StoreStackSlot { Slot: var directResultSlot, Value: Constant { Value: true or 1 } },
            ]
            || directResultSlot != innerResultSlot)
        {
            return null;
        }

        if (join.Children is not
            [
                StoreStackSlot
                {
                    Slot: var joinedVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: LoadStackSlot joinedResult,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || joinedVerdictSlot != verdictSlot
            || joinedResult.Slot != innerResultSlot)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var variable = new LoadLocal(variableIndex, exceptionType);
        var direct = new IsInstance(testedType, new LoadLocal(variableIndex, exceptionType));
        var inner = new IsInstance(testedType, new Call(innerExceptionGetter, isVirtual: true, [variable]));
        return new FilterInfo(exceptionType, variableIndex, new LogicalBinary(LogicalKind.Or, direct, inner));
    }

    static FilterInfo? TryBuildTwoTypeExceptionFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var alternateTypeTest,
                var directMatch,
                var join,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset
            || !IsSupportedCatchFilterType(exceptionType))
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                ConditionalBranch { Condition: IsInstance { Type: var directTestedType, Operand: LoadLocal directOperand }, TargetOffset: var directMatchOffset },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || directOperand.Index != variableIndex
            || directMatchOffset != directMatch.StartOffset
            || !IsSupportedExceptionTypeTest(directTestedType))
        {
            return null;
        }

        if (alternateTypeTest.Children is not
            [
                StoreStackSlot
                {
                    Slot: var alternateResultSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: IsInstance { Type: var alternateTestedType, Operand: LoadLocal alternateOperand },
                        Right: Constant { Value: null },
                    },
                },
                Branch { TargetOffset: var joinOffset },
            ]
            || alternateOperand.Index != variableIndex
            || joinOffset != join.StartOffset
            || !IsSupportedExceptionTypeTest(alternateTestedType))
        {
            return null;
        }

        if (directMatch.Children is not
            [
                StoreStackSlot { Slot: var directResultSlot, Value: Constant { Value: true or 1 } },
            ]
            || directResultSlot != alternateResultSlot)
        {
            return null;
        }

        if (join.Children is not
            [
                StoreStackSlot
                {
                    Slot: var joinedVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: LoadStackSlot joinedResult,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || joinedVerdictSlot != verdictSlot
            || joinedResult.Slot != alternateResultSlot)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var direct = new IsInstance(directTestedType, new LoadLocal(variableIndex, exceptionType));
        var alternate = new IsInstance(alternateTestedType, new LoadLocal(variableIndex, exceptionType));
        return new FilterInfo(exceptionType, variableIndex, new LogicalBinary(LogicalKind.Or, direct, alternate));
    }

    static FilterInfo? TryBuildThreeTypeExceptionFilter(
        IReadOnlyList<Block> blocks,
        HandlerRegion handler,
        IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var firstTest,
                var secondTest,
                var thirdTest,
                var trueVerdict,
                var falseVerdict,
                var join,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var firstTestOffset },
            ]
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || firstTestOffset != firstTest.StartOffset
            || !IsSupportedCatchFilterType(exceptionType))
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (firstTest.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                ConditionalBranch { Condition: IsInstance { Type: var firstType, Operand: LoadLocal firstOperand }, TargetOffset: var firstTrueOffset },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || firstOperand.Index != variableIndex
            || firstTrueOffset != trueVerdict.StartOffset
            || !IsSupportedExceptionTypeTest(firstType))
        {
            return null;
        }

        if (secondTest.Children is not
            [
                ConditionalBranch { Condition: IsInstance { Type: var secondType, Operand: LoadLocal secondOperand }, TargetOffset: var secondTrueOffset },
            ]
            || secondOperand.Index != variableIndex
            || secondTrueOffset != trueVerdict.StartOffset
            || !IsSupportedExceptionTypeTest(secondType))
        {
            return null;
        }

        if (thirdTest.Children is not
            [
                ConditionalBranch
                {
                    Condition: LogicalNot { Operand: IsInstance { Type: var thirdType, Operand: LoadLocal thirdOperand } },
                    TargetOffset: var falseOffset,
                },
            ]
            || thirdOperand.Index != variableIndex
            || falseOffset != falseVerdict.StartOffset
            || !IsSupportedExceptionTypeTest(thirdType))
        {
            return null;
        }

        if (firstType.Equals(secondType) || firstType.Equals(thirdType) || secondType.Equals(thirdType))
            return null;

        if (trueVerdict.Children is not
            [
                StoreLocal { Index: var verdictLocal, Type: var verdictLocalType, Value: Constant { Value: true or 1 } },
                Branch { TargetOffset: var joinOffset },
            ]
            || joinOffset != join.StartOffset)
        {
            return null;
        }

        if (falseVerdict.Children is not
            [
                StoreLocal { Index: var falseVerdictLocal, Type: var falseVerdictLocalType, Value: Constant { Value: false or 0 } },
            ]
            || falseVerdictLocal != verdictLocal
            || !falseVerdictLocalType.Equals(verdictLocalType)
            || LocalReferencedOutsideFilterHandler(blocks, handler, verdictLocal))
        {
            return null;
        }

        if (join.Children is not
            [
                StoreStackSlot
                {
                    Slot: var joinedVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: LoadLocal joinedResult,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || joinedVerdictSlot != verdictSlot
            || joinedResult.Index != verdictLocal)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var first = new IsInstance(firstType, new LoadLocal(variableIndex, exceptionType));
        var second = new IsInstance(secondType, new LoadLocal(variableIndex, exceptionType));
        var third = new IsInstance(thirdType, new LoadLocal(variableIndex, exceptionType));
        return new FilterInfo(
            exceptionType,
            variableIndex,
            new LogicalBinary(LogicalKind.Or, new LogicalBinary(LogicalKind.Or, first, second), third));
    }

    static FilterInfo? TryBuildIoRelatedDisposeFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var helperCall,
                var suppressed,
                var join,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || !exceptionType.Equals(TypeRef.CoreLib("System", "Exception"))
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                ConditionalBranch { Condition: var guardValue, TargetOffset: var suppressedOffset },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || suppressedOffset != suppressed.StartOffset
            || BoolFilterCondition(guardValue) is not { } guard)
        {
            return null;
        }

        if (helperCall.Children is not
            [
                StoreStackSlot
                {
                    Slot: var helperResultSlot,
                    Value: Call { Arguments: [LoadLocal helperArgument] } helper,
                },
                Branch { TargetOffset: var joinOffset },
            ]
            || helperArgument.Index != variableIndex
            || joinOffset != join.StartOffset
            || !MemberIdentity.IsFileStreamHelpersIsIoRelatedException(helper))
        {
            return null;
        }

        if (suppressed.Children is not
            [
                StoreStackSlot { Slot: var suppressedResultSlot, Value: Constant { Value: false or 0 } },
            ]
            || suppressedResultSlot != helperResultSlot)
        {
            return null;
        }

        if (join.Children is not
            [
                StoreStackSlot
                {
                    Slot: var joinedVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: LoadStackSlot joinedResult,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || joinedVerdictSlot != verdictSlot
            || joinedResult.Slot != helperResultSlot)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var helperCondition = new Call(helper.Callee, isVirtual: false, [new LoadLocal(variableIndex, exceptionType)]);
        return new FilterInfo(
            exceptionType,
            variableIndex,
            new LogicalBinary(LogicalKind.And, new LogicalNot(guard), helperCondition));
    }

    static FilterInfo? TryBuildTypedExceptionFilter(
        IrFunction? function,
        IReadOnlyList<Block> blocks,
        IReadOnlyList<Block> filterBlocks,
        HandlerRegion handler,
        bool allocateVariable,
        int? preferredVariable,
        TypeRef? preferredVariableType)
    {
        if (filterBlocks.Count != 4)
            return null;

        var exceptionAliases = new HashSet<int>();
        var exceptionAliasTypes = new Dictionary<int, TypeRef>();
        var exceptionAliasRoots = new Dictionary<int, int>();
        var entryChildren = filterBlocks[0].Children;
        if (entryChildren.Count == 0
            || entryChildren[^1] is not ConditionalBranch { Condition: LoadStackSlot isInstLoad } branch)
        {
            return null;
        }
        foreach (var entryStore in entryChildren.Take(entryChildren.Count - 1))
        {
            if (entryStore is not StoreStackSlot store)
                return null;
            switch (store.Value)
            {
                case IsInstance { Operand: CaughtException, Type: var type }:
                    exceptionAliases.Add(store.Slot);
                    exceptionAliasTypes[store.Slot] = type;
                    exceptionAliasRoots[store.Slot] = store.Slot;
                    break;
                case LoadStackSlot load when exceptionAliases.Contains(load.Slot):
                    exceptionAliases.Add(store.Slot);
                    exceptionAliasTypes[store.Slot] = exceptionAliasTypes[load.Slot];
                    exceptionAliasRoots[store.Slot] = exceptionAliasRoots[load.Slot];
                    break;
                default:
                    return null;
            }
        }

        if (!exceptionAliasTypes.TryGetValue(isInstLoad.Slot, out var exceptionType))
            return null;
        if (!IsSupportedCatchFilterType(exceptionType))
            return null;
        if (preferredVariable is not null && !exceptionType.Equals(preferredVariableType))
            return null;
        int testedRoot = exceptionAliasRoots[isInstLoad.Slot];
        var testedAliases = exceptionAliasRoots
            .Where(kvp => kvp.Value == testedRoot)
            .Select(kvp => kvp.Key)
            .ToHashSet();

        var falseBlock = filterBlocks[1];
        var trueBlock = filterBlocks.FirstOrDefault(block => block.StartOffset == branch.TargetOffset);
        var endFilterBlock = filterBlocks[^1];
        if (trueBlock is null || ReferenceEquals(trueBlock, falseBlock) || ReferenceEquals(trueBlock, endFilterBlock))
            return null;
        if (endFilterBlock.Children is not [EndFilter { Value: LoadStackSlot resultLoad }])
            return null;
        if (falseBlock.Children is not [StoreStackSlot { Value: Constant { Value: 0 } } falseStore, Branch falseBranch]
            || falseStore.Slot != resultLoad.Slot
            || falseBranch.TargetOffset != endFilterBlock.StartOffset)
        {
            return null;
        }

        var trueChildren = trueBlock.Children;
        Branch? trueBranch = null;
        if (trueChildren is [.., Branch branchToEnd])
        {
            if (branchToEnd.TargetOffset != endFilterBlock.StartOffset)
                return null;
            trueBranch = branchToEnd;
        }
        var trueStatements = trueBranch is null ? trueChildren : trueChildren.Take(trueChildren.Count - 1).ToList();
        if (trueStatements.Count == 0 || trueStatements[^1] is not StoreStackSlot predicateStore || predicateStore.Slot != resultLoad.Slot)
            return null;
        if (predicateStore.Value is Constant { Value: 0 })
            return null;
        foreach (var statement in trueStatements.Take(trueStatements.Count - 1))
            if (statement is not StoreLocal)
                return null;

        var exceptionLocalStoreNodes = filterBlocks
            .SelectMany(b => b.Children)
            .OfType<StoreLocal>()
            .Where(s => s.Value is LoadStackSlot load
                && testedAliases.Contains(load.Slot)
                && s.Type.Equals(exceptionType))
            .ToList();
        var exceptionLocalStores = exceptionLocalStoreNodes
            .Select(s => s.Index)
            .Distinct()
            .ToList();
        var existingVariable = exceptionLocalStores.Count > 0 ? exceptionLocalStores[0] : (int?)null;

        int selectedVariable = preferredVariable ?? existingVariable ?? (allocateVariable && function is not null ? function.AddSynthesizedLocal(exceptionType, "e") : -1);
        if (selectedVariable >= 0 && LocalReferencedOutsideFilterHandler(blocks, handler, selectedVariable))
            return null;
        if (selectedVariable >= 0 && !ValidateSelectedFilterLocal(filterBlocks, testedAliases, selectedVariable, exceptionType))
            return null;

        var ignoredExceptionStores = new HashSet<StoreLocal>();
        foreach (var store in exceptionLocalStoreNodes.Where(s => s.Index != selectedVariable))
        {
            bool loaded = blocks.Any(block => block.Descendants.OfType<LoadLocal>().Any(load => load.Index == store.Index)
                || block.Descendants.OfType<LoadLocalAddress>().Any(load => load.Index == store.Index));
            if (loaded)
                continue;
            ignoredExceptionStores.Add(store);
        }
        var filter = (IrExpression)predicateStore.Value.Clone();
        if (!ReplaceExceptionAliasLoads(filter, testedAliases, selectedVariable, exceptionType, out var replacementRoot))
            return null;
        filter = (IrExpression)(replacementRoot ?? filter);
        if (!InlineFilterLocalLoads(filter, blocks, filterBlocks, ignoredExceptionStores, testedAliases, selectedVariable, exceptionType, out replacementRoot))
            return null;
        filter = (IrExpression)(replacementRoot ?? filter);
        if (ContainsExceptionAliasLoad(filter, exceptionAliases))
            return null;
        filter = NormalizeFilterCondition(filter);
        return new FilterInfo(exceptionType, selectedVariable >= 0 ? selectedVariable : null, filter);
    }

    static bool InlineFilterLocalLoads(
        IrNode node,
        IReadOnlyList<Block> allBlocks,
        IReadOnlyList<Block> filterBlocks,
        HashSet<StoreLocal> ignoredExceptionStores,
        HashSet<int> exceptionAliases,
        int exceptionVariable,
        TypeRef exceptionType,
        out IrNode? replacementRoot)
    {
        replacementRoot = null;
        var stores = new Dictionary<int, StoreLocal>();
        foreach (var store in filterBlocks.SelectMany(b => b.Children).OfType<StoreLocal>())
        {
            if (store.Index == exceptionVariable || ignoredExceptionStores.Contains(store))
                continue;
            if (!IsInlineSafeFilterTemp(store.Value, exceptionAliases))
                return false;
            if (stores.ContainsKey(store.Index))
                return false;
            stores[store.Index] = store;
        }
        if (stores.Count == 0)
            return true;
        foreach (var index in stores.Keys)
        {
            int storeCount = allBlocks.SelectMany(b => b.Descendants).OfType<StoreLocal>().Count(s => s.Index == index);
            int loadCount = allBlocks.SelectMany(b => b.Descendants).OfType<LoadLocal>().Count(l => l.Index == index);
            int addressCount = allBlocks.SelectMany(b => b.Descendants).OfType<LoadLocalAddress>().Count(l => l.Index == index);
            if (storeCount != 1 || loadCount != 1 || addressCount != 0)
                return false;
        }

        var replacements = new List<(IrNode Old, IrNode New)>();
        var usedStores = new Dictionary<int, int>();
        bool ok = true;
        Visit(node);
        if (!ok)
            return false;
        if (usedStores.Count != stores.Count)
            return false;
        foreach (var (old, replacement) in replacements)
        {
            if (ReferenceEquals(old, node))
                replacementRoot = replacement;
            else
                old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            if (current is LoadLocal load && stores.TryGetValue(load.Index, out var store))
            {
                if (usedStores.TryGetValue(load.Index, out int useCount))
                {
                    ok = false;
                    return;
                }
                usedStores[load.Index] = useCount + 1;
                var clone = (IrExpression)store.Value.Clone();
                if (!ReplaceExceptionAliasLoads(clone, exceptionAliases, exceptionVariable, exceptionType, out var root))
                {
                    ok = false;
                    return;
                }
                replacements.Add((current, root ?? clone));
                return;
            }
            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static bool IsInlineSafeFilterTemp(IrExpression value, HashSet<int> exceptionAliases)
        => value is Constant
            || value is LoadStackSlot load && exceptionAliases.Contains(load.Slot);

    static bool IsSupportedCatchFilterType(TypeRef type)
        => IsSupportedExceptionTypeTest(type);

    static bool IsSupportedExceptionTypeTest(TypeRef type)
        => type.Kind == TypeRefKind.Definition
            && type.Assembly == TypeRef.CoreLibrary
            && type.DeclaredValueTypeHint != ValueTypeHint.ValueType
            && type.Name.EndsWith("Exception", StringComparison.Ordinal);

    static bool ContainsExceptionAliasLoad(IrNode node, HashSet<int> aliases)
        => node is LoadStackSlot load && aliases.Contains(load.Slot)
            || node.Children.Any(child => ContainsExceptionAliasLoad(child, aliases));

    static bool ValidateSelectedFilterLocal(
        IReadOnlyList<Block> filterBlocks,
        HashSet<int> testedAliases,
        int localIndex,
        TypeRef exceptionType)
    {
        bool hasAliasStore = false;
        bool hasLoad = false;
        foreach (var node in filterBlocks.SelectMany(block => block.Descendants))
        {
            switch (node)
            {
                case StoreLocal store when store.Index == localIndex:
                    if (store.Type.Equals(exceptionType)
                        && store.Value is LoadStackSlot aliasLoad
                        && testedAliases.Contains(aliasLoad.Slot))
                    {
                        hasAliasStore = true;
                        break;
                    }
                    return false;
                case LoadLocal localLoad when localLoad.Index == localIndex:
                    hasLoad = true;
                    break;
                case LoadLocalAddress address when address.Index == localIndex:
                    return false;
            }
        }
        return !hasLoad || hasAliasStore;
    }

    static bool LocalReferencedOutsideFilterHandler(IReadOnlyList<Block> blocks, HandlerRegion handler, int localIndex)
    {
        int scopeStart = handler.FilterOffset;
        int scopeEnd = handler.HandlerOffset + handler.HandlerLength;
        return blocks.Any(block => (block.StartOffset < scopeStart || block.StartOffset >= scopeEnd)
            && block.Descendants.Any(node => node switch
            {
                StoreLocal store => store.Index == localIndex,
                LoadLocal load => load.Index == localIndex,
                LoadLocalAddress address => address.Index == localIndex,
                _ => false,
            }));
    }

    static IrExpression NormalizeFilterCondition(IrExpression expression)
        => expression is Comparison
        {
            Kind: ComparisonKind.GreaterThan,
            IsUnsigned: true,
            Left.ResultType: { Namespace: "System", Name: "Boolean" },
            Right: Constant { Value: false }
        } comparison
            ? (IrExpression)comparison.DetachChildren()[0]
            : expression;

    static bool ReplaceExceptionAliasLoads(
        IrNode node,
        HashSet<int> aliases,
        int localIndex,
        TypeRef localType,
        out IrNode? replacementRoot)
    {
        replacementRoot = null;
        var replacements = new List<(IrNode Old, IrNode New)>();
        bool ok = true;
        Visit(node);
        if (!ok)
            return false;
        foreach (var (old, replacement) in replacements)
        {
            if (ReferenceEquals(old, node))
                replacementRoot = replacement;
            else
                old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            switch (current)
            {
                case LoadStackSlot load when aliases.Contains(load.Slot):
                    replacements.Add((current, new LoadLocal(localIndex, localType)));
                    return;
                case CaughtException:
                    ok = false;
                    return;
            }
            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static void ReplaceCaughtExceptions(BlockContainer body, int localIndex, TypeRef localType)
    {
        Visit(body);

        void Visit(IrNode node)
        {
            if (node is CaughtException caught)
            {
                if (!IsBareRethrow(caught))
                    caught.ReplaceWith(new LoadLocal(localIndex, localType));
                return;
            }
            if (node is TryCatch nested)
            {
                Visit(nested.TryBody);
                return;
            }
            foreach (var child in node.Children.ToList())
                Visit(child);
        }
    }

}
