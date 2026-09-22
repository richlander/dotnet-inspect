using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal enum LocalBindingNameProvenance
{
    Eliminated,
    Exact,
    ApproximatePdb,
    Synthesized,
    Readable,
    SlotFallback,
}

internal sealed record PlannedLocalBinding(
    int LocalIndex,
    string Identifier,
    LocalBindingNameProvenance Provenance,
    ExactLocalNameDisposition ExactDisposition,
    string PreferredIdentifier);

/// <summary>
/// Final declaration ownership and binding allocation for already-materialized
/// locals in one raised body. Residual stack slots remain a printer concern.
/// </summary>
internal sealed class LocalDeclarationPlan
{
    readonly HashSet<IrNode> _declaringNodes = [];
    readonly HashSet<IrNode> _unsafeRunDeclarations = [];
    readonly HashSet<IrNode> _legacyAwaitScopedDeclarations = [];
    readonly HashSet<int> _usingLocals = [];
    readonly HashSet<int> _foreachLocals = [];
    readonly HashSet<int> _patternLocals = [];
    readonly HashSet<int> _deconstructionLocals = [];
    readonly HashSet<int> _fixedLocals = [];
    readonly HashSet<int> _catchLocals = [];
    readonly HashSet<int> _outArgumentLocals = [];
    readonly HashSet<LoadLocalAddress> _outVariableDeclarations = [];
    readonly HashSet<int> _scopedLocals = [];
    readonly Dictionary<int, StoreLocal> _scopeEntryProjections = [];
    readonly List<(int Local, IrNode Owner, LoadLocalAddress Address)>
        _verifiedOutDeclarations = [];
    readonly Dictionary<int, IrNode> _declarationScopes = [];
    readonly IrFunction _function;
    readonly HashSet<int> _labelTargets;
    readonly HashSet<int> _retainedLocalSlots;
    readonly LocalDeclarationUnsafeContext _unsafeContext;

    LocalDeclarationPlan(
        IrFunction function,
        int localCount,
        PrinterOptions options,
        IEnumerable<string>? enclosingScopeNames,
        IReadOnlySet<int>? excludedLocalSlots)
    {
        _function = function;
        _labelTargets = ReferenceOwnership.CollectBranchTargets(function);
        _retainedLocalSlots = ExactLocalNameAllocation.RetainedLocalSlots(
            function,
            localCount,
            function.EliminatedLocalSlots);
        if (excludedLocalSlots is not null)
            _retainedLocalSlots.ExceptWith(excludedLocalSlots);
        _unsafeContext = new LocalDeclarationUnsafeContext(function);

        CollectSyntaxOwners();
        CollectDeclaringNodes();
        CollectDeclarationScopes(localCount);
        Bindings = AllocateBindings(
            localCount,
            options,
            enclosingScopeNames);
    }

    public IReadOnlySet<IrNode> DeclaringNodes => _declaringNodes;
    public IReadOnlySet<int> UsingLocals => _usingLocals;
    public IReadOnlySet<int> ForeachLocals => _foreachLocals;
    public IReadOnlySet<int> PatternLocals => _patternLocals;
    public IReadOnlySet<int> DeconstructionLocals => _deconstructionLocals;
    public IReadOnlySet<int> FixedLocals => _fixedLocals;
    public IReadOnlySet<int> CatchLocals => _catchLocals;
    public IReadOnlySet<int> OutArgumentLocals => _outArgumentLocals;
    public IReadOnlySet<LoadLocalAddress> OutVariableDeclarations
        => _outVariableDeclarations;
    public IReadOnlySet<int> ScopedLocals => _scopedLocals;
    public IReadOnlyDictionary<int, StoreLocal> ScopeEntryProjections
        => _scopeEntryProjections;
    public IReadOnlyDictionary<int, IrNode> DeclarationScopes
        => _declarationScopes;
    public IReadOnlySet<int> RetainedLocalSlots => _retainedLocalSlots;
    public ImmutableArray<PlannedLocalBinding> Bindings { get; }

    public static LocalDeclarationPlan Create(
        IrNode scope,
        int localCount,
        PrinterOptions? options = null,
        IEnumerable<string>? enclosingScopeNames = null,
        IReadOnlySet<int>? excludedLocalSlots = null)
        => new(
            CreatePlanningFunction(scope),
            localCount,
            options ?? PrinterOptions.Default,
            enclosingScopeNames,
            excludedLocalSlots);

    static IrFunction CreatePlanningFunction(IrNode scope)
    {
        if (scope is IrFunction function)
            return function;

        var owner = scope is Lambda or LocalFunctionStatement
            ? scope
            : scope.Parent;
        (BlockContainer? Body, ImmutableArray<TypeRef> Locals,
            ImmutableArray<string?> Names, ImmutableArray<bool> NestedScopes,
            ImmutableArray<PdbLocalDeclaration?> Bindings,
            ImmutableArray<string?> SynthesizedNames,
            ImmutableArray<string?> PdbNameCandidates,
            ImmutableArray<Parameter> Parameters, TypeRef? ReturnType) nested =
            owner switch
            {
                Lambda lambda => (
                    lambda.Body,
                    lambda.Locals,
                    lambda.LocalNames,
                    lambda.LocalDeclaredInNestedScope,
                    lambda.LocalDeclarationBindings,
                    lambda.SynthesizedLocalNames,
                    lambda.PdbLocalNameCandidates,
                    lambda.Parameters,
                    LambdaReturnType(lambda)
                        ?? TypeRef.CoreLib("System", "Void")),
                LocalFunctionStatement local => (
                    local.Body,
                    local.Locals,
                    local.LocalNames,
                    local.LocalDeclaredInNestedScope,
                    local.LocalDeclarationBindings,
                    local.SynthesizedLocalNames,
                    local.PdbLocalNameCandidates,
                    local.Parameters,
                    local.ReturnType),
                _ => default,
            };
        if (nested.Body is null || nested.ReturnType is null)
            throw new InvalidOperationException(
                "A local declaration plan requires a method, lambda, or local-function body.");

        IrFunction? enclosing = null;
        for (IrNode? ancestor = owner?.Parent;
            ancestor is not null;
            ancestor = ancestor.Parent)
        {
            if (ancestor is IrFunction parentFunction)
            {
                enclosing = parentFunction;
                break;
            }
        }
        function = new IrFunction(
            "",
            enclosing?.DeclaringType
                ?? TypeRef.CoreLib("System", "Object"),
            new MethodSignature(
                nested.ReturnType,
                nested.Parameters,
                false,
                0),
            nested.Locals,
            (BlockContainer)nested.Body.Clone())
        {
            LocalNames = nested.Names,
            SynthesizedLocalNames = nested.SynthesizedNames,
            LocalDeclaredInNestedScope = nested.NestedScopes,
            LocalDeclarationBindings = nested.Bindings,
            PdbLocalNameCandidates = nested.PdbNameCandidates,
            UsesUpdatedMemorySafetyRules =
                owner is Lambda { UsesUpdatedMemorySafetyRules: true }
                    or LocalFunctionStatement
                {
                    UsesUpdatedMemorySafetyRules: true,
                },
            SkipLocalsInit = owner is Lambda { SkipLocalsInit: true }
                or LocalFunctionStatement { SkipLocalsInit: true },
        };
        if (enclosing is not null)
            function.CopyTypeFactsFrom(enclosing);
        return function;
    }

    static TypeRef? LambdaReturnType(Lambda lambda)
    {
        foreach (var node in lambda.Body.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (node is Return { Value.ResultType: { } type })
                return type;
        }
        return null;
    }

    ImmutableArray<PlannedLocalBinding> AllocateBindings(
        int localCount,
        PrinterOptions options,
        IEnumerable<string>? enclosingScopeNames)
    {
        var display = new string[localCount];
        var provenance = new LocalBindingNameProvenance[localCount];
        var preferred = new string[localCount];
        var assigned = new bool[localCount];
        for (var index = 0; index < localCount; index++)
        {
            display[index] = preferred[index] = $"V_{index}";
            if (!_retainedLocalSlots.Contains(index))
            {
                assigned[index] = true;
                provenance[index] = LocalBindingNameProvenance.Eliminated;
            }
        }

        var enclosing = enclosingScopeNames is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                enclosingScopeNames,
                StringComparer.Ordinal);
        if (_function.HasAccessorStorageBinding)
            enclosing.Add("field");
        var captured = CSharpSpellability.ExternalArgumentNamesInScope(
                _function,
                _function.Signature.Parameters)
            .Where(enclosing.Contains);
        var taken = ExactLocalNameAllocation.ReservedNames(
            _function,
            _function.Signature.Parameters,
            _function.Signature.GenericParameterNames,
            captured);
        var exact = ExactLocalNameAllocation.Allocate(
            _function,
            localCount,
            _function.LocalNames,
            taken,
            _retainedLocalSlots,
            _declarationScopes);
        for (var index = 0; index < localCount; index++)
        {
            if (exact.Dispositions[index]
                    != ExactLocalNameDisposition.Preserved
                || exact.DisplayNames[index] is not { } name)
            {
                continue;
            }

            display[index] = preferred[index] = name;
            provenance[index] = LocalBindingNameProvenance.Exact;
            assigned[index] = true;
        }
        taken.UnionWith(exact.DisplayNames.OfType<string>());

        // Exact names may legally shadow non-captured enclosing or descendant
        // binders. Approximate and generated presentation stays conservative.
        taken.UnionWith(enclosing);
        AddDescendantBinderNames(taken);

        if (options.ApproximatePdbLocalNames)
        {
            for (var index = 0; index < localCount; index++)
            {
                if (assigned[index]
                    || ApproximatePdbLocalName(
                        index,
                        _function.LocalNames,
                        exact) is not { } candidate)
                {
                    continue;
                }

                display[index] = ReserveName(candidate, taken);
                preferred[index] = candidate;
                provenance[index] =
                    LocalBindingNameProvenance.ApproximatePdb;
                assigned[index] = true;
            }
        }

        var synthesizedNames = _function.SynthesizedLocalNames;
        for (var index = 0;
            index < localCount && index < synthesizedNames.Length;
            index++)
        {
            if (assigned[index]
                || synthesizedNames[index] is not { } synthesized)
            {
                continue;
            }

            display[index] = ReserveName(synthesized, taken);
            preferred[index] = synthesized;
            provenance[index] = LocalBindingNameProvenance.Synthesized;
            assigned[index] = true;
        }

        if (options.ReadableLocalNames)
        {
            var counters = LoopCounterLocals();
            for (var index = 0; index < localCount; index++)
            {
                if (assigned[index])
                    continue;
                TypeRef? type = index < _function.Locals.Length
                    ? _function.Locals[index]
                    : null;
                if (LocalNameSynthesizer.Synthesize(
                        type,
                        counters.Contains(index),
                        taken) is not { } synthesized)
                {
                    continue;
                }

                display[index] = preferred[index] = synthesized;
                taken.Add(synthesized);
                provenance[index] = LocalBindingNameProvenance.Readable;
                assigned[index] = true;
            }
        }

        for (var index = 0; index < localCount; index++)
        {
            if (assigned[index])
                continue;
            display[index] = ReserveName(display[index], taken);
            provenance[index] = LocalBindingNameProvenance.SlotFallback;
        }

        return
        [
            .. Enumerable.Range(0, localCount).Select(index =>
                new PlannedLocalBinding(
                    index,
                    display[index],
                    provenance[index],
                    exact.Dispositions[index],
                    preferred[index])),
        ];
    }

    void AddDescendantBinderNames(HashSet<string> names)
    {
        foreach (var nested in _function.Descendants.OfType<Lambda>())
        {
            foreach (var parameter in nested.Parameters)
                names.Add(parameter.DisplayName);
        }
        foreach (var nested in _function.Descendants
            .OfType<LocalFunctionStatement>())
        {
            names.Add(nested.Name);
            foreach (var parameter in nested.Parameters)
                names.Add(parameter.DisplayName);
        }
    }

    string? ApproximatePdbLocalName(
        int index,
        ImmutableArray<string?> exactNames,
        ExactLocalNameAllocation exact)
    {
        if (index < exact.Dispositions.Length
            && exact.Dispositions[index]
                == ExactLocalNameDisposition.Collision
            && index < exactNames.Length
            && exactNames[index] is { } collided
            && CSharpNaming.IsUsableIdentifier(collided))
        {
            return collided;
        }

        return index < _function.PdbLocalNameCandidates.Length
            ? _function.PdbLocalNameCandidates[index]
            : null;
    }

    static string ReserveName(
        string baseName,
        HashSet<string> taken)
    {
        if (taken.Add(baseName))
            return baseName;
        for (var index = 1; ; index++)
        {
            string candidate = $"{baseName}_{index}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    HashSet<int> LoopCounterLocals()
    {
        var counters = new HashSet<int>();
        foreach (var loop in _function.DescendantsOutsideNestedFunctions
            .OfType<ForLoop>())
        {
            var increment = loop.Increment;
            if (increment is StoreLocal direct)
                counters.Add(direct.Index);
            foreach (var node in increment.Descendants)
            {
                if (node is StoreLocal store)
                    counters.Add(store.Index);
            }
        }
        return counters;
    }

    void CollectSyntaxOwners()
    {
        foreach (var node in _function.DescendantsOutsideNestedFunctions)
        {
            switch (node)
            {
                case UsingStatement resource:
                    _usingLocals.Add(resource.LocalIndex);
                    break;
                case ForeachStatement loop:
                    _foreachLocals.Add(loop.LocalIndex);
                    break;
                case IsPattern pattern:
                    _patternLocals.Add(pattern.LocalIndex);
                    break;
                case RecursivePropertyDeclarationPattern pattern:
                    _patternLocals.Add(pattern.LocalIndex);
                    break;
                case UnionSwitchExpressionArm { LocalIndex: { } local }:
                    _patternLocals.Add(local);
                    break;
                case PatternSwitchExpressionArm arm:
                    if (arm.LocalIndex is { } armLocal)
                        _patternLocals.Add(armLocal);
                    if (arm.Subpattern is { } subpattern)
                        _patternLocals.Add(subpattern.LocalIndex);
                    break;
                case DeconstructionAssignment deconstruction:
                    foreach (var target in deconstruction.Targets)
                    {
                        if (target is
                            {
                                Kind: DeconstructionTargetKind.Local,
                                IsDeclared: true,
                            })
                        {
                            _deconstructionLocals.Add(target.LocalIndex);
                        }
                    }
                    break;
                case Fixed { LocalIsStackSlot: false } pin:
                    _fixedLocals.Add(pin.LocalIndex);
                    break;
                case CatchClause { VariableIndex: { } local }:
                    _catchLocals.Add(local);
                    break;
            }
        }

        _verifiedOutDeclarations.AddRange(
            VerifiedOutLocalDeclarations());
        foreach (var (local, _, address) in _verifiedOutDeclarations)
        {
            _outArgumentLocals.Add(local);
            _outVariableDeclarations.Add(address);
        }
    }

    void CollectDeclaringNodes()
    {
        if (_function.Body.Blocks.Count == 0)
            return;
        var entryStatements = new HashSet<IrNode>(
            _function.Body.Blocks[0].Children);
        var seenLocals = new HashSet<int>();
        var seenSlots = new HashSet<int>();
        // Only unsafe-run extent crosses the #2095 residual-slot boundary;
        // this plan never owns or emits these declarations.
        var supportingStackStores = new HashSet<StoreStackSlot>();
        var legacyAwaitScopedStackStores =
            new HashSet<StoreStackSlot>();
        var slotStoreCounts = new Dictionary<int, int>();
        foreach (var store in _function
            .DescendantsOutsideNestedFunctions
            .OfType<StoreStackSlot>())
        {
            slotStoreCounts[store.Slot] =
                slotStoreCounts.GetValueOrDefault(store.Slot) + 1;
        }

        foreach (var node in
            _function.DescendantsOutsideNestedFunctions)
        {
            switch (node)
            {
                case StoreLocal store
                    when !seenLocals.Contains(store.Index):
                    seenLocals.Add(store.Index);
                    if (store.PdbScopeEntryProjection is not null)
                        _scopeEntryProjections.Add(store.Index, store);
                    if (entryStatements.Contains(store)
                        && !ReferencesLocal(store.Value, store.Index)
                        && !HasBranchTargetAfterStatement(store))
                    {
                        AddLocalDeclaration(store);
                    }
                    else if (store.Type.Kind == TypeRefKind.ByRef
                        && LocalReferencesStayInBlockAfterStore(store))
                    {
                        AddLocalDeclaration(store);
                    }
                    else if (!_function.UsesUpdatedMemorySafetyRules
                        && _unsafeContext.ContainsAwaitSyntax
                        && OperationMemorySafetyContract.ContainsPointer(
                            store.Type)
                        && UnsafeAwaitOperand.CanScopeLegacyPointerLocal(
                            _function,
                            store))
                    {
                        AddLocalDeclaration(store);
                        _legacyAwaitScopedDeclarations.Add(store);
                    }
                    else if ((_function.IsLocalDeclaredInNestedScope(
                                    store.Index)
                            || store.Parent is Block
                            {
                                Parent: Block,
                            })
                        && LocalReferencesStayInsideDeclarationBlock(
                            store,
                            store.Index))
                    {
                        AddLocalDeclaration(store);
                    }
                    else if (store is
                    {
                        Parent: ForLoop forLoop,
                        ChildIndex: 0,
                    }
                        && LastReferenceIsInside(
                            store.Index,
                            forLoop))
                    {
                        AddLocalDeclaration(store);
                    }
                    break;
                case InitObject
                {
                    Address:
                            LoadLocalAddress initTarget,
                } init
                    when !seenLocals.Contains(initTarget.Index):
                    seenLocals.Add(initTarget.Index);
                    if (entryStatements.Contains(init)
                        || (_function.IsLocalDeclaredInNestedScope(
                                    initTarget.Index)
                                || init.Parent is Block
                                {
                                    Parent: Block,
                                })
                            && LocalReferencesStayInsideDeclarationBlock(
                                init,
                                initTarget.Index))
                    {
                        AddLocalDeclaration(init);
                    }
                    break;
                case LoadLocal load:
                    seenLocals.Add(load.Index);
                    break;
                case LoadLocalAddress address:
                    seenLocals.Add(address.Index);
                    break;
                case NullCoalescingAssignment assignment:
                    seenLocals.Add(assignment.LocalIndex);
                    break;
                case StoreStackSlot slotStore
                    when !seenSlots.Contains(slotStore.Slot):
                    seenSlots.Add(slotStore.Slot);
                    if (entryStatements.Contains(slotStore)
                        && slotStore.Value.ResultType is not null
                        && slotStoreCounts[slotStore.Slot] == 1)
                    {
                        supportingStackStores.Add(slotStore);
                        _unsafeRunDeclarations.Add(slotStore);
                    }
                    else if (slotStore.Value.ResultType is
                    {
                        Kind: TypeRefKind.ByRef,
                    }
                        && StackSlotReferencesStayInBlockAfterStore(
                            slotStore))
                    {
                        supportingStackStores.Add(slotStore);
                        _unsafeRunDeclarations.Add(slotStore);
                    }
                    else if (!_function.UsesUpdatedMemorySafetyRules
                        && _unsafeContext.ContainsAwaitSyntax
                        && OperationMemorySafetyContract.ContainsPointer(
                            slotStore.Value.ResultType)
                        && UnsafeAwaitOperand
                            .CanScopeLegacyPointerStackSlot(
                                _function,
                                slotStore))
                    {
                        supportingStackStores.Add(slotStore);
                        _unsafeRunDeclarations.Add(slotStore);
                        legacyAwaitScopedStackStores.Add(slotStore);
                    }
                    break;
                case LoadStackSlot slotLoad:
                    seenSlots.Add(slotLoad.Slot);
                    break;
            }
        }

        if (!_unsafeContext.EmitsExplicitUnsafeContexts)
            return;

        foreach (var store in _declaringNodes
            .OfType<StoreLocal>()
            .ToList())
        {
            if (_legacyAwaitScopedDeclarations.Contains(store))
                continue;
            if (store.Type.Kind != TypeRefKind.ByRef
                && _unsafeContext.NeedsUnsafeBlock(store)
                && LocalIsRead(store.Index)
                && !LocalReadsStayInsideUnsafeRun(store))
            {
                RemoveLocalDeclaration(store);
                if (store.Value is StackAllocArray
                    && store.Type.Kind != TypeRefKind.Pointer)
                {
                    _scopedLocals.Add(store.Index);
                }
                continue;
            }
            if (DeclarationIsInsideUnsafeRun(store))
                RemoveLocalDeclaration(store);
        }

        foreach (var store in supportingStackStores)
        {
            if (legacyAwaitScopedStackStores.Contains(store))
                continue;
            if (!_unsafeContext.NeedsUnsafeBlock(store)
                || StackSlotReferencesStayInBlockAfterStore(store)
                    && !StackSlotUnsafeRunContainsAwait(store))
            {
                continue;
            }
            _unsafeRunDeclarations.Remove(store);
        }

        foreach (var init in _declaringNodes.OfType<InitObject>().ToList())
        {
            if (DeclarationIsInsideUnsafeRun(init))
                RemoveLocalDeclaration(init);
        }
    }

    bool LastReferenceIsInside(int localIndex, IrNode subtree)
    {
        IrNode? last = null;
        foreach (var node in
            _function.DescendantsOutsideNestedFunctions)
        {
            if (node is LoadLocal load
                    && load.Index == localIndex
                || node is StoreLocal store
                    && store.Index == localIndex
                || node is LoadLocalAddress address
                    && address.Index == localIndex)
            {
                last = node;
            }
        }
        for (IrNode? current = last;
            current is not null;
            current = current.Parent)
        {
            if (ReferenceEquals(current, subtree))
                return true;
        }
        return false;
    }

    bool StackSlotUnsafeRunContainsAwait(StoreStackSlot store)
    {
        if (store.Parent is not Block block
            || store.ChildIndex < 0)
        {
            return false;
        }

        int lastReference = store.ChildIndex;
        for (int index = store.ChildIndex + 1;
            index < block.Children.Count;
            index++)
        {
            if (ReferencesStackSlot(
                block.Children[index],
                store.Slot))
            {
                lastReference = index;
            }
        }

        return block.Children
            .Skip(store.ChildIndex)
            .Take(lastReference - store.ChildIndex + 1)
            .Any(UnsafeAwaitOperand.ContainsAwait);
    }

    void AddLocalDeclaration(IrNode declaration)
    {
        _declaringNodes.Add(declaration);
        _unsafeRunDeclarations.Add(declaration);
    }

    void RemoveLocalDeclaration(IrNode declaration)
    {
        _declaringNodes.Remove(declaration);
        _unsafeRunDeclarations.Remove(declaration);
    }


    void CollectDeclarationScopes(int localCount)
    {
        for (int index = 0; index < localCount; index++)
            _declarationScopes[index] = _function.Body;

        foreach (var declaration in _declaringNodes)
        {
            int? index = declaration switch
            {
                StoreLocal store => store.Index,
                InitObject
                {
                    Address: LoadLocalAddress address,
                } => address.Index,
                _ => null,
            };
            if (index is { } local && declaration.Parent is { } parent)
            {
                _declarationScopes[local] =
                    EmittedDeclarationScope(parent);
            }
        }

        foreach (var node in _function.DescendantsOutsideNestedFunctions)
        {
            switch (node)
            {
                case IsPattern pattern
                    when DeclarationScope(pattern) is { } patternScope:
                    AddOwned(pattern.LocalIndex, patternScope);
                    break;
                case RecursivePropertyDeclarationPattern pattern
                    when DeclarationScope(pattern) is { } patternScope:
                    AddOwned(pattern.LocalIndex, patternScope);
                    break;
                case PatternSwitchExpressionArm arm:
                    AddOwned(arm.LocalIndex, arm);
                    AddOwned(arm.Subpattern?.LocalIndex, arm);
                    break;
                case UnionSwitchExpressionArm arm:
                    AddOwned(arm.LocalIndex, arm);
                    break;
                case ForeachStatement loop:
                    AddOwned(loop.LocalIndex, loop);
                    break;
                case UsingStatement
                {
                    DeclaresResourceVariable: true,
                } resource:
                    AddOwned(resource.LocalIndex, resource);
                    break;
                case Fixed { LocalIsStackSlot: false } pin:
                    AddOwned(pin.LocalIndex, pin);
                    break;
                case CatchClause clause:
                    AddOwned(clause.VariableIndex, clause);
                    break;
            }
        }
        foreach (var (local, owner, _) in _verifiedOutDeclarations)
            _declarationScopes[local] = EmittedDeclarationScope(owner);

        void AddOwned(int? index, IrNode owner)
        {
            if (index is { } local
                && IrFunction.LocalSlotReferencesInScope(
                        _function.Body,
                        local)
                    .All(reference =>
                        ExactLocalNameAllocation.Contains(owner, reference)))
            {
                _declarationScopes[local] =
                    EmittedDeclarationScope(owner);
            }
        }
    }

    IEnumerable<(int Local, IrNode Owner, LoadLocalAddress Address)>
        VerifiedOutLocalDeclarations()
    {
        var retained = ExactLocalNameAllocation.RetainedLocalSlots(
            _function,
            _function.Locals.Length,
            _function.EliminatedLocalSlots);
        var repeatedNames = retained
            .Where(index => index < _function.LocalNames.Length)
            .Select(index => _function.LocalNames[index])
            .Where(name => name is not null)
            .GroupBy(name => name!, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var node in _function.DescendantsOutsideNestedFunctions)
        {
            MethodRef? callee;
            IReadOnlyList<IrExpression>? arguments;
            int parameterStart;
            switch (node)
            {
                case Call call:
                    callee = call.Callee;
                    arguments = call.Arguments;
                    parameterStart = callee.HasThis ? 1 : 0;
                    break;
                case NewObject creation:
                    callee = creation.Constructor;
                    arguments = creation.Arguments;
                    parameterStart = 0;
                    break;
                default:
                    continue;
            }
            if (OutVariableDeclarationScope(node) is not { } owner)
                continue;
            for (int argumentIndex = parameterStart;
                argumentIndex < arguments.Count;
                argumentIndex++)
            {
                int parameterIndex = argumentIndex - parameterStart;
                if (callee.TryGetVerifiedOutLocal(
                        parameterIndex,
                        arguments[argumentIndex],
                        out int local)
                    && arguments[argumentIndex]
                        is LoadLocalAddress address
                    && _function.IsLocalDeclaredInNestedScope(local)
                    && local < _function.LocalNames.Length
                    && _function.LocalNames[local] is { } name
                    && repeatedNames.Contains(name)
                    && ReferenceEquals(
                        IrFunction.LocalSlotReferencesInScope(
                                _function.Body,
                                local)
                            .FirstOrDefault(),
                        arguments[argumentIndex])
                    && IrFunction.LocalSlotReferencesInScope(
                            _function.Body,
                            local)
                        .All(reference =>
                            ExactLocalNameAllocation.Contains(
                                owner,
                                reference)))
                {
                    yield return (local, owner, address);
                }
            }
        }
    }

    static IrNode? DeclarationScope(IrNode declaration)
    {
        for (IrNode? current = declaration.Parent;
            current is not null and not IrFunction;
            current = current.Parent)
        {
            if (current is Block block)
            {
                return block.Parent is BlockContainer container
                    ? container
                    : block;
            }
        }
        return null;
    }

    static IrNode? OutVariableDeclarationScope(IrNode declaration)
    {
        for (IrNode? current = declaration.Parent;
            current is not null and not IrFunction;
            current = current.Parent)
        {
            if (current is WhileLoop or DoWhileLoop or ForLoop
                or UsingStatement or ForeachStatement or Fixed
                or CatchClause or SwitchExpressionArm
                or UnionSwitchExpressionArm
                or SynthesizedSwitchExpressionArm
                or TupleSwitchExpressionArm
                or PatternSwitchExpressionArm)
            {
                return current;
            }
            if (current is Block block)
            {
                return block.Parent is BlockContainer container
                    ? container
                    : block;
            }
        }
        return null;
    }

    static IrNode EmittedDeclarationScope(IrNode owner)
        => owner switch
        {
            Block
            {
                Parent: BlockContainer
                {
                    Parent: SwitchSection
                    {
                        Parent: Switch switchStatement,
                    },
                },
            } => switchStatement,
            BlockContainer
            {
                Parent: SwitchSection
                {
                    Parent: Switch switchStatement,
                },
            } => switchStatement,
            Block { Parent: BlockContainer container } => container,
            _ => owner,
        };

    internal static IReadOnlySet<int>? PdbLocalEntryLabelsPrintedOutside(
        IrFunction function,
        IrNode declaration,
        Block declarationBlock)
    {
        HashSet<int>? labels = null;
        if (PdbLocalPrecedingScopeAnchors(
                declaration,
                declarationBlock) is { } anchors)
        {
            labels = anchors
                .Select(anchor => anchor.SourceOffset)
                .ToHashSet();
        }

        if (declaration.ChildIndex == 0)
        {
            Block entryBlock = declarationBlock;
            while (entryBlock.Parent is Block parent)
                entryBlock = parent;
            if (entryBlock.Parent is BlockContainer
                && entryBlock.StartOffset >= 0
                && ReferenceOwnership.CollectBranchTargets(function)
                    .Contains(entryBlock.StartOffset))
            {
                (labels ??= []).Add(entryBlock.StartOffset);
            }
        }
        return labels;
    }

    static IReadOnlyList<LabelAnchor>? PdbLocalPrecedingScopeAnchors(
        IrNode declaration,
        Block declarationBlock)
    {
        List<LabelAnchor>? anchors = null;
        AddPrecedingRetainedAnchor(declaration, declarationBlock);
        if (declaration.ChildIndex == 0)
        {
            Block entryBlock = declarationBlock;
            while (entryBlock.Parent is Block parent)
            {
                AddPrecedingRetainedAnchor(entryBlock, parent);
                entryBlock = parent;
            }
        }
        return anchors;

        void AddPrecedingRetainedAnchor(IrNode child, Block parent)
        {
            if (child.ChildIndex > 0
                && parent.Children[child.ChildIndex - 1] is LabelAnchor
                {
                    RetainsPdbLocalScope: true,
                    SourceOffset: >= 0,
                } anchor)
            {
                (anchors ??= []).Add(anchor);
            }
        }
    }

    bool LocalIsRead(int index)
        => _function.DescendantsOutsideNestedFunctions.Any(node =>
            node is LoadLocal load && load.Index == index
                || node is LoadLocalAddress address
                    && address.Index == index);

    bool LocalReadsStayInsideUnsafeRun(StoreLocal store)
    {
        if (store.Parent is not Block container)
            return false;
        int start = store.ChildIndex;
        if (start < 0 || start >= container.Children.Count)
            return false;

        int end = start;
        if (store.Value is StackAllocArray
            && store.Type.Kind == TypeRefKind.Pointer)
        {
            end = UnsafeRunEnd(container.Children, start) - 1;
        }
        else
        {
            while (end + 1 < container.Children.Count
                && _unsafeContext.NeedsUnsafeBlock(
                    container.Children[end + 1]))
            {
                end++;
            }
        }

        foreach (var node in _function.DescendantsOutsideNestedFunctions)
        {
            if (node is LoadLocal load && load.Index == store.Index
                || node is LoadLocalAddress address
                    && address.Index == store.Index)
            {
                bool insideRun = false;
                for (int index = start; index <= end; index++)
                {
                    insideRun |= ReferenceOwnership.IsInside(
                        node,
                        container.Children[index]);
                }
                if (!insideRun)
                    return false;
            }
        }
        return true;
    }

    bool DeclarationIsInsideUnsafeRun(IrNode statement)
    {
        if (statement.Parent is not Block block
            || statement.ChildIndex <= 0)
        {
            return false;
        }
        for (int index = 0; index < statement.ChildIndex; index++)
        {
            if (_unsafeContext.NeedsUnsafeBlock(block.Children[index])
                && UnsafeRunEnd(block.Children, index)
                    > statement.ChildIndex)
            {
                return true;
            }
        }
        return false;
    }

    int UnsafeRunEnd(IReadOnlyList<IrNode> statements, int start)
    {
        int end = start + 1;
        while (end < statements.Count
            && _unsafeContext.NeedsUnsafeBlock(statements[end]))
        {
            end++;
        }

        for (int index = start; index < end; index++)
        {
            int requiredEnd = statements[index] switch
            {
                StoreStackSlot store
                    when _unsafeRunDeclarations.Contains(store)
                    => LastReferenceEnd(
                        statements,
                        end,
                        node => ReferencesStackSlot(node, store.Slot)),
                StoreLocal store
                    when _unsafeRunDeclarations.Contains(store)
                        && _unsafeContext.NeedsUnsafeBlock(store)
                    => LastReferenceEnd(
                        statements,
                        end,
                        node => ReferencesLocalIncludingSharedNestedScopes(
                            node,
                            store.Index)),
                _ => end,
            };
            if (requiredEnd <= end)
                continue;
            end = requiredEnd;
            while (end < statements.Count
                && _unsafeContext.NeedsUnsafeBlock(statements[end]))
            {
                end++;
            }
        }
        return end;
    }

    static int LastReferenceEnd(
        IReadOnlyList<IrNode> statements,
        int start,
        Func<IrNode, bool> hasReference)
    {
        int end = start;
        for (int index = start; index < statements.Count; index++)
        {
            if (hasReference(statements[index]))
                end = index + 1;
        }
        return end;
    }

    bool LocalReferencesStayInBlockAfterStore(StoreLocal store)
    {
        if (store.Parent is not Block || store.ChildIndex < 0)
            return false;
        if (ReferencesLocal(store.Value, store.Index))
            return false;
        if (HasBranchTargetAfterStatement(store))
            return false;
        return LocalReferencesStayInBlockAfterStatement(
            store,
            store.Index);
    }

    bool LocalReferencesStayInsideDeclarationBlock(
        IrNode declaration,
        int index)
    {
        if (declaration.Parent is not Block block
            || declaration.ChildIndex < 0)
        {
            return false;
        }
        if (declaration is StoreLocal store
            && ReferencesLocal(store.Value, store.Index))
        {
            return false;
        }
        if (declaration.ChildIndex >= block.Children.Count
            || !ReferenceEquals(
                block.Children[declaration.ChildIndex],
                declaration))
        {
            return false;
        }

        var allowed = block.Children
            .Skip(declaration.ChildIndex)
            .ToList();
        IReadOnlySet<int>? labelsPrintedOutside =
            PdbLocalEntryLabelsPrintedOutside(
                _function,
                declaration,
                block);
        bool hasRetainedAnchor = allowed.Any(statement =>
                statement.DescendantsOutsideNestedFunctions
                    .Prepend(statement)
                    .Any(node => node is LabelAnchor
                    {
                        RetainsPdbLocalScope: true,
                    }))
            || PdbLocalPrecedingScopeAnchors(
                declaration,
                block) is not null;
        if (HasBranchTargetAfterStatement(declaration)
            && (!hasRetainedAnchor
                || ReferenceOwnership.RewriteWouldInvalidateLabels(
                    _function,
                    allowed,
                    [],
                    labelsPrintedOutside)))
        {
            return false;
        }

        foreach (var reference in
            IrFunction.LocalSlotReferencesInScope(
                _function.Body,
                index))
        {
            if (!allowed.Any(statement =>
                ReferenceOwnership.IsInside(reference, statement)))
            {
                return false;
            }
        }
        return true;
    }

    bool LocalReferencesStayInBlockAfterStatement(
        IrNode statement,
        int index)
    {
        if (statement.Parent is not Block block
            || statement.ChildIndex < 0)
        {
            return false;
        }
        var allowed = block.Children.Skip(statement.ChildIndex).ToList();
        if (HasBranchTargetAfterStatement(statement))
            return false;
        foreach (var candidateBlock in _function.Body.Blocks)
        {
            foreach (var node in candidateBlock.Children)
            {
                if (ReferencesLocalIncludingSharedNestedScopes(
                        node,
                        index)
                    && !allowed.Any(candidate =>
                        ReferenceOwnership.IsInside(node, candidate)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    bool StackSlotReferencesStayInBlockAfterStore(
        StoreStackSlot store)
    {
        if (store.Parent is not Block block
            || store.ChildIndex < 0)
        {
            return false;
        }
        if (ReferencesStackSlot(store.Value, store.Slot))
            return false;
        var allowed = block.Children.Skip(store.ChildIndex).ToList();
        if (HasBranchTargetAfterStatement(store))
            return false;
        bool sawLoad = false;
        foreach (var node in _function.DescendantsOutsideNestedFunctions)
        {
            if (node is StoreStackSlot candidate
                    && candidate.Slot == store.Slot
                || node is LoadStackSlot load
                    && load.Slot == store.Slot)
            {
                if (!allowed.Any(statement =>
                    ReferenceOwnership.IsInside(node, statement)))
                {
                    return false;
                }
                sawLoad |= node is LoadStackSlot;
            }
        }
        return sawLoad;
    }

    bool HasBranchTargetAfterStatement(IrNode statement)
    {
        if (statement.Parent is not Block block
            || statement.ChildIndex < 0)
        {
            return false;
        }
        return block.Children
            .Skip(statement.ChildIndex + 1)
            .SelectMany(node =>
                node.DescendantsAndSelfOutsideNestedFunctions)
            .Any(node => node.OwnsSourceLabel
                && node.SourceOffset >= 0
                && _labelTargets.Contains(node.SourceOffset));
    }

    static bool ReferencesLocal(IrNode node, int index)
        => IsLocalReference(node, index)
            || node.DescendantsOutsideNestedFunctions
                .Any(candidate => IsLocalReference(candidate, index));

    static bool ReferencesLocalIncludingSharedNestedScopes(
        IrNode node,
        int index)
    {
        if (node is Lambda { NeedsIsolatedLocalScope: true }
            or LocalFunctionStatement
            {
                NeedsIsolatedLocalScope: true,
            })
        {
            return false;
        }
        if (IsLocalReference(node, index))
            return true;
        foreach (var child in node.Children)
        {
            if (child is Lambda { NeedsIsolatedLocalScope: true }
                or LocalFunctionStatement
                {
                    NeedsIsolatedLocalScope: true,
                })
            {
                continue;
            }
            if (ReferencesLocalIncludingSharedNestedScopes(child, index))
                return true;
        }
        return false;
    }

    static bool ReferencesStackSlot(IrNode node, int slot)
        => IsStackSlotReference(node, slot)
            || node.DescendantsOutsideNestedFunctions
                .Any(candidate =>
                    IsStackSlotReference(candidate, slot));

    static bool IsLocalReference(IrNode node, int index)
        => node is StoreLocal store && store.Index == index
            || node is LoadLocal load && load.Index == index
            || node is LoadLocalAddress address
                && address.Index == index;

    static bool IsStackSlotReference(IrNode node, int slot)
        => node is LoadStackSlot load && load.Slot == slot
            || node is StoreStackSlot store && store.Slot == slot;

}

/// <summary>
/// The C# unsafe-context policy as observed while declaration ownership is
/// planned, before residual stack-slot unification and inline-receiver
/// rendering decisions.
/// </summary>
sealed class LocalDeclarationUnsafeContext(IrFunction function)
{
    readonly bool _newMemorySafetyRules =
        function.UsesUpdatedMemorySafetyRules;
    readonly bool _skipLocalsInit = function.SkipLocalsInit;
    readonly TypeRef _returnType = function.Signature.ReturnType;
    readonly List<ConsumedMemberEvidence> _consumedMembers = [];

    public bool ContainsAwaitSyntax { get; } =
        UnsafeAwaitOperand.ContainsAwait(function);

    public bool EmitsExplicitUnsafeContexts
        => _newMemorySafetyRules || ContainsAwaitSyntax;

    public bool NeedsUnsafeBlock(IrNode node)
        => EmitsExplicitUnsafeContexts
            && NeedsUnsafeContext(node)
            && !CanRenderUnsafeContextAsExpressions(node);

    bool NeedsUnsafeContext(IrNode node)
        => node switch
        {
            ForLoop loop => HasRequiredUnsafeOperation(loop.Initializer)
                || HasRequiredUnsafeOperation(loop.Condition)
                || HasRequiredUnsafeOperation(loop.Increment),
            WhileLoop loop =>
                HasRequiredUnsafeOperation(loop.Condition),
            DoWhileLoop loop =>
                HasRequiredUnsafeOperation(loop.Condition),
            IfStatement conditional =>
                HasRequiredUnsafeOperation(conditional.Condition),
            Switch switchNode =>
                HasRequiredUnsafeOperation(switchNode.Value),
            Lock lockNode =>
                HasRequiredUnsafeOperation(lockNode.LockObject),
            Fixed { RequiresUnsafeContext: true } => true,
            Fixed fixedNode =>
                HasRequiredUnsafeOperation(fixedNode.PinSource)
                || !_newMemorySafetyRules,
            UsingStatement usingNode =>
                HasRequiredUnsafeOperation(usingNode.Resource)
                || MethodsRequireUnsafe(usingNode.ConsumedMemberRefs),
            ForeachStatement foreachNode =>
                HasRequiredUnsafeOperation(foreachNode.Collection)
                || MethodsRequireUnsafe(
                    foreachNode.ConsumedMemberRefs)
                || !_newMemorySafetyRules
                    && OperationMemorySafetyContract.ContainsPointer(
                        foreachNode.LocalType),
            LocalFunctionStatement => false,
            TryCatch tryCatch => tryCatch.Clauses.Any(clause =>
                HasRequiredUnsafeOperation(clause.Filter)),
            TryFinally => false,
            _ => HasRequiredUnsafeOperation(node),
        };

    bool HasRequiredUnsafeOperation(IrNode? node)
        => node is not null
            && (node.DescendantsAndSelfOutsideNestedFunctions
                    .Any(IsUnsafeOperation)
                || !_newMemorySafetyRules
                    && node.DescendantsAndSelfOutsideNestedFunctions
                        .Any(IsLegacyPointerOperation));

    bool CanRenderUnsafeContextAsExpressions(IrNode node)
    {
        if (!_newMemorySafetyRules)
            return false;

        switch (node)
        {
            case Return { Value: { } value }
                when value is not StackAllocate
                    and not SwitchExpression
                    and not UnionSwitchExpression
                    and not PatternSwitchExpression
                    and not TupleSwitchExpression:
                return UnsafeRequirementsAreWithin(
                    node,
                    _returnType.Kind == TypeRefKind.ByRef,
                    value);
            case YieldReturn yieldReturn:
                return UnsafeRequirementsAreWithin(
                    node,
                    yieldReturn.Value);
            case Throw { Value: { } value }
                when value is not CaughtException:
                return UnsafeRequirementsAreWithin(node, value);
            case ScalarStore { Value: not StackAllocate } store:
                return AssignmentUnsafeExpressionRoot(
                        store.Value,
                        store.UpdateKind) is { } root
                    && UnsafeRequirementsAreWithin(
                        store,
                        store is StoreLocal
                        {
                            Type.Kind: TypeRefKind.ByRef,
                        },
                        root);
            case StoreStackSlot
            {
                Value: not StackAllocate,
            } store:
                TypeRef? slotType = store.Value.ResultType;
                return AssignmentUnsafeExpressionRoot(
                        store.Value,
                        ResidualSlotUpdateKind(store)) is { } slotRoot
                    && UnsafeRequirementsAreWithin(
                        store,
                        slotType?.Kind == TypeRefKind.ByRef,
                        slotRoot);
            case StoreElement store:
                return UnsafeRequirementsAreWithin(
                    store,
                    store.Value);
            case DeconstructionAssignment assignment:
                return UnsafeRequirementsAreWithin(
                    assignment,
                    assignment.Source);
            case ChainedAssignment assignment:
                return UnsafeRequirementsAreWithin(
                    assignment,
                    assignment.Value);
            case NullCoalescingAssignment assignment:
                return UnsafeRequirementsAreWithin(
                    assignment,
                    assignment.Value);
            case NullCoalescingFieldAssignment assignment:
                return UnsafeRequirementsAreWithin(
                    assignment,
                    assignment.Value);
            case NullCoalescingPropertyAssignment assignment:
                return UnsafeRequirementsAreWithin(
                    assignment,
                    assignment.Value);
            case EventSubscription subscription:
                return UnsafeRequirementsAreWithin(
                    subscription,
                    subscription.Value);
            case ExpressionStatement
            {
                Expression: { } expression,
            }
                when expression is not UnsupportedNode:
                return CanDiscardUnsafeExpression(expression)
                    && UnsafeRequirementsAreWithin(
                        node,
                        expression);
            case ConditionalBranch branch:
                return UnsafeRequirementsAreWithin(
                    branch,
                    branch.Condition);
            case SwitchBranch branch:
                return UnsafeRequirementsAreWithin(
                    branch,
                    branch.Value);
            case ForLoop loop:
                return !NeedsUnsafeBlock(loop.Initializer)
                    && !NeedsUnsafeBlock(loop.Increment)
                    && UnsafeExpressionCompilerSupports(
                        loop.Condition,
                        acceptsDirectRequiresUnsafeMember: true);
            case WhileLoop loop:
                return UnsafeExpressionCompilerSupports(
                    loop.Condition);
            case DoWhileLoop loop:
                return UnsafeExpressionCompilerSupports(
                    loop.Condition);
            case IfStatement conditional:
                return UnsafeExpressionCompilerSupports(
                    conditional.Condition);
            case Switch switchNode:
                return UnsafeExpressionCompilerSupports(
                    switchNode.Value,
                    acceptsDirectRequiresUnsafeMember: true);
            case Fixed fixedNode:
                return !fixedNode.RequiresUnsafeContext
                    && !fixedNode.SourceIsAddress
                    && UnsafeExpressionCompilerSupports(
                        fixedNode.PinSource);
            case UsingStatement usingNode:
                return !MethodsRequireUnsafe(
                        usingNode.ConsumedMemberRefs)
                    && UnsafeExpressionCompilerSupports(
                        usingNode.Resource);
            case ForeachStatement foreachNode:
                return !MethodsRequireUnsafe(
                        foreachNode.ConsumedMemberRefs)
                    && UnsafeExpressionCompilerSupports(
                        foreachNode.Collection,
                        acceptsDirectRequiresUnsafeMember: true);
            case Lock lockNode:
                return UnsafeExpressionCompilerSupports(
                    lockNode.LockObject);
            case TryCatch tryCatch:
                return tryCatch.Clauses.All(clause =>
                    clause.Filter is not { } filter
                    || UnsafeExpressionCompilerSupports(filter));
            default:
                return false;
        }
    }

    bool UnsafeRequirementsAreWithin(
        IrNode node,
        params IrExpression[] expressions)
        => UnsafeRequirementsAreWithin(
            node,
            allowOwnerOperation: false,
            expressions);

    bool UnsafeRequirementsAreWithin(
        IrNode node,
        bool allowOwnerOperation,
        params IrExpression[] expressions)
    {
        bool found = false;
        foreach (var operation in
            node.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (!IsUnsafeOperation(operation))
                continue;
            found = true;
            if (allowOwnerOperation
                && ReferenceEquals(operation, node))
            {
                continue;
            }
            if (!expressions.Any(expression =>
                ReferenceOwnership.IsInside(operation, expression)))
            {
                return false;
            }
        }
        return found && expressions.All(expression =>
            UnsafeExpressionCompilerSupports(expression));
    }

    bool UnsafeExpressionCompilerSupports(
        IrExpression expression,
        bool acceptsDirectRequiresUnsafeMember = false)
        => (acceptsDirectRequiresUnsafeMember
                || !(expression is AddressOfMethod
                {
                    Method: { } method,
                }
                    && MethodRequiresUnsafe(method))
                && !(expression is LoadProperty
                {
                    Accessor: { } accessor,
                }
                    && MethodRequiresUnsafe(accessor)))
            && !expression.DescendantsAndSelfOutsideNestedFunctions
                .Any(operation => operation is StackAllocArray
                {
                    ResultType:
                    {
                        Kind: TypeRefKind.Pointer,
                    },
                });

    bool IsUnsafeOperation(IrNode node)
        => OperationMemorySafetyContract.RequiresUnsafe(
            node,
            _newMemorySafetyRules,
            _skipLocalsInit,
            _consumedMembers,
            RefBindingTargetType);

    TypeRef? RefBindingTargetType(IrNode node)
        => node switch
        {
            StoreLocal store => store.Type,
            StoreStackSlot store => store.Value.ResultType,
            Return => _returnType,
            _ => null,
        };

    bool IsLegacyPointerOperation(IrNode node)
        => node is StoreStackSlot store
            ? OperationMemorySafetyContract.ContainsPointer(
                store.Value.ResultType)
            : UnsafeAwaitOperand.IsLegacyPointerOperation(node);

    bool MethodRequiresUnsafe(MethodRef? method)
        => method is not null
            && OperationMemorySafetyContract.MethodRequiresUnsafe(
                method,
                _newMemorySafetyRules);

    bool MethodsRequireUnsafe(IEnumerable<MethodRef?> methods)
        => methods.Any(MethodRequiresUnsafe);

    static IrExpression? AssignmentUnsafeExpressionRoot(
        IrExpression value,
        ScalarUpdateKind? updateKind)
    {
        if (updateKind is null)
            return value;
        var binary = (Binary)value;
        if (binary.IsChecked
            || binary.Kind is BinaryKind.ShiftLeft
                or BinaryKind.ShiftRight)
        {
            return null;
        }
        return binary.Right;
    }

    static ScalarUpdateKind? ResidualSlotUpdateKind(
        StoreStackSlot store)
        => store.Value.ResultType?.Kind != TypeRefKind.Pointer
            && store.Value is Binary
            {
                Left: LoadStackSlot read,
            } binary
            && read.Slot == store.Slot
                ? ScalarSelfUpdatePass.Classify(binary)
                : null;

    static bool CanDiscardUnsafeExpression(IrExpression expression)
        => expression.ResultType is
        {
            Kind: not TypeRefKind.ByRef,
        } type
            && type is not
            {
                Namespace: "System",
                Name: "Void",
            };

}
