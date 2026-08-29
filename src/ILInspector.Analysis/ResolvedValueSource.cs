using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// The kind of physical IL producer Analysis proved for one resolved value.
/// </summary>
/// <remarks>
/// This union is deliberately narrow: it names only the producers the shared
/// transparent-operation walk can prove (see
/// <see cref="ResolvedValueSet"/>). Anything else stays unresolved rather than
/// being approximated, so a consumer that requires proof fails closed.
/// </remarks>
public enum ResolvedValueSourceKind
{
    /// <summary>A <c>call</c>/<c>callvirt</c> result.</summary>
    CallResult,

    /// <summary>A <c>newobj</c> result.</summary>
    NewObjectResult,

    /// <summary>An <c>ldc.i4*</c> literal.</summary>
    Int32Literal,

    /// <summary>An <c>ldstr</c> literal resolved through the user-string heap.</summary>
    StringLiteral,

    /// <summary>An <c>ldnull</c> constant.</summary>
    NullReference,

    /// <summary>An <c>ldsfld</c> of a resolved static field.</summary>
    StaticFieldLoad,

    /// <summary>
    /// An <c>ldfld</c> of a resolved instance field whose receiver Analysis
    /// proved to be an argument slot
    /// (<see cref="ResolvedValueSource.ArgumentIndex"/>).
    /// </summary>
    InstanceFieldLoad,

    /// <summary>An <c>ldsflda</c> of a resolved static field.</summary>
    StaticFieldAddress,

    /// <summary>
    /// An <c>ldflda</c> of a resolved instance field whose receiver Analysis
    /// proved to be an argument slot.
    /// </summary>
    InstanceFieldAddress,

    /// <summary>An <c>ldarg*</c> of an argument slot.</summary>
    Argument,

    /// <summary>An <c>ldtoken</c> of a resolved type.</summary>
    TypeHandle,
}

/// <summary>
/// One proven physical producer of a value, with the operand facts that
/// producer carries.
/// </summary>
/// <param name="Kind">Which producer Analysis proved.</param>
/// <param name="ILOffset">Physical IL offset of the producing instruction.</param>
/// <remarks>
/// Deliberately flat so record value equality stays structural. Unused members
/// carry their neutral default for the kind, never a guess.
/// <c>MethodCallResolvedValueTests.ResolvesArgumentValueSourceKinds</c> gates
/// the union's coverage.
/// </remarks>
public sealed record ResolvedValueSource(
    ResolvedValueSourceKind Kind,
    int ILOffset)
{
    /// <summary>Metadata token of the field or type operand, or zero.</summary>
    public int Token { get; init; }

    /// <summary>
    /// The literal value for <see cref="ResolvedValueSourceKind.Int32Literal"/>.
    /// </summary>
    public int Int32Value { get; init; }

    /// <summary>
    /// The literal text for <see cref="ResolvedValueSourceKind.StringLiteral"/>.
    /// </summary>
    public string? StringValue { get; init; }

    /// <summary>
    /// Zero-based argument slot for <see cref="ResolvedValueSourceKind.Argument"/>,
    /// and the proven receiver slot for
    /// <see cref="ResolvedValueSourceKind.InstanceFieldLoad"/> and
    /// <see cref="ResolvedValueSourceKind.InstanceFieldAddress"/>. Slot zero
    /// is <c>this</c> for an instance method. Negative when not applicable.
    /// </summary>
    public int ArgumentIndex { get; init; } = -1;

    /// <summary>
    /// The declaring type of a resolved field access, or the resolved type of
    /// an <c>ldtoken</c>. Null when the producer carries no type operand or
    /// the token could not be resolved.
    /// </summary>
    public TypeRef? Type { get; init; }

    /// <summary>Field name for a resolved field access; null otherwise.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Canonical field identity for a resolved field access; null otherwise.
    /// </summary>
    public FieldIdentity? FieldIdentity { get; init; }

    /// <summary>True when this producer is a call, callvirt, or newobj result.</summary>
    public bool IsCallResult
        => Kind is ResolvedValueSourceKind.CallResult
            or ResolvedValueSourceKind.NewObjectResult;

    /// <summary>True when this producer loads the named field.</summary>
    public bool IsFieldLoad
        => Kind is ResolvedValueSourceKind.StaticFieldLoad
            or ResolvedValueSourceKind.InstanceFieldLoad;
}

/// <summary>
/// The proven producers of one value, or an explicit unresolved answer.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>new</em> union that lives alongside the older call-only
/// <c>SourceCallOffsets</c>/<c>IsComplete</c> provenance. It never reinterprets
/// those: a consumer that needs literals, field loads, <c>newobj</c> results, or
/// type handles asks here, and a consumer that needs the historical call-only
/// completeness keeps asking there.
/// </para>
/// <para>
/// Resolution sees through only the transparent operations the C# compiler emits
/// on these paths — <c>castclass</c>, <c>dup</c>, and unaddressed local
/// store/load — and fails closed on anything else.
/// <c>MethodCallResolvedValueTests.ResolvesValuesThroughTransparentOperations</c> and
/// <c>MethodCallResolvedValueTests.LeavesAddressedLocalValuesUnresolved</c> gate that
/// boundary.
/// </para>
/// </remarks>
public sealed class ResolvedValueSet : IEquatable<ResolvedValueSet>
{
    /// <summary>The explicit "Analysis could not prove this value" answer.</summary>
    public static ResolvedValueSet Unresolved { get; } =
        new([], isResolved: false);

    public ResolvedValueSet(
        ImmutableArray<ResolvedValueSource> sources,
        bool isResolved)
    {
        Sources = ImmutableArrayValueEquality.RequireInitialized(
            sources,
            nameof(sources));
        if (isResolved == Sources.IsEmpty)
        {
            throw new ArgumentException(
                "Resolved values require at least one source, and unresolved values must not carry sources.",
                nameof(sources));
        }
        IsResolved = isResolved;
    }

    /// <summary>
    /// Every proven producer that can reach this value. Empty is valid only
    /// when <see cref="IsResolved"/> is false.
    /// </summary>
    public ImmutableArray<ResolvedValueSource> Sources { get; }

    /// <summary>
    /// True when every control-flow and evaluation-stack path to this value was
    /// proven to be one of <see cref="Sources"/>.
    /// </summary>
    public bool IsResolved { get; }

    /// <summary>
    /// The single proven producer, when this value has exactly one.
    /// </summary>
    public ResolvedValueSource? Single
        => IsResolved && Sources.Length == 1 ? Sources[0] : null;

    public bool Equals(ResolvedValueSet? other)
        => other is not null
            && IsResolved == other.IsResolved
            && ImmutableArrayValueEquality.SequenceEqual(
                Sources,
                other.Sources);

    public override bool Equals(object? obj)
        => obj is ResolvedValueSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ImmutableArrayValueEquality.AddToHash(ref hash, Sources);
        hash.Add(IsResolved);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Exact call-result provenance carried through one compiler async
/// state-machine field across suspension.
/// </summary>
/// <remarks>
/// The field, store, and load coordinates are physical evidence in the
/// containing <see cref="MethodResultSink.EvidenceMethod"/>. This fact is
/// issued only for a sink with authenticated
/// <see cref="AsyncLoweringKind.StateMachine"/> attribution, one unambiguous
/// pre-suspension store that dominates every suspension, one exact
/// post-suspension load with no address escape, and a trusted framework
/// async-builder completion using the same exact builder field as every
/// suspension. It does not infer provenance from generated field names.
/// Custom async builders remain unresolved.
/// <c>LibraryBodyIndexTests.ResultSinks_PreserveCallSourceAcrossAsyncStateMachineField</c>
/// and
/// <c>LibraryBodyIndexTests.ResultSinks_RejectAmbiguousAsyncStateMachineFieldSources</c>
/// and
/// <c>LibraryBodyIndexTests.ResultSinks_RejectUnresolvedStateMachineFieldStoreAlias</c>
/// and
/// <c>LibraryBodyIndexTests.ResultSinks_AuthenticateStateMachineCompletionBuilderField</c>
/// gate the positive and fail-closed boundaries.
/// </remarks>
public sealed class AsyncStateMachineFieldResultSource
{
    internal AsyncStateMachineFieldResultSource(
        FieldIdentity field,
        int storeOffset,
        int loadOffset,
        ImmutableArray<int> sourceCallOffsets)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.LocalDefinitionToken == 0)
        {
            throw new ArgumentException(
                "Async state-machine field provenance requires a local field definition.",
                nameof(field));
        }
        if (storeOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(storeOffset));
        if (loadOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(loadOffset));
        if (storeOffset >= loadOffset)
        {
            throw new ArgumentException(
                "The field store must precede the field load.",
                nameof(storeOffset));
        }
        sourceCallOffsets =
            ImmutableArrayValueEquality.RequireInitialized(
                sourceCallOffsets,
                nameof(sourceCallOffsets));
        if (sourceCallOffsets.IsEmpty)
        {
            throw new ArgumentException(
                "Async state-machine field provenance requires a call source.",
                nameof(sourceCallOffsets));
        }

        Field = field;
        StoreOffset = storeOffset;
        LoadOffset = loadOffset;
        SourceCallOffsets = sourceCallOffsets;
    }

    public FieldIdentity Field { get; }
    public int StoreOffset { get; }
    public int LoadOffset { get; }
    public ImmutableArray<int> SourceCallOffsets { get; }
}

/// <summary>
/// Equality-stable, position-indexed collection of <see cref="ResolvedValueSet"/>.
/// </summary>
public sealed class ResolvedValueSets :
    IReadOnlyList<ResolvedValueSet>,
    IEquatable<ResolvedValueSets>
{
    readonly ImmutableArray<ResolvedValueSet> _values;

    public static ResolvedValueSets Empty { get; } = new([]);

    public ResolvedValueSets(ImmutableArray<ResolvedValueSet> values)
    {
        _values = ImmutableArrayValueEquality.RequireInitialized(
            values,
            nameof(values));
    }

    public int Count => _values.Length;

    public ResolvedValueSet this[int index] => _values[index];

    public IEnumerator<ResolvedValueSet> GetEnumerator()
        => ((IEnumerable<ResolvedValueSet>)_values).GetEnumerator();

    System.Collections.IEnumerator
        System.Collections.IEnumerable.GetEnumerator()
        => GetEnumerator();

    public bool Equals(ResolvedValueSets? other)
        => other is not null
            && ImmutableArrayValueEquality.SequenceEqual(
                _values,
                other._values);

    public override bool Equals(object? obj)
        => obj is ResolvedValueSets other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ImmutableArrayValueEquality.AddToHash(ref hash, _values);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The ordered element values of a span-shaped call argument the C# compiler
/// built from an inline-array buffer or a single-element
/// <c>ReadOnlySpan&lt;T&gt;</c> constructor.
/// </summary>
/// <param name="ArgumentIndex">Zero-based declared parameter position.</param>
/// <param name="Elements">
/// One resolved value per span element, in element order.
/// </param>
/// <param name="IsResolved">
/// True when Analysis proved the span's length and every element store. False
/// means the span shape was not one of the recognized compiler lowerings, or an
/// element write could not be attributed.
/// </param>
/// <remarks>
/// Scoped to the two lowerings Roslyn emits for a collection-expression span
/// argument, and anchored to the trusted core library's
/// <c>System.Runtime.CompilerServices.InlineArray&lt;N&gt;`1</c> buffer type so a
/// same-assembly helper cannot impersonate the lowering.
/// <c>MethodCallResolvedValueTests.ResolvesInlineArraySpanArgumentElements</c> and
/// <c>MethodCallResolvedValueTests.RejectsInlineArraySpanWithUntrustedBufferType</c>
/// gate it.
/// </remarks>
public sealed record SpanArgumentElements(
    int ArgumentIndex,
    ResolvedValueSets Elements,
    bool IsResolved);

/// <summary>
/// Equality-stable collection of <see cref="SpanArgumentElements"/>.
/// </summary>
public sealed class SpanArgumentSources :
    IReadOnlyList<SpanArgumentElements>,
    IEquatable<SpanArgumentSources>
{
    readonly ImmutableArray<SpanArgumentElements> _sources;

    public static SpanArgumentSources Empty { get; } = new([]);

    public SpanArgumentSources(
        ImmutableArray<SpanArgumentElements> sources)
    {
        _sources = ImmutableArrayValueEquality.RequireInitialized(
            sources,
            nameof(sources));
    }

    public int Count => _sources.Length;

    public SpanArgumentElements this[int index] => _sources[index];

    /// <summary>The elements recorded for one declared argument position.</summary>
    public SpanArgumentElements? ForArgument(int argumentIndex)
    {
        foreach (SpanArgumentElements source in _sources)
        {
            if (source.ArgumentIndex == argumentIndex)
                return source;
        }

        return null;
    }

    public IEnumerator<SpanArgumentElements> GetEnumerator()
        => ((IEnumerable<SpanArgumentElements>)_sources).GetEnumerator();

    System.Collections.IEnumerator
        System.Collections.IEnumerable.GetEnumerator()
        => GetEnumerator();

    public bool Equals(SpanArgumentSources? other)
        => other is not null
            && ImmutableArrayValueEquality.SequenceEqual(
                _sources,
                other._sources);

    public override bool Equals(object? obj)
        => obj is SpanArgumentSources other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ImmutableArrayValueEquality.AddToHash(ref hash, _sources);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One physical <c>stsfld</c>/<c>stfld</c> site with the resolved provenance of
/// the value it stores.
/// </summary>
/// <param name="Caller">
/// Declared method attributed to the body, which can differ from
/// <paramref name="EvidenceMethod"/> for a synthesized body.
/// </param>
/// <param name="EvidenceMethod">Physical method body containing the store.</param>
/// <param name="ILOffset">Physical IL offset of the store instruction.</param>
/// <param name="FieldToken">Metadata token in the store operand.</param>
/// <param name="IsStatic">True for <c>stsfld</c>.</param>
/// <param name="DeclaringType">
/// Resolved declaring type of the stored field, or null when the field token
/// could not be resolved.
/// </param>
/// <param name="FieldName">Resolved field name, or null.</param>
/// <param name="Identity">
/// Canonical field identity, including the local <c>FieldDef</c> token when
/// available; null when the operand could not be resolved unambiguously.
/// </param>
/// <param name="ReceiverArgumentIndex">
/// For an instance store, the argument slot Analysis proved supplies the
/// receiver; -1 for a static store or an unproven receiver.
/// </param>
/// <param name="Value">Resolved provenance of the stored value.</param>
/// <param name="IsReachable">
/// Whether the containing block is reachable from the body entry. Null when the
/// block graph is incomplete, so reachability is unknown rather than assumed.
/// </param>
/// <remarks>
/// Every store site is recorded, including one whose value stays
/// <see cref="ResolvedValueSet.Unresolved"/>, so a consumer asking "is this the
/// only write to this field?" can fail closed on an unproven sibling instead of
/// silently not seeing it. <c>MethodCallResolvedValueTests.CollectsFieldStoreFacts</c>
/// gates the resolved and unresolved rows.
/// </remarks>
public sealed record FieldStoreFact(
    MethodIdentity Caller,
    MethodIdentity EvidenceMethod,
    int ILOffset,
    int FieldToken,
    bool IsStatic,
    TypeRef? DeclaringType,
    string? FieldName,
    FieldIdentity? Identity,
    int ReceiverArgumentIndex,
    ResolvedValueSet Value,
    bool? IsReachable);

/// <summary>
/// One physical <c>ldsfld</c>/<c>ldfld</c>/<c>ldsflda</c>/<c>ldflda</c> site,
/// with the receiver Analysis proved for an instance access.
/// </summary>
/// <param name="Caller">
/// Declared method attributed to the body, which can differ from
/// <paramref name="EvidenceMethod"/> for a synthesized body.
/// </param>
/// <param name="EvidenceMethod">Physical method body containing the access.</param>
/// <param name="ILOffset">Physical IL offset of the access instruction.</param>
/// <param name="FieldToken">Metadata token in the access operand.</param>
/// <param name="IsStatic">True for <c>ldsfld</c>/<c>ldsflda</c>.</param>
/// <param name="DeclaringType">
/// Resolved declaring type of the loaded field, or null when the field token
/// could not be resolved.
/// </param>
/// <param name="FieldName">Resolved field name, or null.</param>
/// <param name="Identity">
/// Canonical field identity, including the local <c>FieldDef</c> token when
/// available; null when the operand could not be resolved unambiguously.
/// </param>
/// <param name="ReceiverArgumentIndex">
/// For an instance access, the argument slot Analysis proved supplies the
/// receiver; -1 for a static access or an unproven receiver.
/// </param>
/// <param name="IsReachable">
/// Whether the containing block is reachable from the body entry. Null when the
/// block graph is incomplete, so reachability is unknown rather than assumed.
/// </param>
/// <remarks>
/// The read/address counterpart of <see cref="FieldStoreFact"/>. A consumer that has
/// proven where a cached value is written still needs to see which field the
/// cached-read path reads, and a merged read/write return leaves that read off
/// every stack-slot resolution.
/// <c>MethodCallResolvedValueTests.CollectsFieldLoadFacts</c> gates it.
/// </remarks>
public sealed record FieldLoadFact(
    MethodIdentity Caller,
    MethodIdentity EvidenceMethod,
    int ILOffset,
    int FieldToken,
    bool IsStatic,
    TypeRef? DeclaringType,
    string? FieldName,
    FieldIdentity? Identity,
    int ReceiverArgumentIndex,
    bool? IsReachable)
{
    /// <summary>
    /// True when the instruction takes the field address with
    /// <c>ldsflda</c>/<c>ldflda</c>, allowing indirect mutation.
    /// </summary>
    public bool IsAddress { get; init; }
}
