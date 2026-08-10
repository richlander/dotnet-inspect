using System.Collections.Frozen;

namespace ILInspector.Decompiler;

/// <summary>
/// Stable names for text structure in an <see cref="AnnotatedSourceDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// These names describe rendered syntax, not decompiler implementation types.
/// Producers emit only names in this catalog. Consumers should tolerate names
/// they do not yet know so that adding a kind does not make newer documents
/// unreadable by older clients.
/// </para>
/// <para>
/// The vocabulary uses PascalCase because <see cref="Instruction"/> established
/// that convention in the original document contract.
/// </para>
/// </remarks>
public static class AnnotatedSourceNodeKinds
{
    /// <summary>An exact-offset rendered IL instruction.</summary>
    public const string Instruction = nameof(Instruction);

    /// <summary>A producer-recognized node whose syntax has no more specific catalog entry.</summary>
    public const string Unknown = nameof(Unknown);

    private static readonly FrozenSet<string> Known =
        new[]
        {
            Unknown,
            "MemberBody",
            "Block",
            "IfStatement",
            "WhileStatement",
            "DoStatement",
            "ForStatement",
            "TryStatement",
            "CatchClause",
            "SwitchStatement",
            "SwitchSection",
            "SwitchExpression",
            "SwitchExpressionArm",
            "LockStatement",
            "FixedStatement",
            "UsingStatement",
            "ForeachStatement",
            "GotoStatement",
            "ConditionalGotoStatement",
            "SwitchDispatchStatement",
            "BreakStatement",
            "ContinueStatement",
            "BinaryExpression",
            "CoalesceExpression",
            "CoalesceAssignmentExpression",
            "ConditionalAccessExpression",
            "ConditionalExpression",
            "UnaryExpression",
            "AwaitExpression",
            "IncrementOrDecrementExpression",
            "ConversionExpression",
            "BoxingExpression",
            "ExpressionStatement",
            "NameExpression",
            "AssignmentStatement",
            "LiteralExpression",
            "InvocationExpression",
            "IndirectInvocationExpression",
            "ObjectCreationExpression",
            "AnonymousObjectCreationExpression",
            "InterpolatedStringExpression",
            "TupleExpression",
            "DeconstructionTarget",
            "DeconstructionAssignment",
            "ObjectInitializerExpression",
            "WithExpression",
            "InitializerExpression",
            "FunctionPointerExpression",
            "MethodAddressExpression",
            "DelegateCreationExpression",
            "LambdaExpression",
            "LocalFunctionStatement",
            "ThrowStatement",
            "MemberAccessExpression",
            "ReturnStatement",
            "YieldReturnStatement",
            "YieldBreakStatement",
            "ArrayLengthExpression",
            "RangeExpression",
            "SliceExpression",
            "IndexFromEndExpression",
            "TypeTestExpression",
            "PatternExpression",
            "ArrayCreationExpression",
            "StackAllocationExpression",
            "TypeOfExpression",
            "SpanCreationExpression",
            "CollectionExpression",
            "SpreadElement",
            "ElementAccessExpression",
            "TypeTokenExpression",
            "EventAssignmentStatement",
            "CaughtExceptionExpression",
            "ControlTransferStatement",
            "AddressExpression",
            "IndirectAccessExpression",
            "CopyBlockStatement",
            "ObjectInitializationStatement",
            "SizeOfExpression",
            "DefaultExpression",
            "UnsupportedExpression",
            "DynamicMemberAccessExpression",
            Instruction,
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>All node kinds emitted by this version of the producer.</summary>
    public static IReadOnlySet<string> All => Known;

    /// <summary>Tests whether <paramref name="kind"/> belongs to this producer's catalog.</summary>
    public static bool IsKnown(string kind)
        => kind is not null && Known.Contains(kind);
}
