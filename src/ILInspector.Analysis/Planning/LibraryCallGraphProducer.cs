using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

public sealed record LibraryCallGraphProducerResult(
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> Methods,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    IReadOnlyDictionary<int, BodySignals> BodySignals,
    IReadOnlyDictionary<(string Namespace, string Name), bool>
        InAssemblyTypeIsException,
    IReadOnlySet<int> NonHeapNewObjOperandTokens,
    IReadOnlyDictionary<int, MethodIdentity> DeclaredSources,
    IReadOnlySet<int> DeclaredSourceSuppressedMethodTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed record LibraryCallGraphBodyEvidence(
    MethodIdentity? Method,
    bool HasManagedBody,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    BodySignals? BodySignals,
    IReadOnlyDictionary<(string Namespace, string Name), bool>
        InAssemblyTypeIsException,
    IReadOnlySet<int> NonHeapNewObjOperandTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

internal sealed class LibraryCallGraphAllocationProducer :
    MethodDefinitionProducer<
        ExactMethodAllocationBody,
        ImmutableArray<ExactMethodAllocationBody>.Builder,
        ImmutableArray<ExactMethodAllocationBody>>
{
    LibraryCallGraphAllocationProducer()
        : base(
            "Library Call Graph Allocation",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.MethodAnalysis,
            dependencies: static () =>
            [
                new(
                    PhysicalMethodAnalysisProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
    }

    internal static LibraryCallGraphAllocationProducer Instance { get; } =
        new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.BoundedFlow,
        ];

    internal override ExactMethodAllocationBody Visit(
        scoped MethodDefinitionView view) =>
        ExactMethodAllocationProducer.Collect(
            view,
            view.FactOf(PhysicalMethodAnalysisProducer.Instance),
            classifyEscapes: false);

    internal override ImmutableArray<ExactMethodAllocationBody>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodAllocationBody>();

    internal override ImmutableArray<ExactMethodAllocationBody>.Builder
        Accumulate(
            ImmutableArray<ExactMethodAllocationBody>.Builder accumulator,
            ExactMethodAllocationBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ImmutableArray<ExactMethodAllocationBody> Complete(
        ImmutableArray<ExactMethodAllocationBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.ToImmutable();
}

public sealed class LibraryCallGraphProducer :
    MethodDefinitionProducer<
        LibraryCallGraphBodyEvidence,
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder,
        LibraryCallGraphProducerResult>
{
    LibraryCallGraphProducer()
        : base(
            "Library Call Graph",
            version: 1,
            tier: 0,
            layers: 0,
            dependencies: static () =>
            [
                new(
                    PhysicalMethodAnalysisProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
                new(
                    LibraryCallGraphAllocationProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
    }

    public static LibraryCallGraphProducer Instance { get; } = new();

    internal override LibraryCallGraphBodyEvidence Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAnalysisBody method =
            view.FactOf(PhysicalMethodAnalysisProducer.Instance);
        ExactMethodAllocationBody allocation =
            view.FactOf(LibraryCallGraphAllocationProducer.Instance);
        ImmutableArray<DirectCall> directCalls =
            ExactMethodAnalysisProducer.CollectDirectCalls(
                method,
                allocation.Facts,
                out _);
        BodySignals? bodySignals = null;

        if (method.Method is { } identity
            && method.Context is { } context)
        {
            bodySignals = BodySignalAnalysis.Collect(
                context,
                operandToken =>
                    allocation.Allocations.Any(
                        occurrence =>
                            occurrence.Kind == AllocationKind.Box
                            && occurrence.OperandToken
                                == operandToken
                            && occurrence.CountsAsHeapAllocation));
        }

        return new(
            method.Method,
            method.HasManagedBody,
            directCalls,
            method.SafetyEvidence,
            method.Diagnostics.IsEmpty
                && HasSignals(bodySignals)
                ? bodySignals
                : null,
            allocation.InAssemblyTypeIsException,
            allocation.Facts?.NonHeapNewObjectOperandTokens
                ?? new HashSet<int>(),
            method.Diagnostics);
    }

    internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<LibraryCallGraphBodyEvidence>();

    internal override ImmutableArray<LibraryCallGraphBodyEvidence>.Builder
        Accumulate(
            ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
            LibraryCallGraphBodyEvidence fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override LibraryCallGraphProducerResult Complete(
        ImmutableArray<LibraryCallGraphBodyEvidence>.Builder accumulator,
        MethodDefinitionCompletionView completion)
    {
        ImmutableArray<LibraryCallGraphBodyEvidence> bodies =
            accumulator.ToImmutable();
        var bodySignals = new Dictionary<int, BodySignals>();
        var exceptionTypes =
            new Dictionary<(string Namespace, string Name), bool>();
        var nonHeapNewObjectTokens = new HashSet<int>();
        var declaredSources = new Dictionary<int, MethodIdentity>();
        var declaredSourceSuppressedMethodTokens = new HashSet<int>();
        Dictionary<int, MethodIdentity> methodsByToken =
            bodies
                .Select(static body => body.Method)
                .OfType<MethodIdentity>()
                .ToDictionary(
                    static method => method.MetadataToken);
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var unsafeEvidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();

        foreach (LibraryCallGraphBodyEvidence body in bodies)
        {
            if (body.Method is not { } method)
                continue;
            if (body.BodySignals is { } signals)
                bodySignals.Add(method.MetadataToken, signals);
            foreach (KeyValuePair<
                (string Namespace, string Name),
                bool> classification in body.InAssemblyTypeIsException)
            {
                exceptionTypes.TryAdd(
                    classification.Key,
                    classification.Value);
            }
            nonHeapNewObjectTokens.UnionWith(
                body.NonHeapNewObjOperandTokens);
            MethodIdentity? owner = null;
            bool ownerSuppressesOpportunities = false;
            if (completion.DeclaredSourceOf(
                    MetadataTokens.MethodDefinitionHandle(
                        method.MetadataToken))
                    is MethodDefinitionDeclaredSource.Generated generated
                && methodsByToken.TryGetValue(
                    MetadataTokens.GetToken(
                        generated.Origin.DeclaredOwner),
                    out MethodIdentity? declaredOwner)
                && (generated.Origin.Kind
                        == MethodDefinitionGeneratedExpansionOriginKind
                            .LiftedExecutionBody
                    || LibraryBodyAsyncSiblingSignatureMatcher
                        .IsAsyncReturnType(declaredOwner.ReturnType)
                    || declaredOwner.ReturnType.Equals(
                        TypeRef.CoreLib("System", "Void"))))
            {
                owner = declaredOwner;
                ownerSuppressesOpportunities =
                    generated.Origin
                        .DeclaredOwnerSuppressesOpportunities;
            }
            ImmutableArray<DirectCall> normalizedCalls =
            [
                .. body.DirectCalls.Select(call =>
                    owner is null
                        ? call
                        : call with
                        {
                            Caller = owner,
                        }),
            ];
            calls.AddRange(normalizedCalls);
            unsafeEvidence.AddRange(
                ReconcileCallSafetyEvidence(
                    body.UnsafeEvidence,
                    body.DirectCalls));
            if (owner is not null)
            {
                declaredSources.Add(
                    method.MetadataToken,
                    owner);
                if (ownerSuppressesOpportunities)
                {
                    declaredSourceSuppressedMethodTokens.Add(
                        method.MetadataToken);
                }
            }
        }

        return new(
            [
                .. bodies
                    .Select(static body => body.Method)
                    .OfType<MethodIdentity>(),
            ],
            [
                .. bodies
                    .Where(static body => body.HasManagedBody)
                    .Select(static body => body.Method)
                    .OfType<MethodIdentity>(),
            ],
            calls.ToImmutable(),
            unsafeEvidence.ToImmutable(),
            bodySignals,
            exceptionTypes,
            nonHeapNewObjectTokens,
            declaredSources,
            declaredSourceSuppressedMethodTokens,
            [
                .. bodies.SelectMany(
                    static body => body.Diagnostics),
            ]);
    }

    static bool HasSignals(BodySignals? signals) =>
        signals is
        {
            Newarr: > 0,
        }
        or
        {
            Throws: > 0,
        }
        or
        {
            Catches: > 0,
        }
        or
        {
            Finallys: > 0,
        }
        or
        {
            Boxes: > 0,
        };

    static ImmutableArray<UnsafeEvidence> ReconcileCallSafetyEvidence(
        ImmutableArray<UnsafeEvidence> evidence,
        ImmutableArray<DirectCall> calls)
    {
        if (calls.IsDefaultOrEmpty)
            return evidence;

        IEnumerable<UnsafeEvidence> nonCallEvidence =
            evidence.IsDefaultOrEmpty
                ? []
                : evidence.Where(
                    static item => item.Reason != "Unsafe call");
        IEnumerable<UnsafeEvidence> callEvidence =
            calls.Where(
                    static call =>
                        call.Kind != CallKind.CallIndirect)
                .Select(call =>
                    MethodSafetyAnalysis.InspectCall(
                        call.Caller,
                        call.Callee,
                        call.Kind,
                        call.ILOffset,
                        call.OperandToken,
                        call.TargetCallerUnsafeMode))
                .OfType<UnsafeEvidence>();

        return
        [
            .. nonCallEvidence
                .Concat(callEvidence)
                .OrderBy(static item =>
                    item.ILOffset ?? int.MinValue),
        ];
    }
}
