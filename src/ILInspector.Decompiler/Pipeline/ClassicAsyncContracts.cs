using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>The role of the requested method in an owner-issued classic state-machine relationship.</summary>
public enum ClassicAsyncHostRole
{
    DeclaredKickoff,
    Execution,
    Support,
    Ordinary,
}

/// <summary>How a classic-async decision resolves the declaration's async modifier.</summary>
public enum ClassicAsyncDeclarationDisposition
{
    NoOpinion,
    IncludeAsync,
    OmitAsync,
}

internal enum ClassicAsyncStage
{
    Raised,
    Lowered,
}

/// <summary>Why a healthy classic state machine was not reconstructed.</summary>
public enum ClassicAsyncDeclineReason
{
    NoExecutionMethod,
    KickoffMachineMismatch,
    NonNarrowKickoffHandoff,
    UnsupportedBuilder,
    UnconsumedExecutionRegion,
    UnrecognizedAwaiterProtocol,
}

/// <summary>How a declined kickoff body preserves its physical handoff.</summary>
public enum ClassicAsyncKickoffDisposition
{
    ReplacedNarrowHandoff,
    PreservedOriginal,
}

/// <summary>The typed terminal outcome of a healthy classic-async decision.</summary>
public abstract record ClassicAsyncOutcome
{
    private ClassicAsyncOutcome()
    {
    }

    public sealed record Reconstructed : ClassicAsyncOutcome;

    public sealed record Declined(
        ClassicAsyncDeclineReason Reason,
        ClassicAsyncKickoffDisposition KickoffDisposition)
        : ClassicAsyncOutcome;
}

internal sealed record ClassicAsyncFailure(
    string DiagnosticId,
    string Message);

internal abstract record ClassicAsyncPreparationResult
{
    private ClassicAsyncPreparationResult()
    {
    }

    internal sealed record NotApplicable(
        ClassicAsyncHostRole HostRole,
        MethodClassification? Classification)
        : ClassicAsyncPreparationResult;

    internal sealed record InputUnavailable(
        StateMachineRelationshipFailure Failure)
        : ClassicAsyncPreparationResult;

    internal sealed record ImportFailed(
        ClassicAsyncHostRole Role,
        ClassicAsyncFailure Failure)
        : ClassicAsyncPreparationResult;

    internal sealed record PlanningFailed(
        ClassicAsyncFailure Failure)
        : ClassicAsyncPreparationResult;

    internal sealed record Decided(
        ClassicAsyncDecision Decision)
        : ClassicAsyncPreparationResult;
}

internal abstract record ClassicAsyncDecision
{
    private ClassicAsyncDecision()
    {
    }

    internal sealed record Reconstruct(
        ClassicAsyncPlan Plan)
        : ClassicAsyncDecision;

    internal sealed record Decline(
        ClassicAsyncDeclineReason Reason,
        ClassicAsyncKickoffDisposition KickoffDisposition)
        : ClassicAsyncDecision;
}

internal abstract record ClassicAsyncStageResult
{
    private ClassicAsyncStageResult()
    {
    }

    internal sealed record Applied(
        ClassicAsyncStage Stage,
        ClassicAsyncOutcome Outcome,
        ClassicAsyncDeclarationDisposition DeclarationDisposition)
        : ClassicAsyncStageResult;

    internal sealed record NotApplicable(
        ClassicAsyncStage Stage)
        : ClassicAsyncStageResult;

    internal sealed record Failed(
        ClassicAsyncStage Stage,
        ClassicAsyncFailure Failure)
        : ClassicAsyncStageResult;
}

internal sealed record ClassicAsyncMachine(
    MetadataMethodAddress Kickoff,
    MetadataMethodAddress Execution,
    MetadataTypeDefinitionAddress StateMachine,
    MetadataTypeDefinitionName StateMachineName,
    TypeRef StateMachineType,
    int StateMachineLocal,
    TypeRef BuilderType,
    ClassicAsyncStorage StateStorage,
    ClassicAsyncStorage BuilderStorage,
    ClassicAsyncStorageSet AwaiterStorages,
    object AcquisitionGuard);

internal sealed record ClassicAsyncStorage(
    string Name,
    TypeRef Type);

internal sealed class ClassicAsyncStorageSet
    : IEquatable<ClassicAsyncStorageSet>
{
    readonly ImmutableArray<ClassicAsyncStorage> _items;

    ClassicAsyncStorageSet(
        ImmutableArray<ClassicAsyncStorage> items)
        => _items = items;

    internal IReadOnlyList<ClassicAsyncStorage> Items
        => _items;

    internal static ClassicAsyncStorageSet Create(
        IEnumerable<ClassicAsyncStorage> items)
        => new(
            [.. items
                .Distinct()
                .OrderBy(static item => item.Name, StringComparer.Ordinal)
                .ThenBy(
                    static item => item.Type.ToDisplayString(),
                    StringComparer.Ordinal)]);

    public bool Equals(ClassicAsyncStorageSet? other)
        => other is not null
            && _items.SequenceEqual(other._items);

    public override bool Equals(object? obj)
        => obj is ClassicAsyncStorageSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (ClassicAsyncStorage item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}

internal sealed record ClassicAsyncPlan(
    ClassicAsyncMachine Machine,
    ClassicAsyncBodyPlan Body,
    IrTypeFactsSnapshot TypeFacts);

/// <summary>
/// A detached body template owned by one immutable classic decision. The
/// captured tree is never exposed; every application receives a deep copy.
/// </summary>
internal sealed class ClassicAsyncBodyPlan
    : IEquatable<ClassicAsyncBodyPlan>
{
    readonly BlockContainer _body;
    readonly string _fingerprint;

    ClassicAsyncBodyPlan(
        BlockContainer body,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames)
    {
        _body = (BlockContainer)body.Clone();
        _fingerprint = Fingerprint(_body);
        Locals = locals;
        LocalNames = localNames;
    }

    internal ImmutableArray<TypeRef> Locals { get; }

    internal ImmutableArray<string?> LocalNames { get; }

    internal static ClassicAsyncBodyPlan Capture(
        BlockContainer body,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames)
        => new(body, locals, localNames);

    internal BlockContainer Materialize()
        => (BlockContainer)_body.Clone();

    public bool Equals(ClassicAsyncBodyPlan? other)
        => other is not null
            && _fingerprint == other._fingerprint
            && Locals.SequenceEqual(other.Locals)
            && LocalNames.SequenceEqual(other.LocalNames);

    public override bool Equals(object? obj)
        => obj is ClassicAsyncBodyPlan other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_fingerprint, StringComparer.Ordinal);
        foreach (TypeRef local in Locals)
            hash.Add(local);
        foreach (string? name in LocalNames)
            hash.Add(name, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    static string Fingerprint(IrNode root)
    {
        var builder = new StringBuilder();
        Append(root);
        return builder.ToString();

        void Append(IrNode node)
        {
            builder.Append(node.GetType().FullName)
                .Append('|')
                .Append(node.Describe())
                .Append('|')
                .Append(node.Children.Count)
                .Append('|')
                .Append(node.OwnsSourceLabel)
                .Append('|')
                .Append(SemanticDetail(node))
                .AppendLine();
            foreach (IrNode child in node.Children)
                Append(child);
        }
    }

    static string SemanticDetail(IrNode node)
        => node switch
        {
            AwaitExpression awaitExpression
                => $"{awaitExpression.ResultType?.ToDisplayString()}|"
                    + $"{awaitExpression.ResultIsDynamic}",
            LoadArgument argument
                => $"{argument.IsDynamic}|"
                    + $"{argument.ArrayElementIsDynamic}",
            Call call
                => MethodDetail(call),
            Conditional conditional
                => conditional.MergedType?.ToDisplayString() ?? "",
            ForeachStatement foreachStatement
                => $"{foreachStatement.IsAwait}|"
                    + string.Join(
                        ";",
                        foreachStatement.ConsumedMemberRefs.Select(
                            MethodDetail)),
            LoadElement element
                => $"{element.ElementType?.ToDisplayString()}|"
                    + $"{element.ResultIsDynamic}",
            _ => string.Join(
                ";",
                node.DirectTypes.Select(
                    static type => type.ToDisplayString())),
        };

    static string MethodDetail(Call call)
        => $"{call.IsVirtual}|"
            + $"{call.ConstrainedTo?.ToDisplayString()}|"
            + MethodDetail(call.Callee);

    static string MethodDetail(MethodRef method)
        => $"{method.DeclaringType.ToDisplayString()}|{method.Name}|"
            + $"{method.ReturnType.ToDisplayString()}|{method.HasThis}|"
            + $"{string.Join(",", method.ParameterTypes.Select(static type => type.ToDisplayString()))}|"
            + $"{string.Join(",", method.TypeArguments.Select(static type => type.ToDisplayString()))}|"
            + $"{method.ReturnIsDynamic}|{method.ReturnArrayElementIsDynamic}|"
            + $"{method.ParameterRefKindsFacts}|"
            + $"{string.Join(",", method.ParameterRefKinds)}|"
            + $"{method.SafeTrailingElidableCount}|{method.RequiresUnsafe}|"
            + $"{method.AccessorKind}|{method.LocalFunctionRaise}";
}

/// <summary>
/// Source-lifetime cache for classic decisions. Work is performed outside the
/// dictionary so nested or concurrent preparation never holds another
/// address's publication lock.
/// </summary>
internal interface IClassicAsyncPlanningSession
{
    ClassicAsyncPreparationResult Prepare(
        ClassicAsyncRelationshipEvidence evidence);
}

internal sealed class ClassicAsyncPlanningSession
    : IClassicAsyncPlanningSession
{
    static int s_nextId;

    [ThreadStatic]
    static HashSet<(int Session, MetadataMethodAddress Method)>? s_active;

    readonly ConcurrentDictionary<
        MetadataMethodAddress,
        ClassicAsyncPreparationResult> _preparations = new();
    readonly MetadataSource _source;
    readonly int _id = Interlocked.Increment(ref s_nextId);
    int _preparationCount;

    internal ClassicAsyncPlanningSession(MetadataSource source)
        => _source = source;

    internal int PreparationCount => Volatile.Read(ref _preparationCount);

    internal int PublishedPreparationCount => _preparations.Count;

    ClassicAsyncPreparationResult IClassicAsyncPlanningSession.Prepare(
        ClassicAsyncRelationshipEvidence evidence)
        => Prepare(evidence);

    internal ClassicAsyncPreparationResult Prepare(
        ClassicAsyncRelationshipEvidence evidence)
    {
        if (_preparations.TryGetValue(
                evidence.RequestedHost,
                out ClassicAsyncPreparationResult? prepared))
        {
            return prepared;
        }

        s_active ??= [];
        var key = (_id, evidence.RequestedHost);
        if (!s_active.Add(key))
        {
            return new ClassicAsyncPreparationResult.PlanningFailed(
                new(
                    DiagnosticIds.InternalError,
                    "classic async planning re-entered the same method"));
        }

        try
        {
            Interlocked.Increment(ref _preparationCount);
            ClassicAsyncPreparationResult candidate =
                ClassicAsyncReconstructionPass.Prepare(
                    _source,
                    evidence);
            return _preparations.GetOrAdd(
                evidence.RequestedHost,
                candidate);
        }
        finally
        {
            s_active.Remove(key);
        }
    }
}

/// <summary>
/// Metadata-owned relationship evidence stamped onto one imported method while
/// its acquisition source is live.
/// </summary>
internal sealed record ClassicAsyncRelationshipEvidence(
    MetadataMethodAddress RequestedHost,
    ClassicAsyncHostRole HostRole,
    MethodClassification? Classification,
    StateMachineRelationshipResult Relationship,
    object AcquisitionGuard,
    IClassicAsyncPlanningSession PlanningSession);
