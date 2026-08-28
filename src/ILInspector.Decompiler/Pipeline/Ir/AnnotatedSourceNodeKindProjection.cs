using CSharpText;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Projects implementation nodes onto the stable rendered-syntax vocabulary.
/// </summary>
/// <remarks>
/// The type map is exhaustive. <see cref="From"/> refines that default when one
/// implementation type prints more than one surface syntax according to node
/// state, such as property access versus indexer access.
/// </remarks>
internal static class AnnotatedSourceNodeKindProjection
{
    private static readonly IReadOnlyDictionary<Type, string> Kinds =
        new Dictionary<Type, string>
        {
            [typeof(IrFunction)] = "MemberBody",
            [typeof(BlockContainer)] = "Block",
            [typeof(Block)] = "Block",
            [typeof(IfStatement)] = "IfStatement",
            [typeof(WhileLoop)] = "WhileStatement",
            [typeof(DoWhileLoop)] = "DoStatement",
            [typeof(ForLoop)] = "ForStatement",
            [typeof(TryCatch)] = "TryStatement",
            [typeof(CatchClause)] = "CatchClause",
            [typeof(TryFinally)] = "TryStatement",
            [typeof(Switch)] = "SwitchStatement",
            [typeof(SwitchSection)] = "SwitchSection",
            [typeof(SwitchExpression)] = "SwitchExpression",
            [typeof(SwitchExpressionArm)] = "SwitchExpressionArm",
            [typeof(UnionSwitchExpression)] = "SwitchExpression",
            [typeof(UnionSwitchExpressionArm)] = "SwitchExpressionArm",
            [typeof(SynthesizedSwitchExpressionArm)] = "SwitchExpressionArm",
            [typeof(SynthesizedRenderedExpression)] = "ConversionExpression",
            [typeof(TupleSwitchExpression)] = "SwitchExpression",
            [typeof(TupleSwitchExpressionArm)] = "SwitchExpressionArm",
            [typeof(PatternSwitchExpression)] = "SwitchExpression",
            [typeof(PatternSwitchExpressionArm)] = "SwitchExpressionArm",
            [typeof(Lock)] = "LockStatement",
            [typeof(Fixed)] = "FixedStatement",
            [typeof(UsingStatement)] = "UsingStatement",
            [typeof(ForeachStatement)] = "ForeachStatement",
            [typeof(Branch)] = "GotoStatement",
            [typeof(ConditionalBranch)] = "ConditionalGotoStatement",
            [typeof(Break)] = "BreakStatement",
            [typeof(Continue)] = "ContinueStatement",
            [typeof(LabelAnchor)] = "EmptyStatement",
            [typeof(Comparison)] = "BinaryExpression",
            [typeof(LogicalBinary)] = "BinaryExpression",
            [typeof(Coalesce)] = "CoalesceExpression",
            [typeof(NullCoalescingAssignment)] = "CoalesceAssignmentExpression",
            [typeof(NullCoalescingFieldAssignment)] = "CoalesceAssignmentExpression",
            [typeof(NullCoalescingFieldAssignmentExpression)] = "CoalesceAssignmentExpression",
            [typeof(NullCoalescingPropertyAssignment)] = "CoalesceAssignmentExpression",
            [typeof(NullConditional)] = "ConditionalAccessExpression",
            [typeof(Conditional)] = "ConditionalExpression",
            [typeof(LogicalNot)] = "UnaryExpression",
            [typeof(Unary)] = "UnaryExpression",
            [typeof(AwaitExpression)] = "AwaitExpression",
            [typeof(IncrementDecrement)] = "IncrementOrDecrementExpression",
            [typeof(Coerce)] = "ConversionExpression",
            [typeof(Convert)] = "ConversionExpression",
            [typeof(ExpressionStatement)] = "ExpressionStatement",
            [typeof(LoadArgument)] = "NameExpression",
            [typeof(StoreArgument)] = "AssignmentStatement",
            [typeof(LoadLocal)] = "NameExpression",
            [typeof(StoreLocal)] = "AssignmentStatement",
            [typeof(Constant)] = "LiteralExpression",
            [typeof(Binary)] = "BinaryExpression",
            [typeof(Call)] = "InvocationExpression",
            [typeof(CallIndirect)] = "IndirectInvocationExpression",
            [typeof(NewObject)] = "ObjectCreationExpression",
            [typeof(AnonymousObject)] = "AnonymousObjectCreationExpression",
            [typeof(InterpolatedStringExpression)] = "InterpolatedStringExpression",
            [typeof(TupleExpression)] = "TupleExpression",
            [typeof(TupleBinaryExpression)] = "BinaryExpression",
            [typeof(DeconstructionTarget)] = "DeconstructionTarget",
            [typeof(DeconstructionAssignment)] = "DeconstructionAssignment",
            [typeof(ChainedAssignment)] = "AssignmentStatement",
            [typeof(ObjectInitializerExpression)] = "ObjectInitializerExpression",
            [typeof(WithExpression)] = "WithExpression",
            [typeof(InitializerBlock)] = "InitializerExpression",
            [typeof(LoadFunctionPointer)] = "UnsupportedExpression",
            [typeof(AddressOfMethod)] = "MethodAddressExpression",
            [typeof(DelegateCreation)] = "DelegateCreationExpression",
            [typeof(Lambda)] = "LambdaExpression",
            [typeof(LocalFunctionStatement)] = "LocalFunctionStatement",
            [typeof(LocalFunctionInvocation)] = "InvocationExpression",
            [typeof(Throw)] = "ThrowStatement",
            [typeof(LoadField)] = "MemberAccessExpression",
            [typeof(StoreField)] = "AssignmentStatement",
            [typeof(Return)] = "ReturnStatement",
            [typeof(YieldReturn)] = "YieldReturnStatement",
            [typeof(YieldBreak)] = "YieldBreakStatement",
            [typeof(StoreStackSlot)] = "AssignmentStatement",
            [typeof(LoadStackSlot)] = "NameExpression",
            [typeof(ArrayLength)] = "ArrayLengthExpression",
            [typeof(RangeExpression)] = "RangeExpression",
            [typeof(SliceExpression)] = "SliceExpression",
            [typeof(IndexFromEnd)] = "IndexFromEndExpression",
            [typeof(Box)] = "ConversionExpression",
            [typeof(IsInstance)] = "ConversionExpression",
            [typeof(IsPattern)] = "PatternExpression",
            [typeof(RecursivePropertyDeclarationPattern)] = "PatternExpression",
            [typeof(SingleElementListPattern)] = "PatternExpression",
            [typeof(PositionalPattern)] = "PatternExpression",
            [typeof(CastClass)] = "ConversionExpression",
            [typeof(NewArray)] = "ArrayCreationExpression",
            [typeof(StackAllocate)] = "StackAllocationExpression",
            [typeof(StackAllocArray)] = "StackAllocationExpression",
            [typeof(TypeOf)] = "TypeOfExpression",
            [typeof(SpanLiteral)] = "ArrayCreationExpression",
            [typeof(CollectionExpression)] = "CollectionExpression",
            [typeof(CollectionSpreadElement)] = "SpreadElement",
            [typeof(ArrayLiteral)] = "ArrayCreationExpression",
            [typeof(InlineArraySpanConversion)] = "ConversionExpression",
            [typeof(LoadToken)] = "UnsupportedExpression",
            [typeof(LoadProperty)] = "MemberAccessExpression",
            [typeof(StoreProperty)] = "AssignmentStatement",
            [typeof(EventSubscription)] = "EventAssignmentStatement",
            [typeof(CaughtException)] = "CaughtExceptionExpression",
            [typeof(Leave)] = "GotoStatement",
            [typeof(EndFinally)] = "UnsupportedExpression",
            [typeof(EndFilter)] = "UnsupportedExpression",
            [typeof(LoadLocalAddress)] = "AddressExpression",
            [typeof(LoadArgumentAddress)] = "AddressExpression",
            [typeof(LoadFieldAddress)] = "AddressExpression",
            [typeof(LoadElementAddress)] = "AddressExpression",
            [typeof(FixedBufferElementAddress)] = "AddressExpression",
            [typeof(LoadIndirect)] = "IndirectAccessExpression",
            [typeof(StoreIndirect)] = "AssignmentStatement",
            [typeof(CopyBlock)] = "UnsupportedExpression",
            [typeof(InitObject)] = "ObjectInitializationStatement",
            [typeof(LoadElement)] = "ElementAccessExpression",
            [typeof(StoreElement)] = "AssignmentStatement",
            [typeof(SizeOf)] = "SizeOfExpression",
            [typeof(DefaultValue)] = "DefaultExpression",
            [typeof(SwitchBranch)] = "SwitchDispatchStatement",
            [typeof(Unbox)] = "ConversionExpression",
            [typeof(UnboxAny)] = "ConversionExpression",
            [typeof(UnsupportedNode)] = "UnsupportedExpression",
            [typeof(DynamicGetMember)] = "DynamicMemberAccessExpression",
        };

    /// <summary>Every implementation type that has made an explicit vocabulary decision.</summary>
    internal static IEnumerable<Type> MappedTypes => Kinds.Keys;

    /// <summary>Every explicit implementation-to-syntax decision.</summary>
    internal static IEnumerable<KeyValuePair<Type, string>> Mappings => Kinds;

    /// <summary>Returns the stable rendered-syntax kind for <paramref name="node"/>, including instance-sensitive refinements.</summary>
    internal static string From(IrNode node)
        => node switch
        {
            SynthesizedRenderedExpression rendered => rendered.Kind,
            LoadProperty { IndexArguments.Count: > 0 } => "ElementAccessExpression",
            LoadToken { Kind: RuntimeTokenKind.Type, Type: not null } => "TypeOfExpression",
            _ => Kinds.GetValueOrDefault(node.GetType(), AnnotatedSourceNodeKinds.Unknown),
        };

    /// <summary>
    /// Returns the syntax kind when <paramref name="call"/> renders as a C#
    /// operator, or <see langword="null"/> when it renders as an invocation.
    /// </summary>
    internal static string? OperatorKind(Call call)
    {
        if (call.Callee.HasThis)
        {
            if (call.Callee.IsOperator != MetadataFactState.Yes || !call.Callee.IsSpecialName)
                return null;

            string instanceName = call.Callee.Name;
            bool isChecked = instanceName.StartsWith("op_Checked", StringComparison.Ordinal);
            string? suffix = isChecked
                ? instanceName["op_Checked".Length..]
                : instanceName.StartsWith("op_", StringComparison.Ordinal)
                    ? instanceName["op_".Length..]
                    : null;
            string? symbol = suffix is null
                ? null
                : isChecked
                    ? OperatorNames.MapCheckedAssignment(suffix)
                    : OperatorNames.MapAssignment(suffix);
            bool isIncrement = suffix is "IncrementAssignment" or "DecrementAssignment";
            int expectedArgumentCount = isIncrement ? 1 : 2;
            return symbol is not null && call.Arguments.Count == expectedArgumentCount
                ? "AssignmentStatement"
                : null;
        }

        if ((call.Callee.IsOperator != MetadataFactState.Yes || !call.Callee.IsSpecialName)
                && !MemberIdentity.IsKnownFrameworkOperator(call.Callee))
        {
            return null;
        }

        string name = call.Callee.Name;
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
        {
            string checkedName = name["op_Checked".Length..];
            return (checkedName, call.Arguments.Count) switch
            {
                ("Explicit", 1) => "ConversionExpression",
                ("Addition" or "Subtraction" or "Multiply" or "Division", 2) => "BinaryExpression",
                ("UnaryNegation", 1) => "UnaryExpression",
                _ => null,
            };
        }

        return (name, call.Arguments.Count) switch
        {
            ("op_Equality" or "op_Inequality"
                or "op_LessThan" or "op_LessThanOrEqual"
                or "op_GreaterThan" or "op_GreaterThanOrEqual"
                or "op_Addition" or "op_Subtraction"
                or "op_Multiply" or "op_Division" or "op_Modulus"
                or "op_BitwiseAnd" or "op_BitwiseOr" or "op_ExclusiveOr"
                or "op_LeftShift" or "op_RightShift" or "op_UnsignedRightShift", 2)
                => "BinaryExpression",
            ("op_UnaryNegation" or "op_UnaryPlus" or "op_LogicalNot" or "op_OnesComplement", 1)
                => "UnaryExpression",
            ("op_Implicit" or "op_Explicit", 1) => "ConversionExpression",
            _ => null,
        };
    }
}
