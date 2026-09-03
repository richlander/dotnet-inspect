using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Turns one recipe candidate into either a proof-carrying
/// <see cref="ClassicInversePlan"/> or a decline.
/// <para>
/// The accountant is the only thing that licenses <c>Reconstruct</c>. It
/// discharges three independent obligations over the request's own bodies:
/// a complete, disjoint physical partition; a one-to-one semantic realization
/// relation checked in both directions; and a complete modeled path from every
/// consumed node to its recipe root, including the flat-IR control contexts the
/// classic shell encodes as branches rather than tree ancestors.
/// </para>
/// </summary>
internal sealed class ClassicInverseAccountant
{
    readonly ClassicInverseRequest _request;
    readonly ClassicInversePlanningView _planning;
    readonly ClassicInverseCandidate _candidate;
    readonly ClassicInverseShellFacts _shell;
    readonly ClassicInverseBudget _budget;

    readonly Dictionary<IrNode, ImmutableArray<int>> _executionPaths =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, ImmutableArray<int>> _kickoffPaths =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, ImmutableArray<int>> _outputPaths =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, ImmutableArray<int>> _rawExecutionPaths =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, ImmutableArray<int>> _rawKickoffPaths =
        new(ReferenceEqualityComparer.Instance);

    readonly Dictionary<IrNode, ClassicInverseClaim> _claimBySource =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, ClassicInverseClaim> _claimByOutput =
        new(ReferenceEqualityComparer.Instance);

    readonly List<ClassicInversePhysicalRegion> _rawRegions = [];
    readonly List<(IrNode Node, ClassicInversePhysicalRegion Region)>
        _rawReceiptNodes = [];
    readonly List<ClassicInverseSemanticRealization> _realizations = [];
    readonly List<ClassicInverseAncestorReceipt> _ancestors = [];
    readonly HashSet<IrNode> _covered = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _rawCovered =
        new(ReferenceEqualityComparer.Instance);
    ImmutableArray<string> _planningEffectOrder = [];

    BlockContainer _output = null!;
    ClassicInverseDecision? _terminal;

    ClassicInverseAccountant(
        ClassicInverseRequest request,
        ClassicInversePlanningView planning,
        ClassicInverseCandidate candidate,
        ClassicInverseShellFacts shell,
        ClassicInverseBudget budget)
    {
        _request = request;
        _planning = planning;
        _candidate = candidate;
        _shell = shell;
        _budget = budget;
    }

    /// <summary>
    /// Accounts for <paramref name="candidate"/>. Returns a
    /// <c>Reconstruct</c>, a <c>Decline</c>, or a <c>Failed</c>; never null.
    /// </summary>
    internal static ClassicInverseDecision Account(
        ClassicInverseRequest request,
        ClassicInversePlanningView planning,
        ClassicInverseCandidate candidate,
        ClassicInverseShellFacts shell,
        ClassicInverseBudget budget)
        => new ClassicInverseAccountant(
            request,
            planning,
            candidate,
            shell,
            budget).Run();

    ClassicInverseDecision Run()
    {
        if (!_candidate.Sound)
        {
            return Decline(
                ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                $"recipe '{_candidate.Recipe}' produced an unsound claim set");
        }

        _output = BuildOutputContainer();
        IndexPaths(_output, _outputPaths);
        IndexPaths(_planning.ExecutionBody.Body, _executionPaths);
        IndexPaths(_planning.KickoffBody.Body, _kickoffPaths);
        if (_terminal is not null)
            return _terminal;

        foreach (ClassicInverseClaim claim in _candidate.Claims)
        {
            if (!_executionPaths.ContainsKey(claim.Source))
            {
                return Decline(
                    ClassicInverseDeclineReason.MissingImportCorrespondence,
                    $"claimed source is outside the execution body ({claim.Rule})");
            }
            if (!_outputPaths.ContainsKey(claim.Output))
            {
                return Decline(
                    ClassicInverseDeclineReason.InventedOutputEffect,
                    $"claimed output is outside the proposed body ({claim.Rule})");
            }
            _claimBySource[claim.Source] = claim;
            _claimByOutput[claim.Output] = claim;
        }

        if (!AccountKickoff())
            return _terminal!;
        if (!VerifyAncestorPaths())
            return _terminal!;
        if (!VerifyControlContexts())
            return _terminal!;
        if (!AccountExecution())
            return _terminal!;
        if (!VerifyPartitionIsComplete())
            return _terminal!;
        if (!VerifyRealizations())
            return _terminal!;
        if (!VerifyOutputIsFullyCited())
            return _terminal!;
        if (!AccountRawImportSnapshots())
            return _terminal!;
        if (!BindImportPaths())
            return _terminal!;

        ClassicInverseBodyNode? blueprint =
            ClassicInverseBodyCapture.TryCapture(_output, _budget);
        if (_budget.Exhausted)
            return Failure("output capture exhausted the planning budget");
        if (blueprint is null)
        {
            return Decline(
                ClassicInverseDeclineReason.UnsupportedOutputNode,
                $"recipe '{_candidate.Recipe}' proposed a node outside the closed body blueprint");
        }

        var plan = new ClassicInversePlan(
            _candidate.Recipe,
            blueprint,
            _candidate.Locals,
            _candidate.LocalNames,
            _planning.ExecutionBody.CaptureTypeFacts(),
            _request.KickoffSourceOffset,
            [.. _rawRegions],
            [.. _realizations],
            [.. _ancestors]);
        return new ClassicInverseDecision.Reconstruct(plan);
    }

    BlockContainer BuildOutputContainer()
    {
        var block = new Block(0);
        foreach (IrNode statement in _candidate.Statements)
            block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        return container;
    }

    // ---- Ledger 1: the physical partition -----------------------------

    /// <summary>
    /// The kickoff shell is accounted statement by statement, in order. Every
    /// statement must match the next expected role; an unexpected statement is
    /// a decline rather than something absorbed after the expected ones were
    /// found.
    /// </summary>
    bool AccountKickoff()
    {
        IrFunction kickoff = _planning.KickoffBody;
        if (kickoff.Body.Blocks is not [Block block])
        {
            return DeclineFalse(
                ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                "kickoff body is not a single block");
        }

        Cover(kickoff.Body, ClassicInverseBodyId.Kickoff);
        Cover(block, ClassicInverseBodyId.Kickoff);
        RecordRegion(
            ClassicInverseBodyId.Kickoff,
            kickoff.Body,
            ClassicInverseRegionDisposition.Protocol,
            ownsSubtree: false,
            "kickoff-container");
        RecordRegion(
            ClassicInverseBodyId.Kickoff,
            block,
            ClassicInverseRegionDisposition.Protocol,
            ownsSubtree: false,
            "kickoff-block");

        // Roles, in the exact order csc emits them.
        const int ExpectBuilderCreate = 0;
        const int ExpectStateOrTransfer = 1;
        const int ExpectStart = 2;
        const int ExpectReturn = 3;
        const int ExpectEnd = 4;

        int stage = ExpectBuilderCreate;
        bool sawState = false;

        foreach (IrNode statement in block.Children)
        {
            switch (stage)
            {
                case ExpectBuilderCreate
                    when IsKickoffBuilderCreate(statement):
                    ClaimKickoffProtocol(statement, "kickoff-builder-create");
                    stage = ExpectStateOrTransfer;
                    continue;

                case ExpectStateOrTransfer
                    when !sawState && IsKickoffParameterTransfer(statement):
                    ClaimKickoffProtocol(statement, "kickoff-parameter-transfer");
                    continue;

                case ExpectStateOrTransfer
                    when !sawState && IsKickoffInitialState(statement):
                    ClaimKickoffProtocol(statement, "kickoff-initial-state");
                    sawState = true;
                    stage = ExpectStart;
                    continue;

                case ExpectStart when IsKickoffStart(statement):
                    ClaimKickoffProtocol(statement, "kickoff-start");
                    stage = ExpectReturn;
                    continue;

                case ExpectReturn when IsKickoffReturnTask(statement):
                    ClaimKickoffProtocol(statement, "kickoff-return-task");
                    stage = ExpectEnd;
                    continue;

                default:
                    return DeclineFalse(
                        ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                        $"kickoff statement '{statement.Describe()}' has no role at stage {stage}");
            }
        }

        return stage == ExpectEnd
            || DeclineFalse(
                ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                $"kickoff shell is incomplete at stage {stage}");
    }

    void ClaimKickoffProtocol(IrNode statement, string rule)
    {
        RecordRegion(
            ClassicInverseBodyId.Kickoff,
            statement,
            ClassicInverseRegionDisposition.Protocol,
            ownsSubtree: true,
            rule);
        CoverSubtree(statement, ClassicInverseBodyId.Kickoff);
    }

    bool IsKickoffBuilderCreate(IrNode statement)
        => statement is StoreField
        {
            Field: { Name: "<>t__builder" } field,
            Instance: LoadLocalAddress local,
            Value: Call { Callee.Name: "Create" } create,
        }
            && local.Index == _request.StateMachineLocal
            && ClassicInverseNodeFacts.IsMachineField(field, _shell.Machine)
            && ClassicInverseNodeFacts.IsAsyncMethodBuilder(create.Callee.DeclaringType);

    bool IsKickoffParameterTransfer(IrNode statement)
    {
        if (statement is not StoreField
            {
                Instance: LoadLocalAddress local,
                Value: LoadArgument argument,
            } store
            || local.Index != _request.StateMachineLocal
            || !ClassicInverseNodeFacts.IsMachineField(store.Field, _shell.Machine))
        {
            return false;
        }

        if (store.Field.Name == "<>4__this")
        {
            _candidate.MapParameterField(store.Field.Name, argument.Index);
            return argument.Index == 0;
        }

        if (!TryGetParameterIndex(store.Field.Name, out int index)
            || index != argument.Index)
        {
            return false;
        }

        _candidate.MapParameterField(store.Field.Name, index);
        return true;
    }

    bool AccountRawImportSnapshots()
    {
        IndexPaths(_request.KickoffBody.Body, _rawKickoffPaths);
        IndexPaths(_request.ExecutionBody.Body, _rawExecutionPaths);
        if (_terminal is not null)
            return false;

        if (!ValidateRawKickoff())
            return false;
        RecordRawRegion(
            ClassicInverseBodyId.Kickoff,
            _request.KickoffBody.Body,
            ClassicInverseRegionDisposition.Protocol,
            ownsSubtree: true,
            "raw-kickoff-shell");
        CoverRawSubtree(_request.KickoffBody.Body);

        if (!WalkRawExecution(_request.ExecutionBody.Body))
            return false;
        if (_terminal is not null)
            return false;
        if (!VerifyRawSemanticClosure())
            return false;

        return VerifyRawPartitionIsComplete();
    }

    bool ValidateRawKickoff()
    {
        int createCount = 0;
        int startCount = 0;
        int taskCount = 0;
        foreach (IrNode node in
            _request.KickoffBody.Body.Descendants.Prepend(
                _request.KickoffBody.Body))
        {
            if (ClassicInverseNodeFacts.IsUnknownEffectForm(node))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"raw kickoff contains an unknown effect form "
                        + $"'{node.Describe()}'");
            }

            string? effect =
                ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine);
            if (effect is null)
                continue;
            if (IsRawKickoffProtocolEffect(
                    node,
                    ref createCount,
                    ref startCount,
                    ref taskCount))
            {
                continue;
            }

            return DeclineFalse(
                ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                $"raw kickoff effect '{node.Describe()}' is not protocol");
        }
        return createCount == 1 && startCount == 1 && taskCount == 1
            || DeclineFalse(
                ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                "raw kickoff does not contain exactly one builder create, "
                    + "start, and task acquisition");
    }

    bool VerifyRawSemanticClosure()
    {
        ImmutableArray<string> rawEffects =
            RawSemanticEffects(_request.ExecutionBody.Body);
        if (_terminal is not null)
            return false;
        ImmutableArray<string> planningEffects =
        [
            .. _planningEffectOrder.Select(static effect =>
            {
                int marker = effect.LastIndexOf("@claim:", StringComparison.Ordinal);
                return marker < 0 ? effect : effect[..marker];
            }),
        ];
        if (!rawEffects.SequenceEqual(planningEffects, StringComparer.Ordinal))
        {
            return DeclineFalse(
                ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                "the raw import and planning view have different semantic "
                    + $"effect sequences: [{string.Join(",", rawEffects)}] -> "
                    + $"[{string.Join(",", planningEffects)}]");
        }
        return true;
    }

    ImmutableArray<string> RawSemanticEffects(IrNode root)
    {
        var effects = ImmutableArray.CreateBuilder<string>();
        Visit(root);
        return effects.ToImmutable();

        void Visit(IrNode node)
        {
            ClassicInverseProtocolRule protocol =
                ClassicInverseProtocol.Classify(node, _shell, _candidate);
            if (protocol.Kind == ClassicInverseProtocolKind.OwnedProtocol)
                return;

            foreach (IrNode child in node.Children)
            {
                if (_terminal is not null)
                    return;
                Visit(child);
            }

            if (protocol.Kind != ClassicInverseProtocolKind.None)
                return;
            if (IsRawExecutionProtocolEffect(node))
                return;
            if (ClassicInverseNodeFacts.IsUnknownEffectForm(node))
            {
                _terminal = Decline(
                    ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                    $"raw import contains an unmodeled effect form "
                        + $"'{node.Describe()}'");
                return;
            }
            string? signature =
                ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine);
            if (signature is null || IsRawLoopElementArtifact(node))
                return;
            effects.Add(NormalizeRawEffect(node, signature));
        }
    }

    bool IsRawLoopElementArtifact(IrNode node)
        => node is LoadElement
            && node.SourceOffset >= 0
            && _candidate.Claims.Any(claim =>
                claim.Rule == ClassicInverseRealizationRule.LoopElement
                && ImportOffsets(claim.Source).Contains(node.SourceOffset));

    bool IsRawExecutionProtocolEffect(IrNode node)
        => node switch
        {
            Call { Callee.Name: "get_IsCompleted" } call =>
                call.Arguments.Count == 1
                && call.Arguments[0] is LoadLocalAddress awaiter
                && _shell.AwaiterLocals.Contains(awaiter.Index),
            Call { Callee.Name: "<Clone>$" } =>
                _candidate.Recipe == "classic-sequential-await-void"
                && _candidate.Statements.Any(statement =>
                    statement.Descendants.Prepend(statement)
                        .Any(candidate => candidate is WithExpression)),
            ArrayLength =>
                _candidate.Recipe == "classic-await-foreach-array",
            _ => false,
        };

    string NormalizeRawEffect(IrNode node, string signature)
    {
        if (node is Call call && IsConsumedInitializerMethod(call.Callee))
        {
            return $"call:{call.Callee.DeclaringType.ToDisplayString()}."
                + $"{call.Callee.Name}/{call.Callee.ParameterTypes.Length}";
        }
        return ClassicInverseRealizationRules.NormalizeEffect(
            node,
            signature,
            _shell,
            ClassicInverseRealizationRule.Statement);
    }

    bool IsConsumedInitializerMethod(MethodRef method)
        => _planning.ExecutionBody.Body.Descendants.Any(node =>
            node switch
            {
                ObjectInitializerExpression initializer =>
                    initializer.ConsumedMethods.Any(
                        consumed => consumed == method),
                WithExpression with =>
                    with.ConsumedMethods.Any(consumed => consumed == method),
                InitializerBlock block =>
                    block.ConsumedMethods.Any(consumed => consumed == method),
                _ => false,
            });

    bool IsRawKickoffProtocolEffect(
        IrNode node,
        ref int createCount,
        ref int startCount,
        ref int taskCount)
    {
        switch (node)
        {
            case Call call when
                ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                    call.Callee.DeclaringType):
                switch (call.Callee.Name)
                {
                    case "Create":
                        createCount++;
                        return true;
                    case "Start":
                        startCount++;
                        return true;
                    case "get_Task":
                        taskCount++;
                        return true;
                    default:
                        return false;
                }
            case LoadProperty property when
                ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                    property.Accessor.DeclaringType)
                && property.PropertyName == "Task":
                taskCount++;
                return true;
            case LoadFieldAddress field:
                return ClassicInverseNodeFacts.IsMachineField(
                    field.Field,
                    _shell.Machine);
            case NewObject creation:
                return ClassicInverseNodeFacts.Definition(
                        creation.Constructor.DeclaringType)
                    == ClassicInverseNodeFacts.Definition(_shell.Machine);
            case InitObject { Address: LoadLocalAddress local }:
                return local.Index == _request.StateMachineLocal;
            default:
                return false;
        }
    }

    bool WalkRawExecution(IrNode node)
    {
        if (!_budget.Charge())
        {
            _terminal = ClassicInverseDecision.FailWith(
                ClassicInverseFailureKind.BudgetExhausted,
                "raw import accounting exhausted the planning budget");
            return false;
        }

        ClassicInverseProtocolRule protocol =
            ClassicInverseProtocol.Classify(node, _shell, _candidate);
        switch (protocol.Kind)
        {
            case ClassicInverseProtocolKind.OwnedProtocol:
                RecordRawRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Protocol,
                    ownsSubtree: true,
                    $"raw:{protocol.Name}");
                CoverRawSubtree(node);
                return true;

            case ClassicInverseProtocolKind.ProtocolFrame:
            case ClassicInverseProtocolKind.ProtocolContainer:
                RecordRawRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Protocol,
                    ownsSubtree: false,
                    $"raw:{protocol.Name}");
                _rawCovered.Add(node);
                return WalkRawChildren(node);

            case ClassicInverseProtocolKind.TransparentContainer:
            case ClassicInverseProtocolKind.Preserved:
                RecordRawRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Preserved,
                    ownsSubtree: false,
                    $"raw:{protocol.Name}");
                _rawCovered.Add(node);
                return WalkRawChildren(node);
        }

        if (IsRawExecutionProtocolEffect(node))
        {
            RecordRawRegion(
                ClassicInverseBodyId.Execution,
                node,
                ClassicInverseRegionDisposition.Protocol,
                ownsSubtree: true,
                "raw:awaiter-protocol-effect");
            CoverRawSubtree(node);
            return true;
        }

        if (ClassicInverseNodeFacts.IsUnknownEffectForm(node))
        {
            return DeclineFalse(
                ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                $"raw import node '{node.Describe()}' has an unknown effect form");
        }

        string? effect =
            ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine);
        RecordRawRegion(
            ClassicInverseBodyId.Execution,
            node,
            effect is null
                ? ClassicInverseRegionDisposition.Preserved
                : ClassicInverseRegionDisposition.Semantic,
            ownsSubtree: false,
            effect is null ? "raw:pure-structure" : $"raw:user-effect:{effect}");
        _rawCovered.Add(node);
        return WalkRawChildren(node);
    }

    bool WalkRawChildren(IrNode node)
    {
        foreach (IrNode child in node.Children)
        {
            if (!WalkRawExecution(child))
                return false;
        }
        return true;
    }

    bool VerifyRawPartitionIsComplete()
    {
        var receipts = new Dictionary<IrNode, ClassicInversePhysicalRegion>(
            ReferenceEqualityComparer.Instance);
        foreach ((IrNode node, ClassicInversePhysicalRegion region) in
            _rawReceiptNodes)
        {
            if (!receipts.TryAdd(node, region))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"raw node '{node.Describe()}' carries two dispositions");
            }
        }
        foreach ((IrNode node, ClassicInversePhysicalRegion region) in
            _rawReceiptNodes)
        {
            if (!region.OwnsSubtree)
                continue;
            foreach (IrNode descendant in node.Descendants)
            {
                if (receipts.ContainsKey(descendant))
                {
                    return DeclineFalse(
                        ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                        $"raw region '{region.Rule}' owns a subtree "
                            + "that carries a second receipt");
                }
            }
        }

        foreach (IrNode node in
            _request.ExecutionBody.Body.Descendants.Prepend(
                _request.ExecutionBody.Body))
        {
            if (!_rawCovered.Contains(node))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"raw execution node '{node.Describe()}' is unaccounted");
            }
        }
        foreach (IrNode node in
            _request.KickoffBody.Body.Descendants.Prepend(
                _request.KickoffBody.Body))
        {
            if (!_rawCovered.Contains(node))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"raw kickoff node '{node.Describe()}' is unaccounted");
            }
        }
        return true;
    }

    bool BindImportPaths()
    {
        for (int i = 0; i < _realizations.Count; i++)
        {
            ClassicInverseSemanticRealization realization = _realizations[i];
            ImmutableArray<ImmutableArray<int>> paths =
                ImportPaths(realization.ImportOffsets);
            if (paths.IsEmpty)
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.MissingImportCorrespondence,
                    $"{realization.Rule} has no raw import region");
            }
            _realizations[i] = realization with { ImportPaths = paths };
        }

        for (int i = 0; i < _ancestors.Count; i++)
        {
            ClassicInverseAncestorReceipt receipt = _ancestors[i];
            ImmutableArray<ImmutableArray<int>> paths =
                ImportPaths(receipt.ImportOffsets);
            if (paths.IsEmpty)
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.MissingImportCorrespondence,
                    "an ancestor receipt has no raw import region");
            }
            _ancestors[i] = receipt with { ImportPaths = paths };
        }
        return true;
    }

    ImmutableArray<ImmutableArray<int>> ImportPaths(
        ImmutableArray<int> offsets)
        =>
        [
            .. _rawRegions
                .Where(region =>
                    region.Body == ClassicInverseBodyId.Execution
                    && region.ImportOffsets.Any(offsets.Contains))
                .Select(static region => region.Path)
                .Distinct(),
        ];

    bool IsKickoffInitialState(IrNode statement)
        => statement is StoreField
        {
            Field: { Name: "<>1__state" } field,
            Instance: LoadLocalAddress local,
            Value: Constant { Value: -1 },
        }
            && local.Index == _request.StateMachineLocal
            && ClassicInverseNodeFacts.IsMachineField(field, _shell.Machine);

    bool IsKickoffStart(IrNode statement)
        => statement is ExpressionStatement
        {
            Expression: Call { Callee.Name: "Start" } start,
        }
            && ClassicInverseNodeFacts.IsAsyncMethodBuilder(start.Callee.DeclaringType)
            && start.Arguments.Count == 2
            && ClassicInverseNodeFacts.IsBuilderAccessOnLocal(
                start.Arguments[0],
                _shell.Machine,
                _request.StateMachineLocal)
            && start.Arguments[1] is LoadLocalAddress machine
            && machine.Index == _request.StateMachineLocal;

    bool IsKickoffReturnTask(IrNode statement)
        => statement is Return { Value: { } value }
            && value switch
            {
                Call { Callee.Name: "get_Task" } task =>
                    ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                        task.Callee.DeclaringType)
                    && task.Arguments.Count == 1
                    && ClassicInverseNodeFacts.IsBuilderAccessOnLocal(
                        task.Arguments[0],
                        _shell.Machine,
                        _request.StateMachineLocal),
                LoadProperty { PropertyName: "Task", Instance: { } receiver } property =>
                    ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                        property.Accessor.DeclaringType)
                    && ClassicInverseNodeFacts.IsBuilderAccessOnLocal(
                        receiver,
                        _shell.Machine,
                        _request.StateMachineLocal),
                _ => false,
            };

    bool TryGetParameterIndex(string fieldName, out int index)
    {
        IrFunction kickoff = _planning.KickoffBody;
        int argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (int i = 0; i < kickoff.Signature.Parameters.Length; i++)
        {
            if (kickoff.Signature.Parameters[i].Name == fieldName)
            {
                index = argumentBase + i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    bool AccountExecution()
        => Walk(_planning.ExecutionBody.Body);

    bool Walk(IrNode node)
    {
        if (!_budget.Charge())
        {
            _terminal = ClassicInverseDecision.FailWith(
                ClassicInverseFailureKind.BudgetExhausted,
                "physical accounting exhausted the planning budget");
            return false;
        }

        if (_claimBySource.ContainsKey(node))
        {
            RecordRegion(
                ClassicInverseBodyId.Execution,
                node,
                ClassicInverseRegionDisposition.Semantic,
                ownsSubtree: true,
                $"claim:{_claimBySource[node].Rule}");
            CoverSubtree(node, ClassicInverseBodyId.Execution);
            return true;
        }

        if (_candidate.DeclaredProtocol.TryGetValue(node, out string? declared))
        {
            RecordRegion(
                ClassicInverseBodyId.Execution,
                node,
                ClassicInverseRegionDisposition.Protocol,
                ownsSubtree: true,
                $"recipe:{declared}");
            CoverSubtree(node, ClassicInverseBodyId.Execution);
            return true;
        }

        ClassicInverseProtocolRule rule = ClassicInverseProtocol.Classify(
            node,
            _shell,
            _candidate);
        switch (rule.Kind)
        {
            case ClassicInverseProtocolKind.OwnedProtocol:
                RecordRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Protocol,
                    ownsSubtree: true,
                    rule.Name);
                CoverSubtree(node, ClassicInverseBodyId.Execution);
                return true;

            case ClassicInverseProtocolKind.Preserved:
                RecordRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Preserved,
                    ownsSubtree: true,
                    rule.Name);
                CoverSubtree(node, ClassicInverseBodyId.Execution);
                return true;

            case ClassicInverseProtocolKind.ProtocolFrame:
                RecordRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Protocol,
                    ownsSubtree: false,
                    rule.Name);
                Cover(node, ClassicInverseBodyId.Execution);
                return WalkChildren(node, rule.DescendSlots);

            case ClassicInverseProtocolKind.ProtocolContainer:
                RecordRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Protocol,
                    ownsSubtree: false,
                    rule.Name);
                Cover(node, ClassicInverseBodyId.Execution);
                return WalkChildren(node, rule.DescendSlots);

            case ClassicInverseProtocolKind.TransparentContainer:
                RecordRegion(
                    ClassicInverseBodyId.Execution,
                    node,
                    ClassicInverseRegionDisposition.Preserved,
                    ownsSubtree: false,
                    rule.Name);
                Cover(node, ClassicInverseBodyId.Execution);
                return WalkChildren(node, rule.DescendSlots);
        }

        if (_candidate.DeclaredContainers.TryGetValue(node, out var container))
        {
            RecordRegion(
                ClassicInverseBodyId.Execution,
                node,
                container.Kind == ClassicInverseAncestorKind.Protocol
                    ? ClassicInverseRegionDisposition.Protocol
                    : ClassicInverseRegionDisposition.Semantic,
                ownsSubtree: false,
                $"recipe-container:{container.Rule}");
            Cover(node, ClassicInverseBodyId.Execution);
            return WalkChildren(node, default);
        }

        return DeclineFalse(
            ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
            $"execution node '{node.Describe()}' at "
                + $"{ClassicInverseSignature.Path(PathOf(node, _executionPaths))} has no disposition");
    }

    bool WalkChildren(IrNode node, ImmutableArray<int> slots)
    {
        if (slots.IsDefaultOrEmpty)
        {
            foreach (IrNode child in node.Children)
            {
                if (!Walk(child))
                    return false;
            }
            return true;
        }

        if (slots.Length != node.Children.Count
            || slots.Distinct().Count() != slots.Length
            || slots.Any(slot => slot < 0 || slot >= node.Children.Count))
        {
            return DeclineFalse(
                ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                $"protocol frame '{node.Describe()}' has an undesignated child");
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            IrNode child = node.Children[i];
            if (!Walk(child))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Re-checks the partition the walk produced: every in-scope node covered
    /// exactly once, no subtree-owning receipt with a descendant receipt, and
    /// every semantic region carrying unambiguous, non-overlapping import
    /// correspondence.
    /// </summary>
    bool VerifyPartitionIsComplete()
    {
        var receipts = new Dictionary<IrNode, ClassicInversePhysicalRegion>(
            ReferenceEqualityComparer.Instance);
        foreach ((IrNode node, ClassicInversePhysicalRegion region) in _receiptNodes)
        {
            if (!receipts.TryAdd(node, region))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"node '{node.Describe()}' carries two dispositions");
            }
        }

        foreach ((IrNode node, ClassicInversePhysicalRegion region) in _receiptNodes)
        {
            if (!region.OwnsSubtree)
                continue;
            foreach (IrNode descendant in node.Descendants)
            {
                if (receipts.ContainsKey(descendant))
                {
                    return DeclineFalse(
                        ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                        $"region '{region.Rule}' owns a subtree that carries a second receipt");
                }
            }
        }

        foreach (IrNode node in
            _planning.ExecutionBody.Body.Descendants.Prepend(
                _planning.ExecutionBody.Body))
        {
            if (!_covered.Contains(node))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"execution node '{node.Describe()}' is unaccounted");
            }
        }

        foreach (IrNode node in
            _planning.KickoffBody.Body.Descendants.Prepend(
                _planning.KickoffBody.Body))
        {
            if (!_covered.Contains(node))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnclassifiedPhysicalRegion,
                    $"kickoff node '{node.Describe()}' is unaccounted");
            }
        }

        return true;
    }

    // ---- Ledger 2: semantic realizations --------------------------------

    bool VerifyRealizations()
    {
        foreach (ClassicInverseClaim claim in _candidate.Claims)
        {
            ImmutableArray<string> sourceEffects =
                RegionEffects(claim.Source, isOutput: false, claim.Rule);
            if (_terminal is not null)
                return false;
            ImmutableArray<string> outputEffects =
                RegionEffects(claim.Output, isOutput: true, claim.Rule);
            if (_terminal is not null)
                return false;

            if (!ClassicInverseRealizationRules.Verify(
                    claim,
                    _candidate,
                    _shell,
                    _claimBySource,
                    _claimByOutput,
                    out string failure))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                    $"{claim.Rule} realization is not proven: {failure}");
            }

            if (!sourceEffects.SequenceEqual(outputEffects, StringComparer.Ordinal))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                    $"{claim.Rule} realization changes the effect sequence: "
                        + $"[{string.Join(",", sourceEffects)}] -> "
                        + $"[{string.Join(",", outputEffects)}]");
            }

            ImmutableArray<int> offsets = ImportOffsets(claim.Source);
            if (offsets.IsEmpty)
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.MissingImportCorrespondence,
                    $"{claim.Rule} consumed a node with no import correspondence");
            }
            foreach (int offset in offsets)
            {
                if (!_request.ExecutionImportOffsets.Contains(offset))
                {
                    return DeclineFalse(
                        ClassicInverseDeclineReason.MissingImportCorrespondence,
                        $"{claim.Rule} cites IL offset {offset}, absent from the import snapshot");
                }
            }

            _realizations.Add(new ClassicInverseSemanticRealization(
                ClassicInverseBodyId.Execution,
                ClassicInverseCoordinateSpace.Planning,
                PathOf(claim.Source, _executionPaths),
                ClassicInverseCoordinateSpace.Output,
                PathOf(claim.Output, _outputPaths),
                claim.Rule,
                offsets,
                [],
                sourceEffects,
                outputEffects));
        }

        ImmutableArray<string> sourceOrder =
            GlobalClaimEffects(_planning.ExecutionBody.Body, isOutput: false);
        if (_terminal is not null)
            return false;
        ImmutableArray<string> outputOrder =
            GlobalClaimEffects(_output, isOutput: true);
        if (_terminal is not null)
            return false;
        if (!sourceOrder.SequenceEqual(outputOrder, StringComparer.Ordinal))
        {
            return DeclineFalse(
                ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                "the global semantic effect order or multiplicity changed: "
                    + $"[{string.Join(",", sourceOrder)}] -> "
                    + $"[{string.Join(",", outputOrder)}]");
        }
        _planningEffectOrder = sourceOrder;

        return true;
    }

    /// <summary>
    /// Every output effect must sit inside exactly one claim's output region.
    /// An output node under no claim is an invented effect, not a plan.
    /// </summary>
    bool VerifyOutputIsFullyCited()
    {
        foreach (IrNode node in _output.Descendants.Prepend(_output))
        {
            if (ClassicInverseNodeFacts.IsUnknownEffectForm(node))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.InventedOutputEffect,
                    $"proposed body contains an unclassifiable form '{node.Describe()}'");
            }
            if (HasConsumedInitializerEffect(node)
                && EnclosingClaimOutput(node) is null)
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.InventedOutputEffect,
                    $"proposed initializer effect '{node.Describe()}' cites no input effect");
            }
            if (ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine) is null)
                continue;
            if (EnclosingClaimOutput(node) is null)
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.InventedOutputEffect,
                    $"proposed body effect '{node.Describe()}' cites no input effect");
            }
        }
        return true;
    }

    static bool HasConsumedInitializerEffect(IrNode node)
        => node switch
        {
            ObjectInitializerExpression initializer =>
                initializer.ConsumedMethods.Any(static method => method is not null)
                || initializer.ConsumedFields.Any(static field => field is not null),
            WithExpression with =>
                with.ConsumedMethods.Any(static method => method is not null)
                || with.ConsumedFields.Any(static field => field is not null),
            InitializerBlock block =>
                block.ConsumedMethods.Any(static method => method is not null)
                || block.ConsumedFields.Any(static field => field is not null),
            _ => false,
        };

    // ---- Ledger 3: structured ancestors and control contexts ------------

    bool VerifyAncestorPaths()
    {
        foreach (ClassicInverseClaim claim in _candidate.Claims)
        {
            var steps = ImmutableArray.CreateBuilder<ClassicInverseAncestorStep>();
            IrNode? current = claim.Source.Parent;
            while (current is not null)
            {
                if (!_budget.Charge())
                {
                    _terminal = ClassicInverseDecision.FailWith(
                        ClassicInverseFailureKind.BudgetExhausted,
                        "ancestor accounting exhausted the planning budget");
                    return false;
                }

                if (ReferenceEquals(current, _planning.ExecutionBody.Body))
                {
                    steps.Add(new ClassicInverseAncestorStep(
                        ClassicInverseCoordinateSpace.Planning,
                        PathOf(current, _executionPaths),
                        current.Describe(),
                        ClassicInverseAncestorKind.Transparent,
                        "recipe-root",
                        ClassicInverseCoordinateSpace.Output,
                        []));
                    current = null;
                    continue;
                }

                if (_claimBySource.TryGetValue(current, out ClassicInverseClaim? enclosing))
                {
                    if (!IsDescendantOrSelf(claim.Output, enclosing.Output))
                    {
                        return DeclineFalse(
                            ClassicInverseDeclineReason.EscapedControlContext,
                            $"{claim.Rule} realizes outside its enclosing "
                                + $"{enclosing.Rule} realization");
                    }
                    steps.Add(new ClassicInverseAncestorStep(
                        ClassicInverseCoordinateSpace.Planning,
                        PathOf(current, _executionPaths),
                        current.Describe(),
                        ClassicInverseAncestorKind.Reproduced,
                        $"enclosing:{enclosing.Rule}",
                        ClassicInverseCoordinateSpace.Output,
                        PathOf(enclosing.Output, _outputPaths)));
                    current = current.Parent;
                    continue;
                }

                if (EnclosingSourceClaim(current) is { } reproducedBy)
                {
                    if (!IsDescendantOrSelf(claim.Output, reproducedBy.Output))
                    {
                        return DeclineFalse(
                            ClassicInverseDeclineReason.EscapedControlContext,
                            $"{claim.Rule} realizes outside its enclosing "
                                + $"{reproducedBy.Rule} realization");
                    }
                    steps.Add(new ClassicInverseAncestorStep(
                        ClassicInverseCoordinateSpace.Planning,
                        PathOf(current, _executionPaths),
                        current.Describe(),
                        ClassicInverseAncestorKind.Reproduced,
                        $"within:{reproducedBy.Rule}",
                        ClassicInverseCoordinateSpace.Output,
                        PathOf(reproducedBy.Output, _outputPaths)));
                    current = current.Parent;
                    continue;
                }

                if (_candidate.DeclaredContainers.TryGetValue(
                        current,
                        out ClassicInverseContainerDeclaration? declared))
                {
                    if (declared.Kind == ClassicInverseAncestorKind.Reproduced)
                    {
                        if (declared.OutputContext is null
                            || !IsDescendantOrSelf(claim.Output, declared.OutputContext))
                        {
                            return DeclineFalse(
                                ClassicInverseDeclineReason.EscapedControlContext,
                                $"{claim.Rule} escapes reproduced ancestor "
                                    + $"'{declared.Rule}'");
                        }
                    }
                    steps.Add(new ClassicInverseAncestorStep(
                        ClassicInverseCoordinateSpace.Planning,
                        PathOf(current, _executionPaths),
                        current.Describe(),
                        declared.Kind,
                        declared.Rule,
                        ClassicInverseCoordinateSpace.Output,
                        declared.OutputContext is null
                            ? []
                            : PathOf(declared.OutputContext, _outputPaths)));
                    current = current.Parent;
                    continue;
                }

                ClassicInverseAncestorKind? kind =
                    ClassicInverseProtocol.ClassifyAncestor(current, _shell, _candidate);
                if (kind is null)
                {
                    return DeclineFalse(
                        ClassicInverseDeclineReason.UnmodeledStructuredAncestor,
                        $"{claim.Rule} has unmodeled ancestor '{current.Describe()}'");
                }
                steps.Add(new ClassicInverseAncestorStep(
                    ClassicInverseCoordinateSpace.Planning,
                    PathOf(current, _executionPaths),
                    current.Describe(),
                    kind.Value,
                    kind.Value == ClassicInverseAncestorKind.Transparent
                        ? "shell-transparent"
                        : "shell-protocol",
                    ClassicInverseCoordinateSpace.Output,
                    []));
                current = current.Parent;
            }

            if (!IsDescendantOrSelf(
                    claim.Source,
                    _planning.ExecutionBody.Body))
            {
                return DeclineFalse(
                    ClassicInverseDeclineReason.UnmodeledStructuredAncestor,
                    $"{claim.Rule} path does not reach the recipe root");
            }

            _ancestors.Add(new ClassicInverseAncestorReceipt(
                ClassicInverseBodyId.Execution,
                ClassicInverseCoordinateSpace.Planning,
                PathOf(claim.Source, _executionPaths),
                ImportOffsets(claim.Source),
                [],
                steps.ToImmutable()));
        }

        return true;
    }

    ClassicInverseClaim? EnclosingSourceClaim(IrNode node)
    {
        for (IrNode? current = node.Parent;
            current is not null
                && !ReferenceEquals(current, _planning.ExecutionBody.Body);
            current = current.Parent)
        {
            if (_claimBySource.TryGetValue(
                    current,
                    out ClassicInverseClaim? claim))
            {
                return claim;
            }
        }

        return null;
    }

    /// <summary>
    /// The classic execution body encodes user loops and conditions as
    /// branches, so a consumed node's tree ancestors do not by themselves prove
    /// it executes under the same control context in the output. Each recipe
    /// declares those contexts; every claim inside one must realize inside the
    /// declared output context, and every claim outside one must not.
    /// </summary>
    bool VerifyControlContexts()
    {
        foreach (ClassicInverseControlRegion region in _candidate.ControlRegions)
        {
            foreach (ClassicInverseClaim claim in _candidate.Claims)
            {
                bool inside = region.SourceRoots.Any(
                    root => IsDescendantOrSelf(claim.Source, root));
                bool realizesInside =
                    IsDescendantOrSelf(claim.Output, region.OutputContext);
                if (inside && !realizesInside)
                {
                    return DeclineFalse(
                        ClassicInverseDeclineReason.EscapedControlContext,
                        $"{claim.Rule} executes under '{region.Rule}' but realizes outside it");
                }
                if (!inside && realizesInside)
                {
                    return DeclineFalse(
                        ClassicInverseDeclineReason.EscapedControlContext,
                        $"{claim.Rule} realizes inside '{region.Rule}' without executing under it");
                }
            }
        }
        return true;
    }

    // ---- Region and effect helpers --------------------------------------

    ImmutableArray<string> RegionEffects(
        IrNode root,
        bool isOutput,
        ClassicInverseRealizationRule rule)
    {
        var effects = ImmutableArray.CreateBuilder<string>();
        Visit(root);
        return effects.ToImmutable();

        void Visit(IrNode node)
        {
            if (ClassicInverseNodeFacts.IsUnknownEffectForm(node)
                && !(rule == ClassicInverseRealizationRule.LoopElement
                    && !isOutput
                    && root is StoreStackSlot
                    && node is LoadElement))
            {
                _terminal = Decline(
                    isOutput
                        ? ClassicInverseDeclineReason.InventedOutputEffect
                        : ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                    $"{rule} contains an unmodeled effect form "
                        + $"'{node.Describe()}'");
                return;
            }

            if (!ReferenceEquals(node, root))
            {
                bool nested = isOutput
                    ? _claimByOutput.ContainsKey(node)
                    : _claimBySource.ContainsKey(node);
                if (nested)
                    return;
            }

            if (VisitInitializer(node, Visit, effects))
                return;

            foreach (IrNode child in node.Children)
            {
                if (_terminal is not null)
                    return;
                Visit(child);
            }

            string? signature =
                ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine);
            if (signature is not null
                && !(rule == ClassicInverseRealizationRule.LoopElement
                    && !isOutput
                    && root is StoreStackSlot
                    && node is LoadElement))
            {
                effects.Add(ClassicInverseRealizationRules.NormalizeEffect(
                    node,
                    signature,
                    _shell,
                    rule));
            }
        }
    }

    ImmutableArray<string> GlobalClaimEffects(IrNode root, bool isOutput)
    {
        var effects = ImmutableArray.CreateBuilder<string>();
        Visit(root);
        return effects.ToImmutable();

        void Visit(IrNode node)
        {
            ClassicInverseClaim? claim = isOutput
                ? EnclosingClaimOutput(node)
                : EnclosingClaimSource(node);

            if (claim is not null
                && ClassicInverseNodeFacts.IsUnknownEffectForm(node))
            {
                _terminal = Decline(
                    isOutput
                        ? ClassicInverseDeclineReason.InventedOutputEffect
                        : ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                    $"claimed region contains an unmodeled effect form "
                        + $"'{node.Describe()}'");
                return;
            }

            if (VisitInitializer(
                    node,
                    Visit,
                    claim is null ? null : effects,
                    claim is null
                        ? (Func<string, string>?)null
                        : effect => effect + $"@claim:{ClaimToken(claim)}"))
                return;

            foreach (IrNode child in node.Children)
            {
                if (_terminal is not null)
                    return;
                Visit(child);
            }

            if (claim is null)
                return;
            string? signature =
                ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine);
            if (signature is not null
                && !(claim.Rule == ClassicInverseRealizationRule.LoopElement
                    && !isOutput
                    && claim.Source is StoreStackSlot
                    && node is LoadElement))
            {
                effects.Add(ClassicInverseRealizationRules.NormalizeEffect(
                    node,
                    signature,
                    _shell,
                    claim.Rule)
                    + $"@claim:{ClaimToken(claim)}");
            }
        }
    }

    string ClaimToken(ClassicInverseClaim claim)
        => ClassicInverseSignature.Path(
            PathOf(claim.Source, _executionPaths));

    static bool VisitInitializer(
        IrNode node,
        Action<IrNode> visit,
        ImmutableArray<string>.Builder? effects,
        Func<string, string>? qualify = null)
    {
        IReadOnlyList<InitializerEntry> entries;
        switch (node)
        {
            case ObjectInitializerExpression initializer:
                visit(initializer.Creation);
                entries = initializer.Entries;
                break;
            case WithExpression with:
                visit(with.Receiver);
                entries = with.Entries;
                break;
            case InitializerBlock block:
                entries = block.Entries;
                break;
            default:
                return false;
        }

        foreach (InitializerEntry entry in entries)
        {
            foreach (IrExpression argument in entry.Arguments)
                visit(argument);
            if (effects is null)
                continue;
            if (entry.ConsumedMethod is { } method)
            {
                string effect =
                    $"call:{method.DeclaringType.ToDisplayString()}."
                        + $"{method.Name}/{method.ParameterTypes.Length}";
                effects.Add(qualify?.Invoke(effect) ?? effect);
            }
            if (entry.ConsumedField is { } field)
            {
                string effect =
                    $"store:{field.DeclaringType.ToDisplayString()}.{field.Name}";
                effects.Add(qualify?.Invoke(effect) ?? effect);
            }
        }
        return true;
    }

    ClassicInverseClaim? EnclosingClaimSource(IrNode node)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (_claimBySource.TryGetValue(
                    current,
                    out ClassicInverseClaim? claim))
            {
                return claim;
            }
        }
        return null;
    }

    ClassicInverseClaim? EnclosingClaimOutput(IrNode node)
    {
        IrNode? current = node;
        while (current is not null)
        {
            if (_claimByOutput.TryGetValue(current, out ClassicInverseClaim? claim))
                return claim;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// The IL offsets one region cites, excluding the regions nested claims own.
    /// Offsets are not one-to-one with IR nodes, so this proves presence in the
    /// unmodified snapshot, not exclusivity.
    /// </summary>
    ImmutableArray<int> ImportOffsets(IrNode root)
    {
        var offsets = ImmutableArray.CreateBuilder<int>();
        Visit(root);
        return [.. offsets.Distinct().Order()];

        void Visit(IrNode node)
        {
            if (!ReferenceEquals(node, root) && _claimBySource.ContainsKey(node))
                return;
            if (node.SourceOffset >= 0)
                offsets.Add(node.SourceOffset);
            foreach (IrNode child in node.Children)
                Visit(child);
        }
    }

    readonly List<(IrNode Node, ClassicInversePhysicalRegion Region)> _receiptNodes = [];

    void RecordRegion(
        ClassicInverseBodyId body,
        IrNode node,
        ClassicInverseRegionDisposition disposition,
        bool ownsSubtree,
        string rule)
    {
        var region = new ClassicInversePhysicalRegion(
            body,
            ClassicInverseCoordinateSpace.Planning,
            PathOf(
                node,
                body == ClassicInverseBodyId.Kickoff
                    ? _kickoffPaths
                    : _executionPaths),
            node.Describe(),
            disposition,
            ownsSubtree,
            rule,
            disposition == ClassicInverseRegionDisposition.Semantic
                ? ImportOffsets(node)
                : []);
        _receiptNodes.Add((node, region));
    }

    void RecordRawRegion(
        ClassicInverseBodyId body,
        IrNode node,
        ClassicInverseRegionDisposition disposition,
        bool ownsSubtree,
        string rule)
    {
        var region = new ClassicInversePhysicalRegion(
            body,
            ClassicInverseCoordinateSpace.Import,
            PathOf(
                node,
                body == ClassicInverseBodyId.Kickoff
                    ? _rawKickoffPaths
                    : _rawExecutionPaths),
            node.Describe(),
            disposition,
            ownsSubtree,
            rule,
            ownsSubtree
                ? RawOffsets(node)
                : node.SourceOffset >= 0
                    ? [node.SourceOffset]
                    : []);
        if (disposition == ClassicInverseRegionDisposition.Semantic
            && (node.SourceOffset < 0
                || !_request.ExecutionImportOffsets.Contains(
                    node.SourceOffset)))
        {
            _terminal ??= Decline(
                ClassicInverseDeclineReason.MissingImportCorrespondence,
                $"raw semantic node '{node.Describe()}' lacks import correspondence");
        }
        _rawRegions.Add(region);
        _rawReceiptNodes.Add((node, region));
    }

    static ImmutableArray<int> RawOffsets(IrNode node)
        =>
        [
            .. node.Descendants.Prepend(node)
                .Where(static candidate => candidate.SourceOffset >= 0)
                .Select(static candidate => candidate.SourceOffset)
                .Distinct()
                .Order(),
        ];

    void Cover(IrNode node, ClassicInverseBodyId body) => _covered.Add(node);

    void CoverSubtree(IrNode node, ClassicInverseBodyId body)
    {
        _covered.Add(node);
        foreach (IrNode descendant in node.Descendants)
            _covered.Add(descendant);
    }

    void CoverRawSubtree(IrNode node)
    {
        _rawCovered.Add(node);
        foreach (IrNode descendant in node.Descendants)
            _rawCovered.Add(descendant);
    }

    static bool IsDescendantOrSelf(IrNode node, IrNode ancestor)
    {
        IrNode? current = node;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            current = current.Parent;
        }
        return false;
    }

    ImmutableArray<int> PathOf(
        IrNode node,
        Dictionary<IrNode, ImmutableArray<int>> index)
        => index.TryGetValue(node, out ImmutableArray<int> path) ? path : [];

    void IndexPaths(IrNode root, Dictionary<IrNode, ImmutableArray<int>> index)
    {
        Visit(root, []);

        void Visit(IrNode node, ImmutableArray<int> path)
        {
            if (!_budget.Charge())
            {
                _terminal ??= ClassicInverseDecision.FailWith(
                    ClassicInverseFailureKind.BudgetExhausted,
                    "path indexing exhausted the planning budget");
                return;
            }
            index[node] = path;
            for (int i = 0; i < node.Children.Count; i++)
                Visit(node.Children[i], path.Add(i));
        }
    }

    ClassicInverseDecision Decline(
        ClassicInverseDeclineReason reason,
        string detail)
    {
        _terminal = ClassicInverseDecision.DeclineWith(
            reason,
            $"[{_candidate.Recipe}] {detail}");
        return _terminal;
    }

    bool DeclineFalse(ClassicInverseDeclineReason reason, string detail)
    {
        Decline(reason, detail);
        return false;
    }

    ClassicInverseDecision Failure(string detail)
        => ClassicInverseDecision.FailWith(
            ClassicInverseFailureKind.BudgetExhausted,
            detail);
}
