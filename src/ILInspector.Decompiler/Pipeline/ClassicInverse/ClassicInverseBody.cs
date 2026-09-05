using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The closed, immutable body blueprint a <see cref="ClassicInversePlan"/>
/// publishes instead of an IR subtree.
/// <para>
/// The plan must retain no <see cref="IrNode"/>, parent link, mutable input
/// collection, or caller-owned body alias, and must be able to materialize a
/// fresh body after the caller has mutated (or discarded) the request bodies.
/// Every node form the accepted recipes emit has exactly one blueprint case;
/// <see cref="ClassicInverseBodyCapture.TryCapture"/> fails closed for anything
/// else, and each case materializes itself, so the materialization switch is
/// exhaustive by construction.
/// </para>
/// </summary>
internal abstract record ClassicInverseBodyNode
{
    private protected ClassicInverseBodyNode()
    {
    }

    /// <summary>Builds a fresh, unparented IR node from detached values only.</summary>
    internal abstract IrNode Materialize();

    /// <summary>A canonical rendering used for plan equality and determinism.</summary>
    internal abstract string Signature { get; }

    private protected static string Children(
        IEnumerable<ClassicInverseBodyNode> nodes)
        => string.Join(",", nodes.Select(static n => n.Signature));

    private protected static string TypeText(TypeRef? type)
        => type?.ToDisplayString() ?? "<null>";

    internal static string MethodText(MethodRef method)
        => $"{method.DeclaringType.ToDisplayString()}.{method.Name}"
            + $"({string.Join(";", method.ParameterTypes.Select(static p => p.ToDisplayString()))})"
            + $"->{method.ReturnType.ToDisplayString()}";

    internal static string FieldText(FieldRef field)
        => $"{field.DeclaringType.ToDisplayString()}.{field.Name}"
            + $":{field.Type.ToDisplayString()}";

    private protected static IrExpression Expr(ClassicInverseBodyNode node)
        => node.Materialize() as IrExpression
            ?? throw new InvalidOperationException(
                $"Blueprint node '{node.Signature}' is not an expression.");
}

// ---- Containers and statements ----------------------------------------

internal sealed record ClassicInverseBlockContainerNode(
    ImmutableArray<ClassicInverseBodyNode> Blocks)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
    {
        var container = new BlockContainer();
        foreach (var block in Blocks)
            container.Add((Block)block.Materialize());
        return container;
    }

    internal override string Signature => $"container({Children(Blocks)})";
}

internal sealed record ClassicInverseBlockNode(
    int StartOffset,
    ImmutableArray<ClassicInverseBodyNode> Statements)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
    {
        var block = new Block(StartOffset);
        foreach (var statement in Statements)
            block.Add(statement.Materialize());
        return block;
    }

    internal override string Signature =>
        $"block[{StartOffset}]({Children(Statements)})";
}

internal sealed record ClassicInverseReturnNode(ClassicInverseBodyNode? Value)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Return(Value is null ? null : Expr(Value));

    internal override string Signature =>
        $"return({Value?.Signature ?? ""})";
}

internal sealed record ClassicInverseExpressionStatementNode(
    ClassicInverseBodyNode Expression)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new ExpressionStatement(Expr(Expression));

    internal override string Signature => $"stmt({Expression.Signature})";
}

internal sealed record ClassicInverseStoreLocalNode(
    int Index,
    TypeRef Type,
    ClassicInverseBodyNode Value)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new StoreLocal(Index, Type, Expr(Value));

    internal override string Signature =>
        $"stloc[{Index}:{TypeText(Type)}]({Value.Signature})";
}

internal sealed record ClassicInverseForeachNode(
    int LocalIndex,
    TypeRef LocalType,
    ClassicInverseBodyNode Collection,
    ClassicInverseBodyNode Body)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new ForeachStatement(
            LocalIndex,
            LocalType,
            Expr(Collection),
            (Block)Body.Materialize());

    internal override string Signature =>
        $"foreach[{LocalIndex}:{TypeText(LocalType)}]"
        + $"({Collection.Signature},{Body.Signature})";
}

internal sealed record ClassicInverseTryFinallyNode(
    ClassicInverseBodyNode TryBody,
    ClassicInverseBodyNode FinallyBody)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new TryFinally(
            (BlockContainer)TryBody.Materialize(),
            (BlockContainer)FinallyBody.Materialize());

    internal override string Signature =>
        $"try({TryBody.Signature})finally({FinallyBody.Signature})";
}

// ---- Expressions -------------------------------------------------------

internal sealed record ClassicInverseAwaitNode(
    ClassicInverseBodyNode Operand,
    TypeRef? ResultType,
    MetadataFactState ResultIsDynamic)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new AwaitExpression(Expr(Operand), ResultType, ResultIsDynamic);

    internal override string Signature =>
        $"await[{TypeText(ResultType)}:{ResultIsDynamic}]({Operand.Signature})";
}

internal sealed record ClassicInverseLoadArgumentNode(
    int Index,
    string Name,
    TypeRef Type,
    bool IsDynamic,
    MetadataFactState ArrayElementIsDynamic)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new LoadArgument(Index, Name, Type)
        {
            IsDynamic = IsDynamic,
            ArrayElementIsDynamic = ArrayElementIsDynamic,
        };

    internal override string Signature =>
        $"ldarg[{Index}:{Name}:{TypeText(Type)}:{IsDynamic}:{ArrayElementIsDynamic}]";
}

internal sealed record ClassicInverseLoadLocalNode(int Index, TypeRef Type)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new LoadLocal(Index, Type);

    internal override string Signature => $"ldloc[{Index}:{TypeText(Type)}]";
}

internal sealed record ClassicInverseLoadLocalAddressNode(int Index, TypeRef Type)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new LoadLocalAddress(Index, Type);

    internal override string Signature => $"ldloca[{Index}:{TypeText(Type)}]";
}

internal sealed record ClassicInverseConstantNode(object? Value, TypeRef Type)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new Constant(Value, Type);

    internal override string Signature =>
        $"const[{Value ?? "null"}:{TypeText(Type)}]";
}

internal sealed record ClassicInverseTypeOfNode(TypeRef Type) : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new TypeOf(Type);

    internal override string Signature => $"typeof[{ClassicInverseTypedIdentity.Type(Type)}]";
}

internal sealed record ClassicInverseBinaryNode(
    BinaryKind Kind,
    bool IsChecked,
    bool IsUnsigned,
    ClassicInverseBodyNode Left,
    ClassicInverseBodyNode Right)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Binary(
            Kind,
            IsChecked,
            IsUnsigned,
            Expr(Left),
            Expr(Right));

    internal override string Signature =>
        $"binary[{Kind}:{IsChecked}:{IsUnsigned}]"
        + $"({Left.Signature},{Right.Signature})";
}

internal sealed record ClassicInverseComparisonNode(
    ComparisonKind Kind,
    bool IsUnsigned,
    ClassicInverseBodyNode Left,
    ClassicInverseBodyNode Right)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Comparison(Kind, IsUnsigned, Expr(Left), Expr(Right));

    internal override string Signature =>
        $"compare[{Kind}:{IsUnsigned}]({Left.Signature},{Right.Signature})";
}

internal sealed record ClassicInverseConditionalNode(
    ClassicInverseBodyNode Condition,
    ClassicInverseBodyNode WhenTrue,
    ClassicInverseBodyNode WhenFalse,
    TypeRef? MergedType)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Conditional(
            Expr(Condition),
            Expr(WhenTrue),
            Expr(WhenFalse))
        {
            MergedType = MergedType,
        };

    internal override string Signature =>
        $"cond[{TypeText(MergedType)}]"
        + $"({Condition.Signature},{WhenTrue.Signature},{WhenFalse.Signature})";
}

internal sealed record ClassicInverseLogicalNotNode(
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new LogicalNot(Expr(Operand));

    internal override string Signature => $"not({Operand.Signature})";
}

internal sealed record ClassicInverseCoalesceNode(
    ClassicInverseBodyNode Left,
    ClassicInverseBodyNode Right)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new Coalesce(Expr(Left), Expr(Right));

    internal override string Signature => $"coalesce({Left.Signature},{Right.Signature})";
}

internal sealed record ClassicInverseUnaryNode(
    UnaryKind Kind,
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new Unary(Kind, Expr(Operand));

    internal override string Signature => $"unary[{Kind}]({Operand.Signature})";
}

internal sealed record ClassicInverseArrayLengthNode(ClassicInverseBodyNode Array)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new ArrayLength(Expr(Array));

    internal override string Signature => $"length({Array.Signature})";
}

internal sealed record ClassicInverseConvertNode(
    TypeRef Target,
    bool IsChecked,
    bool IsUnsigned,
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Convert(Target, IsChecked, IsUnsigned, Expr(Operand));

    internal override string Signature =>
        $"convert[{TypeText(Target)}:{IsChecked}:{IsUnsigned}]({Operand.Signature})";
}

internal sealed record ClassicInverseCoerceNode(TypeRef Target, ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new Coerce(Target, Expr(Operand));

    internal override string Signature =>
        $"coerce[{ClassicInverseTypedIdentity.Type(Target)}]({Operand.Signature})";
}

internal sealed record ClassicInverseBoxNode(
    TypeRef Type,
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Box(Type, Expr(Operand));

    internal override string Signature =>
        $"box[{TypeText(Type)}]({Operand.Signature})";
}

internal sealed record ClassicInverseCallNode(
    MethodRef Callee,
    bool IsVirtual,
    TypeRef? ConstrainedTo,
    MetadataFactState ExtensionSyntaxConflict,
    ImmutableArray<ClassicInverseBodyNode> Arguments)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new Call(Callee, IsVirtual, Arguments.Select(Expr))
        {
            ConstrainedTo = ConstrainedTo,
            ExtensionSyntaxConflict = ExtensionSyntaxConflict,
        };

    internal override string Signature =>
        $"call[{MethodText(Callee)}:{IsVirtual}:{TypeText(ConstrainedTo)}]"
        + $"({Children(Arguments)})";
}

internal sealed record ClassicInverseCastClassNode(
    TypeRef Type,
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new CastClass(Type, Expr(Operand));

    internal override string Signature => $"cast[{TypeText(Type)}]({Operand.Signature})";
}

internal sealed record ClassicInverseUnboxAnyNode(
    TypeRef Type,
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new UnboxAny(Type, Expr(Operand));

    internal override string Signature => $"unbox-any[{TypeText(Type)}]({Operand.Signature})";
}

internal sealed record ClassicInverseIsInstanceNode(
    TypeRef Type,
    ClassicInverseBodyNode Operand)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new IsInstance(Type, Expr(Operand));

    internal override string Signature => $"isinst[{TypeText(Type)}]({Operand.Signature})";
}

internal sealed record ClassicInverseNewArrayNode(
    TypeRef ElementType,
    ClassicInverseBodyNode Length)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize() => new NewArray(ElementType, Expr(Length));

    internal override string Signature => $"newarr[{TypeText(ElementType)}]({Length.Signature})";
}

internal sealed record ClassicInverseNewObjectNode(
    MethodRef Constructor,
    ImmutableArray<string> AnonymousPropertyNames,
    ImmutableArray<ClassicInverseBodyNode> Arguments)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new NewObject(Constructor, Arguments.Select(Expr))
        {
            AnonymousPropertyNames = AnonymousPropertyNames,
        };

    internal override string Signature =>
        $"newobj[{MethodText(Constructor)}:{string.Join(";", AnonymousPropertyNames)}]"
        + $"({Children(Arguments)})";
}

internal sealed record ClassicInverseLoadPropertyNode(
    MethodRef Accessor,
    bool IsVirtual,
    bool HasInstance,
    ImmutableArray<ClassicInverseBodyNode> Arguments)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new LoadProperty(
            Accessor,
            HasInstance ? Expr(Arguments[0]) : null,
            [.. Arguments.Skip(HasInstance ? 1 : 0).Select(Expr)])
        {
            IsVirtual = IsVirtual,
        };

    internal override string Signature =>
        $"property[{MethodText(Accessor)}:{IsVirtual}:{HasInstance}]"
        + $"({Children(Arguments)})";
}

internal sealed record ClassicInverseLoadElementNode(
    TypeRef? ElementType,
    MetadataFactState ResultIsDynamic,
    ClassicInverseBodyNode Array,
    ClassicInverseBodyNode Index)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new LoadElement(ElementType, Expr(Array), Expr(Index))
        {
            ResultIsDynamic = ResultIsDynamic,
        };

    internal override string Signature =>
        $"ldelem[{TypeText(ElementType)}:{ResultIsDynamic}]"
        + $"({Array.Signature},{Index.Signature})";
}

internal sealed record ClassicInverseLoadFieldNode(
    FieldRef Field,
    bool IsVolatile,
    ClassicInverseBodyNode? Instance)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new LoadField(Field, Instance is null ? null : Expr(Instance))
        {
            IsVolatile = IsVolatile,
        };

    internal override string Signature =>
        $"ldfld[{FieldText(Field)}:{IsVolatile}]({Instance?.Signature ?? ""})";
}

internal sealed record ClassicInverseTupleNode(
    TypeRef TupleType,
    ImmutableArray<ClassicInverseBodyNode> Elements)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new TupleExpression(TupleType, Elements.Select(Expr));

    internal override string Signature =>
        $"tuple[{TypeText(TupleType)}]({Children(Elements)})";
}

/// <summary>One initializer or with-expression entry, detached from IR.</summary>
internal sealed record ClassicInverseInitializerEntry(
    string? Member,
    MethodRef? ConsumedMethod,
    bool ConsumedMethodIsVirtual,
    FieldRef? ConsumedField,
    ImmutableArray<ClassicInverseBodyNode> Arguments)
{
    internal string Signature =>
        $"entry[{Member ?? "<indexer>"}:"
        + $"{(ConsumedMethod is null
            ? ""
            : ClassicInverseBodyNode.MethodText(ConsumedMethod))}:"
        + $"{(ConsumedMethodIsVirtual ? "virt" : "direct")}:"
        + $"{(ConsumedField is null
            ? ""
            : ClassicInverseBodyNode.FieldText(ConsumedField))}]"
        + $"({string.Join(",", Arguments.Select(static a => a.Signature))})";
}

internal sealed record ClassicInverseObjectInitializerNode(
    ClassicInverseBodyNode Creation,
    bool IsCollection,
    ImmutableArray<ClassicInverseInitializerEntry> Entries)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new ObjectInitializerExpression(
            (NewObject)Creation.Materialize(),
            IsCollection,
            Entries.Select(ClassicInverseBodyCapture.MaterializeEntry));

    internal override string Signature =>
        $"objinit[{IsCollection}]({Creation.Signature},"
        + $"{string.Join(",", Entries.Select(static e => e.Signature))})";
}

internal sealed record ClassicInverseWithNode(
    ClassicInverseBodyNode Receiver,
    MethodRef? ConsumedCloneMethod,
    bool ConsumedCloneIsVirtual,
    ImmutableArray<ClassicInverseInitializerEntry> Entries)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new WithExpression(
            Expr(Receiver),
            Entries.Select(ClassicInverseBodyCapture.MaterializeEntry),
            ConsumedCloneMethod,
            ConsumedCloneIsVirtual);

    internal override string Signature =>
        $"with[{(ConsumedCloneMethod is null
            ? ""
            : MethodText(ConsumedCloneMethod))}:"
        + $"{(ConsumedCloneIsVirtual ? "virt" : "direct")}]"
        + $"({Receiver.Signature},"
        + $"{string.Join(",", Entries.Select(static e => e.Signature))})";
}

internal sealed record ClassicInverseInitializerBlockNode(
    bool IsCollection,
    ImmutableArray<ClassicInverseInitializerEntry> Entries)
    : ClassicInverseBodyNode
{
    internal override IrNode Materialize()
        => new InitializerBlock(IsCollection, Entries.Select(ClassicInverseBodyCapture.MaterializeEntry));

    internal override string Signature =>
        $"initblock[{IsCollection}]({string.Join(",", Entries.Select(static entry => entry.Signature))})";
}

/// <summary>
/// Captures a freshly built, recipe-owned output subtree into the closed
/// blueprint union. Capture is the only bridge from IR into a plan, and it
/// fails closed: a node form with no case returns <c>null</c>, which the core
/// turns into <see cref="ClassicInverseDeclineReason.UnsupportedOutputNode"/>.
/// </summary>
internal static class ClassicInverseBodyCapture
{
    internal static ClassicInverseBodyNode? TryCapture(
        IrNode node,
        ClassicInverseBudget budget)
    {
        if (!budget.Charge())
            return null;

        switch (node)
        {
            case BlockContainer container:
            {
                var blocks = TryCaptureAll(container.Children, budget);
                return blocks is null
                    ? null
                    : new ClassicInverseBlockContainerNode(blocks.Value);
            }

            case Block block:
            {
                var statements = TryCaptureAll(block.Children, budget);
                return statements is null
                    ? null
                    : new ClassicInverseBlockNode(
                        block.StartOffset,
                        statements.Value);
            }

            case Return ret:
            {
                if (ret.Value is null)
                    return new ClassicInverseReturnNode(null);
                var value = TryCapture(ret.Value, budget);
                return value is null ? null : new ClassicInverseReturnNode(value);
            }

            case ExpressionStatement statement:
            {
                var expression = TryCapture(statement.Expression, budget);
                return expression is null
                    ? null
                    : new ClassicInverseExpressionStatementNode(expression);
            }

            case StoreLocal store:
            {
                var value = TryCapture(store.Value, budget);
                return value is null
                    ? null
                    : new ClassicInverseStoreLocalNode(
                        store.Index,
                        store.Type,
                        value);
            }

            case ForeachStatement loop when !loop.IsAwait:
            {
                var collection = TryCapture(loop.Collection, budget);
                var body = TryCapture(loop.Body, budget);
                return collection is null || body is null
                    ? null
                    : new ClassicInverseForeachNode(
                        loop.LocalIndex,
                        loop.LocalType,
                        collection,
                        body);
            }

            case TryFinally tryFinally:
            {
                var tryBody = TryCapture(tryFinally.TryBody, budget);
                var finallyBody = TryCapture(tryFinally.FinallyBody, budget);
                return tryBody is null || finallyBody is null
                    ? null
                    : new ClassicInverseTryFinallyNode(tryBody, finallyBody);
            }

            case AwaitExpression await:
            {
                var operand = TryCapture(await.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseAwaitNode(
                        operand,
                        await.ResultType,
                        await.ResultIsDynamic);
            }

            case LoadArgument load:
                return new ClassicInverseLoadArgumentNode(
                    load.Index,
                    load.Name,
                    load.Type,
                    load.IsDynamic,
                    load.ArrayElementIsDynamic);

            case LoadLocal load:
                return new ClassicInverseLoadLocalNode(load.Index, load.Type);

            case LoadLocalAddress load:
                return new ClassicInverseLoadLocalAddressNode(load.Index, load.Type);

            case Constant constant:
                return new ClassicInverseConstantNode(
                    constant.Value,
                    constant.Type);

            case TypeOf typeOf:
                return new ClassicInverseTypeOfNode(typeOf.Type);

            case Binary binary:
            {
                var left = TryCapture(binary.Left, budget);
                var right = TryCapture(binary.Right, budget);
                return left is null || right is null
                    ? null
                    : new ClassicInverseBinaryNode(
                        binary.Kind,
                        binary.IsChecked,
                        binary.IsUnsigned,
                        left,
                        right);
            }

            case Comparison comparison:
            {
                var left = TryCapture(comparison.Left, budget);
                var right = TryCapture(comparison.Right, budget);
                return left is null || right is null
                    ? null
                    : new ClassicInverseComparisonNode(
                        comparison.Kind,
                        comparison.IsUnsigned,
                        left,
                        right);
            }

            case Conditional conditional:
            {
                var condition = TryCapture(conditional.Condition, budget);
                var whenTrue = TryCapture(conditional.WhenTrue, budget);
                var whenFalse = TryCapture(conditional.WhenFalse, budget);
                return condition is null || whenTrue is null || whenFalse is null
                    ? null
                    : new ClassicInverseConditionalNode(
                        condition,
                        whenTrue,
                        whenFalse,
                        conditional.MergedType);
            }

            case LogicalNot not:
            {
                var operand = TryCapture(not.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseLogicalNotNode(operand);
            }

            case Coalesce coalesce:
            {
                var left = TryCapture(coalesce.Left, budget);
                var right = TryCapture(coalesce.Right, budget);
                return left is null || right is null
                    ? null
                    : new ClassicInverseCoalesceNode(left, right);
            }

            case Unary unary:
            {
                var operand = TryCapture(unary.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseUnaryNode(unary.Kind, operand);
            }

            case ArrayLength length:
            {
                var array = TryCapture(length.Array, budget);
                return array is null
                    ? null
                    : new ClassicInverseArrayLengthNode(array);
            }

            case Convert convert:
            {
                var operand = TryCapture(convert.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseConvertNode(
                        convert.Target,
                        convert.IsChecked,
                        convert.IsUnsigned,
                        operand);
            }

            case Coerce coerce:
            {
                var operand = TryCapture(coerce.Operand, budget);
                return operand is null ? null : new ClassicInverseCoerceNode(coerce.Target, operand);
            }

            case Box box:
            {
                var operand = TryCapture(box.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseBoxNode(box.Type, operand);
            }

            case CastClass cast:
            {
                var operand = TryCapture(cast.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseCastClassNode(cast.Type, operand);
            }

            case UnboxAny unbox:
            {
                var operand = TryCapture(unbox.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseUnboxAnyNode(unbox.Type, operand);
            }

            case IsInstance test:
            {
                var operand = TryCapture(test.Operand, budget);
                return operand is null
                    ? null
                    : new ClassicInverseIsInstanceNode(test.Type, operand);
            }

            case NewArray array:
            {
                var length = TryCapture(array.Length, budget);
                return length is null
                    ? null
                    : new ClassicInverseNewArrayNode(array.ElementType, length);
            }

            case Call call:
            {
                var arguments = TryCaptureAll(call.Children, budget);
                return arguments is null
                    ? null
                    : new ClassicInverseCallNode(
                        Detach(call.Callee),
                        call.IsVirtual,
                        call.ConstrainedTo,
                        call.ExtensionSyntaxConflict,
                        arguments.Value);
            }

            case NewObject creation:
            {
                var arguments = TryCaptureAll(creation.Children, budget);
                return arguments is null
                    ? null
                    : new ClassicInverseNewObjectNode(
                        Detach(creation.Constructor),
                        creation.AnonymousPropertyNames,
                        arguments.Value);
            }

            case LoadProperty load:
            {
                var arguments = TryCaptureAll(load.Children, budget);
                return arguments is null
                    ? null
                    : new ClassicInverseLoadPropertyNode(
                        Detach(load.Accessor),
                        load.IsVirtual,
                        load.HasInstance,
                        arguments.Value);
            }

            case LoadField load:
            {
                if (load.Instance is null)
                    return new ClassicInverseLoadFieldNode(
                        load.Field, load.IsVolatile, null);
                var instance = TryCapture(load.Instance, budget);
                return instance is null
                    ? null
                    : new ClassicInverseLoadFieldNode(
                        load.Field, load.IsVolatile, instance);
            }

            case LoadElement load:
            {
                var array = TryCapture(load.Array, budget);
                var index = TryCapture(load.Index, budget);
                return array is null || index is null
                    ? null
                    : new ClassicInverseLoadElementNode(
                        load.ElementType,
                        load.ResultIsDynamic,
                        array,
                        index);
            }

            case TupleExpression tuple:
            {
                var elements = TryCaptureAll(tuple.Children, budget);
                return elements is null
                    ? null
                    : new ClassicInverseTupleNode(tuple.TupleType, elements.Value);
            }

            case ObjectInitializerExpression initializer:
            {
                var creation = TryCapture(initializer.Creation, budget);
                var entries = TryCaptureEntries(initializer.Entries, budget);
                return creation is null || entries is null
                    ? null
                    : new ClassicInverseObjectInitializerNode(
                        creation,
                        initializer.IsCollection,
                        entries.Value);
            }

            case InitializerBlock block:
            {
                var entries = TryCaptureEntries(block.Entries, budget);
                return entries is null
                    ? null
                    : new ClassicInverseInitializerBlockNode(block.IsCollection, entries.Value);
            }

            case WithExpression with:
            {
                var receiver = TryCapture(with.Receiver, budget);
                var entries = TryCaptureEntries(with.Entries, budget);
                return receiver is null || entries is null
                    ? null
                    : new ClassicInverseWithNode(
                        receiver,
                        with.ConsumedCloneMethod is null
                            ? null
                            : Detach(with.ConsumedCloneMethod),
                        with.ConsumedCloneIsVirtual,
                        entries.Value);
            }

            default:
                return null;
        }
    }

    static MethodRef Detach(MethodRef method)
        => method with
        {
            ExactDefinitionAcquisitionGuard = null,
        };

    internal static InitializerEntry MaterializeEntry(
        ClassicInverseInitializerEntry entry)
        => new(
            entry.Member,
            [.. entry.Arguments.Select(static a =>
                (IrExpression)a.Materialize())],
            entry.ConsumedMethod,
            entry.ConsumedField,
            entry.ConsumedMethodIsVirtual);

    static ImmutableArray<ClassicInverseBodyNode>? TryCaptureAll(
        IReadOnlyList<IrNode> nodes,
        ClassicInverseBudget budget)
    {
        var builder =
            ImmutableArray.CreateBuilder<ClassicInverseBodyNode>(nodes.Count);
        foreach (var node in nodes)
        {
            var captured = TryCapture(node, budget);
            if (captured is null)
                return null;
            builder.Add(captured);
        }
        return builder.ToImmutable();
    }

    static ImmutableArray<ClassicInverseInitializerEntry>? TryCaptureEntries(
        IReadOnlyList<InitializerEntry> entries,
        ClassicInverseBudget budget)
    {
        var builder =
            ImmutableArray.CreateBuilder<ClassicInverseInitializerEntry>(
                entries.Count);
        foreach (var entry in entries)
        {
            var arguments =
                ImmutableArray.CreateBuilder<ClassicInverseBodyNode>(
                    entry.Arguments.Count);
            foreach (var argument in entry.Arguments)
            {
                var captured = TryCapture(argument, budget);
                if (captured is null)
                    return null;
                arguments.Add(captured);
            }
            builder.Add(new ClassicInverseInitializerEntry(
                entry.Member,
                entry.ConsumedMethod is { } method
                    ? Detach(method)
                    : null,
                entry.ConsumedMethodIsVirtual,
                entry.ConsumedField,
                arguments.ToImmutable()));
        }
        return builder.ToImmutable();
    }
}
