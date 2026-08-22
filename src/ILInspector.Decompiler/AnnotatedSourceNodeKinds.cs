using System.Collections.Frozen;
using System.Text;

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

    /// <summary>A rendered lambda, and one of the two kinds a capture parent may be.</summary>
    public const string LambdaExpression = nameof(LambdaExpression);

    /// <summary>A rendered local-function declaration, and one of the two kinds a capture parent may be.</summary>
    public const string LocalFunctionStatement = nameof(LocalFunctionStatement);

    /// <summary>A rendered identifier reference, and the only kind a capture use may be.</summary>
    public const string NameExpression = nameof(NameExpression);

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
            "CheckedStatement",
            "ConversionExpression",
            "EmptyStatement",
            "ExpressionStatement",
            NameExpression,
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
            "MethodAddressExpression",
            "DelegateCreationExpression",
            LambdaExpression,
            LocalFunctionStatement,
            "ThrowStatement",
            "MemberAccessExpression",
            "ReturnStatement",
            "YieldReturnStatement",
            "YieldBreakStatement",
            "ArrayLengthExpression",
            "RangeExpression",
            "SliceExpression",
            "IndexFromEndExpression",
            "PatternExpression",
            "ArrayCreationExpression",
            "StackAllocationExpression",
            "TypeOfExpression",
            "CollectionExpression",
            "SpreadElement",
            "ElementAccessExpression",
            "EventAssignmentStatement",
            "CaughtExceptionExpression",
            "AddressExpression",
            "IndirectAccessExpression",
            "ObjectInitializationStatement",
            "SizeOfExpression",
            "DefaultExpression",
            "UnsupportedExpression",
            "DynamicMemberAccessExpression",
            Instruction,
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> DisplayLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BreakStatement"] = "Break",
            ["ReturnStatement"] = "Return",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> VocabularyLabels =
        Known.ToFrozenDictionary(
            static kind => kind,
            static kind => kind switch
            {
                "DoStatement" => "Do loop",
                "ForStatement" => "For loop",
                "ForeachStatement" => "Foreach loop",
                "WhileStatement" => "While loop",
                _ => HumanizeKnownKind(kind),
            },
            StringComparer.Ordinal);

    /// <summary>All node kinds emitted by this version of the producer.</summary>
    public static IReadOnlySet<string> All => Known;

    /// <summary>Tests whether <paramref name="kind"/> belongs to this producer's catalog.</summary>
    public static bool IsKnown(string kind)
        => kind is not null && Known.Contains(kind);

    /// <summary>
    /// Returns the product-owned display label for a stable node kind.
    /// Unknown kinds retain their stable id.
    /// </summary>
    public static string GetDisplayLabel(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return DisplayLabels.GetValueOrDefault(kind, kind);
    }

    /// <summary>
    /// Returns the product-owned human-readable label used in selection vocabularies.
    /// Unknown kinds retain their stable id.
    /// </summary>
    public static string GetVocabularyLabel(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return VocabularyLabels.GetValueOrDefault(kind, kind);
    }

    private static string HumanizeKnownKind(string kind)
    {
        var label = new StringBuilder(kind.Length + 8);
        for (int index = 0; index < kind.Length; index++)
        {
            char current = kind[index];
            bool startsWord = index > 0
                && char.IsUpper(current)
                && (char.IsLower(kind[index - 1])
                    || index + 1 < kind.Length && char.IsLower(kind[index + 1]));
            if (startsWord)
                label.Append(' ');
            label.Append(index == 0 || !startsWord
                ? current
                : char.ToLowerInvariant(current));
        }
        return label.ToString();
    }
}
