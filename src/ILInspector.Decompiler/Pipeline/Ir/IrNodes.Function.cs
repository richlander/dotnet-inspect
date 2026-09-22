using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>The C# method kind decoded from a reserved metadata method name: an ordinary method, an instance constructor (<c>.ctor</c>), or a static constructor (<c>.cctor</c>).</summary>
public enum IrMethodKind { Method, Constructor, StaticConstructor }

/// <summary>The root of one method's IR: signature plus a body container, with diagnostics accumulated during construction and passes.</summary>
public sealed class IrFunction : IrNode
{
    public IrFunction(string name, TypeRef declaringType, MethodSignature signature, ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        Name = name;
        DeclaringType = declaringType;
        Signature = signature;
        ReceiverParameter = signature.HasThis
            ? new Parameter("this", declaringType)
            : null;
        Locals = locals;
        AddChild(body);
    }

    public string Name { get; }
    public TypeRef DeclaringType { get; }
    public string? AssemblyPath { get; set; }
    public int MetadataToken { get; set; }
    internal MetadataMethodAddress? SourceMethodAddress { get; init; }
    public TypeRef? BaseType { get; set; }
    public MethodSignature Signature { get; }
    internal Parameter? ReceiverParameter { get; }
    public ImmutableArray<string> DeclaringTypeGenericParameterNames { get; set; } = [];
    public ImmutableArray<GenericParameterConstraintInfo> DeclaringTypeParameters { get; set; } = [];
    /// <summary>
    /// Typed constructor evidence decoded from the reserved metadata method name
    /// (<c>.ctor</c>/<c>.cctor</c>) at import time. Consumers (e.g. compile-back
    /// source composition) route this instead of re-matching the method-name
    /// string.
    /// </summary>
    public IrMethodKind MethodKind { get; set; } = IrMethodKind.Method;
    public ImmutableArray<TypeRef> Locals { get; private set; }
    ImmutableHashSet<int> _eliminatedLocalSlots = ImmutableHashSet<int>.Empty;
    public MetadataFactState CompilerGenerated { get; set; } = MetadataFactState.Unknown;
    public MetadataFactState DeclaringTypeCompilerGenerated { get; set; } = MetadataFactState.Unknown;
    public MetadataFactState IsRuntimeAsync { get; set; } = MetadataFactState.Unknown;
    public bool RequiresUnsafeContract { get; set; }
    internal ClassicAsyncRequestAdapterResult? ClassicAsyncRequest
        { get; set; }
    internal bool IsMetadataBacked { get; set; }
    internal bool HasAccessorStorageBinding { get; set; }
    internal MethodInstructions? ExceptionInstructions { get; set; }
    internal InstructionExceptionFlowResult<InstructionExceptionFlowFacts>?
        ExceptionFlow => ExceptionInstructions?.ExceptionFlow;
    internal ImmutableArray<DecompilerExceptionClauseImport>
        ExceptionClauseImports
    { get; set; } = [];
    internal string? ExceptionFactFailure { get; set; }

    internal void ClearImportedExceptionFacts()
    {
        ExceptionInstructions = null;
        ExceptionClauseImports = [];
        ExceptionFactFailure = null;
    }

    internal void ValidateArgumentBindings()
    {
        foreach (var node in Descendants)
        {
            bool missing = node switch
            {
                    LoadArgument { Parameter: null } => true,
                    LoadArgumentAddress { Parameter: null } => true,
                    StoreArgument { Parameter: null } => true,
                    DeconstructionTarget
                    {
                        Kind: DeconstructionTargetKind.Argument,
                        ArgumentParameter: null,
                    } => true,
                    _ => false,
            };
            if (missing)
            {
                throw new InvalidOperationException(
                    $"Invariant violated: metadata-backed '{Name}' contains an argument node without binder identity: {node.Describe()}.");
            }
        }
    }

    /// <summary>
    /// True when a consumer embedding this body in a C# method declaration must
    /// provide an <c>async</c> context. Runtime-async metadata establishes the
    /// context even when no <c>await</c> survives; classic async establishes it
    /// only after reconstruction installs an async body contract.
    /// <para>
    /// Gated by
    /// <c>ValidityShellNoiseTests.RuntimeAsyncNoAwaitShell_UsesMetadataAsyncContext</c>
    /// and
    /// <c>ValidityShellNoiseTests.OrdinaryTaskReturningShell_DoesNotInferAsyncFromReturnType</c>.
    /// </para>
    /// </summary>
    public bool RequiresAsyncMethodContext =>
        RequiresAsyncBodyModifier
        || IsRuntimeAsync == MetadataFactState.Yes;

    /// <summary>
    /// Appends a local slot (and its recovered source name) and returns its index. Used by
    /// raising passes that introduce a variable absent from the original IL — e.g.
    /// <see cref="ILInspector.Decompiler.Pipeline.IteratorReconstructionPass"/>
    /// materializing a hoisted iterator loop field back into a C# loop local. Keeps
    /// <see cref="LocalNames"/> length-aligned with <see cref="Locals"/>.
    /// </summary>
    public int AddLocal(TypeRef type, string? name = null)
    {
        var index = Locals.Length;
        Locals = Locals.Add(type);
        var names = LocalNames;
        while (names.Length < index)
            names = names.Add(null);
        LocalNames = names.Add(name);
        if (!SynthesizedLocalNames.IsDefaultOrEmpty)
        {
            var synthesized = SynthesizedLocalNames;
            while (synthesized.Length < index)
                synthesized = synthesized.Add(null);
            SynthesizedLocalNames = synthesized.Add(null);
        }
        if (!LocalDeclaredInNestedScope.IsDefaultOrEmpty)
        {
            // A slot a pass invents has no PDB scope, so it is not nested.
            var nested = LocalDeclaredInNestedScope;
            while (nested.Length < index)
                nested = nested.Add(false);
            LocalDeclaredInNestedScope = nested.Add(false);
        }
        if (!LocalDeclarationBindings.IsDefaultOrEmpty)
        {
            var bindings = LocalDeclarationBindings;
            while (bindings.Length < index)
                bindings = bindings.Add(null);
            LocalDeclarationBindings = bindings.Add(null);
        }
        if (!PdbLocalNameCandidates.IsDefaultOrEmpty)
        {
            var candidates = PdbLocalNameCandidates;
            while (candidates.Length < index)
                candidates = candidates.Add(null);
            PdbLocalNameCandidates = candidates.Add(null);
        }
        return index;
    }

    /// <summary>
    /// Appends a pass-created local with a preferred presentation name. Unlike
    /// <see cref="LocalNames"/>, the name is not artifact identity and remains
    /// collision-resolved against enclosing and descendant binders.
    /// </summary>
    public int AddSynthesizedLocal(TypeRef type, string name)
    {
        int index = AddLocal(type);
        var synthesized = SynthesizedLocalNames;
        while (synthesized.Length <= index)
            synthesized = synthesized.Add(null);
        SynthesizedLocalNames = synthesized.SetItem(index, name);
        return index;
    }

    /// <summary>
    /// Replaces the local slot table wholesale. Used by a pass that installs a
    /// transplanted body whose local indices are its own — iterator reconstruction
    /// moving a structured <c>MoveNext</c> into the kickoff, where the kickoff's
    /// original locals (and body) are being discarded. Keeps
    /// <see cref="LocalNames"/> length-aligned with <see cref="Locals"/>.
    /// <paramref name="eliminatedSlots"/> carries the transplanted body's own
    /// eliminated-slot indices (e.g. a reconstructed <c>MoveNext</c>'s dead
    /// inline-array buffer); pass the source function's
    /// <see cref="EliminatedLocalSlots"/> so a raise that landed inside the
    /// transplanted body is not silently undone. A null set drops any prior
    /// marking, since the new numbering no longer names the same locals.
    /// </summary>
    public void ResetLocals(
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> names,
        IReadOnlySet<int>? eliminatedSlots = null,
        ImmutableArray<string?> synthesizedNames = default,
        ImmutableArray<bool> declaredInNestedScope = default,
        ImmutableArray<PdbLocalDeclaration?> declarationBindings = default,
        ImmutableArray<string?> pdbLocalNameCandidates = default)
    {
        Locals = locals;
        var aligned = names;
        while (aligned.Length < locals.Length)
            aligned = aligned.Add(null);
        LocalNames = aligned;
        var alignedSynthesized = synthesizedNames.IsDefault
            ? ImmutableArray<string?>.Empty
            : synthesizedNames;
        while (alignedSynthesized.Length < locals.Length)
            alignedSynthesized = alignedSynthesized.Add(null);
        SynthesizedLocalNames = alignedSynthesized;
        var alignedNestedScopes = declaredInNestedScope.IsDefaultOrEmpty
            ? ImmutableArray<bool>.Empty
            : declaredInNestedScope;
        if (!alignedNestedScopes.IsEmpty)
        {
            while (alignedNestedScopes.Length < locals.Length)
                alignedNestedScopes = alignedNestedScopes.Add(false);
        }
        LocalDeclaredInNestedScope = alignedNestedScopes;
        var alignedBindings = declarationBindings.IsDefaultOrEmpty
            ? ImmutableArray<PdbLocalDeclaration?>.Empty
            : declarationBindings;
        if (!alignedBindings.IsEmpty)
        {
            while (alignedBindings.Length < locals.Length)
                alignedBindings = alignedBindings.Add(null);
        }
        LocalDeclarationBindings = alignedBindings;
        var alignedCandidates = pdbLocalNameCandidates.IsDefaultOrEmpty
            ? ImmutableArray<string?>.Empty
            : pdbLocalNameCandidates;
        if (!alignedCandidates.IsEmpty)
        {
            while (alignedCandidates.Length < locals.Length)
                alignedCandidates = alignedCandidates.Add(null);
        }
        PdbLocalNameCandidates = alignedCandidates;
        _eliminatedLocalSlots = eliminatedSlots switch
        {
            null => ImmutableHashSet<int>.Empty,
            ImmutableHashSet<int> set => set,
            _ => ImmutableHashSet.CreateRange(eliminatedSlots),
        };
    }

    /// <summary>
    /// Slots a raising pass proved dead — every original reference was consumed by
    /// the raise, so the local renders nowhere (e.g. the compiler-synthesized
    /// <c>&lt;&gt;y__InlineArrayN</c> buffer <see cref="ILInspector.Decompiler.Pipeline.InlineArrayCollectionPass"/>
    /// folds into a collection expression). The slot is retained in
    /// <see cref="Locals"/> so surviving slot indices stay stable, but because it
    /// is never rendered its (often unspellable) type must not degrade method
    /// fidelity. Reset by <see cref="ResetLocals"/>, which renumbers slots.
    /// </summary>
    public IReadOnlySet<int> EliminatedLocalSlots => _eliminatedLocalSlots;

    /// <summary>
    /// Records that slot <paramref name="index"/> is dead — a raising pass consumed
    /// its last reference, so it renders nowhere and its (often unspellable) type
    /// must not degrade method fidelity. Deadness is <em>verified, not trusted</em>:
    /// the slot is marked only when no node still binds or reads it (see
    /// <see cref="NodeBindsLocalSlot"/>). A caller's reference tally can miss a node
    /// kind — a by-value store, a <c>??=</c> target, or a <c>foreach</c> variable the
    /// raise never inspects — or a local-referencing node added to the IR later;
    /// marking such a still-live slot would drop its type from the fidelity view and
    /// report a false Full over output that still names the local. The walk descends
    /// into shared-scope nested lambdas and local functions — those that own no locals
    /// and touch no stack slot, so their bodies address this function's own local pool
    /// — but stops at a nested function that owns its pool, whose same-numbered slot is
    /// an unrelated variable (see <see cref="LocalSlotReferencedInScope"/>). When any
    /// reference survives this no-ops, leaving the slot counted so fidelity stays
    /// honestly Partial. See <see cref="EliminatedLocalSlots"/> (#3221, #3295).
    /// </summary>
    public void MarkLocalEliminated(int index)
    {
        if (index < 0 || index >= Locals.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (!LocalSlotReferencedInScope(this, index))
            _eliminatedLocalSlots = _eliminatedLocalSlots.Add(index);
    }

    /// <summary>
    /// Whether local slot <paramref name="index"/> is bound or read by
    /// <paramref name="node"/> or any descendant that addresses the same local pool.
    /// Mirrors <c>CSharpPrinter.ReferencesLocalIncludingSharedNestedScopes</c>: it
    /// descends through shared-scope nested functions (which reuse the enclosing pool)
    /// but not through ones that own their own locals or stack slots, whose indices are
    /// a separate pool. The IR-owned <c>NeedsIsolatedLocalScope</c> discriminator
    /// is shared with the printer so the two never diverge.
    /// </summary>
    static bool LocalSlotReferencedInScope(IrNode node, int index)
        => LocalSlotReferencesInScope(node, index).Any();

    /// <summary>
    /// Every node in <paramref name="node"/>'s subtree that binds or reads local slot
    /// <paramref name="index"/>, under the same scope rules as
    /// <see cref="LocalSlotReferencedInScope"/> — which delegates here, so the two
    /// cannot drift. Yielding the nodes rather than a bool lets a caller ask *where*
    /// the references are, which is what deciding a declaration's placement needs.
    /// </summary>
    internal static IEnumerable<IrNode> LocalSlotReferencesInScope(IrNode node, int index)
    {
        if (node is Lambda { NeedsIsolatedLocalScope: true })
            yield break;
        if (node is LocalFunctionStatement { NeedsIsolatedLocalScope: true })
            yield break;
        if (NodeBindsLocalSlot(node, index))
            yield return node;
        foreach (var child in node.Children)
        {
            foreach (var reference in LocalSlotReferencesInScope(child, index))
                yield return reference;
        }
    }

    /// <summary>
    /// Whether <paramref name="node"/> binds or reads local slot
    /// <paramref name="index"/> directly, so the C# view would render the slot's
    /// (possibly unspellable) type. This mirrors every local-slot source
    /// <see cref="CSharpPrinter"/> collects when deciding which locals to declare —
    /// the up-front load/store references plus the header- and pattern-declared kinds
    /// (<c>using</c> / <c>fixed</c> / <c>foreach</c>, <c>is</c>-patterns, switch-expression
    /// arm patterns, deconstruction, and catch clauses). Under-counting here marks a
    /// still-rendered slot eliminated and reports a false Full, so any new node kind
    /// that carries a local index must be added here. Two guard tests enforce this:
    /// <c>MarkLocalEliminated_RefusesEveryLocalSlotBindingNodeKind</c> builds a method
    /// whose only local is bound by each carrier kind and asserts the slot survives
    /// elimination — deleting a case below fails that kind's row — and
    /// <c>MarkLocalEliminated_HasABehavioralCaseForEveryLocalSlotCarrierKind</c> fails
    /// when a <c>LocalIndex</c>/<c>LocalIndices</c>/<c>VariableIndex</c> carrier is
    /// added to the IR without a behavioral case. Argument (<c>ldarg</c>) and
    /// stack-slot indices live in separate pools and are intentionally excluded — a
    /// <c>fixed</c> over a stack slot names a stack-slot index, not a local, so it is
    /// matched only when it binds a metadata local.
    /// </summary>
    static bool NodeBindsLocalSlot(IrNode node, int index) => node switch
    {
        LoadLocal load => load.Index == index,
        StoreLocal store => store.Index == index,
        LoadLocalAddress address => address.Index == index,
        NullCoalescingAssignment nullCoalescing => nullCoalescing.LocalIndex == index,
        ForeachStatement foreachStatement => foreachStatement.LocalIndex == index,
        UsingStatement usingStatement =>
            usingStatement.DeclaresResourceVariable && usingStatement.LocalIndex == index,
        Fixed fixedStatement => !fixedStatement.LocalIsStackSlot && fixedStatement.LocalIndex == index,
        IsPattern isPattern => isPattern.LocalIndex == index,
        RecursivePropertyDeclarationPattern recursiveProperty => recursiveProperty.LocalIndex == index,
        UnionSwitchExpressionArm unionArm => unionArm.LocalIndex == index,
        PatternSwitchExpressionArm patternArm =>
            patternArm.LocalIndex == index || patternArm.Subpattern?.LocalIndex == index,
        CatchClause catchClause => catchClause.VariableIndex == index,
        DeconstructionAssignment deconstruction => deconstruction.LocalIndices.Contains(index),
        _ => false,
    };

    /// <summary>
    /// Source names for the entries in <see cref="Locals"/>, by logical local index,
    /// recovered from the PDB at import. A reused physical slot may have several
    /// independently proven logical locals. Empty when no PDB was available;
    /// individual entries are null when a slot has no usable source name. The
    /// printer renders a present name and falls back to <c>V_index</c> otherwise.
    /// </summary>
    public ImmutableArray<string?> LocalNames { get; set; } = [];

    /// <summary>Original physical-slot evidence, never renumbered or deduplicated.</summary>
    public ImmutableArray<PdbLocalDeclaration> LocalDeclarations { get; set; } = [];

    /// <summary>Exact PDB row bound to each logical local; empty without symbols.</summary>
    public ImmutableArray<PdbLocalDeclaration?> LocalDeclarationBindings { get; set; } = [];

    /// <summary>
    /// Import-validated PDB slot labels available only as approximate display
    /// candidates when exact declaration identity could not be bound.
    /// </summary>
    public ImmutableArray<string?> PdbLocalNameCandidates { get; set; } = [];

    /// <summary>Available identity that raw import could not safely bind.</summary>
    public ImmutableArray<DecompilerFidelityCause> LocalNameImportCauses { get; set; } = [];

    /// <summary>
    /// Preferred names for locals introduced by reconstruction rather than
    /// recovered from artifact identity. Length-aligned with <see cref="Locals"/>
    /// when non-empty.
    /// </summary>
    public ImmutableArray<string?> SynthesizedLocalNames { get; set; } = [];

    /// <summary>
    /// Per entry in <see cref="Locals"/>, whether the portable PDB scoped the local to
    /// something narrower than the whole method body — that is, whether the source
    /// declared it inside a nested block. Empty when no PDB was available, which is
    /// why placement never depends on a guess: with no evidence the printer keeps the
    /// byte-stable hoisted shape. Length-aligned with <see cref="Locals"/> when
    /// non-empty.
    /// </summary>
    public ImmutableArray<bool> LocalDeclaredInNestedScope { get; set; } = [];

    /// <summary>
    /// Whether the source declared local <paramref name="index"/> inside a nested
    /// block. False when no PDB evidence exists for the slot, so callers degrade to
    /// the method-scope shape rather than inferring placement.
    /// </summary>
    public bool IsLocalDeclaredInNestedScope(int index)
        => index >= 0 && index < LocalDeclaredInNestedScope.Length && LocalDeclaredInNestedScope[index];

    public BlockContainer Body => (BlockContainer)Children[0];
    public List<DecompilerDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// True when this method is a finalizer (<c>Finalize</c> override) recovered as
    /// a C# destructor by <see cref="ILInspector.Decompiler.Pipeline.DestructorRecoveryPass"/>:
    /// the <c>try { … } finally { base.Finalize(); }</c> scaffold the compiler emits
    /// for <c>~T() { … }</c> has been stripped to its body. Consumers render the
    /// header as <c>~TypeName()</c>, where the base-finalizer call is implicit.
    /// </summary>
    public bool IsDestructor { get; set; }

    /// <summary>
    /// The defining module's normalized C# memory-safety language mode. A mode
    /// is available only for recognized Legacy and Updated rules; rendering and
    /// compiler replay must refuse an unavailable decision rather than treating
    /// it as Legacy.
    /// </summary>
    public MemorySafetyModeDecision MemorySafetyMode { get; set; }
        = MemorySafetyModeDecision.Legacy;

    /// <summary>
    /// Compatibility view used by mode-sensitive lowering after
    /// <see cref="MemorySafetyMode"/> has been admitted. Setting the property in
    /// synthetic tests selects an explicit Legacy or Updated mode.
    /// </summary>
    public bool UsesUpdatedMemorySafetyRules
    {
        get => MemorySafetyMode.UsesUpdatedRules;
        set => MemorySafetyMode = value
            ? MemorySafetyModeDecision.Updated
            : MemorySafetyModeDecision.Legacy;
    }

    /// <summary>
    /// True when the method body's locals are not zero-initialized — the
    /// effective result of <c>[SkipLocalsInit]</c> (applied at the member, type,
    /// or module level), observed as a cleared <c>.locals init</c> flag. Under
    /// the updated memory-safety rules a <c>stackalloc</c> converted to a
    /// <c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c> with no initializer is
    /// unsafe only in such a body, because the stack space is then uninitialized.
    /// </summary>
    public bool SkipLocalsInit { get; set; }

    /// <summary>
    /// True when reconstruction installed an async source-body contract. The
    /// printer uses this while shaping returns and unsupported fallbacks. Final
    /// member modifiers come from defining-method metadata in the C# producer
    /// rather than from this pass-local state.
    /// </summary>
    public bool RequiresAsyncBodyModifier { get; set; }

    /// <summary>
    /// Exception regions over the flat block container, by IL offset. The
    /// importer keeps blocks flat (region boundaries are block leaders);
    /// the EH structuring pass consumes these into <see cref="TryCatch"/>/
    /// <see cref="TryFinally"/> nodes and clears the list — non-empty regions
    /// mean the flat form is still the truth.
    /// </summary>
    public ImmutableArray<HandlerRegion> Regions { get; set; } = [];

    /// <summary>
    /// Resolved C# shapes for the definition types this function references,
    /// materialized at import (the printer is metadata-free). Same-assembly
    /// only; cross-assembly types are absent and read as
    /// <see cref="TypeShape.Unknown"/>. Lets the printer null-test a reference
    /// definition and zero-test an enum where <see cref="TypeFamilies.Of"/>
    /// cannot classify a bare definition.
    /// </summary>
    public IReadOnlyDictionary<TypeRef, TypeShape> TypeShapes { get; set; }
        = ImmutableDictionary<TypeRef, TypeShape>.Empty;

    internal IReadOnlyDictionary<TypeRef, TypeDefinitionIdentity> TypeFactIdentities { get; set; }
        = ImmutableDictionary<TypeRef, TypeDefinitionIdentity>.Empty;

    internal IReadOnlySet<TypeRef> AmbiguousTypeFacts { get; set; }
        = ImmutableHashSet<TypeRef>.Empty;

    /// <summary>
    /// Named members (value → name) of enum types this function references,
    /// materialized at import. External definitions are included only when the
    /// defining assembly resolves through the provenance-aware metadata context.
    /// Lets the printer render an enum constant as <c>EnumType.Member</c>
    /// instead of its raw integer.
    /// </summary>
    public IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>> EnumMembers { get; set; }
        = ImmutableDictionary<TypeRef, IReadOnlyDictionary<long, string>>.Empty;

    /// <summary>
    /// Underlying primitive type of enum types this function references,
    /// materialized while metadata is live. External definitions are included
    /// only when the defining assembly resolves exactly. Lets the printer preserve
    /// enum-to-underlying casts that IL does not encode when widths match.
    /// </summary>
    public IReadOnlyDictionary<TypeRef, TypeRef> EnumUnderlyingTypes { get; set; }
        = ImmutableDictionary<TypeRef, TypeRef>.Empty;

    /// <summary>
    /// Enum types this function references whose exact definitions carry
    /// <see cref="FlagsAttribute"/>. Materialized while metadata is live so the
    /// printer may name complete flag combinations without guessing from member
    /// values alone.
    /// </summary>
    public IReadOnlySet<TypeRef> FlagsEnumTypes { get; set; }
        = ImmutableHashSet<TypeRef>.Empty;

    /// <summary>
    /// Types proven, while metadata was live, to satisfy C# collection-initializer
    /// receiver rules. `ObjectInitializerPass` consumes this so an arbitrary
    /// method named `Add` is not enough to raise `new C { ... }`.
    /// </summary>
    public IReadOnlySet<TypeRef> CollectionInitializerTypes { get; set; }
        = ImmutableHashSet<TypeRef>.Empty;

    /// <summary>
    /// Same-assembly types proven to carry <c>UnionAttribute</c>. Used by the
    /// metadata-free printer to spell compiler-lowered <c>union.Value is T</c>
    /// tests back as union matching without guessing from member names alone.
    /// </summary>
    public IReadOnlySet<TypeRef> UnionTypes { get; set; }
        = ImmutableHashSet<TypeRef>.Empty;

    /// <summary>
    /// Same-assembly types proven to carry <c>IsByRefLikeAttribute</c> — that is,
    /// user-defined <c>ref struct</c>s. Consumed where a value-type declaration
    /// pattern would otherwise be raised: a ref struct cannot be boxed, so an
    /// <c>isinst</c>/<c>unbox.any</c> arm over one is never compiler-produced and
    /// a <c>T t</c> pattern over it is illegal (CS8121).
    /// </summary>
    public IReadOnlySet<TypeRef> ByRefLikeTypes { get; set; }
        = ImmutableHashSet<TypeRef>.Empty;

    /// <summary>
    /// Definition-keyed types this function references that resolved to a C#
    /// <c>interface</c>, materialized at import (the printer is metadata-free).
    /// Definition-backed and cross-assembly-aware, but a type whose interface-ness
    /// cannot be proven is <em>absent</em>, never guessed. Lets the printer
    /// re-insert the <c>((I)this)</c> cast an implicit class→interface (or
    /// variant) upcast erases from the IL, so a default-interface-member access
    /// through <c>this</c> spells back to valid, opcode-faithful C#.
    /// </summary>
    public IReadOnlySet<TypeRef> InterfaceTypes { get; set; }
        = ImmutableHashSet<TypeRef>.Empty;

    /// <summary>
    /// Reference definitions proven while metadata was live not to declare
    /// <c>op_Equality</c> anywhere C# operator lookup can bind it. A definition
    /// absent from this set is not assumed operator-free: unresolved external
    /// hierarchies stay conservative so raw IL reference identity cannot be
    /// rebound to user code by the metadata-free printer.
    /// </summary>
    /// <remarks>Gated by <c>BoxedReferenceEqualityTests</c>.</remarks>
    internal IReadOnlySet<TypeDefinitionIdentity> EqualityOperatorFreeTypes { get; set; }
        = ImmutableHashSet<TypeDefinitionIdentity>.Empty;

    /// <summary>
    /// The <c>op_Inequality</c> counterpart of
    /// <see cref="EqualityOperatorFreeTypes"/>.
    /// </summary>
    internal IReadOnlySet<TypeDefinitionIdentity> InequalityOperatorFreeTypes { get; set; }
        = ImmutableHashSet<TypeDefinitionIdentity>.Empty;

    /// <summary>
    /// An immutable snapshot of every type fact this function carries. A
    /// cross-method raise that must survive its source body being mutated or
    /// discarded captures this instead of retaining the function.
    /// </summary>
    internal IrTypeFactSnapshot CaptureTypeFacts()
        => new(
            TypeShapes.ToImmutableDictionary(),
            TypeFactIdentities.ToImmutableDictionary(),
            AmbiguousTypeFacts.ToImmutableHashSet(),
            EnumMembers.ToImmutableDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyDictionary<long, string>)
                    pair.Value.ToImmutableDictionary()),
            EnumUnderlyingTypes.ToImmutableDictionary(),
            FlagsEnumTypes.ToImmutableHashSet(),
            CollectionInitializerTypes.ToImmutableHashSet(),
            UnionTypes.ToImmutableHashSet(),
            ByRefLikeTypes.ToImmutableHashSet(),
            InterfaceTypes.ToImmutableHashSet(),
            EqualityOperatorFreeTypes.ToImmutableHashSet(),
            InequalityOperatorFreeTypes.ToImmutableHashSet());

    internal void MergeTypeFactsFrom(IrFunction body)
        => MergeTypeFactsFrom(body.CaptureTypeFacts());

    internal void MergeTypeFactsFrom(IrTypeFactSnapshot body)
    {
        var ambiguous = MergeSet(AmbiguousTypeFacts, body.AmbiguousTypeFacts).ToImmutableHashSet();
        foreach (var (type, bodyIdentity) in body.TypeFactIdentities)
        {
            if (TypeFactIdentities.TryGetValue(type, out var outerIdentity)
                && outerIdentity != bodyIdentity)
            {
                ambiguous = ambiguous.Add(type);
            }
        }

        AmbiguousTypeFacts = ambiguous;
        TypeFactIdentities = WithoutAmbiguous(
            MergeMap(TypeFactIdentities, body.TypeFactIdentities),
            ambiguous);
        TypeShapes = WithoutAmbiguous(
            MergeMap(TypeShapes, body.TypeShapes),
            ambiguous,
            keepUnknownSentinel: true);
        EnumMembers = WithoutAmbiguous(
            MergeMap(EnumMembers, body.EnumMembers),
            ambiguous);
        EnumUnderlyingTypes = WithoutAmbiguous(
            MergeMap(EnumUnderlyingTypes, body.EnumUnderlyingTypes),
            ambiguous);
        FlagsEnumTypes = WithoutAmbiguous(
            MergeSet(FlagsEnumTypes, body.FlagsEnumTypes),
            ambiguous);
        CollectionInitializerTypes = WithoutAmbiguous(
            MergeSet(CollectionInitializerTypes, body.CollectionInitializerTypes),
            ambiguous);
        UnionTypes = WithoutAmbiguous(
            MergeSet(UnionTypes, body.UnionTypes),
            ambiguous);
        ByRefLikeTypes = WithoutAmbiguous(
            MergeSet(ByRefLikeTypes, body.ByRefLikeTypes),
            ambiguous);
        InterfaceTypes = WithoutAmbiguous(
            MergeSet(InterfaceTypes, body.InterfaceTypes),
            ambiguous);
        EqualityOperatorFreeTypes = MergeSet(
            EqualityOperatorFreeTypes,
            body.EqualityOperatorFreeTypes);
        InequalityOperatorFreeTypes = MergeSet(
            InequalityOperatorFreeTypes,
            body.InequalityOperatorFreeTypes);
    }

    internal void CopyTypeFactsFrom(IrFunction source)
    {
        TypeShapes = source.TypeShapes;
        TypeFactIdentities = source.TypeFactIdentities;
        AmbiguousTypeFacts = source.AmbiguousTypeFacts;
        EnumMembers = source.EnumMembers;
        EnumUnderlyingTypes = source.EnumUnderlyingTypes;
        FlagsEnumTypes = source.FlagsEnumTypes;
        CollectionInitializerTypes = source.CollectionInitializerTypes;
        UnionTypes = source.UnionTypes;
        ByRefLikeTypes = source.ByRefLikeTypes;
        InterfaceTypes = source.InterfaceTypes;
        EqualityOperatorFreeTypes = source.EqualityOperatorFreeTypes;
        InequalityOperatorFreeTypes = source.InequalityOperatorFreeTypes;
    }

    static IReadOnlyDictionary<TKey, TValue> MergeMap<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> outer,
        IReadOnlyDictionary<TKey, TValue> inner)
        where TKey : notnull
    {
        if (inner.Count == 0)
            return outer;
        var result = outer as ImmutableDictionary<TKey, TValue>
            ?? ImmutableDictionary.CreateRange(outer);
        foreach (var (key, value) in inner)
        {
            if (!result.ContainsKey(key))
                result = result.SetItem(key, value);
        }
        return result;
    }

    static IReadOnlySet<T> MergeSet<T>(IReadOnlySet<T> outer, IReadOnlySet<T> inner)
        where T : notnull
    {
        if (inner.Count == 0)
            return outer;
        var result = outer as ImmutableHashSet<T> ?? ImmutableHashSet.CreateRange(outer);
        return result.Union(inner);
    }

    static IReadOnlyDictionary<TypeRef, TValue> WithoutAmbiguous<TValue>(
        IReadOnlyDictionary<TypeRef, TValue> values,
        IReadOnlySet<TypeRef> ambiguous,
        bool keepUnknownSentinel = false)
    {
        if (ambiguous.Count == 0)
            return values;
        var result = ImmutableDictionary.CreateBuilder<TypeRef, TValue>();
        foreach (var (type, value) in values)
        {
            if (!ambiguous.Contains(type))
                result.Add(type, value);
        }
        if (keepUnknownSentinel && typeof(TValue) == typeof(TypeShape))
        {
            foreach (var type in ambiguous)
                result[type] = (TValue)(object)TypeShape.Unknown;
        }
        return result.ToImmutable();
    }

    static IReadOnlySet<TypeRef> WithoutAmbiguous(
        IReadOnlySet<TypeRef> values,
        IReadOnlySet<TypeRef> ambiguous)
        => ambiguous.Count == 0
            ? values
            : values.Where(type => !ambiguous.Contains(type)).ToImmutableHashSet();

    public override IEnumerable<TypeRef> DirectTypes
        => Signature.Parameters.Select(p => p.Type)
            .Append(Signature.ReturnType)
            .Concat(Locals)
            .Concat(Regions.Where(r => r.CatchType is not null).Select(r => r.CatchType!));

    /// <summary>
    /// Computed from the tree, never asserted: any unsupported node, any
    /// unsupported type referenced anywhere, any metadata name the printer would
    /// have to emit with no C# spelling, any residual runtime token with no C#
    /// expression spelling, any residual exception-filter boundary, any
    /// expression whose result type the pipeline does not know (null — e.g. a
    /// join slot merged from conflicting types), an EH-retry <c>continue</c>
    /// whose source-like spelling is not currently opcode-exact, a
    /// <c>volatile.</c>-prefixed indirect access (the bare <c>*p</c> deref drops
    /// the acquire/release ordering and has no faithful plain-C# spelling), or an
    /// un-raised <c>pinned</c> T&amp; local (no faithful C# spelling) ⇒ at most
    /// <see cref="DecompilationFidelity.Partial"/>.
    /// </summary>
    public DecompilationFidelity Fidelity
        => FidelityRemarks.HasAny(this)
            ? DecompilationFidelity.Partial
            : DecompilationFidelity.Full;

    public override string Describe()
        => $"Function {Signature.ReturnType.ToDisplayString()} {Name}({string.Join(", ", Signature.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})";
}
