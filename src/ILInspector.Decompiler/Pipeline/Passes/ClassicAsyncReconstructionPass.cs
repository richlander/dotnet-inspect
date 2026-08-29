using System.Collections.Immutable;

using ILInspector.ControlFlow;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs classic async state-machine kickoffs (runtime-async=off) back to
/// async bodies. The source logic lives in <c>&lt;M&gt;d__N.MoveNext</c>; the public
/// kickoff only initializes the state machine and returns the builder's task.
/// This pass imports the sibling <c>MoveNext</c>, recovers the fixture-backed
/// await shapes, and replaces the kickoff body with the source-shaped body.
/// </summary>
public sealed class ClassicAsyncReconstructionPass : IIrPass
{
    public string Name => "classic-async-reconstruction";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.ClassicAsyncRelationship is not { } evidence
            || context.ImportMethodBody is null)
        {
            return;
        }

        ClassicAsyncPreparationResult preparation =
            evidence.PlanningSession.Prepare(evidence);
        ClassicAsyncStage stage =
            context.ClassicAsyncStage ?? ClassicAsyncStage.Raised;
        if (preparation is not
            ClassicAsyncPreparationResult.Decided { Decision: var decision })
        {
            function.ClassicAsyncStageResult = preparation switch
            {
                ClassicAsyncPreparationResult.NotApplicable
                    => new ClassicAsyncStageResult.NotApplicable(stage),
                _ => new ClassicAsyncStageResult.Failed(
                    stage,
                    PreparationFailure(preparation)),
            };
            return;
        }

        ApplyDecision(function, context, decision);
        function.ClassicAsyncStageResult =
            new ClassicAsyncStageResult.Applied(
                stage,
                function.ClassicAsyncOutcome!,
                function.ClassicAsyncDeclarationDisposition);

        static ClassicAsyncFailure PreparationFailure(
            ClassicAsyncPreparationResult result)
            => result switch
            {
                ClassicAsyncPreparationResult.InputUnavailable unavailable
                    => new(
                        DiagnosticIds.ContextUnavailable,
                        unavailable.Failure.Detail),
                ClassicAsyncPreparationResult.ImportFailed failed
                    => failed.Failure,
                ClassicAsyncPreparationResult.PlanningFailed failed
                    => failed.Failure,
                _ => new(
                    DiagnosticIds.InternalError,
                    "classic async preparation did not produce a decision"),
            };
    }

    internal static void ApplyDecision(
        IrFunction function,
        PassContext context,
        ClassicAsyncDecision decision)
    {
        switch (decision)
        {
            case ClassicAsyncDecision.Reconstruct reconstruct:
                Apply(function, context, reconstruct.Plan);
                break;
            case ClassicAsyncDecision.Decline decline:
                Decline(
                    function,
                    context,
                    decline.Reason,
                    decline.KickoffDisposition);
                break;
        }
    }

    internal static ClassicAsyncPreparationResult Prepare(
        MetadataSource source,
        ClassicAsyncRelationshipEvidence evidence)
    {
        if (!ReferenceEquals(
                evidence.AcquisitionGuard,
                source.AcquisitionGuard)
            || !evidence.RequestedHost.BelongsTo(source.Reader))
        {
            return ImportFailure(
                ClassicAsyncHostRole.DeclaredKickoff,
                "classic async request belongs to another metadata acquisition");
        }

        if (evidence.Relationship is
            StateMachineRelationshipResult.Rejected rejected)
        {
            return new ClassicAsyncPreparationResult.InputUnavailable(
                rejected.Failure);
        }

        if (evidence.HostRole != ClassicAsyncHostRole.DeclaredKickoff
            || evidence.Relationship is not
                StateMachineRelationshipResult.Resolved resolved
            || resolved.Relationship.Kind
                != StateMachineClaimKind.ClassicAsync)
        {
            return new ClassicAsyncPreparationResult.NotApplicable(
                evidence.HostRole,
                evidence.Classification);
        }

        IrFunction? kickoffFunction = IrImporter.Import(
            source,
            evidence.RequestedHost.Handle);
        if (kickoffFunction is null)
        {
            return ImportFailure(
                ClassicAsyncHostRole.DeclaredKickoff,
                "owner-selected classic async kickoff could not be imported");
        }

        var planningContext = PassContext.ForImport(
            method => IrImporter.Import(source, method),
            source.AreProvablyDisjoint);
        IrPasses.Run(
            kickoffFunction,
            IrPasses.Before<ClassicAsyncReconstructionPass>(),
            planningContext);

        if (!TryGetKickoff(
                kickoffFunction,
                resolved.Relationship.StateMachineName,
                out var kickoff,
                out var declineReason,
                out bool narrowHandoff))
        {
            return DeclineDecision(
                declineReason,
                narrowHandoff);
        }

        if (!resolved.Relationship.TryGetMethod(
                StateMachineMethodRole.MoveNext,
                out var moveNextAddress))
        {
            return DeclineDecision(
                ClassicAsyncDeclineReason.NoExecutionMethod,
                kickoff.IsNarrow);
        }

        var moveNextMethod = new MethodRef(
            kickoff.StateMachineType,
            "MoveNext",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: true)
        {
            ExactDefinitionAddress = moveNextAddress,
            ExactDefinitionAcquisitionGuard = evidence.AcquisitionGuard,
        };
        var moveNextPasses = IrPasses.ForReconstruction<ClassicAsyncReconstructionPass>();
        if (!planningContext.TryImportAndRunMethodBody(
                moveNextMethod,
                moveNextPasses,
                out var moveNext)
            || moveNext is null)
        {
            return ImportFailure(
                ClassicAsyncHostRole.Execution,
                "owner-selected classic async execution method could not be imported");
        }

        var reconstruction = TryReconstruct(
            moveNext,
            kickoffFunction,
            kickoff,
            resolved.Relationship.Kickoff,
            moveNextAddress,
            out var body,
            out var locals,
            out var localNames,
            out var regionLedger);
        if (reconstruction
            == ReconstructionResult.UnconsumedExecutionRegion)
        {
            return DeclineDecision(
                ClassicAsyncDeclineReason.UnconsumedExecutionRegion,
                narrowHandoff: false);
        }
        if (reconstruction
            == ReconstructionResult.UnconsumedKickoffRegion)
        {
            return DeclineDecision(
                ClassicAsyncDeclineReason.NonNarrowKickoffHandoff,
                narrowHandoff: false);
        }
        if (reconstruction != ReconstructionResult.Reconstructed)
        {
            return DeclineDecision(
                ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
                narrowHandoff: false);
        }

        var machine = new ClassicAsyncMachine(
            resolved.Relationship.Kickoff,
            moveNextAddress,
            resolved.Relationship.StateMachineType,
            resolved.Relationship.StateMachineName,
            kickoff.StateMachineType,
            kickoff.StateMachineLocal,
            kickoff.BuilderStorage.Type,
            kickoff.StateStorage,
            kickoff.BuilderStorage,
            ClassicAsyncStorageSet.Create(
                AwaiterStorages(moveNext)),
            evidence.AcquisitionGuard);
        var plan = new ClassicAsyncPlan(
            machine,
            ClassicAsyncBodyPlan.Capture(body, locals, localNames),
            regionLedger,
            IrTypeFactsSnapshot.Capture(moveNext));
        return new ClassicAsyncPreparationResult.Decided(
            new ClassicAsyncDecision.Reconstruct(plan));

        static ClassicAsyncPreparationResult DeclineDecision(
            ClassicAsyncDeclineReason reason,
            bool narrowHandoff)
            => new ClassicAsyncPreparationResult.Decided(
                new ClassicAsyncDecision.Decline(
                    reason,
                    narrowHandoff
                        ? ClassicAsyncKickoffDisposition.ReplacedNarrowHandoff
                        : ClassicAsyncKickoffDisposition.PreservedOriginal));

        static ClassicAsyncPreparationResult ImportFailure(
            ClassicAsyncHostRole role,
            string message)
            => new ClassicAsyncPreparationResult.ImportFailed(
                role,
                new(
                    DiagnosticIds.ContextUnavailable,
                    message));
    }

    static void Apply(
        IrFunction function,
        PassContext context,
        ClassicAsyncPlan plan)
    {
        BlockContainer body = plan.Body.Materialize();
        int sourceOffset = function.Descendants
            .OfType<StoreField>()
            .FirstOrDefault(store => store is
            {
                Field.Name: "<>t__builder",
                Instance: LoadLocalAddress local,
            } && local.Index == plan.Machine.StateMachineLocal)
            ?.SourceOffset ?? -1;
        foreach (IrNode statement in body.Blocks.SelectMany(
            static block => block.Children))
        {
            Reanchor(statement, sourceOffset);
        }

        context.Stepper.StepOver(
            $"reconstruct classic async '{function.Name}' from "
            + $"{plan.Machine.StateMachineType.Name}.MoveNext");
        function.MergeTypeFactsFrom(plan.TypeFacts);
        function.ResetLocals(plan.Body.Locals, plan.Body.LocalNames);
        function.RequiresAsyncBodyModifier = true;
        function.ClassicAsyncOutcome = new ClassicAsyncOutcome.Reconstructed();
        function.ClassicAsyncDeclarationDisposition =
            ClassicAsyncDeclarationDisposition.IncludeAsync;
        function.Body.DetachChildren();
        foreach (var block in body.Blocks.ToList())
        {
            block.Detach();
            function.Body.Add(block);
        }
    }

    static void Decline(
        IrFunction function,
        PassContext context,
        ClassicAsyncDeclineReason reason,
        ClassicAsyncKickoffDisposition kickoffDisposition)
    {
        context.Stepper.StepOver(
            $"decline classic async '{function.Name}': {reason}");
        var marker = new UnsupportedNode(
            0,
            "classic async",
            $"unsupported classic async state machine: {ReasonText(reason)}");
        var statement = new ExpressionStatement(marker);
        if (kickoffDisposition
            == ClassicAsyncKickoffDisposition.ReplacedNarrowHandoff)
        {
            function.ResetLocals([], []);
            function.Body.DetachChildren();
            var markerBlock = new Block(0);
            markerBlock.Add(statement);
            function.Body.Add(markerBlock);
        }
        else if (function.Body.Blocks.FirstOrDefault() is { } firstBlock)
        {
            var preserved = firstBlock.DetachChildren();
            firstBlock.Add(statement);
            foreach (IrNode node in preserved)
                firstBlock.Add(node);
        }
        else
        {
            var markerBlock = new Block(0);
            markerBlock.Add(statement);
            function.Body.Add(markerBlock);
        }
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            $"classic async reconstruction declined: {ReasonText(reason)}"));
        function.ClassicAsyncOutcome = new ClassicAsyncOutcome.Declined(
            reason,
            kickoffDisposition);
        function.ClassicAsyncDeclarationDisposition =
            ClassicAsyncDeclarationDisposition.OmitAsync;
        function.RequiresAsyncBodyModifier = false;
    }

    static string ReasonText(ClassicAsyncDeclineReason reason)
        => reason switch
        {
            ClassicAsyncDeclineReason.NoExecutionMethod
                => "owner relationship has no execution method",
            ClassicAsyncDeclineReason.KickoffMachineMismatch
                => "kickoff does not hand off the owner-selected state machine",
            ClassicAsyncDeclineReason.NonNarrowKickoffHandoff
                => "kickoff handoff is not narrow",
            ClassicAsyncDeclineReason.UnsupportedBuilder
                => "unsupported async method builder",
            ClassicAsyncDeclineReason.UnconsumedExecutionRegion
                => "execution region contains unconsumed user effects; original kickoff preserved",
            ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol
                => "unrecognized awaiter protocol",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    sealed record Kickoff(
        TypeRef StateMachineType,
        int StateMachineLocal,
        int SourceOffset,
        bool IsNarrow,
        ClassicAsyncStorage StateStorage,
        ClassicAsyncStorage BuilderStorage,
        IReadOnlyList<IrNode> HandoffStatementSlots);

    enum ReconstructionResult
    {
        NotRecognized,
        Reconstructed,
        UnconsumedKickoffRegion,
        UnconsumedExecutionRegion,
    }

    sealed class LocalBuilder
    {
        readonly ImmutableArray<TypeRef>.Builder _locals = ImmutableArray.CreateBuilder<TypeRef>();
        readonly ImmutableArray<string?>.Builder _names = ImmutableArray.CreateBuilder<string?>();

        public int Add(TypeRef type, string? name)
        {
            var index = _locals.Count;
            _locals.Add(type);
            _names.Add(name);
            return index;
        }

        public ImmutableArray<TypeRef> Locals => _locals.ToImmutable();
        public ImmutableArray<string?> Names => _names.ToImmutable();
    }

    sealed class RecipeOwnership
    {
        readonly HashSet<IrNode> _statementSlots =
            new(ReferenceEqualityComparer.Instance);

        internal IReadOnlyList<IrNode> StatementSlots
            => [.. _statementSlots];

        internal bool Claim(params IrNode[] nodes)
        {
            foreach (IrNode node in nodes)
            {
                if (!TryGetStatementSlot(node, out IrNode statement))
                    return false;
                _statementSlots.Add(statement);
            }
            return true;
        }

        internal bool Claim(IEnumerable<IrNode> nodes)
        {
            foreach (IrNode node in nodes)
            {
                if (!TryGetStatementSlot(node, out IrNode statement))
                    return false;
                _statementSlots.Add(statement);
            }
            return true;
        }
    }

    static bool TryGetKickoff(
        IrFunction function,
        MetadataTypeDefinitionName expectedStateMachine,
        out Kickoff kickoff,
        out ClassicAsyncDeclineReason declineReason,
        out bool narrowHandoff)
    {
        kickoff = null!;
        narrowHandoff = false;
        declineReason =
            ClassicAsyncDeclineReason.NonNarrowKickoffHandoff;
        if (function.Body.Blocks is not [var block])
            return false;

        StoreField? builderStore = null;
        StoreField? stateStore = null;
        ExpressionStatement? startStatement = null;
        Return? returnTask = null;
        Return? returnVoid = null;

        foreach (var statement in block.Children)
        {
            if (statement is StoreField { Field.Name: "<>t__builder", Instance: LoadLocalAddress builderLocal } store
                && builderStore is null)
            {
                builderStore = store;
                if (builderLocal.Index < 0 || builderLocal.Index >= function.Locals.Length)
                    return false;
                continue;
            }

            if (statement is StoreField
                {
                    Field.Name: "<>1__state",
                    Instance: LoadLocalAddress,
                } state
                && stateStore is null)
            {
                stateStore = state;
            }

            if (statement is ExpressionStatement { Expression: Call { Callee.Name: "Start" } } expression)
                startStatement = expression;
            else if (statement is Return { Value: LoadProperty { PropertyName: "Task" } } taskResult)
                returnTask = taskResult;
            else if (statement is Return { Value: null } voidResult)
                returnVoid = voidResult;
        }

        if (builderStore?.Instance is not LoadLocalAddress stateMachineAddress
            || stateStore is null
            || startStatement is null
            || returnTask is null && returnVoid is null)
        {
            return false;
        }

        var stateMachineType = function.Locals[stateMachineAddress.Index];
        TypeRef stateMachineDefinition =
            stateMachineType.Kind == TypeRefKind.GenericInstance
                && stateMachineType.ElementType is { } definition
                    ? definition
                    : stateMachineType;
        if (stateMachineDefinition.DefinitionName
            != expectedStateMachine)
        {
            declineReason =
                ClassicAsyncDeclineReason.KickoffMachineMismatch;
            return false;
        }

        narrowHandoff = IsNarrowKickoff(
            function,
            block,
            stateMachineAddress.Index);
        if (function.Signature.ReturnType is
            {
                Namespace: "System",
                Name: "Void",
            })
        {
            if (returnVoid is null)
            {
                declineReason =
                    ClassicAsyncDeclineReason.NonNarrowKickoffHandoff;
                return false;
            }
            declineReason =
                ClassicAsyncDeclineReason.UnsupportedBuilder;
            return false;
        }

        if (returnTask is null)
            return false;

        kickoff = new(
            stateMachineType,
            stateMachineAddress.Index,
            builderStore.SourceOffset,
            narrowHandoff,
            new(
                stateStore.Field.Name,
                stateStore.Field.Type),
            new(
                builderStore.Field.Name,
                builderStore.Field.Type),
            narrowHandoff
                ? [.. block.Children]
                : []);
        return true;
    }

    static IEnumerable<ClassicAsyncStorage> AwaiterStorages(
        IrFunction moveNext)
    {
        foreach (IrNode node in moveNext.Descendants)
        {
            FieldRef? field = node switch
            {
                LoadField load => load.Field,
                LoadFieldAddress address => address.Field,
                StoreField store => store.Field,
                _ => null,
            };
            if (field is { Name: var name }
                && name.StartsWith("<>u__", StringComparison.Ordinal))
            {
                yield return new(name, field.Type);
            }
        }
    }

    static bool IsNarrowKickoff(
        IrFunction function,
        Block block,
        int stateMachineLocal)
    {
        int builderCreates = 0;
        int stateInitializations = 0;
        int starts = 0;
        int returns = 0;
        var copiedArguments = new HashSet<int>();

        foreach (IrNode statement in block.Children)
        {
            switch (statement)
            {
                case StoreField
                {
                    Field.Name: "<>t__builder",
                    Instance: LoadLocalAddress builderTarget,
                    Value: Call
                    {
                        Callee.Name: "Create",
                        Arguments.Count: 0,
                    },
                } when builderTarget.Index == stateMachineLocal:
                    builderCreates++;
                    break;

                case StoreField
                {
                    Field.Name: "<>1__state",
                    Instance: LoadLocalAddress stateTarget,
                    Value: Constant { Value: -1 },
                } when stateTarget.Index == stateMachineLocal:
                    stateInitializations++;
                    break;

                case StoreField
                {
                    Instance: LoadLocalAddress copyTarget,
                    Value: LoadArgument argument,
                } when copyTarget.Index == stateMachineLocal
                    && IsSourceArgument(function, argument)
                    && copiedArguments.Add(argument.Index):
                    break;

                case ExpressionStatement
                {
                    Expression: Call
                    {
                        Callee.Name: "Start",
                        Arguments:
                        [
                            LoadFieldAddress
                            {
                                Field.Name: "<>t__builder",
                                Instance: LoadLocalAddress builderOwner,
                            },
                            LoadLocalAddress machine,
                        ],
                    },
                } when builderOwner.Index == stateMachineLocal
                    && machine.Index == stateMachineLocal:
                    starts++;
                    break;

                case Return
                {
                    Value: LoadProperty
                    {
                        PropertyName: "Task",
                        Instance: LoadFieldAddress
                        {
                            Field.Name: "<>t__builder",
                            Instance: LoadLocalAddress builderOwner,
                        },
                        IndexArguments.Count: 0,
                    },
                } when builderOwner.Index == stateMachineLocal:
                    returns++;
                    break;

                case Return { Value: null }
                    when function.Signature.ReturnType is
                    {
                        Namespace: "System",
                        Name: "Void",
                    }:
                    returns++;
                    break;

                default:
                    return false;
            }
        }

        return builderCreates == 1
            && stateInitializations == 1
            && starts == 1
            && returns == 1;
    }

    static bool IsSourceArgument(
        IrFunction function,
        LoadArgument argument)
    {
        int parameterIndex = function.Signature.HasThis
            ? argument.Index - 1
            : argument.Index;
        if (parameterIndex == -1)
        {
            return argument.Type.Equals(function.DeclaringType);
        }

        return parameterIndex >= 0
            && parameterIndex < function.Signature.Parameters.Length
            && argument.Type.Equals(
                function.Signature.Parameters[parameterIndex].Type);
    }

    static ReconstructionResult TryReconstruct(
        IrFunction moveNext,
        IrFunction kickoff,
        Kickoff kickoffModel,
        MetadataMethodAddress kickoffAddress,
        MetadataMethodAddress executionAddress,
        out BlockContainer body,
        out ImmutableArray<TypeRef> locals,
        out ImmutableArray<string?> localNames,
        out ClassicAsyncRegionLedger regionLedger)
    {
        body = null!;
        locals = [];
        localNames = [];
        regionLedger = null!;

        var localBuilder = new LocalBuilder();
        if (!TryBuildStatements(
                moveNext,
                kickoff,
                localBuilder,
                out var statements,
                out bool recipeHasUnconsumedStore,
                out var consumedExecutionSlots))
        {
            return ReconstructionResult.NotRecognized;
        }
        if (!kickoffModel.IsNarrow)
            return ReconstructionResult.UnconsumedKickoffRegion;
        if (recipeHasUnconsumedStore
            || HasUnconsumedExecutionStore(moveNext))
        {
            return ReconstructionResult.UnconsumedExecutionRegion;
        }

        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);

        body = new BlockContainer();
        body.Add(block);
        if (!TryBuildRegionLedger(
                kickoff,
                moveNext,
                kickoffAddress,
                executionAddress,
                kickoffModel.HandoffStatementSlots,
                consumedExecutionSlots,
                body,
                out regionLedger))
        {
            return ReconstructionResult.UnconsumedExecutionRegion;
        }

        locals = localBuilder.Locals;
        localNames = localBuilder.Names;
        return ReconstructionResult.Reconstructed;
    }

    static bool TryBuildRegionLedger(
        IrFunction kickoff,
        IrFunction moveNext,
        MetadataMethodAddress kickoffAddress,
        MetadataMethodAddress executionAddress,
        IReadOnlyList<IrNode> consumedKickoffSlots,
        IReadOnlyList<IrNode> consumedExecutionSlots,
        BlockContainer output,
        out ClassicAsyncRegionLedger ledger)
    {
        if (!TryCapturePhysicalRegions(
                kickoff,
                ClassicAsyncRegionHost.Kickoff,
                kickoffAddress,
                out var kickoffRegions)
            || !TryCapturePhysicalRegions(
                moveNext,
                ClassicAsyncRegionHost.Execution,
                executionAddress,
                out var executionRegions)
            || !TryCaptureRegionIds(
                kickoff,
                ClassicAsyncRegionHost.Kickoff,
                kickoffAddress,
                consumedKickoffSlots,
                out var consumedKickoff)
            || !TryCaptureRegionIds(
                moveNext,
                ClassicAsyncRegionHost.Execution,
                executionAddress,
                consumedExecutionSlots,
                out var consumedExecution)
            || !TryCaptureUserRegions(
                moveNext,
                executionAddress,
                out var regions))
        {
            ledger = null!;
            return false;
        }
        List<ClassicAsyncPhysicalRegion> physical =
        [
            .. kickoffRegions,
            .. executionRegions,
        ];
        List<ClassicAsyncPhysicalRegionId> consumed =
        [
            .. consumedKickoff,
            .. consumedExecution,
        ];
        HashSet<ClassicAsyncPhysicalRegionId> consumedSet =
            consumed.ToHashSet();
        List<ClassicAsyncPhysicalRegionId> preserved =
        [
            .. physical
                .Select(static region => region.Id)
                .Where(region => !consumedSet.Contains(region)),
        ];
        List<ClassicAsyncOutputNode> outputs =
            CaptureOutputNodes(output);
        var available = new List<ClassicAsyncOutputNode>(outputs);
        var realizations =
            new List<ClassicAsyncUserRegionRealization>(regions.Count);

        foreach (ClassicAsyncUserRegion region in regions)
        {
            int outputIndex = available.FindIndex(outputNode =>
                outputNode.Semantics == region.Semantics);
            if (outputIndex < 0)
            {
                ledger = null!;
                return false;
            }

            ClassicAsyncOutputNode primary = available[outputIndex];
            available.RemoveAt(outputIndex);
            realizations.Add(new(region.Id, primary));
        }

        if (available.Count != 0)
        {
            ledger = null!;
            return false;
        }

        return ClassicAsyncRegionLedger.TryCreate(
            kickoffAddress,
            executionAddress,
            physical,
            consumed,
            preserved,
            regions,
            realizations,
            out ledger);
    }

    internal static bool TryCapturePhysicalRegions(
        IrFunction function,
        ClassicAsyncRegionHost host,
        MetadataMethodAddress method,
        out List<ClassicAsyncPhysicalRegion> regions)
    {
        regions = [];
        List<Block> blocks =
        [
            .. function.DescendantsOutsideNestedFunctions.OfType<Block>(),
        ];
        var blockEdges = new Dictionary<Block, BlockEdges>(
            ReferenceEqualityComparer.Instance);
        var predecessorCounts = new Dictionary<Block, int>(
            ReferenceEqualityComparer.Instance);
        var regionEntries = new HashSet<Block>(
            ReferenceEqualityComparer.Instance);
        var containers = new HashSet<BlockContainer>(
            ReferenceEqualityComparer.Instance);
        foreach (Block block in blocks)
        {
            if (block.Parent is BlockContainer container)
                containers.Add(container);
        }

        foreach (BlockContainer container in containers)
        {
            IReadOnlyList<Block> containerBlocks = container.Blocks;
            IReadOnlyList<BlockEdges> edges = Cfg.Build(containerBlocks);
            if (containerBlocks.Count > 0)
                regionEntries.Add(containerBlocks[0]);
            for (var i = 0; i < containerBlocks.Count; i++)
            {
                blockEdges.Add(containerBlocks[i], edges[i]);
                predecessorCounts.TryAdd(containerBlocks[i], 0);
            }
            foreach (BlockEdges edge in edges)
            {
                foreach (int successor in edge.Successors)
                {
                    if ((uint)successor < (uint)containerBlocks.Count)
                    {
                        Block target = containerBlocks[successor];
                        predecessorCounts[target]++;
                    }
                }
            }
        }

        foreach (Block block in blocks)
        {
            if (blockEdges.ContainsKey(block))
                continue;
            blockEdges.Add(block, Cfg.Build([block])[0]);
            predecessorCounts.Add(block, 0);
            regionEntries.Add(block);
        }

        ILookup<int, Block> blocksByOffset =
            blocks.ToLookup(static block => block.StartOffset);
        var externalEntryCounts = new Dictionary<Block, int>(
            ReferenceEqualityComparer.Instance);
        foreach ((Block source, BlockEdges edge) in blockEdges)
        {
            foreach (int targetOffset in edge.ExternalTargets)
            {
                foreach (Block target in blocksByOffset[targetOffset])
                {
                    if (ReferenceEquals(source, target))
                        continue;
                    externalEntryCounts[target] =
                        externalEntryCounts.GetValueOrDefault(target) + 1;
                }
            }
        }

        foreach (Block block in blocks)
        {
            BlockEdges edge = blockEdges[block];
            for (var statementIndex = 0;
                statementIndex < block.Children.Count;
                statementIndex++)
            {
                IrNode statement = block.Children[statementIndex];
                if (!TryStructuralPath(
                        function,
                        statement,
                        out string structuralPath))
                {
                    regions = [];
                    return false;
                }

                bool terminal =
                    statementIndex == block.Children.Count - 1;
                int entries = statementIndex > 0
                    ? 1
                    : predecessorCounts[block]
                        + externalEntryCounts.GetValueOrDefault(block)
                        + (regionEntries.Contains(block) ? 1 : 0);
                int successors = terminal
                    ? edge.Successors.Count
                    : 1;
                regions.Add(new(
                    new(host, method, structuralPath),
                    entries,
                    successors,
                    statementIndex == 0
                        && externalEntryCounts.ContainsKey(block),
                    terminal
                        && edge.ExternalTargets.Count > 0,
                    terminal && edge.LeavesRegion));
            }
        }
        return true;
    }

    static bool TryCaptureRegionIds(
        IrFunction function,
        ClassicAsyncRegionHost host,
        MetadataMethodAddress method,
        IEnumerable<IrNode> statementSlots,
        out List<ClassicAsyncPhysicalRegionId> regions)
    {
        regions = [];
        foreach (IrNode statement in statementSlots)
        {
            if (statement.Parent is not Block
                || !TryStructuralPath(
                    function,
                    statement,
                    out string structuralPath))
            {
                regions = [];
                return false;
            }
            regions.Add(new(host, method, structuralPath));
        }
        return true;
    }

    static bool TryCaptureUserRegions(
        IrFunction moveNext,
        MetadataMethodAddress executionAddress,
        out List<ClassicAsyncUserRegion> regions)
    {
        regions = [];
        var occurrences = new Dictionary<
            (ClassicAsyncUserRegionKind Kind, string Discriminator),
            int>();
        foreach (IrNode node
            in moveNext.DescendantsOutsideNestedFunctions)
        {
            if (!TryGetUserRegion(
                    node,
                    out ClassicAsyncUserRegionKind kind,
                    out string discriminator))
            {
                continue;
            }

            var key = (kind, discriminator);
            int occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            if (!TryStructuralPath(
                    moveNext,
                    node,
                    out string structuralPath)
                || !TryGetStatementSlot(
                    node,
                    out IrNode physicalStatement)
                || !TryStructuralPath(
                    moveNext,
                    physicalStatement,
                    out string physicalPath))
            {
                regions = [];
                return false;
            }
            regions.Add(new(
                new(
                    ClassicAsyncRegionHost.Execution,
                    structuralPath),
                new(
                    ClassicAsyncRegionHost.Execution,
                    executionAddress,
                    physicalPath),
                new(kind, discriminator, occurrence)));
        }
        return true;
    }

    static List<ClassicAsyncOutputNode> CaptureOutputNodes(
        BlockContainer output)
    {
        var nodes = new List<ClassicAsyncOutputNode>();
        var occurrences = new Dictionary<
            (ClassicAsyncUserRegionKind Kind, string Discriminator),
            int>();
        foreach (IrNode node
            in output.DescendantsOutsideNestedFunctions)
        {
            if (!TryGetUserRegion(
                    node,
                    out ClassicAsyncUserRegionKind kind,
                    out string discriminator))
            {
                continue;
            }

            var key = (kind, discriminator);
            int occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            nodes.Add(new(new(kind, discriminator, occurrence)));
        }
        return nodes;
    }

    static bool TryGetUserRegion(
        IrNode node,
        out ClassicAsyncUserRegionKind kind,
        out string discriminator)
    {
        switch (node)
        {
            case Binary binary
                when binary.IsChecked || binary.IsUnsigned:
                kind = ClassicAsyncUserRegionKind.CheckedArithmetic;
                discriminator =
                    $"{binary.Kind}|{binary.IsChecked}|{binary.IsUnsigned}";
                return true;
            case Throw { Value: CaughtException }:
                kind = ClassicAsyncUserRegionKind.Throw;
                discriminator = "rethrow";
                return true;
            case Throw:
                kind = ClassicAsyncUserRegionKind.Throw;
                discriminator = "throw";
                return true;
            case Break:
                // The execution snapshot has run the registered structuring
                // prefix. These transfers conservatively force a decline until
                // a recipe maps them; UnrealizedControlFlowRegionDeclinesAtPartialFidelity
                // gates that shipped boundary.
                kind = ClassicAsyncUserRegionKind.Break;
                discriminator = "break";
                return true;
            case Continue:
                kind = ClassicAsyncUserRegionKind.Continue;
                discriminator = "continue";
                return true;
            default:
                kind = default;
                discriminator = "";
                return false;
        }
    }

    static bool TryStructuralPath(
        IrNode root,
        IrNode node,
        out string path)
    {
        var indices = new Stack<int>();
        IrNode? current = node;
        while (!ReferenceEquals(current, root))
        {
            if (current?.Parent is null)
            {
                path = "";
                return false;
            }
            indices.Push(current.ChildIndex);
            current = current.Parent;
        }
        path = string.Join(".", indices);
        return true;
    }

    static bool TryGetStatementSlot(
        IrNode node,
        out IrNode statement)
    {
        IrNode? current = node;
        while (current is not null)
        {
            if (current.Parent is Block)
            {
                statement = current;
                return true;
            }
            current = current.Parent;
        }

        statement = null!;
        return false;
    }

    static bool HasUnconsumedExecutionStore(IrFunction moveNext)
    {
        TypeRef machine = DefinitionType(moveNext.DeclaringType);
        foreach (IrNode node in moveNext.Descendants)
        {
            switch (node)
            {
                case StoreField store
                    when !IsMachineFieldStore(store, machine):
                case StoreProperty:
                case StoreElement:
                case StoreIndirect:
                case StoreArgument:
                case CopyBlock:
                case ChainedAssignment:
                case DeconstructionAssignment:
                case EventSubscription:
                case NullCoalescingAssignment:
                case NullCoalescingFieldAssignment:
                case NullCoalescingFieldAssignmentExpression:
                case NullCoalescingPropertyAssignment:
                case InitObject init
                    when !IsMachineStorageAddress(init.Address, machine):
                case Call call
                    when IsPotentialWriteAccessor(call.Callee):
                    return true;
            }
        }

        return false;
    }

    static bool IsMachineStorageAddress(
        IrExpression address,
        TypeRef machine)
        => address is LoadFieldAddress field
            && IsMachineField(field.Field, machine)
            && IsCompilerHousekeepingField(field.Field.Name);

    static bool IsPotentialWriteAccessor(MethodRef method)
        => method.AccessorKind is
                AccessorKind.PropertySet
                or AccessorKind.EventAdd
                or AccessorKind.EventRemove
            || (method.AccessorKind == AccessorKind.Unknown
                && (HasPropertySetterSignature(method)
                    || HasEventAccessorSignature(method)));

    static bool HasPropertySetterSignature(MethodRef method)
        => method.Name.StartsWith("set_", StringComparison.Ordinal)
            && method.Name.Length > "set_".Length
            && method.ParameterTypes.Length >= 1
            && method.ReturnType is
                { Namespace: "System", Name: "Void" }
            && method.TypeArguments.IsEmpty
            && (method.HasThis
                || method.ParameterTypes.Length == 1);

    static bool HasEventAccessorSignature(MethodRef method)
    {
        int prefixLength =
            method.Name.StartsWith("add_", StringComparison.Ordinal)
                ? "add_".Length
                : method.Name.StartsWith("remove_", StringComparison.Ordinal)
                    ? "remove_".Length
                    : 0;
        return prefixLength > 0
            && method.Name.Length > prefixLength
            && method.ParameterTypes.Length == 1
            && method.ReturnType is
                { Namespace: "System", Name: "Void" }
            && method.TypeArguments.IsEmpty;
    }

    internal static bool IsMachineFieldStore(
        StoreField store,
        TypeRef machine)
        => IsMachineField(store.Field, machine);

    static bool IsMachineField(
        FieldRef field,
        TypeRef machine)
    {
        TypeRef declaringType =
            DefinitionType(field.DeclaringType);
        machine = DefinitionType(machine);
        return !declaringType.DefinitionHandle.IsNil
            && declaringType.DefinitionHandle
                == machine.DefinitionHandle
            && declaringType.DefinitionModuleVersionId is { } declaringMvid
            && machine.DefinitionModuleVersionId is { } machineMvid
            && declaringMvid == machineMvid;
    }

    static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } definition
                ? definition
                : type;

    static bool TryBuildStatements(
        IrFunction moveNext,
        IrFunction kickoff,
        LocalBuilder locals,
        out List<IrNode> statements,
        out bool hasUnconsumedStore,
        out IReadOnlyList<IrNode> consumedStatementSlots)
    {
        statements = [];
        hasUnconsumedStore = false;
        consumedStatementSlots = [];

        var setResult = FinalSetResult(moveNext);
        var getResults = GetResultCalls(moveNext);
        if (setResult is null)
            return false;

        if (TryBuildTryFinally(
                moveNext,
                kickoff,
                setResult,
                getResults,
                out var tryFinally,
                out hasUnconsumedStore,
                out var tryFinallyOwnership))
        {
            if (!hasUnconsumedStore)
            {
                statements.Add(tryFinally);
                consumedStatementSlots =
                    tryFinallyOwnership.StatementSlots;
            }
            return true;
        }

        if (TryBuildLoop(
                moveNext,
                kickoff,
                setResult,
                locals,
                out var loopStatements,
                out hasUnconsumedStore,
                out var loopOwnership))
        {
            if (!hasUnconsumedStore)
            {
                statements.AddRange(loopStatements);
                consumedStatementSlots =
                    loopOwnership.StatementSlots;
            }
            return true;
        }

        if (TryBuildConditional(
                moveNext,
                kickoff,
                setResult,
                getResults,
                out var conditionalReturn,
                out hasUnconsumedStore,
                out var conditionalOwnership))
        {
            if (!hasUnconsumedStore)
            {
                statements.Add(conditionalReturn);
                consumedStatementSlots =
                    conditionalOwnership.StatementSlots;
            }
            return true;
        }

        if (TryBuildSequentialVoid(
                moveNext,
                kickoff,
                setResult,
                getResults,
                locals,
                out var sequential,
                out hasUnconsumedStore,
                out var sequentialOwnership))
        {
            if (!hasUnconsumedStore)
            {
                statements.AddRange(sequential);
                consumedStatementSlots =
                    sequentialOwnership.StatementSlots;
            }
            return true;
        }

        if (TryBuildSingleAwaitVoid(
                moveNext,
                kickoff,
                setResult,
                getResults,
                out var voidStatements,
                out var voidOwnership))
        {
            statements.AddRange(voidStatements);
            consumedStatementSlots = voidOwnership.StatementSlots;
            return true;
        }

        if (TryBuildSingleAwaitReturn(
                moveNext,
                kickoff,
                setResult,
                getResults,
                out var ret,
                out var returnOwnership))
        {
            statements.Add(ret);
            consumedStatementSlots = returnOwnership.StatementSlots;
            return true;
        }

        return false;
    }

    static Call? FinalSetResult(IrFunction moveNext)
        => moveNext.Descendants.OfType<Call>()
            .LastOrDefault(static call => call.Callee.Name == "SetResult" && IsAsyncMethodBuilder(call.Callee.DeclaringType));

    static List<Call> GetResultCalls(IrFunction moveNext)
        => [.. moveNext.Descendants.OfType<Call>().Where(static call => call.Callee.Name == "GetResult")];

    static bool TryBuildSingleAwaitReturn(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out Return ret,
        out RecipeOwnership ownership)
    {
        ret = null!;
        ownership = new();
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1)
        {
            return false;
        }
        if (HasUnexpectedExpressionStatement(moveNext) || HasHoistedUserState(moveNext))
            return false;

        var store = moveNext.Descendants.OfType<StoreLocal>()
            .LastOrDefault(s => s.Index == result.Index && ContainsNode(s.Value, getResults[0]));
        if (store is null)
            return false;
        if (HasUnexpectedStore(moveNext, store))
            return false;

        var value = CloneWithAwaitsAndRemap(store.Value, moveNext, kickoff);
        if (value is null)
            return false;

        if (!ownership.Claim(setResult, store)
            || !TryClaimAwaitSource(
                moveNext,
                getResults[0],
                ownership))
        {
            return false;
        }
        ret = new Return(value);
        return true;
    }

    static bool TryBuildSingleAwaitVoid(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out List<IrNode> statements,
        out RecipeOwnership ownership)
    {
        statements = [];
        ownership = new();
        if (setResult.Arguments.Count != 1 || getResults.Count != 1)
            return false;
        if (HasHoistedUserState(moveNext))
            return false;

        var awaited = AwaitForGetResult(moveNext, kickoff, getResults[0]);
        if (awaited is null)
            return false;
        var getResultStatement = getResults[0].Parent as ExpressionStatement;
        if (getResultStatement is null || HasUnexpectedExpressionStatement(moveNext, getResultStatement))
            return false;
        if (HasUnexpectedStore(moveNext))
            return false;
        if (!ownership.Claim(setResult, getResultStatement)
            || !TryClaimAwaitSource(
                moveNext,
                getResults[0],
                ownership))
        {
            return false;
        }

        statements.Add(new ExpressionStatement(awaited));
        statements.Add(new Return(null));
        return true;
    }

    static bool TryBuildSequentialVoid(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        LocalBuilder locals,
        out List<IrNode> statements,
        out bool hasUnconsumedStore,
        out RecipeOwnership ownership)
    {
        statements = [];
        hasUnconsumedStore = false;
        ownership = new();
        if (setResult.Arguments.Count != 1 || getResults.Count != 2)
            return false;

        var firstResultStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store => ContainsNode(store.Value, getResults[0]));
        if (firstResultStore is null)
            return false;

        var firstStore = moveNext.Descendants.OfType<StoreField>()
            .FirstOrDefault(store => IsHoistedLocal(store.Field.Name)
                && store.Value is LoadLocal local
                && local.Index == firstResultStore.Index);
        if (firstStore is null || firstStore.Field.Type is not { } firstType)
            return false;

        var firstName = ExtractSourceName(firstStore.Field.Name);
        var firstIndex = locals.Add(firstType, firstName);
        var firstAwait = AwaitForGetResult(moveNext, kickoff, getResults[0]);
        if (firstAwait is null)
            return false;
        statements.Add(new StoreLocal(firstIndex, firstType, firstAwait));

        var secondStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store => ContainsNode(store.Value, getResults[1]));
        if (secondStore is null)
            return false;
        string? secondName =
            secondStore.Index >= 0
            && secondStore.Index < moveNext.LocalNames.Length
                ? moveNext.LocalNames[secondStore.Index]
                : null;
        var secondIndex = locals.Add(secondStore.Type, secondName);
        var secondAwait = AwaitForGetResult(moveNext, kickoff, getResults[1]);
        if (secondAwait is null)
            return false;
        statements.Add(new StoreLocal(secondIndex, secondStore.Type, secondAwait));

        var keepAlive = moveNext.Descendants.OfType<ExpressionStatement>()
            .FirstOrDefault(static statement => statement.Expression is Call { Callee.Name: "KeepAlive" });
        if (keepAlive is not { Expression: Call call })
            return false;
        if (HasUnexpectedExpressionStatement(moveNext, keepAlive))
            return false;
        if (!IsRealizedAwaitResult(firstResultStore, getResults[0])
            || !IsRealizedAwaitResult(secondStore, getResults[1]))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (HasUnexpectedStore(
                moveNext,
                firstResultStore,
                firstStore,
                secondStore))
        {
            hasUnconsumedStore = true;
            return true;
        }

        var hoisted = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal)
        {
            [firstStore.Field.Name] = (firstIndex, firstType),
        };
        var replacements = new Dictionary<int, (int Index, TypeRef Type)> { [secondStore.Index] = (secondIndex, secondStore.Type) };
        var mapped = CloneAndRemap(call, kickoff, hoisted, replacements);
        if (mapped is null)
            return false;
        if (!ownership.Claim(
                setResult,
                firstResultStore,
                firstStore,
                secondStore,
                keepAlive)
            || !TryClaimAwaitSource(
                moveNext,
                getResults[0],
                ownership)
            || !TryClaimAwaitSource(
                moveNext,
                getResults[1],
                ownership))
        {
            return false;
        }
        statements.Add(new ExpressionStatement(mapped));
        statements.Add(new Return(null));
        return true;
    }

    static bool IsRealizedAwaitResult(
        StoreLocal store,
        Call getResult)
        => ReferenceEquals(store.Value, getResult)
            || store.Value is Convert
            {
                IsChecked: false,
                Target: var target,
                Operand: var operand,
            }
            && ReferenceEquals(operand, getResult)
            && target.Equals(store.Type)
            && CSharpConversionRules.IsImplicitNumericAssignment(
                getResult.Callee.ReturnType,
                target);

    static bool TryBuildConditional(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out Return ret,
        out bool hasUnconsumedStore,
        out RecipeOwnership ownership)
    {
        ret = null!;
        hasUnconsumedStore = false;
        ownership = new();
        if (setResult.Arguments is not [_, LoadLocal result] || getResults.Count != 1)
            return false;
        if (HasUnexpectedExpressionStatement(moveNext) || HasHoistedUserState(moveNext))
            return false;

        var flag = moveNext.Descendants.OfType<LoadField>()
            .FirstOrDefault(static field => field.Field.Type is { Name: "Boolean", Namespace: "System" }
                && field.Instance is LoadArgument { Index: 0 }
                && !field.Field.Name.StartsWith("<", StringComparison.Ordinal));
        if (flag is null)
            return false;
        if (!moveNext.Descendants.OfType<ConditionalBranch>().Any(branch => ContainsEquivalentField(branch.Condition, flag.Field.Name)))
            return false;

        var tempStores = moveNext.Descendants.OfType<StoreLocal>().Where(store => store.Index != result.Index).ToList();
        var awaitStores = tempStores.Where(store => ContainsNode(store.Value, getResults[0])).ToList();
        if (awaitStores is not [var awaitStore])
            return false;
        var zeroStores = tempStores.Where(store => store.Index == awaitStore.Index
            && store.Value is Constant { Value: 0 }).ToList();
        if (zeroStores is not [var zeroStore])
            return false;
        if (zeroStore.Parent is not Block zeroBlock)
        {
            return false;
        }
        var zeroBranches = moveNext.Descendants.OfType<ConditionalBranch>()
            .Where(branch =>
                branch.TargetOffset == zeroBlock.StartOffset
                && branch.Condition is LogicalNot not
                && ContainsEquivalentField(not.Operand, flag.Field.Name))
            .ToList();
        if (zeroBranches is not [var zeroBranch])
            return false;
        if (zeroBranch.Condition is not LogicalNot { Operand: LoadField conditionField }
            || conditionField.Field.Name != flag.Field.Name
            || conditionField.Instance is not LoadArgument { Index: 0 })
        {
            hasUnconsumedStore = true;
            return true;
        }
        var finalStores = moveNext.Descendants.OfType<StoreLocal>()
            .Where(store => store.Index == result.Index
                && store.Value is LoadLocal load
                && load.Index == awaitStore.Index)
            .ToList();
        if (finalStores is not [var finalStore])
            return false;
        if (!IsRealizedAwaitResult(awaitStore, getResults[0]))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (HasUnexpectedStore(moveNext, awaitStore, zeroStore, finalStore))
            return false;

        var condition = CloneAndRemap((IrExpression)flag, kickoff);
        var awaited = AwaitForGetResult(moveNext, kickoff, getResults[0]);
        if (condition is null || awaited is null)
            return false;
        if (!ownership.Claim(
                setResult,
                flag,
                awaitStore,
                zeroStore,
                zeroBranch,
                finalStore)
            || !TryClaimAwaitSource(
                moveNext,
                getResults[0],
                ownership))
        {
            return false;
        }

        ret = new Return(new Conditional(condition, awaited, new Constant(0, TypeRef.CoreLib("System", "Int32"))));
        return true;
    }

    static bool TryBuildLoop(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        LocalBuilder locals,
        out List<IrNode> statements,
        out bool hasUnconsumedStore,
        out RecipeOwnership ownership)
    {
        statements = [];
        hasUnconsumedStore = false;
        ownership = new();
        if (setResult.Arguments is not [_, LoadLocal finalResult])
            return false;
        if (HasUnexpectedExpressionStatement(moveNext))
            return false;

        var tasksField = moveNext.Descendants.OfType<LoadField>()
            .FirstOrDefault(static field => field.Field.Name == "tasks"
                && field.Field.Type is { Kind: TypeRefKind.SzArray, ElementType: { } element }
                && IsTaskLike(element));
        if (tasksField is null || tasksField.Field.Type.ElementType is not { } taskType)
            return false;

        var getResults = GetResultCalls(moveNext).ToList();
        if (getResults is not [var getResult])
            return false;
        if (!HasField(moveNext, "<>7__wrap1")
            || !HasField(moveNext, "<>7__wrap2")
            || !HasField(moveNext, "<>7__wrap3"))
        {
            return false;
        }
        var resultStores = moveNext.Descendants.OfType<StoreLocal>()
            .Where(store => ContainsNode(store.Value, getResult))
            .ToList();
        if (resultStores is not [var resultStore])
            return false;
        var accumulatorStores = moveNext.Descendants.OfType<StoreLocal>()
            .Where(store => store.Value is Binary { Kind: BinaryKind.Add } binary
                && IsWrap3Load(binary.Left)
                && binary.Right is LoadLocal load
                && load.Index == resultStore.Index)
            .ToList();
        if (accumulatorStores is not [var accumulatorStore])
            return false;
        var awaitedOperand = AwaitedOperandForGetResult(moveNext, getResult);
        if (awaitedOperand is null
            || !IsCurrentLoopElement(moveNext, awaitedOperand))
        {
            hasUnconsumedStore = true;
            return true;
        }
        var initialAccumulatorStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store =>
                store.Index == accumulatorStore.Index
                && store.Value is Constant { Value: 0 });
        var finalResultStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store =>
                store.Index == finalResult.Index
                && store.Value is LoadLocal load
                && load.Index == accumulatorStore.Index);
        if (initialAccumulatorStore is null
            || finalResultStore is null)
        {
            return false;
        }
        var expectedLoopFieldStores = moveNext.Descendants
            .OfType<StoreField>()
            .Where(store => IsExpectedLoopFieldStore(
                store,
                accumulatorStore.Index))
            .Cast<IrNode>();
        var allowedStores = new List<IrNode>
        {
            resultStore,
            accumulatorStore,
            initialAccumulatorStore,
            finalResultStore,
        };
        allowedStores.AddRange(expectedLoopFieldStores);
        if (HasUnexpectedStore(moveNext, [.. allowedStores]))
        {
            hasUnconsumedStore = true;
            return true;
        }

        var sumType = accumulatorStore.Type;
        var accumulator = (Binary)accumulatorStore.Value;
        var sumIndex = locals.Add(sumType, "sum");
        var taskIndex = locals.Add(taskType, "task");

        statements.Add(new StoreLocal(sumIndex, sumType, new Constant(0, sumType)));
        var body = new Block(0);
        var awaited = new AwaitExpression(
            new LoadLocal(taskIndex, taskType),
            getResult.Callee.ReturnType,
            getResult.Callee.ReturnIsDynamic);
        body.Add(new StoreLocal(
            sumIndex,
            sumType,
            new Binary(
                BinaryKind.Add,
                accumulator.IsChecked,
                accumulator.IsUnsigned,
                new LoadLocal(sumIndex, sumType),
                awaited)));

        var collection = CloneAndRemap((IrExpression)tasksField, kickoff);
        if (collection is null)
            return false;
        if (!ownership.Claim(
                setResult,
                tasksField,
                resultStore,
                accumulatorStore,
                initialAccumulatorStore,
                finalResultStore)
            || !ownership.Claim(expectedLoopFieldStores)
            || !TryClaimAwaitSource(
                moveNext,
                getResult,
                ownership)
            || !TryClaimAwaitedOperandSource(
                moveNext,
                awaitedOperand,
                ownership))
        {
            return false;
        }

        statements.Add(new ForeachStatement(taskIndex, taskType, collection, body));
        statements.Add(new Return(new LoadLocal(sumIndex, sumType)));
        return true;
    }

    static bool IsCurrentLoopElement(
        IrFunction moveNext,
        IrExpression awaitedOperand)
    {
        if (awaitedOperand is not LoadStackSlot load)
            return false;

        var stores = moveNext.Descendants.OfType<StoreStackSlot>()
            .Where(store => store.Slot == load.Slot)
            .ToList();
        return stores is
        [
            {
                Value: LoadElement
                {
                    Array: LoadField
                    {
                        Field.Name: "<>7__wrap1",
                        Instance: LoadArgument { Index: 0 },
                    },
                    Index: LoadField
                    {
                        Field.Name: "<>7__wrap2",
                        Instance: LoadArgument { Index: 0 },
                    },
                },
            },
        ];
    }

    static bool IsExpectedLoopFieldStore(
        StoreField store,
        int accumulatorIndex)
        => store switch
        {
            { Field.Name: "<>7__wrap1", Value: LoadField { Field.Name: "tasks" } }
                or { Field.Name: "<>7__wrap1", Value: Constant { Value: null } }
                or { Field.Name: "<>7__wrap2", Value: Constant { Value: 0 } }
                or
                {
                    Field.Name: "<>7__wrap2",
                    Value: Binary
                    {
                        Kind: BinaryKind.Add,
                        Left: LoadField { Field.Name: "<>7__wrap2" },
                        Right: Constant { Value: 1 },
                    },
                } => true,
            {
                Field.Name: "<>7__wrap3",
                Value: LoadLocal load,
            } => load.Index == accumulatorIndex,
            _ => false,
        };

    static bool TryBuildTryFinally(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out TryFinally tryFinally,
        out bool hasUnconsumedStore,
        out RecipeOwnership ownership)
    {
        tryFinally = null!;
        hasUnconsumedStore = false;
        ownership = new();
        if (setResult.Arguments is not [_, LoadLocal result] || getResults.Count != 1)
            return false;

        var originalTryFinally = moveNext.Descendants.OfType<TryFinally>().FirstOrDefault();
        if (originalTryFinally is null)
            return false;

        var resultStore = originalTryFinally.TryBody.Descendants.OfType<StoreLocal>()
            .LastOrDefault(store => store.Index == result.Index && ContainsNode(store.Value, getResults[0]));
        if (resultStore is null)
            return false;
        var resultValue = CloneWithAwaitsAndRemap(resultStore.Value, moveNext, kickoff);
        if (resultValue is null)
            return false;

        var finallyGuards = originalTryFinally.FinallyBody.Blocks
            .SelectMany(block => block.Children)
            .OfType<IfStatement>()
            .ToList();
        if (finallyGuards is not [var finallyGuard]
            || finallyGuard.Then.Children is not [ExpressionStatement finallyStatement])
        {
            return false;
        }
        if (!IsCompilerFinallyStateGuard(moveNext, finallyGuard.Condition))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (HasUnexpectedExpressionStatement(moveNext, finallyStatement))
            return false;

        var mappedFinally = CloneAndRemap(finallyStatement, kickoff);
        if (mappedFinally is null)
            return false;
        if (HasUnexpectedStore(moveNext, resultStore))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (!ownership.Claim(
                setResult,
                originalTryFinally,
                resultStore,
                finallyGuard,
                finallyStatement)
            || !TryClaimAwaitSource(
                moveNext,
                getResults[0],
                ownership))
        {
            return false;
        }

        tryFinally = new TryFinally(
            Container(new Return(resultValue)),
            Container(mappedFinally));
        return true;
    }

    static bool IsCompilerFinallyStateGuard(
        IrFunction moveNext,
        IrExpression condition)
    {
        var stateLocal = StateLocalIndex(moveNext);
        return stateLocal is { } state
            && condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadLocal load,
                Right: Constant { Value: 0 },
            }
            && load.Index == state;
    }

    static AwaitExpression? AwaitForGetResult(IrFunction moveNext, IrFunction kickoff, Call getResult)
    {
        var awaitedOperand = AwaitedOperandForGetResult(moveNext, getResult);
        if (awaitedOperand is null)
            return null;

        var operand = CloneAndRemap(awaitedOperand, kickoff);
        return operand is null
            ? null
            : new AwaitExpression(
                operand,
                getResult.Callee.ReturnType,
                getResult.Callee.ReturnIsDynamic);
    }

    static IrExpression? AwaitedOperandForGetResult(
        IrFunction moveNext,
        Call getResult)
        => TryGetAwaitSource(
            moveNext,
            getResult,
            out _,
            out IrExpression awaitedOperand)
                ? awaitedOperand
                : null;

    static bool TryGetAwaitSource(
        IrFunction moveNext,
        Call getResult,
        out StoreLocal awaiterStore,
        out IrExpression awaitedOperand)
    {
        awaiterStore = null!;
        awaitedOperand = null!;
        if (getResult.Arguments is not [LoadLocalAddress awaiterAddress])
            return false;

        var nodes = moveNext.Descendants.ToList();
        var getResultPosition = nodes.IndexOf(getResult);
        if (getResultPosition < 0)
            return false;

        StoreLocal? candidateStore = null;
        for (var i = 0; i < getResultPosition; i++)
        {
            if (nodes[i] is StoreLocal { Index: var index, Value: Call { Callee.Name: "GetAwaiter" } call } store
                && index == awaiterAddress.Index
                && call.Arguments.Count == 1)
            {
                if (store.Value is Call { Arguments: [LoadField { Field.Name: var maybeAwaiterField }] }
                    && maybeAwaiterField.StartsWith("<>u__", StringComparison.Ordinal))
                {
                    continue;
                }
                candidateStore = store;
            }
        }

        if (candidateStore?.Value is not Call
            {
                Arguments: [var candidateOperand],
            })
        {
            return false;
        }

        awaiterStore = candidateStore;
        awaitedOperand = candidateOperand;
        return true;
    }

    static bool TryClaimAwaitSource(
        IrFunction moveNext,
        Call getResult,
        RecipeOwnership ownership)
    {
        if (!TryGetAwaitSource(
                moveNext,
                getResult,
                out StoreLocal awaiterStore,
                out IrExpression awaitedOperand)
            || !ownership.Claim(awaiterStore))
        {
            return false;
        }

        return TryClaimAwaitedOperandSource(
            moveNext,
            awaitedOperand,
            ownership);
    }

    static bool TryClaimAwaitedOperandSource(
        IrFunction moveNext,
        IrExpression awaitedOperand,
        RecipeOwnership ownership)
    {
        if (awaitedOperand is not LoadStackSlot load)
            return true;

        List<StoreStackSlot> stores =
        [
            .. moveNext.Descendants
                .OfType<StoreStackSlot>()
                .Where(store => store.Slot == load.Slot),
        ];
        return stores.Count == 1
            && ownership.Claim(stores[0]);
    }

    static bool HasUnexpectedExpressionStatement(IrFunction moveNext, params ExpressionStatement[] allowed)
    {
        var allowedSet = allowed.ToHashSet();
        foreach (var statement in moveNext.Descendants.OfType<ExpressionStatement>())
        {
            if (allowedSet.Contains(statement))
                continue;
            if (statement.Expression is Call call
                && IsCompilerBuilderCallback(moveNext, call))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    static bool IsCompilerBuilderCallback(
        IrFunction moveNext,
        Call call)
    {
        if (call.Callee.Name is not ("AwaitUnsafeOnCompleted" or "SetException" or "SetResult")
            || !IsAsyncMethodBuilder(call.Callee.DeclaringType)
            || call.Arguments.Count == 0
            || call.Arguments[0] is not LoadFieldAddress
            {
                Field: var field,
                Instance: LoadArgument { Index: 0 },
            }
            || field.Name != "<>t__builder")
        {
            return false;
        }

        return IsMachineField(
            field,
            DefinitionType(moveNext.DeclaringType));
    }

    static bool HasUnexpectedStore(IrFunction moveNext, params IrNode[] allowed)
    {
        var allowedSet = allowed.ToHashSet();
        var stateLocal = StateLocalIndex(moveNext);

        foreach (var node in moveNext.Descendants)
        {
            switch (node)
            {
                case StoreField store:
                    if (allowedSet.Contains(store)
                        || IsCompilerHousekeepingField(store.Field.Name))
                    {
                        continue;
                    }
                    return true;
                case StoreProperty or StoreElement or StoreIndirect or StoreArgument:
                    if (!allowedSet.Contains(node))
                        return true;
                    break;
                case InitObject init:
                    if (allowedSet.Contains(init)
                        || init.Address is LoadFieldAddress
                        {
                            Field.Name: var initFieldName,
                        }
                        && IsCompilerHousekeepingField(initFieldName))
                    {
                        continue;
                    }
                    return true;
                case StoreLocal store:
                    if (allowedSet.Contains(store))
                        continue;
                    if (stateLocal is { } state && store.Index == state
                        && store.Value is Constant or LoadStackSlot or LoadField { Field.Name: "<>1__state" })
                        continue;
                    if (store.Value is Call { Callee.Name: "GetAwaiter" }
                        || store.Value is LoadField { Field.Name: var fieldName } && fieldName.StartsWith("<>u__", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    return true;
            }
        }
        return false;
    }

    static int? StateLocalIndex(IrFunction moveNext)
        => moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(static store =>
                store.Value is LoadField { Field.Name: "<>1__state" })
            ?.Index;

    static bool IsCompilerHousekeepingField(string name)
        => name is "<>1__state" or "<>t__builder" or "<>4__this"
            || name.StartsWith("<>u__", StringComparison.Ordinal);

    static bool HasHoistedUserState(IrFunction moveNext)
        => moveNext.Descendants.OfType<StoreField>().Any(static store =>
            IsHoistedLocal(store.Field.Name) || store.Field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal));

    static bool HasField(IrFunction moveNext, string name)
        => moveNext.Descendants.Any(node => node switch
        {
            LoadField { Field.Name: var fieldName } => fieldName == name,
            StoreField { Field.Name: var fieldName } => fieldName == name,
            LoadFieldAddress { Field.Name: var fieldName } => fieldName == name,
            _ => false,
        });

    static bool ContainsEquivalentField(IrNode node, string fieldName)
        => node.Descendants.Prepend(node).Any(candidate =>
            candidate is LoadField { Field.Name: var name, Instance: LoadArgument { Index: 0 } } && name == fieldName);

    static bool IsWrap3Load(IrExpression expression)
        => expression is LoadField { Field.Name: "<>7__wrap3", Instance: LoadArgument { Index: 0 } };

    static IrExpression? CloneWithAwaitsAndRemap(IrExpression expression, IrFunction moveNext, IrFunction kickoff)
    {
        var clone = (IrExpression)expression.Clone();
        var originalGetResults = expression.Descendants.Prepend(expression).OfType<Call>()
            .Where(static call => call.Callee.Name == "GetResult")
            .ToList();
        var clonedGetResults = clone.Descendants.Prepend(clone).OfType<Call>()
            .Where(static call => call.Callee.Name == "GetResult")
            .ToList();
        if (originalGetResults.Count != clonedGetResults.Count)
            return null;

        IrExpression? rootReplacement = null;
        for (var i = 0; i < originalGetResults.Count; i++)
        {
            var awaited = AwaitForGetResult(moveNext, kickoff, originalGetResults[i]);
            if (awaited is null)
                return null;
            if (ReferenceEquals(clonedGetResults[i], clone))
                rootReplacement = awaited;
            else
                clonedGetResults[i].ReplaceWith(awaited);
        }

        var result = rootReplacement ?? clone;
        return RemapInPlace(result, kickoff) ? result : null;
    }

    static IrExpression? CloneAndRemap(IrExpression expression, IrFunction kickoff)
    {
        var clone = (IrExpression)expression.Clone();
        if (clone is LoadField { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var field }
            && TryGetParameter(kickoff, field.Name, out var argIndex, out var parameter))
        {
            return ParameterLoad(argIndex, parameter);
        }
        if (clone is LoadFieldAddress { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var addressField }
            && TryGetParameter(kickoff, addressField.Name, out var addressArgIndex, out var addressParameter))
        {
            return ParameterLoad(addressArgIndex, addressParameter);
        }

        return RemapInPlace(clone, kickoff) ? clone : null;
    }

    static T? CloneAndRemap<T>(T node, IrFunction kickoff) where T : IrNode
    {
        var clone = (T)node.Clone();
        return RemapInPlace(clone, kickoff) ? clone : null;
    }

    static Call? CloneAndRemap(
        Call call,
        IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted,
        IReadOnlyDictionary<int, (int Index, TypeRef Type)> locals)
    {
        var clone = (Call)call.Clone();
        return RemapInPlace(clone, kickoff, hoisted, locals) ? clone : null;
    }

    static bool RemapInPlace(
        IrNode node,
        IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)>? hoisted = null,
        IReadOnlyDictionary<int, (int Index, TypeRef Type)>? localReplacements = null)
    {
        var swaps = new List<(IrNode Old, IrNode New)>();
        var ok = true;
        Visit(node);
        if (!ok)
            return false;

        foreach (var (old, replacement) in swaps)
        {
            if (ReferenceEquals(old, node))
                return false;
            old.ReplaceWith(replacement);
        }
        foreach (var load in node.Descendants.Prepend(node).OfType<LoadElement>())
            load.ResultIsDynamic = IrImporter.ArrayElementDynamicFact(load.Array);
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            switch (current)
            {
                case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (hoisted is not null && hoisted.TryGetValue(field.Name, out var local))
                    {
                        swaps.Add((current, new LoadLocal(local.Index, local.Type)));
                    }
                    else if (TryGetParameter(kickoff, field.Name, out var argIndex, out var parameter))
                    {
                        swaps.Add((current, ParameterLoad(argIndex, parameter)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadFieldAddress { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (TryGetParameter(kickoff, field.Name, out var addressArgIndex, out var addressParameter))
                    {
                        swaps.Add((current, ParameterLoad(addressArgIndex, addressParameter)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadLocal load when localReplacements is not null && localReplacements.TryGetValue(load.Index, out var replacement):
                    swaps.Add((current, new LoadLocal(replacement.Index, replacement.Type)));
                    return;
                case LoadArgument { Index: 0, Name: "this" }:
                    ok = false;
                    return;
            }

            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static LoadArgument ParameterLoad(int index, Parameter parameter)
        => new(index, parameter.Name, parameter.Type)
        {
            IsDynamic = parameter.IsDynamic,
            ArrayElementIsDynamic = parameter.ArrayElementIsDynamic,
        };

    static bool TryGetParameter(IrFunction kickoff, string fieldName, out int index, out Parameter parameter)
    {
        var argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (var i = 0; i < kickoff.Signature.Parameters.Length; i++)
        {
            var candidate = kickoff.Signature.Parameters[i];
            if (candidate.Name == fieldName)
            {
                index = argumentBase + i;
                parameter = candidate;
                return true;
            }
        }

        index = -1;
        parameter = null!;
        return false;
    }

    static bool ContainsNode(IrNode root, IrNode target)
        => ReferenceEquals(root, target) || root.Descendants.Any(descendant => ReferenceEquals(descendant, target));

    static bool IsAsyncMethodBuilder(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is
        {
            Namespace: "System.Runtime.CompilerServices",
            Name: "AsyncTaskMethodBuilder" or "AsyncTaskMethodBuilder`1" or "AsyncValueTaskMethodBuilder`1",
        };
    }

    static bool IsTaskLike(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is
        {
            Namespace: "System.Threading.Tasks",
            Name: "Task" or "Task`1" or "ValueTask" or "ValueTask`1",
        };
    }

    static bool IsHoistedLocal(string fieldName)
        => fieldName.StartsWith("<", StringComparison.Ordinal)
            && !fieldName.StartsWith("<>", StringComparison.Ordinal)
            && fieldName.Contains(">5__", StringComparison.Ordinal);

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "value";
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        container.Add(block);
        return container;
    }

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }
}
