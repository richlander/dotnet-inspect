using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The exact Roslyn classic-async lowering protocol, proven once per request
/// over both the unmodified import snapshot and the derived planning view.
/// <para>
/// Nothing here is a shape allow list. Every completion callback, completion
/// catch, awaiter bind, state constant, dispatcher, and resume point is proven
/// as one role in a single closed protocol: callbacks carry exact typed
/// signatures on the machine's own <c>&lt;&gt;t__builder</c> field type;
/// <c>GetAwaiter</c> binds carry exact typed member and call-site identity in
/// both coordinate spaces; the catch is exactly the compiler's
/// <c>catch (Exception e)</c> whose handler variable reaches
/// <c>SetException</c>; and every suspension state constant is bound to the
/// dispatcher, resume block, awaiter local, and awaiter cache field that
/// consume it. A body that fails any part of the proof yields <em>no</em>
/// protocol roles at all, so the accountant sees unaccounted scaffolding and
/// declines rather than treating a name-matching node as protocol.
/// </para>
/// <para>
/// The proof's work is proportional to what it charges. One charged pass builds
/// every index the later phases need — state stores and awaiter transfers by
/// block, blocks by start offset, dispatch tests by tested state, spill stores
/// by slot, exception-region membership by raw offset, and each node's position
/// in its parent — so no phase rescans the body per state. Every later phase
/// charges for each element it touches, which makes a reintroduced whole-body
/// rescan visible as budget consumption rather than as silent quadratic work.
/// </para>
/// <para>Owning design: <c>docs/design/classic-async-reconstruction.md</c>.</para>
/// </summary>
internal sealed class ClassicInverseLoweringProof
{
    internal const string StateLocalStore = "state-local-store";
    internal const string StateFieldStore = "state-field-store";
    internal const string StateSpill = "state-spill";
    internal const string StateDispatch = "state-dispatch";
    internal const string SuspensionGuard = "state-suspension-guard";
    internal const string CompletionCatch = "builder-completion-catch";
    internal const string SetResultCallback = "builder-SetResult";
    internal const string SetExceptionCallback = "builder-SetException";
    internal const string AwaitCallback = "builder-AwaitUnsafeOnCompleted";
    internal const string AwaiterCacheStore = "awaiter-cache-store";
    internal const string AwaiterRestore = "awaiter-restore";
    internal const string AwaiterClear = "awaiter-clear";
    internal const string AwaiterBind = "awaiter-bind";
    internal const string GetAwaiterCall = "get-awaiter";
    internal const string AwaitOperandStore = "await-operand-store";
    internal const string AwaitOperandAddress = "await-operand-address";
    internal const string AwaitCompletionBranch = "awaiter-completed-branch";

    const string BudgetFailure =
        "the lowering-protocol proof exhausted the planning budget";

    readonly Dictionary<IrNode, string> _roles;

    ClassicInverseLoweringProof(
        Dictionary<IrNode, string> roles,
        string? failure)
    {
        _roles = roles;
        Failure = failure;
    }

    /// <summary>Why the lowering protocol is unproven, or <c>null</c> when it holds.</summary>
    internal string? Failure { get; }

    /// <summary>The proven protocol role of one node, or <c>null</c>.</summary>
    internal string? RoleOf(IrNode node) => _roles.GetValueOrDefault(node);

    internal bool Proves(IrNode node, string role)
        => _roles.TryGetValue(node, out string? actual) && actual == role;

    internal static ClassicInverseLoweringProof Derive(
        IrFunction planning,
        IrFunction raw,
        TypeRef machine,
        int stateLocal,
        ImmutableHashSet<int> awaiterLocals,
        ClassicInverseBudget budget)
    {
        var empty = new Dictionary<IrNode, string>(
            ReferenceEqualityComparer.Instance);

        var planningRoles = new Dictionary<IrNode, string>(
            ReferenceEqualityComparer.Instance);
        BodyProtocol? planningProtocol = DeriveBody(
            planning,
            machine,
            stateLocal,
            awaiterLocals,
            isRawImport: false,
            planningRoles,
            budget,
            out string? planningFailure);
        if (planningProtocol is null)
            return new(empty, $"planning view: {planningFailure}");

        var rawRoles = new Dictionary<IrNode, string>(
            ReferenceEqualityComparer.Instance);
        BodyProtocol? rawProtocol = DeriveBody(
            raw,
            machine,
            stateLocal,
            awaiterLocals,
            isRawImport: true,
            rawRoles,
            budget,
            out string? rawFailure);
        if (rawProtocol is null)
            return new(empty, $"raw import: {rawFailure}");

        if (Mismatch(planningProtocol, rawProtocol, budget) is { } mismatch)
            return new(empty, mismatch);

        foreach ((IrNode node, string role) in rawRoles)
            planningRoles[node] = role;
        return new(planningRoles, null);
    }

    /// <summary>
    /// The raw import and the planning view must describe the same protocol.
    /// The two spaces share IL offsets, so the offsets of every proven role are
    /// the join currency between them.
    /// </summary>
    static string? Mismatch(
        BodyProtocol planning,
        BodyProtocol raw,
        ClassicInverseBudget budget)
    {
        if (ExceptionContextMismatch(
                planning.Index,
                raw.Index,
                budget) is { } exceptionMismatch)
        {
            return exceptionMismatch;
        }
        if (planning.SetResult != raw.SetResult)
            return "the raw import and planning view complete through different SetResult callbacks";
        if (planning.SetException != raw.SetException)
            return "the raw import and planning view complete through different SetException callbacks";
        if (planning.ExceptionLocal != raw.ExceptionLocal)
            return "the raw import and planning view bind different completion catch variables";
        if (!planning.Awaits.SequenceEqual(raw.Awaits))
            return "the raw import and planning view suspend at different await callbacks";
        if (!planning.Suspensions.SequenceEqual(raw.Suspensions))
            return "the raw import and planning view record different suspension states";
        if (!planning.Dispatchers.SequenceEqual(raw.Dispatchers))
            return "the raw import and planning view dispatch different resume states";
        if (!planning.Resumes.SequenceEqual(raw.Resumes))
            return "the raw import and planning view restore different awaiter transfers";
        if (!planning.AwaiterBinds.SequenceEqual(raw.AwaiterBinds))
            return "the raw import and planning view bind different GetAwaiter members";
        if (!planning.AwaitCompletions.SequenceEqual(raw.AwaitCompletions))
            return "the raw import and planning view route await completion differently";
        if (!planning.ResumeOffsets.SequenceEqual(raw.ResumeOffsets))
            return "the raw import and planning view resume at different state stores";
        if (!planning.CompletionOffsets.SequenceEqual(raw.CompletionOffsets))
            return "the raw import and planning view complete at different state stores";
        if (planning.GuardCount != raw.GuardCount)
            return "the raw import and planning view guard suspension differently";
        return null;
    }

    /// <summary>
    /// Exception regions are proved in both coordinate spaces. A planning view
    /// that retains flat regions must retain their exact imported identity, and
    /// its actual <see cref="TryCatch"/>/<see cref="TryFinally"/> structure must
    /// describe the same regions. Every planning node with imported provenance
    /// then keeps the same try/handler membership at that raw offset. Nodes
    /// synthesized only to wrap separately covered descendants carry no offset
    /// and therefore add no vacuous correspondence.
    /// </summary>
    static string? ExceptionContextMismatch(
        BodyIndex planning,
        BodyIndex raw,
        ClassicInverseBudget budget)
    {
        if (planning.ImportedExceptionRegions.Count > 0
            && !planning.ImportedExceptionRegions.SequenceEqual(
                raw.ImportedExceptionRegions))
        {
            return "the raw import and planning view retain different "
                + "exception-region identities";
        }

        if (!planning.StructuredExceptionRegions.SequenceEqual(
                raw.ImportedExceptionRegions.Select(
                    static region => region.Identity)))
        {
            return "the planning view structures different exception regions "
                + "from the raw import";
        }

        foreach ((int offset, ImmutableArray<ExceptionMembership> expected)
            in planning.StructuredExceptionContexts)
        {
            if (!raw.SourceOffsets.Contains(offset))
            {
                return $"the planning exception context at IL_{offset:x4} "
                    + "has no raw import correspondence";
            }

            ImmutableArray<ExceptionMembership>? actual =
                raw.ImportedExceptionContextAt(offset, budget);
            if (actual is null)
                return BudgetFailure;
            if (!expected.SequenceEqual(actual.Value))
            {
                return $"the structured exception context at IL_{offset:x4} "
                    + "does not match the raw exception-region extents";
            }
        }

        return null;
    }

    /// <summary>The offsets, state constants, and awaiter transfers one body's protocol occupies.</summary>
    sealed record BodyProtocol(
        CallbackIdentity SetResult,
        CallbackIdentity SetException,
        int ExceptionLocal,
        ImmutableArray<CallbackIdentity> Awaits,
        ImmutableArray<string> Suspensions,
        ImmutableArray<string> Dispatchers,
        ImmutableArray<string> Resumes,
        ImmutableArray<AwaiterBindIdentity> AwaiterBinds,
        ImmutableArray<AwaitCompletionIdentity> AwaitCompletions,
        ImmutableArray<int> ResumeOffsets,
        ImmutableArray<int> CompletionOffsets,
        int GuardCount,
        BodyIndex Index);

    /// <summary>Stable identity of one imported or structured handler region.</summary>
    readonly record struct ExceptionRegionIdentity(
        HandlerKind Kind,
        string CatchType);

    /// <summary>The imported extents carried by one handler region.</summary>
    readonly record struct ExceptionRegionLayout(
        ExceptionRegionIdentity Identity,
        int TryOffset,
        int TryLength,
        int HandlerOffset,
        int HandlerLength,
        int FilterOffset);

    /// <summary>
    /// One region in the exception-context stack. <c>IsHandler</c> separates
    /// the protected range from its handler without inferring either from
    /// display text.
    /// </summary>
    readonly record struct ExceptionMembership(
        int Region,
        bool IsHandler);

    /// <summary>One callback's import anchor and exact typed member identities.</summary>
    readonly record struct CallbackIdentity(
        int Offset,
        string Method,
        string BuilderField);

    /// <summary>One awaiter's exact source member and call-site identity.</summary>
    readonly record struct AwaiterBindIdentity(
        int Offset,
        int Local,
        string Method,
        bool IsVirtual,
        string ConstrainedTo,
        string ReceiverType,
        int ReceiverOffset);

    /// <summary>
    /// One await's exact completion test and the two paths that join at its
    /// <c>GetResult</c> continuation.
    /// </summary>
    readonly record struct AwaitCompletionIdentity(
        int State,
        int BranchOffset,
        int TestOffset,
        int Local,
        string TestMethod,
        bool TestIsVirtual,
        int SuspensionOffset,
        int ResumeOffset,
        int ContinuationOffset,
        int ResultOffset,
        string ResultMethod,
        bool ResultIsVirtual,
        string ResultConstrainedTo);

    /// <summary>One suspension's proven awaiter transfer.</summary>
    readonly record struct AwaiterTransfer(int Local, string CacheField);

    static BodyProtocol? DeriveBody(
        IrFunction body,
        TypeRef machine,
        int stateLocal,
        ImmutableHashSet<int> awaiterLocals,
        bool isRawImport,
        Dictionary<IrNode, string> roles,
        ClassicInverseBudget budget,
        out string? failure)
    {
        BodyIndex? index = BodyIndex.Build(
            body,
            machine,
            stateLocal,
            awaiterLocals,
            isRawImport,
            budget);
        if (index is null)
        {
            failure = BudgetFailure;
            return null;
        }
        if (index.ExceptionFailure is { } exceptionFailure)
        {
            failure = exceptionFailure;
            return null;
        }

        var awaiterBinds = ImmutableArray.CreateBuilder<AwaiterBindIdentity>();
        var boundAwaiterLocals = new HashSet<int>();
        foreach (StoreLocal bind in index.AwaiterBinds)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (bind.Value is not Call getAwaiter
                || !awaiterLocals.Contains(bind.Index)
                || getAwaiter.Callee is not
                {
                    Name: "GetAwaiter",
                    HasThis: true,
                    ParameterTypes.IsDefaultOrEmpty: true,
                }
                || getAwaiter.Arguments is not [IrExpression receiver]
                || getAwaiter.ConstrainedTo is not null
                || receiver.ResultType is not { } receiverType
                || !IsSourceAwaitDispatch(getAwaiter, receiver, receiverType, isRawImport)
                || !bind.Type.Equals(getAwaiter.Callee.ReturnType))
            {
                failure = "an awaiter bind does not preserve source dispatch "
                    + "to the exact instance GetAwaiter member of its operand type";
                return null;
            }

            // StructReceiverInliningPass removes the adjacent rvalue spill.
            // Bind its exact source offset across spaces; no other local use
            // receives the address-use protocol role.
            IrExpression operand = receiver;
            if (isRawImport
                && !getAwaiter.IsVirtual
                && receiver is LoadLocalAddress address
                && bind.Parent is Block block
                && index.PositionOf(bind) is > 0 and var position
                && block.Children[position - 1] is StoreLocal store
                && store.Index == address.Index
                && store.Type.Equals(address.Type)
                && store.Value is Call or LoadProperty
                && store.Value.ResultType is { } valueType
                && valueType.Equals(address.Type))
            {
                operand = store.Value;
                roles[store] = AwaitOperandStore;
                roles[address] = AwaitOperandAddress;
            }

            roles[bind] = AwaiterBind;
            roles[getAwaiter] = GetAwaiterCall;
            boundAwaiterLocals.Add(bind.Index);
            awaiterBinds.Add(new(
                getAwaiter.SourceOffset,
                bind.Index,
                ClassicInverseTypedIdentity.Method(getAwaiter.Callee),
                getAwaiter.IsVirtual,
                ClassicInverseTypedIdentity.Type(getAwaiter.ConstrainedTo),
                ClassicInverseTypedIdentity.Type(
                    getAwaiter.IsVirtual ? receiverType : getAwaiter.Callee.DeclaringType),
                getAwaiter.IsVirtual ? -1 : operand.SourceOffset));
        }
        foreach (int local in awaiterLocals)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (!boundAwaiterLocals.Contains(local))
            {
                failure = "a proven awaiter slot has no exact GetAwaiter bind";
                return null;
            }
        }

        foreach (Call call in index.BuilderCalls)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (call.Parent is not ExpressionStatement)
            {
                failure = $"builder callback '{call.Callee.Name}' is not a statement";
                return null;
            }
            if (call.Callee.Name is not ("SetResult" or "SetException"
                or "AwaitUnsafeOnCompleted"))
            {
                failure =
                    $"unmodeled builder callback '{call.Callee.Name}' on the machine builder";
                return null;
            }

            // The declaring type is authenticated against the machine's own
            // builder field, not against a name: a callback declared on any
            // other builder is not this machine's completion protocol.
            TypeRef builderType = ClassicInverseNodeFacts
                .BuilderField(call.Arguments[0], machine)!
                .Type;
            if (!call.Callee.DeclaringType.Equals(builderType))
            {
                failure = $"builder callback '{call.Callee.Name}' is declared on "
                    + $"'{call.Callee.DeclaringType.ToDisplayString()}', not on the "
                    + "machine's own '<>t__builder' type "
                    + $"'{builderType.ToDisplayString()}'";
                return null;
            }
            if (!ProvesCallbackSignature(
                    call,
                    builderType,
                    machine,
                    out failure))
            {
                return null;
            }
        }

        List<Call> setResults =
            [.. index.BuilderCalls.Where(static call => call.Callee.Name == "SetResult")];
        List<Call> setExceptions =
            [.. index.BuilderCalls.Where(static call => call.Callee.Name == "SetException")];
        List<Call> awaits =
        [
            .. index.BuilderCalls.Where(static call =>
                call.Callee.Name == "AwaitUnsafeOnCompleted"),
        ];

        if (setResults is not [Call setResult])
        {
            failure = "the completion protocol needs exactly one builder "
                + $"SetResult callback; the body has {setResults.Count}";
            return null;
        }
        if (setExceptions is not [Call setException])
        {
            failure = "the completion protocol needs exactly one builder "
                + $"SetException callback; the body has {setExceptions.Count}";
            return null;
        }
        if (setResult.Arguments is not [_] and not [_, LoadLocal])
        {
            failure = "the SetResult callback does not carry the completion result shape";
            return null;
        }
        if (setException.Arguments is not [_, LoadLocal caught])
        {
            failure = "the SetException callback does not pass a handler local";
            return null;
        }

        var suspensionAwaiters = new List<(Call Await, int Local)>();
        foreach (Call await in awaits)
        {
            if (await.Arguments is not
                [_, LoadLocalAddress awaiter, LoadArgument { Index: 0 }]
                || !awaiterLocals.Contains(awaiter.Index))
            {
                failure = "an AwaitUnsafeOnCompleted callback does not pass a proven awaiter slot";
                return null;
            }
            suspensionAwaiters.Add((await, awaiter.Index));
        }

        var setResultStatement = (ExpressionStatement)setResult.Parent!;
        var setExceptionStatement = (ExpressionStatement)setException.Parent!;
        int exceptionLocal = caught.Index;

        if (isRawImport)
        {
            if (!ProveRawCompletionHandler(
                    body,
                    index,
                    setResult,
                    setException,
                    exceptionLocal,
                    out failure))
            {
                return null;
            }
        }
        else if (!ProvePlanningCompletionCatch(
                setResultStatement,
                setExceptionStatement,
                exceptionLocal,
                machine,
                roles,
                out failure))
        {
            return null;
        }

        roles[setResult] = SetResultCallback;
        roles[setException] = SetExceptionCallback;
        foreach (Call await in awaits)
            roles[await] = AwaitCallback;

        return ProveStateProtocol(
            index,
            stateLocal,
            isRawImport,
            setResult,
            setResultStatement,
            setException,
            setExceptionStatement,
            suspensionAwaiters,
            [.. awaiterBinds
                .OrderBy(static identity => identity.Offset)
                .ThenBy(static identity => identity.Local)],
            exceptionLocal,
            roles,
            budget,
            out failure);
    }

    /// <summary>
    /// The exact typed signature of one builder callback. Shape alone —
    /// argument count and callee name — cannot separate the compiler's
    /// completion protocol from a same-named member with a different contract,
    /// so each callback's instance-ness, return type, parameter types, and (for
    /// the imported generic await callback) its by-ref generic definition shape
    /// are proven against the machine's own builder type.
    /// </summary>
    static bool ProvesCallbackSignature(
        Call call,
        TypeRef builderType,
        TypeRef machine,
        out string? failure)
    {
        MethodRef callee = call.Callee;
        if (!callee.HasThis)
        {
            failure = $"builder callback '{callee.Name}' is not an instance callback";
            return false;
        }
        if (!IsVoid(callee.ReturnType))
        {
            failure = $"builder callback '{callee.Name}' does not return void";
            return false;
        }

        switch (callee.Name)
        {
            case "SetException":
                if (callee.ParameterTypes is not [TypeRef thrown]
                    || !MemberIdentity.IsCoreLibraryType(
                        thrown,
                        "System",
                        "Exception")
                    || call.Arguments.Count != 2)
                {
                    failure = "the SetException callback is not "
                        + "'void SetException(System.Exception)'";
                    return false;
                }
                break;

            case "SetResult":
                TypeRef builder = ClassicInverseNodeFacts.Definition(builderType);
                bool carriesResult = builder.Name.EndsWith("`1", StringComparison.Ordinal);
                if (carriesResult)
                {
                    if (builderType.Kind != TypeRefKind.GenericInstance
                        || builderType.TypeArguments is not [TypeRef result]
                        || callee.ParameterTypes is not [TypeRef declared]
                        || !declared.Equals(result)
                        || call.Arguments.Count != 2)
                    {
                        failure = "the SetResult callback is not "
                            + "'void SetResult(T)' for the builder's own result type";
                        return false;
                    }
                }
                else if (!callee.ParameterTypes.IsDefaultOrEmpty
                    || call.Arguments.Count != 1)
                {
                    failure = "the SetResult callback is not 'void SetResult()' "
                        + "for a builder that carries no result";
                    return false;
                }
                break;

            case "AwaitUnsafeOnCompleted":
                if (callee.TypeArguments is not [TypeRef awaiterArgument, TypeRef machineArgument]
                    || callee.ParameterTypes is not [TypeRef awaiterParameter, TypeRef machineParameter]
                    || awaiterParameter.Kind != TypeRefKind.ByRef
                    || machineParameter.Kind != TypeRefKind.ByRef
                    || awaiterParameter.ElementType is not { } awaiterElement
                    || machineParameter.ElementType is not { } machineElement
                    || !awaiterElement.Equals(awaiterArgument)
                    || !machineElement.Equals(machineArgument)
                    || call.Arguments.Count != 3)
                {
                    failure = "the AwaitUnsafeOnCompleted callback is not the "
                        + "builder's 'void AwaitUnsafeOnCompleted<TAwaiter, "
                        + "TStateMachine>(ref TAwaiter, ref TStateMachine)' "
                        + "instantiation";
                    return false;
                }
                if (callee.DefinitionParameterTypes is not
                        [TypeRef definitionAwaiter, TypeRef definitionMachine]
                    || definitionAwaiter is not
                    {
                        Kind: TypeRefKind.ByRef,
                        ElementType:
                        {
                            Kind: TypeRefKind.MethodGenericParameter,
                            GenericParameterIndex: 0,
                        },
                    }
                    || definitionMachine is not
                    {
                        Kind: TypeRefKind.ByRef,
                        ElementType:
                        {
                            Kind: TypeRefKind.MethodGenericParameter,
                            GenericParameterIndex: 1,
                        },
                    }
                    || callee.DefinitionReturnType is not { } definitionReturn
                    || !IsVoid(definitionReturn))
                {
                    failure = "the AwaitUnsafeOnCompleted callback does not carry "
                        + "the imported by-ref generic definition signature";
                    return false;
                }
                if (!ClassicInverseNodeFacts.Definition(machineArgument)
                    .Equals(ClassicInverseNodeFacts.Definition(machine)))
                {
                    failure = "the AwaitUnsafeOnCompleted callback resumes a "
                        + "state machine other than this one";
                    return false;
                }
                break;
        }

        failure = null;
        return true;
    }

    static bool IsVoid(TypeRef type)
        => MemberIdentity.IsCoreLibraryType(type, "System", "Void");

    static bool IsSourceAwaitDispatch(
        Call getAwaiter, IrExpression receiver, TypeRef receiverType, bool isRawImport)
    {
        if (getAwaiter.IsVirtual)
            return receiverType.Equals(getAwaiter.Callee.DeclaringType);

        TypeRef declared = getAwaiter.Callee.DeclaringType;
        TypeRef definition = ClassicInverseNodeFacts.Definition(declared);
        string? typeNamespace = definition.Name switch
        {
            "ValueTask" or "ValueTask`1" => "System.Threading.Tasks",
            "ConfiguredTaskAwaitable" or "ConfiguredTaskAwaitable`1"
                or "ConfiguredValueTaskAwaitable" or "ConfiguredValueTaskAwaitable`1"
                => "System.Runtime.CompilerServices",
            _ => null,
        };
        bool exactReceiver = receiverType is
            { Kind: TypeRefKind.ByRef, ElementType: { } element }
                && element.Equals(declared);
        if (!isRawImport && receiver is Call or LoadProperty)
            exactReceiver |= receiverType.Equals(declared);
        return exactReceiver && typeNamespace is not null
            && MemberIdentity.IsCoreLibraryType(
                definition, typeNamespace, definition.Name);
    }

    /// <summary>
    /// The unmodified import carries the completion catch as an exception
    /// region, not as tree structure: its exact kind, catch type, filter
    /// absence, handler range, and handler-entry variable are the facts that
    /// bind <c>SetException</c> to the exception the runtime caught.
    /// </summary>
    static bool ProveRawCompletionHandler(
        IrFunction body,
        BodyIndex index,
        Call setResult,
        Call setException,
        int exceptionLocal,
        out string? failure)
    {
        List<HandlerRegion> catches =
        [
            .. body.Regions.Where(static region =>
                region.Kind == HandlerKind.Catch),
        ];
        if (body.Regions.Any(static region =>
                region.Kind is HandlerKind.Filter or HandlerKind.Fault))
        {
            failure = "the import carries a filter or fault handler region";
            return false;
        }
        if (catches is not [HandlerRegion completion])
        {
            failure = "the completion protocol needs exactly one catch handler "
                + $"region; the import has {catches.Count}";
            return false;
        }
        if (completion.CatchType is not { } catchType
            || !MemberIdentity.IsCoreLibraryType(
                ClassicInverseNodeFacts.Definition(catchType),
                "System",
                "Exception"))
        {
            failure = "the completion catch does not catch core-library "
                + $"System.Exception (it catches "
                + $"'{completion.CatchType?.ToDisplayString() ?? "<none>"}')";
            return false;
        }
        if (completion.FilterOffset >= 0)
        {
            failure = "the completion catch carries an exception filter";
            return false;
        }

        int start = completion.HandlerOffset;
        int end = start + completion.HandlerLength;
        if (setException.SourceOffset < start || setException.SourceOffset >= end)
        {
            failure = "the SetException callback is outside the completion catch handler";
            return false;
        }
        if (setResult.SourceOffset >= start && setResult.SourceOffset < end)
        {
            failure = "the SetResult callback is inside the completion catch handler";
            return false;
        }

        List<StoreLocal> entries =
        [
            .. index.CaughtStores.Where(store => store.SourceOffset == start),
        ];
        if (entries is not [StoreLocal entry] || entry.Index != exceptionLocal)
        {
            failure = "the completion catch does not store the caught exception "
                + "into the local SetException reads";
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>
    /// The planning view carries the same catch as structure. Its exact type,
    /// handler variable, and three-statement body must agree with the region the
    /// import proved.
    /// </summary>
    static bool ProvePlanningCompletionCatch(
        ExpressionStatement setResultStatement,
        ExpressionStatement setExceptionStatement,
        int exceptionLocal,
        TypeRef machine,
        Dictionary<IrNode, string> roles,
        out string? failure)
    {
        if (EnclosingCatch(setExceptionStatement) is not { } clause)
        {
            failure = "the SetException callback is outside a catch clause";
            return false;
        }
        if (EnclosingCatch(setResultStatement) is not null)
        {
            failure = "the SetResult callback is inside a catch clause";
            return false;
        }
        if (clause.Filter is not null)
        {
            failure = "the completion catch carries an exception filter";
            return false;
        }
        if (!MemberIdentity.IsCoreLibraryType(
                ClassicInverseNodeFacts.Definition(clause.ExceptionType),
                "System",
                "Exception"))
        {
            failure = "the completion catch does not catch core-library "
                + $"System.Exception (it catches "
                + $"'{clause.ExceptionType.ToDisplayString()}')";
            return false;
        }
        if (clause.VariableIndex != exceptionLocal)
        {
            failure = "the completion catch variable is not the local "
                + "SetException reads";
            return false;
        }
        if (clause.Body.Blocks is not [Block block]
            || block.Children is not
            [
                StoreField { Field.Name: "<>1__state" } state,
                ExpressionStatement handled,
                Return { Value: null },
            ]
            || !ReferenceEquals(handled, setExceptionStatement)
            || state.Instance is not LoadArgument { Index: 0 }
            || !ClassicInverseNodeFacts.IsMachineField(state.Field, machine))
        {
            failure = "the completion catch body is not the compiler's "
                + "state/SetException/return arm";
            return false;
        }

        roles[clause] = CompletionCatch;
        failure = null;
        return true;
    }

    static BodyProtocol? ProveStateProtocol(
        BodyIndex index,
        int stateLocal,
        bool isRawImport,
        Call setResult,
        ExpressionStatement setResultStatement,
        Call setException,
        ExpressionStatement setExceptionStatement,
        List<(Call Await, int Local)> awaits,
        ImmutableArray<AwaiterBindIdentity> awaiterBinds,
        int exceptionLocal,
        Dictionary<IrNode, string> roles,
        ClassicInverseBudget budget,
        out string? failure)
    {
        failure = null;
        List<StoreField> stateFieldStores = index.StateFieldStores;
        if (index.HasForeignStateStore)
        {
            failure = "a state store targets storage outside this state machine";
            return null;
        }
        List<StoreLocal> stateLocalStores = index.StateLocalStores;

        if (stateLocal < 0 && awaits.Count > 0)
        {
            failure = "the body suspends without a proven state dispatch local";
            return null;
        }

        var spills = new Dictionary<IrNode, IrNode>(
            ReferenceEqualityComparer.Instance);
        var suspensions = ImmutableArray.CreateBuilder<string>();
        var dispatchers = ImmutableArray.CreateBuilder<string>();
        var resumes = ImmutableArray.CreateBuilder<string>();
        var resumeOffsets = ImmutableArray.CreateBuilder<int>();
        var completionOffsets = ImmutableArray.CreateBuilder<int>();
        var claimed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        var resumeBlocksByState = new Dictionary<int, Block>();

        StoreLocal? init = null;
        foreach (StoreLocal store in stateLocalStores)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (store.Value is not LoadField { Field.Name: "<>1__state" } read
                || read.Instance is not LoadArgument { Index: 0 }
                || !ClassicInverseNodeFacts.IsMachineField(read.Field, index.Machine))
            {
                continue;
            }
            if (init is not null)
            {
                failure = "the body reads the machine state into its dispatch "
                    + "local more than once";
                return null;
            }
            init = store;
        }
        if (init is not null)
        {
            roles[init] = StateLocalStore;
            claimed.Add(init);
        }
        else if (stateLocal >= 0)
        {
            failure = "the dispatch local is never bound to the machine state field";
            return null;
        }

        // --- suspensions: state constant, awaiter transfer, and callback in one block
        var suspensionStates = new Dictionary<int, Block>();
        var suspensionAwaiters = new Dictionary<int, AwaiterTransfer>();
        foreach ((Call await, int awaiterLocal) in awaits)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            var statement = (ExpressionStatement)await.Parent!;
            if (statement.Parent is not Block block)
            {
                failure = "an await callback is not a block statement";
                return null;
            }

            List<StoreField> fieldStores = index.StateFieldStoresIn(block);
            List<StoreLocal> localStores =
            [
                .. index.StateLocalStoresIn(block)
                    .Where(store => !claimed.Contains(store)),
            ];
            if (fieldStores is not [StoreField fieldStore]
                || localStores is not [StoreLocal localStore])
            {
                failure = "a suspension block does not carry exactly one state "
                    + "field store and one dispatch-local store";
                return null;
            }

            int? fieldState = WrittenState(fieldStore.Value, index, isRawImport, spills);
            int? localState = WrittenState(localStore.Value, index, isRawImport, spills);
            if (fieldState is not int state
                || localState != state
                || state < 0)
            {
                failure = "a suspension does not store one proven non-negative "
                    + "state constant into both the machine field and the dispatch local";
                return null;
            }
            int callbackPosition = index.PositionOf(statement);
            if (index.PositionOf(fieldStore) > callbackPosition
                || index.PositionOf(localStore) > callbackPosition)
            {
                failure = "a suspension stores its state after the await callback";
                return null;
            }

            // The awaiter the callback registers must be the awaiter this block
            // caches, into one named machine field, before the callback runs.
            List<StoreField> caches = index.AwaiterCachesIn(block);
            if (caches is not [StoreField cache]
                || ((LoadLocal)cache.Value).Index != awaiterLocal
                || index.PositionOf(cache) > callbackPosition)
            {
                failure = "a suspension does not cache exactly the awaiter its "
                    + "await callback registers before registering it";
                return null;
            }
            if (!suspensionStates.TryAdd(state, block))
            {
                failure = $"two suspensions share state constant {state}";
                return null;
            }
            string cacheField = ClassicInverseTypedIdentity.Field(cache.Field);
            suspensionAwaiters[state] = new AwaiterTransfer(awaiterLocal, cacheField);

            roles[fieldStore] = StateFieldStore;
            roles[localStore] = StateLocalStore;
            roles[cache] = AwaiterCacheStore;
            claimed.Add(fieldStore);
            claimed.Add(localStore);
            claimed.Add(cache);
            suspensions.Add(
                $"{state}@{fieldStore.SourceOffset}/{localStore.SourceOffset}"
                    + $"~{cache.SourceOffset}~L{awaiterLocal}~{cacheField}");
        }

        // --- dispatch and resume: every suspension state is reachable exactly once
        var resumeBlocks = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        foreach ((int state, Block _) in suspensionStates.OrderBy(
            static pair => pair.Key))
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }

            List<ConditionalBranch> tests = index.StateTestsFor(state);
            if (tests is not [ConditionalBranch dispatch])
            {
                failure = $"state {state} is stored at a suspension but "
                    + $"{tests.Count} dispatch tests resume it";
                return null;
            }

            List<Block> targets = index.BlocksStartingAt(dispatch.TargetOffset);
            if (targets is not [Block resume])
            {
                failure = $"the dispatch test for state {state} has no single resume block";
                return null;
            }
            if (!resumeBlocks.Add(resume))
            {
                failure = "two dispatch tests resume into the same block";
                return null;
            }
            resumeBlocksByState[state] = resume;

            // The resume block must undo exactly this state's suspension: the
            // same awaiter local, restored from and cleared in the same cache
            // field. Restoring "some" awaiter from "some" cache field would let
            // a body exchange two suspensions' awaiters and stay protocol.
            AwaiterTransfer transfer = suspensionAwaiters[state];
            List<StoreLocal> restores = index.AwaiterRestoresIn(resume);
            if (restores is not [StoreLocal restore]
                || restore.Index != transfer.Local
                || ClassicInverseTypedIdentity.Field(
                    ((LoadField)restore.Value).Field) != transfer.CacheField)
            {
                failure = $"the resume block for state {state} does not restore "
                    + "the exact awaiter its suspension cached";
                return null;
            }
            List<InitObject> clears = index.AwaiterClearsIn(resume);
            if (clears is not [InitObject clear]
                || ClassicInverseTypedIdentity.Field(
                    ((LoadFieldAddress)clear.Address).Field) != transfer.CacheField)
            {
                failure = $"the resume block for state {state} does not clear "
                    + "the exact awaiter cache its suspension wrote";
                return null;
            }

            List<StoreField> resumeFieldStores =
            [
                .. index.StateFieldStoresIn(resume)
                    .Where(store => !claimed.Contains(store)),
            ];
            List<StoreLocal> resumeLocalStores =
            [
                .. index.StateLocalStoresIn(resume)
                    .Where(store => !claimed.Contains(store)),
            ];
            if (resumeFieldStores is not [StoreField resumeField]
                || resumeLocalStores is not [StoreLocal resumeLocal]
                || WrittenState(resumeField.Value, index, isRawImport, spills) != -1
                || WrittenState(resumeLocal.Value, index, isRawImport, spills) != -1)
            {
                failure = $"the resume block for state {state} does not restore "
                    + "the running state -1";
                return null;
            }

            roles[dispatch] = StateDispatch;
            roles[resumeField] = StateFieldStore;
            roles[resumeLocal] = StateLocalStore;
            roles[restore] = AwaiterRestore;
            roles[clear] = AwaiterClear;
            claimed.Add(dispatch);
            claimed.Add(resumeField);
            claimed.Add(resumeLocal);
            claimed.Add(restore);
            claimed.Add(clear);
            dispatchers.Add($"{state}@{dispatch.SourceOffset}->{dispatch.TargetOffset}");
            resumes.Add(
                $"{state}@{restore.SourceOffset}/{clear.SourceOffset}"
                    + $"~L{transfer.Local}~{transfer.CacheField}");
            resumeOffsets.Add(resumeField.SourceOffset);
            resumeOffsets.Add(resumeLocal.SourceOffset);
        }

        ImmutableArray<AwaitCompletionIdentity>? awaitCompletions =
            ProveAwaitCompletionPaths(
                index,
                isRawImport,
                suspensionStates,
                suspensionAwaiters,
                resumeBlocksByState,
                roles,
                budget,
                out failure);
        if (awaitCompletions is null)
            return null;

        // Every awaiter transfer in the body belongs to a proven suspension or
        // resume; an unbound cache, restore, or clear is not protocol.
        foreach (IrNode transfer in index.AwaiterTransfers)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (claimed.Contains(transfer))
                continue;
            failure = "an awaiter cache, restore, or clear has no proven "
                + "suspension or resume role";
            return null;
        }

        // --- completion: the two -2 stores that precede the completion callbacks
        foreach (StoreField store in stateFieldStores)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (claimed.Contains(store))
                continue;
            if (WrittenState(store.Value, index, isRawImport, spills) != -2)
            {
                failure = "a machine state store carries a state constant with "
                    + "no proven dispatch, resume, or completion role";
                return null;
            }
            if (store.Parent is not Block block)
            {
                failure = "a completion state store is not a block statement";
                return null;
            }
            int position = index.PositionOf(store);
            IrNode? next = position >= 0 && position + 1 < block.Children.Count
                ? block.Children[position + 1]
                : null;
            if (!ReferenceEquals(next, setResultStatement)
                && !ReferenceEquals(next, setExceptionStatement))
            {
                failure = "a completion state store does not immediately precede "
                    + "a completion callback";
                return null;
            }

            roles[store] = StateFieldStore;
            claimed.Add(store);
            completionOffsets.Add(store.SourceOffset);
        }
        if (completionOffsets.Count != 2)
        {
            failure = "the completion protocol needs exactly two machine state "
                + $"stores of -2; the body has {completionOffsets.Count}";
            return null;
        }
        foreach (StoreLocal store in stateLocalStores)
        {
            if (claimed.Contains(store))
                continue;
            failure = "a dispatch-local store has no proven protocol role";
            return null;
        }

        // --- every read of the dispatch local belongs to a proven test
        int guardCount = 0;
        foreach (LoadLocal read in index.StateReads)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            if (HasClaimedAncestor(read, claimed, budget) is not bool claimedAncestor)
            {
                failure = BudgetFailure;
                return null;
            }
            if (claimedAncestor)
                continue;
            if (suspensionStates.Count > 0
                && index.HasFinallyContext
                && SuspensionGuardNode(read, stateLocal) is { } guard)
            {
                if (roles.TryAdd(guard, SuspensionGuard))
                {
                    claimed.Add(guard);
                    guardCount++;
                }
                continue;
            }

            failure = "a dispatch-local read is not part of a proven state test";
            return null;
        }

        foreach ((IrNode load, IrNode spill) in spills)
        {
            if (!claimed.Contains(load.Parent!))
            {
                failure = "a spilled state constant reaches a store with no protocol role";
                return null;
            }
            roles[spill] = StateSpill;
        }
        if (!VerifySpillUses(index, spills, budget, out failure))
            return null;

        return new BodyProtocol(
            IdentityOf(setResult),
            IdentityOf(setException),
            exceptionLocal,
            [.. awaits.Select(static pair => pair.Await)
                .Select(IdentityOf)
                .OrderBy(static identity => identity.Offset)],
            [.. suspensions.Order(StringComparer.Ordinal)],
            [.. dispatchers.Order(StringComparer.Ordinal)],
            [.. resumes.Order(StringComparer.Ordinal)],
            awaiterBinds,
            awaitCompletions.Value,
            [.. resumeOffsets.Order()],
            [.. completionOffsets.Order()],
            guardCount,
            index);

        CallbackIdentity IdentityOf(Call callback)
        {
            FieldRef builderField = ClassicInverseNodeFacts
                .BuilderField(callback.Arguments[0], index.Machine)!;
            return new(
                callback.SourceOffset,
                ClassicInverseTypedIdentity.Method(callback.Callee),
                ClassicInverseTypedIdentity.Field(builderField));
        }
    }

    /// <summary>
    /// Closes each await's fast and suspended paths around the exact
    /// <c>IsCompleted</c> test. The completed edge and the restored edge must
    /// join at the matching <c>GetResult</c>; the incomplete edge must enter
    /// the suspension block that owns the matching callback.
    /// </summary>
    static ImmutableArray<AwaitCompletionIdentity>? ProveAwaitCompletionPaths(
        BodyIndex index,
        bool isRawImport,
        IReadOnlyDictionary<int, Block> suspensionBlocks,
        IReadOnlyDictionary<int, AwaiterTransfer> suspensionAwaiters,
        IReadOnlyDictionary<int, Block> resumeBlocks,
        Dictionary<IrNode, string> roles,
        ClassicInverseBudget budget,
        out string? failure)
    {
        var identities = ImmutableArray.CreateBuilder<AwaitCompletionIdentity>();
        var claimedBranches = new HashSet<ConditionalBranch>(
            ReferenceEqualityComparer.Instance);

        foreach ((int state, Block suspension) in suspensionBlocks.OrderBy(
            static pair => pair.Key))
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return null;
            }
            AwaiterTransfer transfer = suspensionAwaiters[state];
            Block resume = resumeBlocks[state];
            if (index.PredecessorsOf(suspension) is not [Block completionBlock]
                || completionBlock.Children is not
                    [.., ConditionalBranch completion]
                || !TryGetAwaitCompletionTest(
                    completion.Condition,
                    isRawImport,
                    transfer.Local,
                    out MethodRef completionMethod,
                    out bool completionIsVirtual,
                    out int completionOffset)
                || index.BlocksStartingAt(completion.TargetOffset)
                    is not [Block continuation]
                || !index.HasOnlySuccessors(
                    completionBlock,
                    continuation,
                    suspension)
                || !index.HasOnlyPredecessors(
                    suspension,
                    completionBlock)
                || !index.IsSuspensionExit(suspension)
                || !index.HasOnlySuccessors(resume, continuation)
                || !index.HasOnlyPredecessors(
                    continuation,
                    completionBlock,
                    resume))
            {
                failure = $"state {state} does not carry one closed "
                    + "IsCompleted/suspension/GetResult continuation";
                return null;
            }

            List<Call> results =
            [
                .. index.GetResultsIn(continuation).Where(
                    call => IsAwaiterGetResult(
                        call,
                        transfer.Local,
                        completionMethod.DeclaringType)),
            ];
            if (results is not [Call getResult])
            {
                failure = $"state {state} does not join at exactly one "
                    + "GetResult on its proven awaiter";
                return null;
            }

            roles[completion] = AwaitCompletionBranch;
            claimedBranches.Add(completion);
            identities.Add(new(
                state,
                completion.SourceOffset,
                completionOffset,
                transfer.Local,
                ClassicInverseTypedIdentity.Method(completionMethod),
                completionIsVirtual,
                suspension.StartOffset,
                resume.StartOffset,
                continuation.StartOffset,
                getResult.SourceOffset,
                ClassicInverseTypedIdentity.Method(getResult.Callee),
                getResult.IsVirtual,
                ClassicInverseTypedIdentity.Type(getResult.ConstrainedTo)));
        }

        if (claimedBranches.Count != index.AwaitCompletionBranches.Count)
        {
            failure = "an await completion branch has no proven suspension "
                + "and GetResult continuation";
            return null;
        }

        failure = null;
        return [.. identities];
    }

    static bool TryGetAwaitCompletionTest(
        IrExpression condition,
        bool isRawImport,
        int expectedLocal,
        out MethodRef method,
        out bool isVirtual,
        out int sourceOffset)
    {
        method = null!;
        isVirtual = false;
        sourceOffset = -1;

        LoadLocalAddress? awaiter;
        if (isRawImport
            && condition is Call
            {
                Callee:
                {
                    Name: "get_IsCompleted",
                    HasThis: true,
                    ParameterTypes.IsDefaultOrEmpty: true,
                } callee,
                Arguments: [LoadLocalAddress rawAddress],
                ConstrainedTo: null,
            } call)
        {
            method = callee;
            awaiter = rawAddress;
            isVirtual = call.IsVirtual;
            sourceOffset = call.SourceOffset;
        }
        else if (!isRawImport
            && condition is LoadProperty
            {
                Accessor:
                {
                    Name: "get_IsCompleted",
                    HasThis: true,
                    ParameterTypes.IsDefaultOrEmpty: true,
                } accessor,
                Instance: LoadLocalAddress planningAddress,
                IndexArguments.Count: 0,
            } property)
        {
            method = accessor;
            awaiter = planningAddress;
            isVirtual = property.IsVirtual;
            sourceOffset = property.SourceOffset;
        }
        else
        {
            return false;
        }

        return awaiter.Index == expectedLocal
            && awaiter.Type.Equals(method.DeclaringType)
            && MemberIdentity.IsCoreLibraryType(
                method.ReturnType,
                "System",
                "Boolean");
    }

    static bool IsAwaiterGetResult(
        Call call,
        int expectedLocal,
        TypeRef awaiterType)
        => call.Callee is
            {
                Name: "GetResult",
                HasThis: true,
                ParameterTypes.IsDefaultOrEmpty: true,
            }
            && call.Arguments is [LoadLocalAddress awaiter]
            && awaiter.Index == expectedLocal
            && awaiter.Type.Equals(awaiterType)
            && call.Callee.DeclaringType.Equals(awaiterType);

    /// <summary>
    /// Every use of a state-constant spill slot must reach a proven state store.
    /// A slot the compiler shares with anything else is not the state protocol.
    /// </summary>
    static bool VerifySpillUses(
        BodyIndex index,
        Dictionary<IrNode, IrNode> spills,
        ClassicInverseBudget budget,
        out string? failure)
    {
        var slots = new HashSet<int>();
        var spillStores = new HashSet<IrNode>(
            spills.Values,
            ReferenceEqualityComparer.Instance);
        foreach (IrNode spill in spills.Values)
            slots.Add(((StoreStackSlot)spill).Slot);

        foreach (LoadStackSlot load in index.SlotLoads)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return false;
            }
            if (slots.Contains(load.Slot) && !spills.ContainsKey(load))
            {
                failure = "a state-constant spill slot is read outside the state protocol";
                return false;
            }
        }
        foreach (StoreStackSlot store in index.AllSlotStores)
        {
            if (!budget.Charge())
            {
                failure = BudgetFailure;
                return false;
            }
            if (slots.Contains(store.Slot) && !spillStores.Contains(store))
            {
                failure = "a state-constant spill slot is written outside the state protocol";
                return false;
            }
        }

        failure = null;
        return true;
    }

    /// <summary>
    /// Whether a proven role already owns this node or one of its ancestors.
    /// The walk is depth-bounded by the body, not constant, so it charges for
    /// every step: an adversarially deep body must buy its own ancestor walks.
    /// Returns <c>null</c> when the budget is exhausted.
    /// </summary>
    static bool? HasClaimedAncestor(
        IrNode node,
        HashSet<IrNode> claimed,
        ClassicInverseBudget budget)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (!budget.Charge())
                return null;
            if (claimed.Contains(current))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The compiler's "not suspended" guard around a user <c>finally</c>:
    /// <c>state &lt; 0</c> in the planning view, or its inverted
    /// <c>state &gt;= 0</c> branch in the import.
    /// </summary>
    static IrNode? SuspensionGuardNode(LoadLocal read, int stateLocal)
    {
        if (read.Parent is not Comparison
            {
                Kind: ComparisonKind.LessThan or ComparisonKind.GreaterThanOrEqual,
                IsUnsigned: false,
                Right: Constant { Value: 0 },
            } comparison
            || !ReferenceEquals(comparison.Left, read)
            || read.Index != stateLocal)
        {
            return null;
        }
        return comparison.Parent is ConditionalBranch branch
            ? branch
            : comparison;
    }

    static int? TestedState(IrExpression condition, int stateLocal)
    {
        if (stateLocal < 0)
            return null;
        return condition switch
        {
            LogicalNot { Operand: LoadLocal load } when load.Index == stateLocal => 0,
            Comparison
            {
                Kind: ComparisonKind.Equal,
                Left: LoadLocal load,
                Right: Constant { Value: int value },
            } when load.Index == stateLocal => value,
            Comparison
            {
                Kind: ComparisonKind.Equal,
                Right: LoadLocal load,
                Left: Constant { Value: int value },
            } when load.Index == stateLocal => value,
            _ => null,
        };
    }

    /// <summary>
    /// The constant one state store writes. The import spills the constant
    /// through a stack slot (the compiler's <c>dup</c>), so the slot's single
    /// defining store supplies the value and is itself recorded as protocol.
    /// </summary>
    static int? WrittenState(
        IrExpression value,
        BodyIndex index,
        bool isRawImport,
        Dictionary<IrNode, IrNode> spills)
    {
        switch (value)
        {
            case Constant { Value: int constant }:
                return constant;

            case LoadStackSlot load when isRawImport:
                if (index.SlotStoresFor(load.Slot) is not [StoreStackSlot store]
                    || store.Value is not Constant { Value: int spilled })
                {
                    return null;
                }
                spills[load] = store;
                return spilled;

            default:
                return null;
        }
    }

    static CatchClause? EnclosingCatch(IrNode node)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (current is CatchClause clause)
                return clause;
        }
        return null;
    }

    /// <summary>
    /// Every repeated whole-body query the proof needs, computed in one charged
    /// pass. Without it the state loop rescans all nodes per state, so an
    /// adversarial body buys quadratic planning work at a linear charge.
    /// </summary>
    sealed class BodyIndex
    {
        static readonly List<StoreField> s_noFieldStores = [];
        static readonly List<StoreLocal> s_noLocalStores = [];
        static readonly List<InitObject> s_noClears = [];
        static readonly List<Block> s_noBlocks = [];
        static readonly List<ConditionalBranch> s_noBranches = [];
        static readonly List<StoreStackSlot> s_noSlotStores = [];
        static readonly List<Call> s_noCalls = [];

        readonly Dictionary<IrNode, List<StoreField>> _stateFieldStoresByBlock =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<IrNode, List<StoreLocal>> _stateLocalStoresByBlock =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<IrNode, List<StoreField>> _awaiterCachesByBlock =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<IrNode, List<StoreLocal>> _awaiterRestoresByBlock =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<IrNode, List<InitObject>> _awaiterClearsByBlock =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<int, List<Block>> _blocksByStart = [];
        readonly Dictionary<int, List<ConditionalBranch>> _stateTests = [];
        readonly Dictionary<int, List<StoreStackSlot>> _slotStores = [];
        readonly Dictionary<IrNode, List<Call>> _getResultsByBlock =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<IrNode, int> _positions =
            new(ReferenceEqualityComparer.Instance);
        readonly List<BlockContainer> _containers = [];
        readonly Dictionary<Block, List<Block>> _successors =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<Block, List<Block>> _predecessors =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<Block, ILInspector.ControlFlow.BlockEdges>
            _controlFlow =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<int, List<Block>> _externalPredecessors = [];
        readonly Dictionary<IrNode, ImmutableArray<ExceptionMembership>>
            _structuredContextByNode =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<int, ImmutableArray<ExceptionMembership>>
            _structuredExceptionContexts = [];
        readonly Dictionary<TryCatch, ImmutableArray<int>> _tryCatchRegions =
            new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<TryFinally, int> _tryFinallyRegions =
            new(ReferenceEqualityComparer.Instance);
        readonly List<ExceptionRegionIdentity> _structuredExceptionRegions = [];
        readonly HashSet<int> _sourceOffsets = [];
        ImmutableArray<ExceptionRegionLayout> _importedExceptionRegions = [];

        BodyIndex(TypeRef machine, bool isRawImport)
        {
            Machine = machine;
            IsRawImport = isRawImport;
        }

        internal TypeRef Machine { get; }

        bool IsRawImport { get; }

        internal List<Call> BuilderCalls { get; } = [];

        internal List<StoreField> StateFieldStores { get; } = [];

        internal List<StoreLocal> StateLocalStores { get; } = [];

        internal List<StoreLocal> CaughtStores { get; } = [];

        internal List<StoreLocal> AwaiterBinds { get; } = [];

        internal List<LoadLocal> StateReads { get; } = [];

        internal List<LoadStackSlot> SlotLoads { get; } = [];

        internal List<StoreStackSlot> AllSlotStores { get; } = [];

        internal List<ConditionalBranch> AwaitCompletionBranches { get; } = [];

        /// <summary>Every awaiter cache, restore, and clear in the body.</summary>
        internal List<IrNode> AwaiterTransfers { get; } = [];

        /// <summary>A state store that writes storage outside this machine.</summary>
        internal bool HasForeignStateStore { get; private set; }

        internal bool HasFinallyContext { get; private set; }

        internal string? ExceptionFailure { get; private set; }

        internal IReadOnlyList<ExceptionRegionLayout>
            ImportedExceptionRegions => _importedExceptionRegions;

        internal IReadOnlyList<ExceptionRegionIdentity>
            StructuredExceptionRegions => _structuredExceptionRegions;

        internal IReadOnlyDictionary<int, ImmutableArray<ExceptionMembership>>
            StructuredExceptionContexts => _structuredExceptionContexts;

        internal IReadOnlySet<int> SourceOffsets => _sourceOffsets;

        internal List<StoreField> StateFieldStoresIn(Block block)
            => _stateFieldStoresByBlock.GetValueOrDefault(block, s_noFieldStores);

        internal List<StoreLocal> StateLocalStoresIn(Block block)
            => _stateLocalStoresByBlock.GetValueOrDefault(block, s_noLocalStores);

        internal List<StoreField> AwaiterCachesIn(Block block)
            => _awaiterCachesByBlock.GetValueOrDefault(block, s_noFieldStores);

        internal List<StoreLocal> AwaiterRestoresIn(Block block)
            => _awaiterRestoresByBlock.GetValueOrDefault(block, s_noLocalStores);

        internal List<InitObject> AwaiterClearsIn(Block block)
            => _awaiterClearsByBlock.GetValueOrDefault(block, s_noClears);

        internal List<Block> BlocksStartingAt(int offset)
            => _blocksByStart.GetValueOrDefault(offset, s_noBlocks);

        internal List<ConditionalBranch> StateTestsFor(int state)
            => _stateTests.GetValueOrDefault(state, s_noBranches);

        internal List<StoreStackSlot> SlotStoresFor(int slot)
            => _slotStores.GetValueOrDefault(slot, s_noSlotStores);

        internal List<Call> GetResultsIn(Block block)
            => _getResultsByBlock.GetValueOrDefault(block, s_noCalls);

        internal IReadOnlyList<Block> PredecessorsOf(Block block)
            => _predecessors.GetValueOrDefault(block, s_noBlocks);

        internal bool HasOnlySuccessors(Block block, params Block[] expected)
            => _controlFlow.TryGetValue(block, out var edges)
                && !edges.ExitsMethod
                && !edges.LeavesRegion
                && edges.ExternalTargets.Count == 0
                && HasOnlyBlocks(
                    _successors.GetValueOrDefault(block, s_noBlocks),
                    expected);

        internal bool HasOnlyPredecessors(Block block, params Block[] expected)
        {
            IReadOnlyList<Block> local =
                _predecessors.GetValueOrDefault(block, s_noBlocks);
            IReadOnlyList<Block> external =
                _externalPredecessors.GetValueOrDefault(
                    block.StartOffset,
                    s_noBlocks);
            return local.Count + external.Count == expected.Length
                && expected.All(candidate =>
                    local.Any(block => ReferenceEquals(block, candidate))
                    || external.Any(
                        block => ReferenceEquals(block, candidate)));
        }

        internal bool IsSuspensionExit(Block block)
        {
            if (IsRawImport)
            {
                return block.Children.LastOrDefault() is Leave leave
                    && BlocksStartingAt(leave.TargetOffset) is [Block target]
                    && target.Children is [Return { Value: null }];
            }
            return block.Children.LastOrDefault() is Return { Value: null };
        }

        /// <summary>This node's index among its parent's children, or -1.</summary>
        internal int PositionOf(IrNode node)
            => _positions.GetValueOrDefault(node, -1);

        internal ImmutableArray<ExceptionMembership>?
            ImportedExceptionContextAt(
                int offset,
                ClassicInverseBudget budget)
        {
            var context =
                ImmutableArray.CreateBuilder<ExceptionMembership>();
            for (int i = 0; i < _importedExceptionRegions.Length; i++)
            {
                if (!budget.Charge())
                    return null;

                ExceptionRegionLayout region =
                    _importedExceptionRegions[i];
                if (Contains(
                        offset,
                        region.TryOffset,
                        region.TryLength))
                {
                    context.Add(new(i, IsHandler: false));
                }
                else if (Contains(
                    offset,
                    region.HandlerOffset,
                    region.HandlerLength))
                {
                    context.Add(new(i, IsHandler: true));
                }
            }
            return context.ToImmutable();
        }

        internal static BodyIndex? Build(
            IrFunction body,
            TypeRef machine,
            int stateLocal,
            ImmutableHashSet<int> awaiterLocals,
            bool isRawImport,
            ClassicInverseBudget budget)
        {
            var index = new BodyIndex(machine, isRawImport);
            if (!index.BuildImportedExceptionRegions(body.Regions, budget))
                return null;
            if (index.ExceptionFailure is not null)
                return index;
            // The import carries a user finally as a region; the planning view
            // carries it as structure. Each space reads its own evidence.
            index.HasFinallyContext = isRawImport
                && index._importedExceptionRegions.Any(
                    static region =>
                        region.Identity.Kind == HandlerKind.Finally);

            foreach (IrNode node in body.Body.Descendants.Prepend(body.Body))
            {
                if (!budget.Charge())
                    return null;
                if (!index.IndexExceptionContext(node, budget))
                    return null;
                if (index.ExceptionFailure is not null)
                    return index;
                for (int i = 0; i < node.Children.Count; i++)
                {
                    if (!budget.Charge())
                        return null;
                    index._positions[node.Children[i]] = i;
                }
                index.Add(node, stateLocal, awaiterLocals);
            }

            if (!index.BuildControlFlow(budget))
                return null;
            return index;
        }

        bool BuildImportedExceptionRegions(
            ImmutableArray<HandlerRegion> regions,
            ClassicInverseBudget budget)
        {
            if (regions.Length > 2)
            {
                ExceptionFailure =
                    "the closed recipes admit one completion catch and at most one finally";
                return true;
            }
            var layouts = new List<ExceptionRegionLayout>(regions.Length);
            foreach (HandlerRegion region in regions)
            {
                if (!budget.Charge())
                    return false;
                layouts.Add(new(
                    IdentityOf(region),
                    region.TryOffset,
                    region.TryLength,
                    region.HandlerOffset,
                    region.HandlerLength,
                    region.FilterOffset));
            }
            layouts.Sort(static (left, right) =>
            {
                int comparison = left.TryOffset.CompareTo(right.TryOffset);
                if (comparison != 0)
                    return comparison;
                comparison = right.TryLength.CompareTo(left.TryLength);
                if (comparison != 0)
                    return comparison;
                comparison = left.HandlerOffset.CompareTo(
                    right.HandlerOffset);
                if (comparison != 0)
                    return comparison;
                return left.HandlerLength.CompareTo(right.HandlerLength);
            });
            _importedExceptionRegions = [.. layouts];
            return true;
        }

        bool IndexExceptionContext(
            IrNode node,
            ClassicInverseBudget budget)
        {
            ImmutableArray<ExceptionMembership> context =
                node.Parent is { } parent
                && _structuredContextByNode.TryGetValue(
                    parent,
                    out ImmutableArray<ExceptionMembership> parentContext)
                    ? parentContext
                    : [];

            switch (node.Parent)
            {
                case TryCatch tryCatch
                    when ReferenceEquals(node, tryCatch.TryBody):
                    context = Append(
                        context,
                        _tryCatchRegions[tryCatch],
                        isHandler: false);
                    break;

                case TryCatch tryCatch when node is CatchClause:
                {
                    int clause = node.ChildIndex - 1;
                    ImmutableArray<int> regions =
                        _tryCatchRegions[tryCatch];
                    if (clause < 0 || clause >= regions.Length)
                    {
                        ExceptionFailure =
                            "a structured catch clause has no exception-region identity";
                        return true;
                    }
                    context = context.Add(new(
                        regions[clause],
                        IsHandler: true));
                    break;
                }

                case TryFinally tryFinally
                    when ReferenceEquals(node, tryFinally.TryBody):
                    context = context.Add(new(
                        _tryFinallyRegions[tryFinally],
                        IsHandler: false));
                    break;

                case TryFinally tryFinally
                    when ReferenceEquals(node, tryFinally.FinallyBody):
                    context = context.Add(new(
                        _tryFinallyRegions[tryFinally],
                        IsHandler: true));
                    break;
            }

            _structuredContextByNode[node] = context;
            if (node.SourceOffset >= 0)
            {
                _sourceOffsets.Add(node.SourceOffset);
                if (_structuredExceptionContexts.TryGetValue(
                        node.SourceOffset,
                        out ImmutableArray<ExceptionMembership> existing)
                    && !existing.SequenceEqual(context))
                {
                    ExceptionFailure =
                        $"planning nodes at IL_{node.SourceOffset:x4} "
                        + "occupy different structured exception contexts";
                    return true;
                }
                _structuredExceptionContexts[node.SourceOffset] = context;
            }

            switch (node)
            {
                case TryCatch tryCatch:
                {
                    if (tryCatch.Children.Count != 2
                        || _structuredExceptionRegions.Count >= 2)
                    {
                        ExceptionFailure =
                            "the closed recipes admit one completion catch and at most one finally";
                        return true;
                    }
                    var regions = ImmutableArray.CreateBuilder<int>(
                        tryCatch.Children.Count - 1);
                    for (int i = 1; i < tryCatch.Children.Count; i++)
                    {
                        if (!budget.Charge())
                            return false;
                        var clause =
                            (CatchClause)tryCatch.Children[i];
                        regions.Add(_structuredExceptionRegions.Count);
                        _structuredExceptionRegions.Add(new(
                            clause.Filter is null
                                ? HandlerKind.Catch
                                : HandlerKind.Filter,
                            ClassicInverseTypedIdentity.Type(
                                clause.ExceptionType)));
                    }
                    _tryCatchRegions[tryCatch] = regions.ToImmutable();
                    break;
                }

                case TryFinally tryFinally:
                    if (_structuredExceptionRegions.Count >= 2)
                    {
                        ExceptionFailure =
                            "the closed recipes admit one completion catch and at most one finally";
                        return true;
                    }
                    if (!budget.Charge())
                        return false;
                    _tryFinallyRegions[tryFinally] =
                        _structuredExceptionRegions.Count;
                    _structuredExceptionRegions.Add(new(
                        HandlerKind.Finally,
                        ClassicInverseTypedIdentity.Type(null)));
                    break;
            }

            return true;
        }

        static ExceptionRegionIdentity IdentityOf(HandlerRegion region)
            => new(
                region.Kind,
                ClassicInverseTypedIdentity.Type(region.CatchType));

        static ImmutableArray<ExceptionMembership> Append(
            ImmutableArray<ExceptionMembership> context,
            ImmutableArray<int> regions,
            bool isHandler)
        {
            var appended = ImmutableArray.CreateBuilder<ExceptionMembership>(
                context.Length + regions.Length);
            appended.AddRange(context);
            foreach (int region in regions)
                appended.Add(new(region, isHandler));
            return appended.ToImmutable();
        }

        static bool Contains(int offset, int start, int length)
            => length >= 0
                && offset >= start
                && (long)offset < (long)start + length;

        void Add(IrNode node, int stateLocal, ImmutableHashSet<int> awaiterLocals)
        {
            switch (node)
            {
                case BlockContainer container:
                    _containers.Add(container);
                    return;

                case TryFinally when !IsRawImport:
                    HasFinallyContext = true;
                    return;

                case Block block:
                    Group(_blocksByStart, block.StartOffset, block);
                    return;

                case Call call
                    when ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                            call.Callee.DeclaringType)
                        && call.Arguments.Count > 0
                        && ClassicInverseNodeFacts.IsBuilderAccess(
                            call.Arguments[0],
                            Machine):
                    BuilderCalls.Add(call);
                    return;

                case StoreField { Field.Name: "<>1__state" } state:
                    StateFieldStores.Add(state);
                    if (state.Instance is not LoadArgument { Index: 0 }
                        || !ClassicInverseNodeFacts.IsMachineField(
                            state.Field,
                            Machine))
                    {
                        HasForeignStateStore = true;
                        return;
                    }
                    if (state.Parent is { } stateBlock)
                        Group(_stateFieldStoresByBlock, stateBlock, state);
                    return;

                case StoreField { Value: LoadLocal cached } cache
                    when IsAwaiterCacheField(cache.Field)
                        && cache.Instance is LoadArgument { Index: 0 }
                        && awaiterLocals.Contains(cached.Index):
                    AwaiterTransfers.Add(cache);
                    if (cache.Parent is { } cacheBlock)
                        Group(_awaiterCachesByBlock, cacheBlock, cache);
                    return;

                case StoreField unboundCache
                    when IsAwaiterCacheField(unboundCache.Field)
                        && unboundCache.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            unboundCache.Field,
                            Machine):
                    // A write to the awaiter cache that is not a proven awaiter
                    // local stays visible so no protocol role can claim it.
                    AwaiterTransfers.Add(unboundCache);
                    return;

                case StoreLocal store when stateLocal >= 0 && store.Index == stateLocal:
                    StateLocalStores.Add(store);
                    if (store.Parent is { } storeBlock)
                        Group(_stateLocalStoresByBlock, storeBlock, store);
                    return;

                case StoreLocal { Value: LoadField restored } restore
                    when IsAwaiterCacheField(restored.Field)
                        && restored.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            restored.Field,
                            Machine):
                    AwaiterTransfers.Add(restore);
                    if (awaiterLocals.Contains(restore.Index)
                        && restore.Parent is { } restoreBlock)
                    {
                        Group(_awaiterRestoresByBlock, restoreBlock, restore);
                    }
                    return;

                case StoreLocal { Value: CaughtException } caught:
                    CaughtStores.Add(caught);
                    return;

                case StoreLocal
                {
                    Value: Call { Callee.Name: "GetAwaiter" },
                } bind:
                    AwaiterBinds.Add(bind);
                    return;

                case ConditionalBranch branch
                    when IsAwaitCompletionCondition(branch.Condition):
                    AwaitCompletionBranches.Add(branch);
                    return;

                case Call
                {
                    Callee.Name: "GetResult",
                    Arguments: [LoadLocalAddress awaiter],
                } getResult when awaiterLocals.Contains(awaiter.Index):
                    if (ContainingBlock(getResult) is { } resultBlock)
                        Group(_getResultsByBlock, resultBlock, getResult);
                    return;

                case InitObject { Address: LoadFieldAddress cleared } clear
                    when IsAwaiterCacheField(cleared.Field)
                        && cleared.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            cleared.Field,
                            Machine):
                    AwaiterTransfers.Add(clear);
                    if (clear.Parent is { } clearBlock)
                        Group(_awaiterClearsByBlock, clearBlock, clear);
                    return;

                case ConditionalBranch branch
                    when TestedState(branch.Condition, stateLocal) is int tested:
                    Group(_stateTests, tested, branch);
                    return;

                case LoadLocal read when stateLocal >= 0 && read.Index == stateLocal:
                    StateReads.Add(read);
                    return;

                case LoadStackSlot load:
                    SlotLoads.Add(load);
                    return;

                case StoreStackSlot slotStore:
                    AllSlotStores.Add(slotStore);
                    Group(_slotStores, slotStore.Slot, slotStore);
                    return;
            }
        }

        bool BuildControlFlow(ClassicInverseBudget budget)
        {
            foreach (BlockContainer container in _containers)
            {
                IReadOnlyList<Block> blocks = container.Blocks;
                if (!ClassicInverseCfg.TryBuild(blocks, budget, out var edges))
                    return false;
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (!budget.Charge())
                        return false;
                    Block block = blocks[i];
                    _controlFlow[block] = edges[i];
                    var successors = new List<Block>(edges[i].Successors.Count);
                    _successors[block] = successors;
                    _predecessors.TryAdd(block, []);
                    foreach (int successorIndex in edges[i].Successors)
                    {
                        if (!budget.Charge())
                            return false;
                        Block successor = blocks[successorIndex];
                        successors.Add(successor);
                        if (!_predecessors.TryGetValue(
                                successor,
                                out List<Block>? predecessors))
                        {
                            _predecessors[successor] = predecessors = [];
                        }
                        predecessors.Add(block);
                    }
                    foreach (int target in edges[i].ExternalTargets)
                    {
                        if (!budget.Charge())
                            return false;
                        Group(_externalPredecessors, target, block);
                    }
                    if (block.Children.LastOrDefault() is Leave leave)
                    {
                        if (!budget.Charge())
                            return false;
                        Group(
                            _externalPredecessors,
                            leave.TargetOffset,
                            block);
                    }
                }
            }

            return true;
        }

        bool IsAwaitCompletionCondition(IrExpression condition)
            => IsRawImport
                ? condition is Call
                {
                    Callee.Name: "get_IsCompleted",
                    Arguments: [LoadLocalAddress],
                }
                : condition is LoadProperty
                {
                    PropertyName: "IsCompleted",
                    Instance: LoadLocalAddress,
                    IndexArguments.Count: 0,
                };

        static Block? ContainingBlock(IrNode node)
        {
            for (IrNode? current = node.Parent;
                current is not null;
                current = current.Parent)
            {
                if (current is Block block)
                    return block;
            }
            return null;
        }

        static bool HasOnlyBlocks(
            IReadOnlyList<Block> actual,
            IReadOnlyList<Block> expected)
            => actual.Count == expected.Count
                && expected.All(candidate => actual.Any(
                    block => ReferenceEquals(block, candidate)));

        static bool IsAwaiterCacheField(FieldRef field)
            => field.Name.StartsWith("<>u__", StringComparison.Ordinal);

        static void Group<TKey, TValue>(
            Dictionary<TKey, List<TValue>> map,
            TKey key,
            TValue value)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out List<TValue>? bucket))
                map[key] = bucket = [];
            bucket.Add(value);
        }
    }
}

/// <summary>
/// Budget admission for the shared CFG builder. The preflight pays for its own
/// scan and for the bounded block/target work that <see cref="Cfg.Build"/> will
/// perform before that work begins.
/// </summary>
internal static class ClassicInverseCfg
{
    internal static bool TryBuild(
        IReadOnlyList<Block> blocks,
        ClassicInverseBudget budget,
        out IReadOnlyList<ILInspector.ControlFlow.BlockEdges> edges)
    {
        edges = [];
        foreach (Block block in blocks)
        {
            // Preflight visit, offset-map insertion, block dispatch, and at
            // most one non-switch successor.
            for (int charge = 0; charge < 4; charge++)
            {
                if (!budget.Charge())
                    return false;
            }

            if (block.Children.LastOrDefault() is not SwitchBranch branch)
                continue;
            foreach (int _ in branch.TargetOffsets)
            {
                // One preflight visit and one target lookup/add.
                if (!budget.Charge() || !budget.Charge())
                    return false;
            }
        }

        edges = Cfg.Build(blocks);
        return true;
    }
}
