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
    RejectedRelationship,
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
    ClassicAsyncParameterBindingSet ParameterBindings,
    object AcquisitionGuard);

internal sealed record ClassicAsyncStorage(
    string Name,
    TypeRef Type,
    ExactFieldDefinitionAddress? DefinitionAddress = null,
    object? DefinitionAcquisitionGuard = null);

internal sealed record ClassicAsyncParameterBinding(
    string FieldName,
    TypeRef FieldType,
    int ArgumentIndex,
    string ArgumentName,
    TypeRef ArgumentType,
    bool IsDynamic,
    MetadataFactState ArrayElementIsDynamic,
    ExactFieldDefinitionAddress? FieldDefinitionAddress = null,
    object? FieldDefinitionAcquisitionGuard = null);

internal sealed class ClassicAsyncParameterBindingSet
    : IEquatable<ClassicAsyncParameterBindingSet>
{
    readonly ImmutableArray<ClassicAsyncParameterBinding> _items;

    ClassicAsyncParameterBindingSet(
        ImmutableArray<ClassicAsyncParameterBinding> items)
        => _items = items;

    internal IReadOnlyList<ClassicAsyncParameterBinding> Items
        => _items;

    internal static ClassicAsyncParameterBindingSet Create(
        IEnumerable<ClassicAsyncParameterBinding> items)
        => new(
            [.. items
                .OrderBy(static item => item.ArgumentIndex)
                .ThenBy(
                    static item => item.FieldName,
                    StringComparer.Ordinal)]);

    public bool Equals(ClassicAsyncParameterBindingSet? other)
        => other is not null
            && _items.SequenceEqual(other._items);

    public override bool Equals(object? obj)
        => obj is ClassicAsyncParameterBindingSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (ClassicAsyncParameterBinding item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}

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

internal enum ClassicAsyncRegionHost
{
    Kickoff,
    Execution,
}

internal enum ClassicAsyncUserRegionKind
{
    AwaitedOperand,
    Predicate,
    GuardedEffect,
    CheckedArithmetic,
    Throw,
    Break,
    Continue,
}

internal sealed record ClassicAsyncRegionId(
    ClassicAsyncRegionHost Host,
    string StructuralPath);

internal sealed record ClassicAsyncPhysicalRegionId(
    ClassicAsyncRegionHost Host,
    MetadataMethodAddress Method,
    string StructuralPath);

internal sealed record ClassicAsyncPhysicalRegion(
    ClassicAsyncPhysicalRegionId Id,
    int EntryMultiplicity,
    int SuccessorMultiplicity,
    bool HasExternalEntry,
    bool HasExternalTarget,
    bool LeavesRegion);

internal sealed record ClassicAsyncRegionSemantics(
    ClassicAsyncUserRegionKind Kind,
    string Discriminator,
    int Occurrence);

internal sealed record ClassicAsyncUserRegion(
    ClassicAsyncRegionId Id,
    ClassicAsyncPhysicalRegionId PhysicalRegion,
    ClassicAsyncRegionSemantics Semantics);

internal sealed record ClassicAsyncOutputNode(
    ClassicAsyncRegionSemantics Semantics);

internal sealed record ClassicAsyncUserRegionRealization(
    ClassicAsyncRegionId UserRegion,
    ClassicAsyncOutputNode PrimaryOutputNode);

internal sealed class ClassicAsyncRegionLedger
    : IEquatable<ClassicAsyncRegionLedger>
{
    readonly ImmutableArray<ClassicAsyncPhysicalRegion> _physicalRegions;
    readonly ImmutableArray<ClassicAsyncPhysicalRegionId> _consumedRegions;
    readonly ImmutableArray<ClassicAsyncPhysicalRegionId> _preservedRegions;
    readonly ImmutableArray<ClassicAsyncUserRegion> _userRegions;
    readonly ImmutableArray<ClassicAsyncUserRegionRealization> _realizations;

    ClassicAsyncRegionLedger(
        ImmutableArray<ClassicAsyncPhysicalRegion> physicalRegions,
        ImmutableArray<ClassicAsyncPhysicalRegionId> consumedRegions,
        ImmutableArray<ClassicAsyncPhysicalRegionId> preservedRegions,
        ImmutableArray<ClassicAsyncUserRegion> userRegions,
        ImmutableArray<ClassicAsyncUserRegionRealization> realizations)
    {
        _physicalRegions = physicalRegions;
        _consumedRegions = consumedRegions;
        _preservedRegions = preservedRegions;
        _userRegions = userRegions;
        _realizations = realizations;
    }

    internal IReadOnlyList<ClassicAsyncPhysicalRegion> PhysicalRegions
        => _physicalRegions;

    internal IReadOnlyList<ClassicAsyncPhysicalRegionId> ConsumedRegions
        => _consumedRegions;

    internal IReadOnlyList<ClassicAsyncPhysicalRegionId> PreservedRegions
        => _preservedRegions;

    internal IReadOnlyList<ClassicAsyncUserRegion> UserRegions
        => _userRegions;

    internal IReadOnlyList<ClassicAsyncUserRegionRealization> Realizations
        => _realizations;

    internal static bool TryCreate(
        MetadataMethodAddress kickoff,
        MetadataMethodAddress execution,
        IEnumerable<ClassicAsyncPhysicalRegion> physicalRegions,
        IEnumerable<ClassicAsyncPhysicalRegionId> consumedRegions,
        IEnumerable<ClassicAsyncPhysicalRegionId> preservedRegions,
        IEnumerable<ClassicAsyncUserRegion> userRegions,
        IEnumerable<ClassicAsyncUserRegionRealization> realizations,
        out ClassicAsyncRegionLedger ledger)
    {
        ImmutableArray<ClassicAsyncPhysicalRegion> physical =
        [
            .. physicalRegions.OrderBy(
                static region => RegionOrderKey(region.Id),
                StringComparer.Ordinal),
        ];
        ImmutableArray<ClassicAsyncPhysicalRegionId> consumed =
        [
            .. consumedRegions.OrderBy(
                static region => RegionOrderKey(region),
                StringComparer.Ordinal),
        ];
        ImmutableArray<ClassicAsyncPhysicalRegionId> preserved =
        [
            .. preservedRegions.OrderBy(
                static region => RegionOrderKey(region),
                StringComparer.Ordinal),
        ];
        ImmutableArray<ClassicAsyncUserRegion> regions =
        [
            .. userRegions.OrderBy(
                static region => region.Id.StructuralPath,
                StringComparer.Ordinal),
        ];
        ImmutableArray<ClassicAsyncUserRegionRealization> realized =
        [
            .. realizations.OrderBy(
                static realization =>
                    realization.UserRegion.StructuralPath,
                StringComparer.Ordinal),
        ];

        HashSet<ClassicAsyncPhysicalRegionId> physicalIds =
            physical.Select(static region => region.Id).ToHashSet();
        HashSet<ClassicAsyncPhysicalRegionId> consumedIds =
            consumed.ToHashSet();
        HashSet<ClassicAsyncPhysicalRegionId> preservedIds =
            preserved.ToHashSet();

        bool valid = physical.Length > 0
            && physical.All(region =>
                IsCanonical(region.Id.StructuralPath)
                && region.Id.Method == (region.Id.Host
                    == ClassicAsyncRegionHost.Kickoff
                        ? kickoff
                        : execution))
            && physicalIds.Count == physical.Length
            && consumedIds.Count == consumed.Length
            && preservedIds.Count == preserved.Length
            && consumedIds.Count + preservedIds.Count == physicalIds.Count
            && consumedIds.All(physicalIds.Contains)
            && preservedIds.All(physicalIds.Contains)
            && !consumedIds.Overlaps(preservedIds)
            && physical
                .Where(region => consumedIds.Contains(region.Id))
                .All(static region =>
                    region.EntryMultiplicity is > 0 and <= 2
                    && region.SuccessorMultiplicity is >= 0 and <= 2
                    && !region.HasExternalEntry
                    && !region.HasExternalTarget
                    && !region.LeavesRegion)
            && physical
                .Where(static region =>
                    region.Id.Host == ClassicAsyncRegionHost.Kickoff)
                .All(region => consumedIds.Contains(region.Id))
            && regions
                .Select(static region => region.Id)
                .Distinct()
                .Count() == regions.Length
            && regions.All(region =>
                IsCanonical(region.Id.StructuralPath)
                && region.Id.Host == region.PhysicalRegion.Host
                && physicalIds.Contains(region.PhysicalRegion)
                && consumedIds.Contains(region.PhysicalRegion))
            && realized
                .Select(static realization => realization.UserRegion)
                .Distinct()
                .Count() == realized.Length
            && realized
                .Select(static realization =>
                    realization.PrimaryOutputNode.Semantics)
                .Distinct()
                .Count() == realized.Length
            && regions.All(region => realized.Any(realization =>
                realization.UserRegion == region.Id
                && realization.PrimaryOutputNode.Semantics
                    == region.Semantics))
            && realized.All(realization => regions.Any(region =>
                region.Id == realization.UserRegion));

        ledger = valid
            ? new(
                physical,
                consumed,
                preserved,
                regions,
                realized)
            : null!;
        return valid;
    }

    static bool IsCanonical(string path)
    {
        if (path.Length == 0)
            return false;

        foreach (string segment in path.Split('.'))
        {
            if (!int.TryParse(
                    segment,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int index)
                || index < 0
                || segment != index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
            {
                return false;
            }
        }
        return true;
    }

    static string RegionOrderKey(ClassicAsyncPhysicalRegionId region)
        => string.Join(
            "|",
            (int)region.Host,
            region.Method.ModuleVersionId.ToString("D"),
            region.Method.Token.ToString("X8"),
            region.StructuralPath);

    public bool Equals(ClassicAsyncRegionLedger? other)
        => other is not null
            && _physicalRegions.SequenceEqual(other._physicalRegions)
            && _consumedRegions.SequenceEqual(other._consumedRegions)
            && _preservedRegions.SequenceEqual(other._preservedRegions)
            && _userRegions.SequenceEqual(other._userRegions)
            && _realizations.SequenceEqual(other._realizations);

    public override bool Equals(object? obj)
        => obj is ClassicAsyncRegionLedger other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (ClassicAsyncPhysicalRegion region in _physicalRegions)
            hash.Add(region);
        foreach (ClassicAsyncPhysicalRegionId region in _consumedRegions)
            hash.Add(region);
        foreach (ClassicAsyncPhysicalRegionId region in _preservedRegions)
            hash.Add(region);
        foreach (ClassicAsyncUserRegion region in _userRegions)
            hash.Add(region);
        foreach (ClassicAsyncUserRegionRealization realization in _realizations)
            hash.Add(realization);
        return hash.ToHashCode();
    }
}

internal sealed record ClassicAsyncPlan(
    ClassicAsyncMachine Machine,
    ClassicAsyncBodyPlan Body,
    ClassicAsyncRegionLedger RegionLedger,
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
