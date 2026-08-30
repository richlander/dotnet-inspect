using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;

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
            if (rejected.Failure.Kind
                == StateMachineRelationshipFailureKind.BudgetExceeded)
            {
                return new ClassicAsyncPreparationResult.InputUnavailable(
                    rejected.Failure);
            }
            if (evidence.HostRole
                == ClassicAsyncHostRole.DeclaredKickoff)
            {
                return new ClassicAsyncPreparationResult.Decided(
                    new ClassicAsyncDecision.Decline(
                        ClassicAsyncDeclineReason.RejectedRelationship,
                        ClassicAsyncKickoffDisposition.PreservedOriginal));
            }
            if (rejected.Failure.Claims.Length > 0)
            {
                return new ClassicAsyncPreparationResult.NotApplicable(
                    evidence.HostRole,
                    evidence.Classification);
            }
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
                resolved.Relationship.StateMachineType,
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
            kickoff.ParameterBindings,
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
            ClassicAsyncDeclineReason.RejectedRelationship
                => "owner rejected the classic state-machine relationship",
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

    internal sealed record Kickoff(
        TypeRef StateMachineType,
        int StateMachineLocal,
        int SourceOffset,
        bool IsNarrow,
        ClassicAsyncStorage StateStorage,
        ClassicAsyncStorage BuilderStorage,
        ClassicAsyncParameterBindingSet ParameterBindings,
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

    internal static bool TryGetKickoff(
        IrFunction function,
        MetadataTypeDefinitionAddress expectedStateMachineAddress,
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
                != expectedStateMachine
            || !IsExactDefinition(
                stateMachineDefinition,
                expectedStateMachineAddress))
        {
            declineReason =
                ClassicAsyncDeclineReason.KickoffMachineMismatch;
            return false;
        }

        narrowHandoff = TryGetNarrowKickoffBindings(
            function,
            block,
            stateMachineType,
            stateMachineAddress.Index,
            out ClassicAsyncParameterBindingSet parameterBindings);
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
                StateMachineFieldType(
                    builderStore.Field.Type,
                    stateMachineType)),
            parameterBindings,
            narrowHandoff
                ? [.. block.Children]
                : []);
        return true;
    }

    static bool IsExactDefinition(
        TypeRef type,
        MetadataTypeDefinitionAddress expected)
    {
        TypeRef definition = DefinitionType(type);
        return definition.DefinitionModuleVersionId
                == expected.ModuleVersionId
            && !definition.DefinitionHandle.IsNil
            && MetadataTokens.GetToken(definition.DefinitionHandle)
                == expected.Definition.Value;
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

    static bool TryGetNarrowKickoffBindings(
        IrFunction function,
        Block block,
        TypeRef stateMachineType,
        int stateMachineLocal,
        out ClassicAsyncParameterBindingSet parameterBindings)
    {
        int builderCreates = 0;
        int stateInitializations = 0;
        int starts = 0;
        int returns = 0;
        var copiedArguments = new HashSet<int>();
        var copiedFields = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new List<ClassicAsyncParameterBinding>();
        TypeRef? builderType = null;

        foreach (IrNode statement in block.Children)
        {
            switch (statement)
            {
                case StoreField
                {
                    Field.Name: "<>t__builder",
                    Instance: LoadLocalAddress builderTarget,
                    Value: Call { Callee.Name: "Create" } create,
                } builderStore
                    when builderTarget.Index == stateMachineLocal
                    && IsMachineField(
                        builderStore.Field,
                        stateMachineType)
                    && TryAuthenticateBuilderStorage(
                        StateMachineFieldType(
                            builderStore.Field.Type,
                            stateMachineType),
                        function.Signature.ReturnType,
                        out builderType)
                    && IsExactBuilderCreate(create, builderType):
                    builderCreates++;
                    break;

                case StoreField
                {
                    Field.Name: "<>1__state",
                    Instance: LoadLocalAddress stateTarget,
                    Value: Constant { Value: -1 },
                } stateStore
                    when stateTarget.Index == stateMachineLocal
                    && IsMachineField(
                        stateStore.Field,
                        stateMachineType):
                    stateInitializations++;
                    break;

                case StoreField
                {
                    Field: var targetField,
                    Instance: LoadLocalAddress copyTarget,
                    Value: LoadArgument argument,
                } when copyTarget.Index == stateMachineLocal
                    && IsMachineField(targetField, stateMachineType)
                    && TryCreateParameterBinding(
                        function,
                        stateMachineType,
                        targetField,
                        argument,
                        out ClassicAsyncParameterBinding binding)
                    && copiedArguments.Add(binding.ArgumentIndex)
                    && copiedFields.Add(binding.FieldName):
                    bindings.Add(binding);
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
                                Field: var builderField,
                                Instance: LoadLocalAddress builderOwner,
                            },
                            LoadLocalAddress machine,
                        ],
                    } start,
                } when builderOwner.Index == stateMachineLocal
                    && machine.Index == stateMachineLocal
                    && builderField.Name == "<>t__builder"
                    && IsMachineField(builderField, stateMachineType)
                    && builderType is not null
                    && SameExactType(
                        StateMachineFieldType(
                            builderField.Type,
                            stateMachineType),
                        builderType)
                    && IsExactBuilderStart(
                        start,
                        builderType,
                        stateMachineType,
                        machine.Type):
                    starts++;
                    break;

                case Return
                {
                    Value: LoadProperty
                    {
                        PropertyName: "Task",
                        Instance: LoadFieldAddress
                        {
                            Field: var builderField,
                            Instance: LoadLocalAddress builderOwner,
                        },
                        IndexArguments.Count: 0,
                    } task,
                } when builderOwner.Index == stateMachineLocal
                    && builderField.Name == "<>t__builder"
                    && IsMachineField(builderField, stateMachineType)
                    && builderType is not null
                    && SameExactType(
                        StateMachineFieldType(
                            builderField.Type,
                            stateMachineType),
                        builderType)
                    && IsExactBuilderTaskAccessor(
                        task,
                        builderType,
                        function.Signature.ReturnType):
                    returns++;
                    break;

                case Return { Value: null }
                    when builderType is not null
                    && IsAsyncVoidBuilder(builderType)
                    && IsVoid(function.Signature.ReturnType):
                    returns++;
                    break;

                default:
                    parameterBindings =
                        ClassicAsyncParameterBindingSet.Create([]);
                    return false;
            }
        }

        bool isNarrow = builderCreates == 1
            && stateInitializations == 1
            && starts == 1
            && returns == 1;
        parameterBindings = ClassicAsyncParameterBindingSet.Create(
            isNarrow
                ? bindings
                : []);
        return isNarrow;
    }

    static bool TryAuthenticateBuilderStorage(
        TypeRef builderType,
        TypeRef declaredReturnType,
        out TypeRef authenticatedBuilder)
    {
        authenticatedBuilder = null!;
        TypeRef definition = DefinitionType(builderType);
        if (definition.Assembly != TypeRef.CoreLibrary
            || definition.Namespace
                != "System.Runtime.CompilerServices")
        {
            return false;
        }

        bool matchesReturn = builderType switch
        {
            {
                Kind: TypeRefKind.Definition,
                Name: "AsyncTaskMethodBuilder",
            } => IsTaskType(declaredReturnType, "Task"),
            {
                Kind: TypeRefKind.Definition,
                Name: "AsyncValueTaskMethodBuilder",
            } => IsTaskType(declaredReturnType, "ValueTask"),
            {
                Kind: TypeRefKind.Definition,
                Name: "AsyncVoidMethodBuilder",
            } => IsVoid(declaredReturnType),
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType.Name: "AsyncTaskMethodBuilder`1",
                TypeArguments: [var result],
            } => IsTaskType(
                declaredReturnType,
                "Task`1",
                result),
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType.Name:
                    "AsyncValueTaskMethodBuilder`1",
                TypeArguments: [var result],
            } => IsTaskType(
                declaredReturnType,
                "ValueTask`1",
                result),
            _ => false,
        };
        if (!matchesReturn)
            return false;

        authenticatedBuilder = builderType;
        return true;
    }

    static bool IsTaskType(
        TypeRef type,
        string name,
        TypeRef? resultType = null)
    {
        TypeRef definition = DefinitionType(type);
        if (definition.Assembly != TypeRef.CoreLibrary
            || definition.Namespace != "System.Threading.Tasks"
            || definition.Name != name)
        {
            return false;
        }

        return resultType is null
            ? type.Kind == TypeRefKind.Definition
            : type is
                {
                    Kind: TypeRefKind.GenericInstance,
                    TypeArguments: [var actualResult],
                }
                && SameExactType(actualResult, resultType);
    }

    static bool IsAsyncVoidBuilder(TypeRef builderType)
        => builderType is
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary,
                Namespace: "System.Runtime.CompilerServices",
                Name: "AsyncVoidMethodBuilder",
            };

    static bool IsExactBuilderCreate(
        Call create,
        TypeRef builderType)
        => !create.IsVirtual
            && !create.Callee.HasThis
            && create.Callee.Name == "Create"
            && SameExactType(
                create.Callee.DeclaringType,
                builderType)
            && HasExternalMemberReferenceProvenance(create.Callee)
            && SameExactType(create.Callee.ReturnType, builderType)
            && create.Callee.ParameterTypes.IsEmpty
            && create.Callee.TypeArguments.IsEmpty
            && create.Callee.DefinitionParameterTypes.IsEmpty
            && create.Callee.DefinitionReturnType is null
            && create.Arguments.Count == 0;

    static bool IsExactBuilderStart(
        Call start,
        TypeRef builderType,
        TypeRef stateMachineType,
        TypeRef addressedMachineType)
        => !start.IsVirtual
            && start.Callee.HasThis
            && start.Callee.Name == "Start"
            && SameExactType(
                start.Callee.DeclaringType,
                builderType)
            && HasExternalMemberReferenceProvenance(start.Callee)
            && SameExactType(
                start.Callee.ReturnType,
                TypeRef.CoreLib("System", "Void"))
            && start.Callee.ParameterTypes is
                [var parameterMachine]
            && SameExactType(
                parameterMachine,
                TypeRef.ByRef(stateMachineType))
            && start.Callee.TypeArguments is
                [var methodMachine]
            && SameExactType(methodMachine, stateMachineType)
            && start.Callee.DefinitionParameterTypes is
                [var definitionMachine]
            && SameExactType(
                definitionMachine,
                TypeRef.ByRef(
                    TypeRef.MethodGenericParameter(0)))
            && start.Callee.DefinitionReturnType is { } definitionReturn
            && SameExactType(
                definitionReturn,
                TypeRef.CoreLib("System", "Void"))
            && SameExactType(
                addressedMachineType,
                stateMachineType);

    static bool IsExactBuilderTaskAccessor(
        LoadProperty task,
        TypeRef builderType,
        TypeRef declaredReturnType)
        => !task.IsVirtual
            && task.Accessor.Name == "get_Task"
            && task.Accessor.AccessorKind
                == AccessorKind.PropertyGet
            && task.Accessor.HasThis
            && SameExactType(
                task.Accessor.DeclaringType,
                builderType)
            && HasExternalMemberReferenceProvenance(task.Accessor)
            && SameExactType(
                task.Accessor.ReturnType,
                declaredReturnType)
            && task.Accessor.ParameterTypes.IsEmpty
            && task.Accessor.TypeArguments.IsEmpty
            && task.Accessor.DefinitionParameterTypes.IsEmpty
            && task.Accessor.DefinitionReturnType is null;

    static bool HasExternalMemberReferenceProvenance(MethodRef method)
        => method.ExactDefinitionAddress is null
            && method.ExactDefinitionAcquisitionGuard is null;

    internal static bool SameExactType(TypeRef left, TypeRef right)
    {
        if (!left.Equals(right)
            || left.Kind != right.Kind
            || left.ResolutionAssembly != right.ResolutionAssembly
            || left.DefinitionName != right.DefinitionName
            || left.DefinitionHandle != right.DefinitionHandle
            || left.DefinitionModuleVersionId
                != right.DefinitionModuleVersionId
            || left.CustomModifiers.Length
                != right.CustomModifiers.Length)
        {
            return false;
        }

        for (var i = 0; i < left.CustomModifiers.Length; i++)
        {
            TypeRefCustomModifier leftModifier =
                left.CustomModifiers[i];
            TypeRefCustomModifier rightModifier =
                right.CustomModifiers[i];
            if (leftModifier.IsRequired
                    != rightModifier.IsRequired
                || !SameExactType(
                    leftModifier.Modifier,
                    rightModifier.Modifier))
            {
                return false;
            }
        }
        if (left.ElementType is null != (right.ElementType is null)
            || left.ElementType is not null
                && !SameExactType(
                    left.ElementType,
                    right.ElementType!))
        {
            return false;
        }
        if (left.TypeArguments.Length
            != right.TypeArguments.Length)
        {
            return false;
        }
        for (var i = 0; i < left.TypeArguments.Length; i++)
        {
            if (!SameExactType(
                    left.TypeArguments[i],
                    right.TypeArguments[i]))
            {
                return false;
            }
        }
        return true;
    }

    static bool IsVoid(TypeRef type)
        => type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System",
            Name: "Void",
        };

    static bool TryCreateParameterBinding(
        IrFunction function,
        TypeRef stateMachineType,
        FieldRef targetField,
        LoadArgument argument,
        out ClassicAsyncParameterBinding binding)
    {
        TypeRef fieldType = StateMachineFieldType(
            targetField.Type,
            stateMachineType);
        int parameterIndex = function.Signature.HasThis
            ? argument.Index - 1
            : argument.Index;
        if (parameterIndex == -1)
        {
            if (targetField.Name != "<>4__this"
                || argument.Name != "this"
                || !argument.Type.Equals(function.DeclaringType)
                || !fieldType.Equals(function.DeclaringType))
            {
                binding = null!;
                return false;
            }

            binding = new(
                targetField.Name,
                fieldType,
                argument.Index,
                argument.Name,
                argument.Type,
                argument.IsDynamic,
                argument.ArrayElementIsDynamic);
            return true;
        }

        if (parameterIndex < 0
            || parameterIndex >= function.Signature.Parameters.Length)
        {
            binding = null!;
            return false;
        }

        Parameter parameter =
            function.Signature.Parameters[parameterIndex];
        if (targetField.Name != parameter.Name
            || argument.Name != parameter.Name
            || !argument.Type.Equals(parameter.Type)
            || !fieldType.Equals(parameter.Type)
            || argument.IsDynamic != parameter.IsDynamic
            || argument.ArrayElementIsDynamic
                != parameter.ArrayElementIsDynamic)
        {
            binding = null!;
            return false;
        }

        binding = new(
            targetField.Name,
            fieldType,
            argument.Index,
            parameter.Name,
            parameter.Type,
            parameter.IsDynamic,
            parameter.ArrayElementIsDynamic);
        return true;
    }

    static TypeRef StateMachineFieldType(
        TypeRef fieldType,
        TypeRef stateMachineType)
        => fieldType.Instantiate(
            stateMachineType.Kind == TypeRefKind.GenericInstance
                ? stateMachineType.TypeArguments
                : [],
            []);

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
                kickoffModel,
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
                kickoffModel,
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
        Kickoff kickoffModel,
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
                kickoffModel,
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
        if (!TryCaptureOutputNodes(output, out var outputs))
        {
            ledger = null!;
            return false;
        }
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

    internal static bool TryCaptureRegionIds(
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
        Kickoff kickoffModel,
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

        var awaitOccurrence = 0;
        foreach (Call getResult in GetResultCalls(moveNext))
        {
            if (!TryGetAwaitedOperandRegion(
                    moveNext,
                    kickoffModel,
                    getResult,
                    out IrNode source,
                    out string discriminator)
                || !TryAddUserRegion(
                    moveNext,
                    executionAddress,
                    source,
                    ClassicAsyncUserRegionKind.AwaitedOperand,
                    discriminator,
                    awaitOccurrence,
                    regions))
            {
                regions = [];
                return false;
            }
            awaitOccurrence++;
        }

        var predicateOccurrence = 0;
        foreach (IrNode node
            in moveNext.DescendantsOutsideNestedFunctions)
        {
            IrExpression? condition = node switch
            {
                ConditionalBranch branch => branch.Condition,
                IfStatement statement => statement.Condition,
                _ => null,
            };
            if (condition is null
                || !TryGetInputPredicateDiscriminator(
                    condition,
                    kickoffModel,
                    out IrExpression source,
                    out string discriminator))
            {
                continue;
            }

            if (!TryAddUserRegion(
                    moveNext,
                    executionAddress,
                    source,
                    ClassicAsyncUserRegionKind.Predicate,
                    discriminator,
                    predicateOccurrence,
                    regions))
            {
                regions = [];
                return false;
            }
            predicateOccurrence++;
        }

        var guardedEffectOccurrence = 0;
        foreach (IfStatement guard
            in moveNext.DescendantsOutsideNestedFunctions
                .OfType<IfStatement>())
        {
            if (!IsInFinallyBody(guard)
                || !IsCompilerFinallyStateGuard(
                    moveNext,
                    guard))
            {
                continue;
            }
            if (guard.Then.Children is not
                [
                    ExpressionStatement
                    {
                        Expression: Call effect,
                    },
                ])
            {
                regions = [];
                return false;
            }

            Call? normalized = CloneAndRemap<Call>(
                effect,
                kickoffModel);
            if (normalized is null
                || !TryGetSemanticExpressionKey(
                    normalized,
                    out string effectKey)
                || !TryAddUserRegion(
                    moveNext,
                    executionAddress,
                    effect,
                    ClassicAsyncUserRegionKind.GuardedEffect,
                    SemanticParts("finally", effectKey),
                    guardedEffectOccurrence,
                    regions))
            {
                regions = [];
                return false;
            }
            guardedEffectOccurrence++;
        }
        return true;
    }

    static bool TryAddUserRegion(
        IrFunction moveNext,
        MetadataMethodAddress executionAddress,
        IrNode source,
        ClassicAsyncUserRegionKind kind,
        string discriminator,
        int occurrence,
        List<ClassicAsyncUserRegion> regions)
    {
        if (!TryStructuralPath(
                moveNext,
                source,
                out string structuralPath)
            || !TryGetStatementSlot(
                source,
                out IrNode physicalStatement)
            || !TryStructuralPath(
                moveNext,
                physicalStatement,
                out string physicalPath))
        {
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
        return true;
    }

    internal static bool TryCaptureOutputNodes(
        BlockContainer output,
        out List<ClassicAsyncOutputNode> nodes)
    {
        nodes = [];
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

        var awaitOccurrence = 0;
        AwaitExpression[] rootAwaits =
        [
            .. output.DescendantsOutsideNestedFunctions
                .OfType<AwaitExpression>(),
        ];
        if (rootAwaits.Length
            != output.Descendants.OfType<AwaitExpression>().Count())
        {
            nodes = [];
            return false;
        }
        foreach (AwaitExpression awaitExpression in rootAwaits)
        {
            if (!TryGetOutputAwaitedOperandDiscriminator(
                    awaitExpression,
                    out string discriminator))
            {
                nodes = [];
                return false;
            }

            nodes.Add(new(new(
                ClassicAsyncUserRegionKind.AwaitedOperand,
                discriminator,
                awaitOccurrence)));
            awaitOccurrence++;
        }

        var predicateOccurrence = 0;
        // Capture every rendered condition so an unmodeled one remains an
        // unmatched output and forces the ledger to decline.
        foreach (IrNode node
            in output.DescendantsOutsideNestedFunctions)
        {
            IrExpression? condition = node switch
            {
                Conditional conditional => conditional.Condition,
                IfStatement statement => statement.Condition,
                _ => null,
            };
            if (condition is null)
                continue;
            if (!TryGetSemanticExpressionKey(
                    condition,
                    out string discriminator))
            {
                nodes = [];
                return false;
            }

            nodes.Add(new(new(
                ClassicAsyncUserRegionKind.Predicate,
                discriminator,
                predicateOccurrence)));
            predicateOccurrence++;
        }

        var guardedEffectOccurrence = 0;
        foreach (ExpressionStatement statement
            in output.DescendantsOutsideNestedFunctions
                .OfType<ExpressionStatement>())
        {
            if (!IsInFinallyBody(statement))
                continue;
            if (statement.Expression is not Call effect)
            {
                nodes = [];
                return false;
            }
            if (!TryGetSemanticExpressionKey(
                    effect,
                    out string effectKey))
            {
                nodes = [];
                return false;
            }

            nodes.Add(new(new(
                ClassicAsyncUserRegionKind.GuardedEffect,
                SemanticParts("finally", effectKey),
                guardedEffectOccurrence)));
            guardedEffectOccurrence++;
        }
        return true;
    }

    static bool TryGetInputPredicateDiscriminator(
        IrExpression condition,
        Kickoff kickoffModel,
        out IrExpression source,
        out string discriminator)
    {
        source = condition;
        discriminator = "";
        bool usesUserParameter = condition.Descendants
            .Prepend(condition)
            .OfType<LoadField>()
            .Any(field =>
                field.Instance is LoadArgument { Index: 0 }
                && TryGetParameterBinding(
                    kickoffModel,
                    field.Field,
                    out _));
        if (!usesUserParameter)
            return false;

        source = condition is LogicalNot not
            ? not.Operand
            : condition;
        IrExpression? normalized = CloneAndRemap(
            source,
            kickoffModel);
        return normalized is not null
            && TryGetSemanticExpressionKey(
                normalized,
                out discriminator);
    }

    static bool TryGetAwaitedOperandRegion(
        IrFunction moveNext,
        Kickoff kickoffModel,
        Call getResult,
        out IrNode source,
        out string discriminator)
    {
        source = null!;
        discriminator = "";
        if (!TryGetAwaitSource(
                moveNext,
                getResult,
                out _,
                out IrExpression awaitedOperand))
        {
            return false;
        }

        if (awaitedOperand is LoadStackSlot load)
        {
            List<StoreStackSlot> stores =
            [
                .. moveNext.Descendants
                    .OfType<StoreStackSlot>()
                    .Where(store => store.Slot == load.Slot),
            ];
            if (stores is not
                [
                    {
                        Value: LoadElement
                        {
                            Array: LoadField
                            {
                                Field: var collectionField,
                                Instance: LoadArgument { Index: 0 },
                            },
                            Index: LoadField
                            {
                                Field.Name: "<>7__wrap2",
                                Instance: LoadArgument { Index: 0 },
                            },
                        } element,
                    } store,
                ])
            {
                return false;
            }

            string collectionName = collectionField.Name;
            if (collectionName.StartsWith(
                    "<>7__wrap",
                    StringComparison.Ordinal))
            {
                List<StoreField> collectionStores =
                [
                    .. moveNext.Descendants
                        .OfType<StoreField>()
                        .Where(store =>
                            store.Field.Name == collectionName
                            && store.Value is LoadField
                            {
                                Field.Name: var sourceName,
                                Instance: LoadArgument { Index: 0 },
                            }
                            && !sourceName.StartsWith(
                                "<",
                                StringComparison.Ordinal)),
                ];
                if (collectionStores is not
                    [
                        { Value: LoadField sourceCollection },
                    ])
                {
                    return false;
                }
                if (!TryGetParameterBinding(
                        kickoffModel,
                        sourceCollection.Field,
                        out ClassicAsyncParameterBinding binding))
                {
                    return false;
                }
                collectionName = binding.ArgumentName;
            }
            else if (!TryGetParameterBinding(
                    kickoffModel,
                    collectionField,
                    out ClassicAsyncParameterBinding binding))
            {
                return false;
            }
            else
            {
                collectionName = binding.ArgumentName;
            }

            source = element;
            discriminator = AwaitedOperandKey(
                "foreach-element",
                collectionName,
                load.ResultType,
                "loop-counter");
            return true;
        }

        IrExpression? normalized =
            CloneAndRemap(
                awaitedOperand,
                kickoffModel);
        if (normalized is null
            || !TryGetSemanticExpressionKey(
                normalized,
                out discriminator))
        {
            return false;
        }

        source = awaitedOperand;
        return true;
    }

    static bool TryGetOutputAwaitedOperandDiscriminator(
        AwaitExpression awaitExpression,
        out string discriminator)
    {
        if (awaitExpression.Operand is LoadLocal load)
        {
            for (IrNode? ancestor = awaitExpression.Parent;
                ancestor is not null;
                ancestor = ancestor.Parent)
            {
                if (ancestor is ForeachStatement
                    {
                        LocalIndex: var localIndex,
                        Collection: LoadArgument collection,
                    }
                    && localIndex == load.Index)
                {
                    discriminator = AwaitedOperandKey(
                        "foreach-element",
                        collection.Name,
                        load.ResultType,
                        "loop-counter");
                    return true;
                }
            }
        }

        return TryGetSemanticExpressionKey(
            awaitExpression.Operand,
            out discriminator);
    }

    internal static bool TryGetSemanticExpressionKey(
        IrExpression expression,
        out string key)
    {
        switch (expression)
        {
            case LoadArgument argument:
                key = SemanticParts(
                    "parameter",
                    argument.Index.ToString(CultureInfo.InvariantCulture),
                    argument.Name,
                    SemanticTypeKey(argument.Type),
                    argument.IsDynamic.ToString(),
                    argument.ArrayElementIsDynamic.ToString());
                return true;
            case Call call:
                var arguments = new List<string>(call.Arguments.Count);
                foreach (IrExpression argument in call.Arguments)
                {
                    if (!TryGetSemanticExpressionKey(
                            argument,
                            out string argumentKey))
                    {
                        key = "";
                        return false;
                    }
                    arguments.Add(argumentKey);
                }

                string exactDefinition =
                    call.Callee.ExactDefinitionAddress is { } address
                        ? string.Join(
                            ":",
                            address.ModuleVersionId.ToString("D"),
                            address.Token.ToString(
                                "X8",
                                CultureInfo.InvariantCulture))
                        : "";
                key = SemanticParts(
                    "call",
                    exactDefinition,
                    SemanticTypeKey(call.Callee.DeclaringType),
                    call.Callee.Name,
                    call.Callee.HasThis.ToString(),
                    call.IsVirtual.ToString(),
                    call.ExtensionSyntaxConflict.ToString(),
                    SemanticTypeKey(call.Callee.ReturnType),
                    call.Callee.ReturnIsDynamic.ToString(),
                    call.Callee.ReturnArrayElementIsDynamic.ToString(),
                    SemanticList(call.Callee.ParameterTypes.Select(
                        SemanticTypeKey)),
                    SemanticList(call.Callee.TypeArguments.Select(
                        SemanticTypeKey)),
                    SemanticList(
                        call.Callee.DefinitionParameterTypes.Select(
                            SemanticTypeKey)),
                    call.Callee.DefinitionReturnType is { } definitionReturn
                        ? SemanticTypeKey(definitionReturn)
                        : "",
                    SemanticList(call.Callee.ParameterRefKinds.Select(
                        static kind => kind.ToString())),
                    call.ConstrainedTo is { } constrainedTo
                        ? SemanticTypeKey(constrainedTo)
                        : "",
                    SemanticList(arguments));
                return true;
            case LoadField field:
                string instanceKey = "";
                if (field.Instance is { } instance
                    && !TryGetSemanticNodeKey(
                        instance,
                        out instanceKey))
                {
                    key = "";
                    return false;
                }
                key = SemanticParts(
                    "field",
                    SemanticTypeKey(field.Field.DeclaringType),
                    field.Field.Name,
                    SemanticTypeKey(field.Field.Type),
                    field.Field.IsDynamic.ToString(),
                    field.Field.DynamicFact.ToString(),
                    field.Field.ArrayElementIsDynamic.ToString(),
                    field.IsVolatile.ToString(),
                    instanceKey);
                return true;
            case UnsupportedNode:
                key = "";
                return false;
            default:
                return TryGetSemanticNodeKey(expression, out key);
        }
    }

    static bool TryGetSemanticNodeKey(
        IrNode node,
        out string key)
    {
        if (node is UnsupportedNode)
        {
            key = "";
            return false;
        }
        if (node is LoadArgument or Call or LoadField)
        {
            return TryGetSemanticExpressionKey(
                (IrExpression)node,
                out key);
        }

        var children = new List<string>(node.Children.Count);
        foreach (IrNode child in node.Children)
        {
            if (!TryGetSemanticNodeKey(
                    child,
                    out string childKey))
            {
                key = "";
                return false;
            }
            children.Add(childKey);
        }

        key = SemanticParts(
            "node",
            node.GetType().FullName ?? node.GetType().Name,
            node.Describe(),
            node is IrExpression expression
                && expression.ResultType is { } resultType
                    ? SemanticTypeKey(resultType)
                    : "",
            SemanticList(node.DirectTypes.Select(SemanticTypeKey)),
            SemanticList(children));
        return true;
    }

    static string AwaitedOperandKey(
        string role,
        string name,
        TypeRef? type,
        string detail = "")
        => SemanticParts(
            role,
            name,
            type is null ? "" : SemanticTypeKey(type),
            detail);

    static string SemanticTypeKey(TypeRef type)
    {
        AssemblyReferenceIdentity? resolution =
            type.ResolutionAssembly;
        string definition = type.DefinitionModuleVersionId is { } mvid
            && !type.DefinitionHandle.IsNil
                ? string.Join(
                    ":",
                    mvid.ToString("D"),
                    MetadataTokens.GetToken(type.DefinitionHandle).ToString(
                        "X8",
                        CultureInfo.InvariantCulture))
                : "";
        return SemanticParts(
            ((int)type.Kind).ToString(CultureInfo.InvariantCulture),
            type.Assembly,
            type.Namespace,
            type.Name,
            SemanticList(type.MetadataNameSegments()),
            type.Rank.ToString(CultureInfo.InvariantCulture),
            type.GenericParameterIndex.ToString(
                CultureInfo.InvariantCulture),
            type.UnsupportedReason,
            type.CallingConvention,
            SemanticList(type.FunctionPointerParameterRefKinds.Select(
                static kind => kind.ToString())),
            SemanticList(type.CustomModifiers.Select(modifier =>
                SemanticParts(
                    modifier.IsRequired.ToString(),
                    SemanticTypeKey(modifier.Modifier)))),
            definition,
            (resolution is not null).ToString(),
            resolution?.Name ?? "",
            resolution?.Version?.ToString() ?? "",
            resolution?.Culture ?? "",
            resolution?.PublicKeyToken ?? "",
            type.ElementType is null
                ? ""
                : SemanticTypeKey(type.ElementType),
            SemanticList(type.TypeArguments.Select(SemanticTypeKey)));
    }

    static string SemanticList(IEnumerable<string> items)
        => SemanticParts([.. items]);

    static string SemanticParts(params string[] parts)
        => string.Concat(parts.Select(static part => string.Concat(
            part.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            part)));

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
        Kickoff kickoff,
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

        bool ClaimCallbacks(RecipeOwnership ownership)
            => TryClaimExpectedBuilderCallbacks(
                moveNext,
                kickoff.StateMachineType,
                setResult,
                getResults,
                ownership);

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
                if (!ClaimCallbacks(tryFinallyOwnership))
                {
                    return false;
                }
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
                if (!ClaimCallbacks(loopOwnership))
                {
                    return false;
                }
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
                if (!ClaimCallbacks(conditionalOwnership))
                {
                    return false;
                }
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
                if (!ClaimCallbacks(sequentialOwnership))
                {
                    return false;
                }
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
            if (!ClaimCallbacks(voidOwnership))
            {
                return false;
            }
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
            if (!ClaimCallbacks(returnOwnership))
            {
                return false;
            }
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
        =>
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<Call>()
                .Where(static call => call.Callee.Name == "GetResult"),
        ];

    static bool TryBuildSingleAwaitReturn(
        IrFunction moveNext,
        Kickoff kickoff,
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

        var value = CloneWithAwaitsAndRemap(
            store.Value,
            moveNext,
            kickoff);
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
        Kickoff kickoff,
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

        var awaited = AwaitForGetResult(
            moveNext,
            kickoff,
            getResults[0]);
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
        Kickoff kickoff,
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
        var firstAwait = AwaitForGetResult(
            moveNext,
            kickoff,
            getResults[0]);
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
        var secondAwait = AwaitForGetResult(
            moveNext,
            kickoff,
            getResults[1]);
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
        var mapped = CloneAndRemap(
            call,
            kickoff,
            hoisted,
            replacements);
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
        Kickoff kickoff,
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

        var condition = CloneAndRemap(
            (IrExpression)flag,
            kickoff);
        var awaited = AwaitForGetResult(
            moveNext,
            kickoff,
            getResults[0]);
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
        Kickoff kickoff,
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

        var collection = CloneAndRemap(
            (IrExpression)tasksField,
            kickoff);
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
        Kickoff kickoff,
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

        TryFinally[] tryFinallyRegions =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<TryFinally>(),
        ];
        if (tryFinallyRegions is not [var originalTryFinally])
            return false;

        var resultStore = originalTryFinally.TryBody.Descendants.OfType<StoreLocal>()
            .LastOrDefault(store => store.Index == result.Index && ContainsNode(store.Value, getResults[0]));
        if (resultStore is null)
            return false;
        var resultValue = CloneWithAwaitsAndRemap(
            resultStore.Value,
            moveNext,
            kickoff);
        if (resultValue is null)
            return false;

        var finallyGuards = originalTryFinally.FinallyBody.Blocks
            .SelectMany(block => block.Children)
            .OfType<IfStatement>()
            .ToList();
        if (finallyGuards is not [var finallyGuard]
            || finallyGuard.Then.Children
                is not [ExpressionStatement finallyStatement])
        {
            return false;
        }
        if (!IsCompilerFinallyStateGuard(moveNext, finallyGuard))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (HasUnexpectedExpressionStatement(moveNext, finallyStatement))
            return false;

        var mappedFinally = CloneAndRemap(
            finallyStatement,
            kickoff);
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

    internal static bool IsCompilerFinallyStateGuard(
        IrFunction moveNext,
        IfStatement guard)
    {
        var stateLocal = StateLocalIndex(moveNext);
        return !guard.HasElse
            && stateLocal is { } state
            && HasOnlyRecognizedStateLocalAssignments(
                moveNext,
                state)
            && guard.Condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadLocal load,
                Right: Constant { Value: 0 } zero,
            }
            && load.Index == state
            && IsInt32(load.Type)
            && IsInt32(zero.Type);
    }

    static bool HasOnlyRecognizedStateLocalAssignments(
        IrFunction moveNext,
        int stateLocal)
    {
        TypeRef machine = DefinitionType(moveNext.DeclaringType);
        StoreLocal[] assignments =
        [
            .. moveNext.Descendants
                .OfType<StoreLocal>()
                .Where(store => store.Index == stateLocal),
        ];
        return assignments is
            [var seed, var suspension, var resumption]
            && IsInt32(seed.Type)
            && seed.Value is LoadField
            {
                Field.Name: "<>1__state",
                Field: var seedField,
                Instance: LoadArgument { Index: 0 },
            }
            && IsInt32(seedField.Type)
            && IsMachineField(seedField, machine)
            && IsRecognizedStateTransition(
                moveNext,
                machine,
                suspension,
                expectedState: 0)
            && IsRecognizedStateTransition(
                moveNext,
                machine,
                resumption,
                expectedState: -1);
    }

    static bool IsRecognizedStateTransition(
        IrFunction moveNext,
        TypeRef machine,
        StoreLocal store,
        int expectedState)
    {
        if (!IsInt32(store.Type)
            || store.Parent is not Block block
            || !TryGetStateTransitionConstant(
                moveNext,
                store.Value,
                [],
                out int state)
            || state != expectedState)
        {
            return false;
        }

        for (int i = 0; i + 1 < block.Children.Count; i++)
        {
            if (!ReferenceEquals(block.Children[i], store))
                continue;
            return block.Children[i + 1] is StoreField
            {
                Field.Name: "<>1__state",
                Field: var stateField,
                Instance: LoadArgument { Index: 0 },
                Value: var stateValue,
            }
                && IsInt32(stateField.Type)
                && IsMachineField(stateField, machine)
                && SameStateTransitionValue(
                    store.Value,
                    stateValue);
        }

        return false;
    }

    static bool SameStateTransitionValue(
        IrExpression localValue,
        IrExpression fieldValue)
        => localValue switch
        {
            Constant localConstant
                when fieldValue is Constant fieldConstant
                => Equals(
                        localConstant.Value,
                        fieldConstant.Value)
                    && localConstant.Type.Equals(
                        fieldConstant.Type),
            LoadStackSlot localSlot
                when fieldValue is LoadStackSlot fieldSlot
                => localSlot.Slot == fieldSlot.Slot
                    && Equals(
                        localSlot.ResultType,
                        fieldSlot.ResultType),
            _ => false,
        };

    static bool TryGetStateTransitionConstant(
        IrFunction moveNext,
        IrExpression value,
        HashSet<int> visitingSlots,
        out int state)
    {
        if (value is Constant
            {
                Value: int constant,
                Type: var constantType,
            })
        {
            state = constant;
            return IsInt32(constantType);
        }
        if (value is not LoadStackSlot
            {
                ResultType: { } resultType,
            } load
            || !IsInt32(resultType)
            || !visitingSlots.Add(load.Slot))
        {
            state = 0;
            return false;
        }

        StoreStackSlot[] definitions =
        [
            .. moveNext.Descendants
                .OfType<StoreStackSlot>()
                .Where(store => store.Slot == load.Slot),
        ];
        int? candidate = null;
        bool recognized = definitions.Length > 0;
        foreach (StoreStackSlot definition in definitions)
        {
            if (!TryGetStateTransitionConstant(
                    moveNext,
                    definition.Value,
                    visitingSlots,
                    out int definitionState)
                || candidate is { } previous
                    && previous != definitionState)
            {
                recognized = false;
                break;
            }
            candidate = definitionState;
        }
        visitingSlots.Remove(load.Slot);
        state = candidate ?? 0;
        return recognized;
    }

    static AwaitExpression? AwaitForGetResult(
        IrFunction moveNext,
        Kickoff kickoff,
        Call getResult)
    {
        var awaitedOperand = AwaitedOperandForGetResult(moveNext, getResult);
        if (awaitedOperand is null)
            return null;

        var operand = CloneAndRemap(
            awaitedOperand,
            kickoff);
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

    internal static bool TryGetAwaitSource(
        IrFunction moveNext,
        Call getResult,
        out StoreLocal awaiterStore,
        out IrExpression awaitedOperand)
        => TryGetAwaitSource(
            moveNext,
            getResult,
            out awaiterStore,
            out awaitedOperand,
            out _);

    static bool TryGetAwaitSource(
        IrFunction moveNext,
        Call getResult,
        out StoreLocal awaiterStore,
        out IrExpression awaitedOperand,
        out Call suspensionCallback)
    {
        awaiterStore = null!;
        awaitedOperand = null!;
        suspensionCallback = null!;
        if (getResult.Arguments is not [LoadLocalAddress awaiterAddress])
            return false;
        Block? useBlock = OwningBlock(getResult);
        if (useBlock?.Parent is not BlockContainer container)
            return false;

        var rootNodes = moveNext.DescendantsOutsideNestedFunctions
            .ToHashSet(ReferenceEqualityComparer.Instance);
        if (moveNext.Descendants
            .OfType<StoreLocal>()
            .Any(store =>
                store.Index == awaiterAddress.Index
                && !rootNodes.Contains(store)))
        {
            return false;
        }

        var blocks = container.Blocks;
        var blockSet = blocks.ToHashSet(
            ReferenceEqualityComparer.Instance);
        foreach (IrNode node in moveNext
            .DescendantsOutsideNestedFunctions)
        {
            bool relevant = node is StoreLocal store
                    && store.Index == awaiterAddress.Index
                || node is Call call
                    && IsSameAwaiterGetResult(
                        call,
                        awaiterAddress.Index);
            if (!relevant)
                continue;
            Block? owner = OwningBlock(node);
            if (owner is null || !blockSet.Contains(owner))
            {
                return false;
            }
        }

        IReadOnlyList<BlockEdges> edges = Cfg.Build(blocks);
        if (edges.Count != blocks.Count
            || edges.Any(edge =>
                edge.ExternalTargets.Count > 0
                || edge.LeavesRegion
                || edge.Successors.Any(successor =>
                    (uint)successor >= (uint)blocks.Count))
            || HasCrossContainerEntry(moveNext, container))
        {
            return false;
        }

        List<StoreLocal> candidates =
        [
            .. blocks
                .SelectMany(static block => block.Children)
                .OfType<StoreLocal>()
                .Where(store => IsAwaiterSourceDefinition(
                    store,
                    awaiterAddress)),
        ];
        List<StoreLocal> resumeStores =
        [
            .. blocks
                .SelectMany(static block => block.Children)
                .OfType<StoreLocal>()
                .Where(store => IsAwaiterResumeDefinition(
                    moveNext,
                    store,
                    awaiterAddress)),
        ];
        if (candidates.Count == 0)
            return false;

        var candidateIndexes =
            new Dictionary<StoreLocal, int>(
                ReferenceEqualityComparer.Instance);
        for (var i = 0; i < candidates.Count; i++)
            candidateIndexes.Add(candidates[i], i);
        var resumeIndexes =
            new Dictionary<StoreLocal, int>(
                ReferenceEqualityComparer.Instance);
        for (var i = 0; i < resumeStores.Count; i++)
        {
            resumeIndexes.Add(
                resumeStores[i],
                candidates.Count + i);
        }

        const int missingDefinition = -1;
        const int unmodeledDefinition = -2;
        var inputs = new HashSet<int>?[blocks.Count];
        inputs[0] = [missingDefinition];
        var work = new Queue<int>();
        var queued = new bool[blocks.Count];
        work.Enqueue(0);
        queued[0] = true;
        while (work.Count > 0)
        {
            int blockIndex = work.Dequeue();
            queued[blockIndex] = false;
            HashSet<int> output = TransferAwaitDefinitions(
                blocks[blockIndex],
                inputs[blockIndex]!,
                awaiterAddress.Index,
                getResult,
                candidateIndexes,
                resumeIndexes,
                missingDefinition,
                unmodeledDefinition,
                out _);
            foreach (int successor in edges[blockIndex].Successors)
            {
                inputs[successor] ??= [];
                int before = inputs[successor]!.Count;
                inputs[successor]!.UnionWith(output);
                if (inputs[successor]!.Count != before
                    && !queued[successor])
                {
                    work.Enqueue(successor);
                    queued[successor] = true;
                }
            }
        }

        int useBlockIndex = -1;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], useBlock))
            {
                useBlockIndex = i;
                break;
            }
        }
        if (useBlockIndex < 0
            || inputs[useBlockIndex] is null)
        {
            return false;
        }
        _ = TransferAwaitDefinitions(
            useBlock,
            inputs[useBlockIndex]!,
            awaiterAddress.Index,
            getResult,
            candidateIndexes,
            resumeIndexes,
            missingDefinition,
            unmodeledDefinition,
            out HashSet<int>? reaching);
        if (reaching is null
            || reaching.Contains(missingDefinition)
            || reaching.Contains(unmodeledDefinition))
        {
            return false;
        }

        int[] reachingCandidates =
        [
            .. reaching.Where(index =>
                index >= 0
                && index < candidates.Count),
        ];
        if (reachingCandidates is not [var candidateIndex])
            return false;

        StoreLocal candidateStore = candidates[candidateIndex];
        int[] reachingResumes =
        [
            .. reaching.Where(index =>
                index >= candidates.Count),
        ];
        if (reachingResumes.Length > 1)
            return false;

        FieldRef? expectedSpill = null;
        if (reachingResumes is [var resumeIndex])
        {
            int index = resumeIndex - candidates.Count;
            if ((uint)index >= (uint)resumeStores.Count
                || resumeStores[index].Value is not LoadField resumeLoad)
            {
                return false;
            }
            expectedSpill = resumeLoad.Field;
        }
        if (!TryGetSuspensionCallback(
                moveNext,
                blocks,
                inputs,
                awaiterAddress.Index,
                candidateIndex,
                expectedSpill,
                candidateIndexes,
                resumeIndexes,
                missingDefinition,
                unmodeledDefinition,
                out suspensionCallback))
        {
            return false;
        }

        if (candidateStore.Value is not Call
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

    static bool HasCrossContainerEntry(
        IrFunction function,
        BlockContainer target)
    {
        HashSet<int> targetOffsets =
        [
            .. target.Blocks.Select(static block =>
                block.StartOffset),
        ];
        var visited = new HashSet<BlockContainer>(
            ReferenceEqualityComparer.Instance);
        foreach (Block block in function
            .DescendantsOutsideNestedFunctions
            .OfType<Block>())
        {
            if (block.Parent is BlockContainer container)
            {
                if (ReferenceEquals(container, target)
                    || !visited.Add(container))
                {
                    continue;
                }
                if (Cfg.Build(container.Blocks).Any(edge =>
                    edge.ExternalTargets.Any(
                        targetOffsets.Contains)))
                {
                    return true;
                }
                continue;
            }

            if (Cfg.Build([block])[0].ExternalTargets.Any(
                targetOffsets.Contains))
            {
                return true;
            }
        }
        return false;
    }

    static HashSet<int> TransferAwaitDefinitions(
        Block block,
        IReadOnlySet<int> input,
        int awaiterLocal,
        Call target,
        IReadOnlyDictionary<StoreLocal, int> candidateIndexes,
        IReadOnlyDictionary<StoreLocal, int> resumeIndexes,
        int missingDefinition,
        int unmodeledDefinition,
        out HashSet<int>? targetInput)
    {
        var state = new HashSet<int>(input);
        targetInput = null;
        foreach (IrNode statement in block.Children)
        {
            state = TransferAwaitDefinition(
                statement,
                state,
                awaiterLocal,
                target,
                candidateIndexes,
                resumeIndexes,
                missingDefinition,
                unmodeledDefinition,
                out HashSet<int>? statementTargetInput);
            if (statementTargetInput is not null)
            {
                targetInput = statementTargetInput;
            }
        }
        return state;
    }

    static HashSet<int> TransferAwaitDefinition(
        IrNode statement,
        IReadOnlySet<int> input,
        int awaiterLocal,
        Call? target,
        IReadOnlyDictionary<StoreLocal, int> candidateIndexes,
        IReadOnlyDictionary<StoreLocal, int> resumeIndexes,
        int missingDefinition,
        int unmodeledDefinition,
        out HashSet<int>? targetInput)
    {
        var state = new HashSet<int>(input);
        targetInput = null;
        if (statement.DescendantsOutsideNestedFunctions
            .OfType<StoreLocal>()
            .Any(store => store.Index == awaiterLocal))
        {
            state = [unmodeledDefinition];
        }

        Call[] uses =
        [
            .. statement
                .DescendantsAndSelfOutsideNestedFunctions
                .OfType<Call>()
                .Where(call => IsSameAwaiterGetResult(
                    call,
                    awaiterLocal)),
        ];
        if (uses.Length > 1)
        {
            state = [unmodeledDefinition];
        }
        else if (uses is [var use])
        {
            if (ReferenceEquals(use, target))
                targetInput = [.. state];
            state = [missingDefinition];
        }

        if (statement is StoreLocal
            {
                Index: var index,
            } definition
            && index == awaiterLocal)
        {
            state = candidateIndexes.TryGetValue(
                definition,
                out int candidate)
                    ? [candidate]
                    : resumeIndexes.TryGetValue(
                        definition,
                        out int resume)
                        ? [resume]
                        : [unmodeledDefinition];
        }
        return state;
    }

    static bool TryGetSuspensionCallback(
        IrFunction moveNext,
        IReadOnlyList<Block> blocks,
        IReadOnlyList<HashSet<int>?> inputs,
        int awaiterLocal,
        int candidateIndex,
        FieldRef? expectedSpill,
        IReadOnlyDictionary<StoreLocal, int> candidateIndexes,
        IReadOnlyDictionary<StoreLocal, int> resumeIndexes,
        int missingDefinition,
        int unmodeledDefinition,
        out Call suspensionCallback)
    {
        suspensionCallback = null!;
        int matchingSpills = 0;
        for (var blockIndex = 0;
            blockIndex < blocks.Count;
            blockIndex++)
        {
            if (inputs[blockIndex] is null)
                continue;
            var state = new HashSet<int>(inputs[blockIndex]!);
            Block block = blocks[blockIndex];
            for (var statementIndex = 0;
                statementIndex < block.Children.Count;
                statementIndex++)
            {
                IrNode statement = block.Children[statementIndex];
                if (statement is StoreField
                    {
                        Field: var spillField,
                        Instance: LoadArgument { Index: 0 },
                        Value: LoadLocal { Index: var local },
                    }
                    && local == awaiterLocal
                    && (expectedSpill is null
                        || SameAwaiterField(
                            spillField,
                            expectedSpill,
                            moveNext.DeclaringType))
                    && state.SetEquals([candidateIndex])
                    && TryGetSuspensionAfter(
                        moveNext,
                        block,
                        statementIndex,
                        awaiterLocal,
                        out Call callback))
                {
                    matchingSpills++;
                    suspensionCallback = callback;
                }

                state = TransferAwaitDefinition(
                    statement,
                    state,
                    awaiterLocal,
                    target: null,
                    candidateIndexes,
                    resumeIndexes,
                    missingDefinition,
                    unmodeledDefinition,
                    out _);
            }
        }
        return matchingSpills == 1;
    }

    static bool TryGetSuspensionAfter(
        IrFunction moveNext,
        Block block,
        int spillIndex,
        int awaiterLocal,
        out Call suspensionCallback)
    {
        suspensionCallback = null!;
        for (int i = spillIndex + 1;
            i < block.Children.Count;
            i++)
        {
            switch (block.Children[i])
            {
                case ExpressionStatement
                {
                    Expression: Call call,
                } when call.Callee.Name is
                        "AwaitUnsafeOnCompleted"
                        or "AwaitOnCompleted"
                    && call.Arguments.Any(argument =>
                        argument is LoadLocalAddress
                        {
                            Index: var index,
                        }
                        && index == awaiterLocal)
                    && IsCompilerBuilderCallback(
                        moveNext,
                        call):
                    if (suspensionCallback is not null)
                        return false;
                    suspensionCallback = call;
                    break;
                case Return when suspensionCallback is not null:
                    return true;
            }
        }
        return false;
    }

    static bool SameAwaiterField(
        FieldRef left,
        FieldRef right,
        TypeRef machine)
        => left.Name == right.Name
            && left.Name.StartsWith(
                "<>u__",
                StringComparison.Ordinal)
            && IsMachineField(left, DefinitionType(machine))
            && IsMachineField(right, DefinitionType(machine))
            && SameExactType(
                StateMachineFieldType(left.Type, machine),
                StateMachineFieldType(right.Type, machine));

    static bool IsAwaiterSourceDefinition(
        StoreLocal store,
        LoadLocalAddress awaiterAddress)
    {
        if (store.Index != awaiterAddress.Index
            || !SameExactType(store.Type, awaiterAddress.Type)
            || store.Value is not Call
            {
                Callee.Name: "GetAwaiter",
                Arguments: [var operand],
            })
        {
            return false;
        }
        return operand is not LoadField
        {
            Field.Name: var field,
        } || !field.StartsWith("<>u__", StringComparison.Ordinal);
    }

    static bool IsAwaiterResumeDefinition(
        IrFunction moveNext,
        StoreLocal store,
        LoadLocalAddress awaiterAddress)
        => store.Index == awaiterAddress.Index
            && SameExactType(store.Type, awaiterAddress.Type)
            && store.Value is LoadField
            {
                Field.Name: var fieldName,
                Field: var field,
                Instance: LoadArgument { Index: 0 },
            }
            && fieldName.StartsWith(
                "<>u__",
                StringComparison.Ordinal)
            && IsMachineField(
                field,
                DefinitionType(moveNext.DeclaringType))
            && SameExactType(
                StateMachineFieldType(
                    field.Type,
                    moveNext.DeclaringType),
                awaiterAddress.Type);

    static bool IsSameAwaiterGetResult(
        Call call,
        int awaiterLocal)
        => call.Callee.Name == "GetResult"
            && call.Arguments is
                [LoadLocalAddress { Index: var index }]
            && index == awaiterLocal;

    static Block? OwningBlock(IrNode node)
    {
        for (IrNode? current = node;
            current is not null;
            current = current.Parent)
        {
            if (current is Block block)
                return block;
        }
        return null;
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

    static bool TryClaimExpectedBuilderCallbacks(
        IrFunction moveNext,
        TypeRef expectedStateMachineType,
        Call selectedSetResult,
        IReadOnlyList<Call> getResults,
        RecipeOwnership ownership)
        => TryGetExpectedBuilderCallbackSlots(
                moveNext,
                expectedStateMachineType,
                selectedSetResult,
                getResults,
                out IReadOnlyList<IrNode> callbacks)
            && ownership.Claim(callbacks);

    internal static bool TryGetExpectedBuilderCallbackSlots(
        IrFunction moveNext,
        TypeRef expectedStateMachineType,
        Call selectedSetResult,
        IReadOnlyList<Call> getResults,
        out IReadOnlyList<IrNode> callbackSlots)
    {
        callbackSlots = [];
        if (!TryGetExecutionStateMachineType(
                moveNext,
                expectedStateMachineType,
                out TypeRef executionStateMachineType))
        {
            return false;
        }
        List<(ExpressionStatement Statement, Call Call)> callbacks =
        [
            .. moveNext.DescendantsOutsideNestedFunctions
                .OfType<ExpressionStatement>()
                .Where(static statement =>
                    statement.Expression is Call)
                .Select(static statement =>
                    (statement, (Call)statement.Expression))
                .Where(static pair =>
                    IsBuilderCallbackName(pair.Item2.Callee.Name)),
        ];
        if (callbacks.Count(pair =>
                pair.Call.Callee.Name == "SetResult") != 1
            || !callbacks.Any(pair =>
                ReferenceEquals(pair.Call, selectedSetResult))
            || callbacks.Count(pair =>
                pair.Call.Callee.Name == "SetException") != 1
            || callbacks.Count(pair =>
                pair.Call.Callee.Name is
                    "AwaitUnsafeOnCompleted"
                    or "AwaitOnCompleted")
                != getResults.Count
            || callbacks.Any(pair =>
                !IsCompilerBuilderCallback(
                    moveNext,
                    pair.Call)))
        {
            return false;
        }

        var expectedSuspensions = new HashSet<Call>(
            ReferenceEqualityComparer.Instance);
        foreach (Call getResult in getResults)
        {
            if (!TryGetAwaitSource(
                    moveNext,
                    getResult,
                    out StoreLocal awaiterStore,
                    out _,
                    out Call suspension)
                || !IsExactAwaitCallbackForPoint(
                    moveNext,
                    executionStateMachineType,
                    suspension,
                    getResult,
                    awaiterStore)
                || !expectedSuspensions.Add(suspension))
            {
                return false;
            }
        }
        if (!callbacks
                .Where(pair => pair.Call.Callee.Name is
                    "AwaitUnsafeOnCompleted"
                    or "AwaitOnCompleted")
                .Select(static pair => pair.Call)
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(expectedSuspensions))
        {
            return false;
        }

        callbackSlots =
        [
            .. callbacks.Select(static pair =>
                (IrNode)pair.Statement),
        ];
        return true;
    }

    static bool TryGetExecutionStateMachineType(
        IrFunction moveNext,
        TypeRef kickoffStateMachineType,
        out TypeRef executionStateMachineType)
    {
        executionStateMachineType = null!;
        TypeRef executionDefinition =
            DefinitionType(moveNext.DeclaringType);
        int executionArity =
            executionDefinition.IntroducedTypeParameterCounts.Sum();
        if (!SameExactType(
                DefinitionType(kickoffStateMachineType),
                executionDefinition)
            || !kickoffStateMachineType.CustomModifiers.IsEmpty)
        {
            return false;
        }

        if (kickoffStateMachineType.Kind == TypeRefKind.Definition)
        {
            if (executionArity != 0)
                return false;
            executionStateMachineType = executionDefinition;
            return true;
        }
        if (kickoffStateMachineType.Kind
                != TypeRefKind.GenericInstance
            || kickoffStateMachineType.TypeArguments.Length
                != executionArity
            || kickoffStateMachineType.TypeArguments.Any(
                static argument =>
                    !argument.CustomModifiers.IsEmpty))
        {
            return false;
        }

        executionStateMachineType = TypeRef.GenericInstance(
            executionDefinition,
            [
                .. Enumerable.Range(
                        0,
                        kickoffStateMachineType.TypeArguments.Length)
                    .Select(static index =>
                        TypeRef.GenericParameter(index)),
            ]);
        return true;
    }

    static bool IsBuilderCallbackName(string name)
        => name is "AwaitUnsafeOnCompleted"
            or "AwaitOnCompleted"
            or "SetException"
            or "SetResult";

    static bool IsCompilerBuilderCallback(
        IrFunction moveNext,
        Call call)
    {
        if (!IsBuilderCallbackName(call.Callee.Name)
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

        TypeRef machine = DefinitionType(moveNext.DeclaringType);
        return IsMachineField(field, machine)
            && IsAsyncMethodBuilder(
                StateMachineFieldType(
                    field.Type,
                    moveNext.DeclaringType))
            && SameExactType(
                call.Callee.DeclaringType,
                StateMachineFieldType(
                    field.Type,
                    moveNext.DeclaringType))
            && HasExternalMemberReferenceProvenance(call.Callee)
            && IsExactBuilderCallbackSignature(
                moveNext,
                call);
    }

    static bool IsExactBuilderCallbackSignature(
        IrFunction moveNext,
        Call call)
    {
        if (call.IsVirtual
            || !call.Callee.HasThis
            || !SameExactType(
                call.Callee.ReturnType,
                TypeRef.CoreLib("System", "Void"))
            || call.Arguments.Count
                != call.Callee.ParameterTypes.Length + 1)
        {
            return false;
        }

        return call.Callee.Name switch
        {
            "SetResult" => IsExactSetResult(call.Callee),
            "SetException" => call.Callee.ParameterTypes is
                [var exception]
                && exception is
                {
                    Kind: TypeRefKind.Definition,
                    Assembly: TypeRef.CoreLibrary,
                    Namespace: "System",
                    Name: "Exception",
                    CustomModifiers.IsEmpty: true,
                }
                && call.Callee.TypeArguments.IsEmpty
                && call.Callee.DefinitionParameterTypes.IsEmpty
                && call.Callee.DefinitionReturnType is null,
            "AwaitUnsafeOnCompleted" or "AwaitOnCompleted"
                => call.Callee.TypeArguments is
                    [var awaiterType, var machineType]
                    && call.Callee.ParameterTypes is
                    [
                        var awaiterParameter
                            and {
                            Kind: TypeRefKind.ByRef,
                            ElementType: var parameterAwaiter,
                        },
                        var machineParameter
                            and {
                            Kind: TypeRefKind.ByRef,
                            ElementType: var parameterMachine,
                        },
                    ]
                    && parameterAwaiter is not null
                    && parameterMachine is not null
                    && SameExactType(
                        parameterAwaiter,
                        awaiterType)
                    && SameExactType(
                        awaiterParameter,
                        TypeRef.ByRef(awaiterType))
                    && SameExactType(
                        parameterMachine,
                        machineType)
                    && SameExactType(
                        machineParameter,
                        TypeRef.ByRef(machineType))
                    && SameExactType(
                        DefinitionType(machineType),
                        DefinitionType(moveNext.DeclaringType))
                    && call.Arguments is
                    [
                        _,
                        LoadLocalAddress awaiterAddress,
                        LoadArgument
                        {
                            Index: 0,
                            Type: var machineArgumentType,
                        },
                    ]
                    && SameExactType(
                        awaiterAddress.Type,
                        awaiterType)
                    && SameExactType(
                        DefinitionType(machineArgumentType),
                        DefinitionType(machineType))
                    && call.Callee.DefinitionParameterTypes is
                    [
                        var definitionAwaiter,
                        var definitionMachine,
                    ]
                    && SameExactType(
                        definitionAwaiter,
                        TypeRef.ByRef(
                            TypeRef.MethodGenericParameter(0)))
                    && SameExactType(
                        definitionMachine,
                        TypeRef.ByRef(
                            TypeRef.MethodGenericParameter(1)))
                    && call.Callee.DefinitionReturnType is
                        { } definitionReturn
                    && SameExactType(
                        definitionReturn,
                        TypeRef.CoreLib("System", "Void")),
            _ => false,
        };
    }

    static bool IsExactAwaitCallbackForPoint(
        IrFunction moveNext,
        TypeRef expectedStateMachineType,
        Call callback,
        Call getResult,
        StoreLocal awaiterStore)
        => IsCompilerBuilderCallback(moveNext, callback)
            && callback.Callee.Name is
                "AwaitUnsafeOnCompleted"
                or "AwaitOnCompleted"
            && callback.Arguments is
            [
                _,
                LoadLocalAddress callbackAwaiter,
                LoadArgument { Index: 0 },
            ]
            && getResult.Arguments is
                [LoadLocalAddress resultAwaiter]
            && callback.Callee.TypeArguments is
                [_, var callbackMachine]
            && callback.Callee.ParameterTypes is
                [
                    _,
                    {
                        Kind: TypeRefKind.ByRef,
                        ElementType: var parameterMachine,
                    },
                ]
            && parameterMachine is not null
            && SameExactType(
                callbackMachine,
                expectedStateMachineType)
            && SameExactType(
                parameterMachine,
                expectedStateMachineType)
            && callbackAwaiter.Index == awaiterStore.Index
            && resultAwaiter.Index == awaiterStore.Index
            && SameExactType(
                callbackAwaiter.Type,
                awaiterStore.Type)
            && SameExactType(
                resultAwaiter.Type,
                awaiterStore.Type);

    static bool IsExactSetResult(MethodRef method)
    {
        if (!method.TypeArguments.IsEmpty
            || !method.DefinitionParameterTypes.IsEmpty
            || method.DefinitionReturnType is not null)
        {
            return false;
        }

        return method.DeclaringType switch
        {
            {
                Kind: TypeRefKind.GenericInstance,
                TypeArguments: [var resultType],
            } => method.ParameterTypes is [var parameter]
                && SameExactType(parameter, resultType),
            _ => method.ParameterTypes.IsEmpty,
        };
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
                        && store.Value is Constant
                            or LoadStackSlot
                            or LoadField { Field.Name: "<>1__state" })
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
    {
        TypeRef machine = DefinitionType(moveNext.DeclaringType);
        int[] candidates =
        [
            .. moveNext.Descendants
                .OfType<StoreLocal>()
                .Where(store => store.Value is LoadField
                {
                    Field.Name: "<>1__state",
                    Field: var field,
                    Instance: LoadArgument { Index: 0 },
                }
                    && IsInt32(store.Type)
                    && IsInt32(field.Type)
                    && IsMachineField(field, machine))
                .Select(static store => store.Index)
                .Distinct(),
        ];
        return candidates is [var state] ? state : null;
    }

    static bool IsInt32(TypeRef type)
        => type is { Namespace: "System", Name: "Int32" };

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

    static IrExpression? CloneWithAwaitsAndRemap(
        IrExpression expression,
        IrFunction moveNext,
        Kickoff kickoff)
    {
        var originalGetResults = expression.Descendants.Prepend(expression).OfType<Call>()
            .Where(static call => call.Callee.Name == "GetResult")
            .ToList();
        if (HasUnverifiedPostAwaitResultUse(
                expression,
                originalGetResults))
        {
            return null;
        }

        var clone = (IrExpression)expression.Clone();
        var clonedGetResults = clone.Descendants.Prepend(clone).OfType<Call>()
            .Where(static call => call.Callee.Name == "GetResult")
            .ToList();
        if (originalGetResults.Count != clonedGetResults.Count)
            return null;

        IrExpression? rootReplacement = null;
        for (var i = 0; i < originalGetResults.Count; i++)
        {
            var awaited = AwaitForGetResult(
                moveNext,
                kickoff,
                originalGetResults[i]);
            if (awaited is null)
                return null;
            if (ReferenceEquals(clonedGetResults[i], clone))
                rootReplacement = awaited;
            else
                clonedGetResults[i].ReplaceWith(awaited);
        }

        var result = rootReplacement ?? clone;
        return RemapInPlace(
            result,
            kickoff)
                ? result
                : null;
    }

    internal static bool HasUnverifiedPostAwaitResultUse(
        IrExpression expression,
        IReadOnlyList<Call> getResults)
    {
        var allowedCalls = getResults.ToHashSet(
            ReferenceEqualityComparer.Instance);
        if (expression.Descendants
                .Prepend(expression)
                .Any(node => node switch
                {
                    Call call => !allowedCalls.Contains(call),
                    CallIndirect
                        or NewObject
                        or ObjectInitializerExpression
                        or DelegateCreation
                        or LocalFunctionInvocation
                        or DynamicGetMember => true,
                    _ => false,
                }))
        {
            return true;
        }

        foreach (Call getResult in getResults)
        {
            for (IrNode current = getResult;
                !ReferenceEquals(current, expression);
                current = current.Parent!)
            {
                if (current.Parent is not { } parent
                    || !IsVerifiedPostAwaitResultWrapper(parent))
                {
                    return true;
                }
            }
        }

        foreach (IrNode node in expression.Descendants
            .Prepend(expression))
        {
            IrExpression? receiver = node switch
            {
                LoadField field => field.Instance,
                LoadFieldAddress field => field.Instance,
                LoadProperty property => property.Instance,
                _ => null,
            };
            if (node is LoadProperty
                || receiver is not null
                    && getResults.Any(getResult =>
                        ContainsNode(receiver, getResult)))
            {
                return true;
            }
        }
        return false;
    }

    static bool IsVerifiedPostAwaitResultWrapper(IrNode node)
        => node is Comparison
            or LogicalBinary
            or Coalesce
            or Conditional
            or LogicalNot
            or Unary
            or Coerce
            or Convert
            or Binary
            or TupleExpression
            or TupleBinaryExpression
            or Box
            or IsInstance
            or IsPattern
            or CastClass
            or Unbox
            or UnboxAny
            or ArrayLength
            or LoadElement
            or LoadIndirect
            or RangeExpression
            or IndexFromEnd;

    static IrExpression? CloneAndRemap(
        IrExpression expression,
        Kickoff kickoff)
    {
        var clone = (IrExpression)expression.Clone();
        if (clone is LoadField { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var field }
            && TryGetParameterBinding(
                kickoff,
                field,
                out ClassicAsyncParameterBinding binding))
        {
            return ParameterLoad(binding);
        }
        if (clone is LoadFieldAddress { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var addressField }
            && TryGetParameterBinding(
                kickoff,
                addressField,
                out ClassicAsyncParameterBinding addressBinding))
        {
            return ParameterLoad(addressBinding);
        }

        return RemapInPlace(
            clone,
            kickoff)
                ? clone
                : null;
    }

    static T? CloneAndRemap<T>(
        T node,
        Kickoff kickoff)
        where T : IrNode
    {
        var clone = (T)node.Clone();
        return RemapInPlace(
            clone,
            kickoff)
                ? clone
                : null;
    }

    static Call? CloneAndRemap(
        Call call,
        Kickoff kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted,
        IReadOnlyDictionary<int, (int Index, TypeRef Type)> locals)
    {
        var clone = (Call)call.Clone();
        return RemapInPlace(
            clone,
            kickoff,
            hoisted,
            locals)
                ? clone
                : null;
    }

    static bool RemapInPlace(
        IrNode node,
        Kickoff kickoff,
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
                    if (!IsMachineField(
                            field,
                            kickoff.StateMachineType))
                    {
                        ok = false;
                    }
                    else if (hoisted is not null
                        && hoisted.TryGetValue(
                            field.Name,
                            out var local))
                    {
                        swaps.Add((current, new LoadLocal(local.Index, local.Type)));
                    }
                    else if (TryGetParameterBinding(
                        kickoff,
                        field,
                        out ClassicAsyncParameterBinding binding))
                    {
                        swaps.Add((current, ParameterLoad(binding)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadFieldAddress { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (TryGetParameterBinding(
                        kickoff,
                        field,
                        out ClassicAsyncParameterBinding addressBinding))
                    {
                        swaps.Add((current, ParameterLoad(addressBinding)));
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

    static LoadArgument ParameterLoad(
        ClassicAsyncParameterBinding binding)
        => new(
            binding.ArgumentIndex,
            binding.ArgumentName,
            binding.ArgumentType)
        {
            IsDynamic = binding.IsDynamic,
            ArrayElementIsDynamic = binding.ArrayElementIsDynamic,
        };

    static bool TryGetParameterBinding(
        Kickoff kickoff,
        FieldRef field,
        out ClassicAsyncParameterBinding binding)
    {
        if (IsMachineField(field, kickoff.StateMachineType))
        {
            foreach (ClassicAsyncParameterBinding candidate
                in kickoff.ParameterBindings.Items)
            {
                // Receiver realization remains outside the accepted recipes.
                if (candidate.FieldName != "<>4__this"
                    && candidate.FieldName == field.Name
                    && candidate.FieldType.Equals(
                        StateMachineFieldType(
                            field.Type,
                            kickoff.StateMachineType)))
                {
                    binding = candidate;
                    return true;
                }
            }
        }

        binding = null!;
        return false;
    }

    static bool ContainsNode(IrNode root, IrNode target)
        => ReferenceEquals(root, target) || root.Descendants.Any(descendant => ReferenceEquals(descendant, target));

    static bool IsInFinallyBody(IrNode node)
    {
        for (IrNode? ancestor = node.Parent;
            ancestor is not null;
            ancestor = ancestor.Parent)
        {
            if (ancestor is TryFinally tryFinally)
                return ContainsNode(tryFinally.FinallyBody, node);
        }
        return false;
    }

    static bool IsAsyncMethodBuilder(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is
        {
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System.Runtime.CompilerServices",
            Name:
                "AsyncTaskMethodBuilder"
                or "AsyncTaskMethodBuilder`1"
                or "AsyncValueTaskMethodBuilder"
                or "AsyncValueTaskMethodBuilder`1"
                or "AsyncVoidMethodBuilder",
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
