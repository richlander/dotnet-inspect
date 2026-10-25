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

public readonly record struct LibraryCallGraphBodyEvidence(
    MethodIdentity? Method,
    bool HasManagedBody,
    ImmutableArray<DirectCall> DirectCalls,
    ImmutableArray<UnsafeEvidence> UnsafeEvidence,
    BodySignals? BodySignals,
    IReadOnlyDictionary<(string Namespace, string Name), bool>?
        InAssemblyTypeIsException,
    IReadOnlySet<int>? NonHeapNewObjOperandTokens,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

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
            MethodDefinitionLayers.MethodAnalysis,
            dependencies: static () =>
            [
                new(
                    PhysicalMethodAnalysisProducer.Instance,
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
        ImmutableArray<DirectCall> directCalls = [];
        BodySignals? bodySignals = null;
        Dictionary<(string Namespace, string Name), bool>?
            exceptionTypes = null;
        HashSet<int>? nonHeapNewObjectTokens = null;

        if (method.Method is { } identity
            && method.Context is { } context)
        {
            MethodAllocationFacts facts =
                MethodAllocationFacts.Create(context);
            directCalls =
                ExactMethodAnalysisProducer.CollectDirectCalls(
                    method,
                    facts,
                    out _);
            using MethodAnalysisQuery query =
                view.BeginMethodAnalysis(
                    identity,
                    context.Instructions);
            IMethodAllocationResolver resolver = query.Resolver;
            bodySignals = BodySignalAnalysis.Collect(
                context,
                operandToken =>
                    resolver.IsAllocatingValueTypeBox(
                        operandToken,
                        resolver.ResolveType(operandToken)));
            foreach (DirectCall call in directCalls)
            {
                if (call.Kind != CallKind.NewObject)
                    continue;
                if (resolver.ClassifyConstruction(
                        call.OperandToken,
                        call.Callee.DeclaringType)
                    != NewObjectConstructionKind.Heap)
                {
                    (nonHeapNewObjectTokens ??= []).Add(
                        call.OperandToken);
                }
                if (resolver.IsInAssemblyExceptionType(
                        call.OperandToken,
                        call.Callee.DeclaringType)
                    is bool isException)
                {
                    TypeRef definition =
                        call.Callee.DeclaringType.Kind
                            == TypeRefKind.GenericInstance
                        ? call.Callee.DeclaringType.ElementType
                            ?? call.Callee.DeclaringType
                        : call.Callee.DeclaringType;
                    (exceptionTypes ??= []).TryAdd(
                        (definition.Namespace, definition.Name),
                        isException);
                }
            }
        }

        return new(
            method.Method,
            method.HasManagedBody,
            directCalls,
            method.SafetyEvidence,
            HasSignals(bodySignals)
                ? bodySignals
                : null,
            exceptionTypes,
            nonHeapNewObjectTokens,
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
        var bodySignals = new Dictionary<int, BodySignals>();
        var exceptionTypes =
            new Dictionary<(string Namespace, string Name), bool>();
        var nonHeapNewObjectTokens = new HashSet<int>();
        var declaredSources = new Dictionary<int, MethodIdentity>();
        var declaredSourceSuppressedMethodTokens = new HashSet<int>();
        var methodsByToken = new Dictionary<int, MethodIdentity>();
        var declaredMethods =
            ImmutableArray.CreateBuilder<MethodIdentity>();
        var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var unsafeEvidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();

        for (int index = 0; index < accumulator.Count; index++)
        {
            if (accumulator[index].Method is { } method)
                methodsByToken.Add(method.MetadataToken, method);
        }

        for (int index = 0; index < accumulator.Count; index++)
        {
            LibraryCallGraphBodyEvidence body = accumulator[index];
            accumulator[index] = default;
            diagnostics.AddRange(body.Diagnostics);
            if (body.Method is not { } method)
                continue;
            declaredMethods.Add(method);
            if (body.HasManagedBody)
                methods.Add(method);
            if (body.BodySignals is { } signals)
                bodySignals.Add(method.MetadataToken, signals);
            if (body.InAssemblyTypeIsException is { } bodyExceptionTypes)
            {
                foreach (KeyValuePair<
                    (string Namespace, string Name),
                    bool> classification in bodyExceptionTypes)
                {
                    exceptionTypes.TryAdd(
                        classification.Key,
                        classification.Value);
                }
            }
            if (body.NonHeapNewObjOperandTokens is { } bodyNonHeapTokens)
                nonHeapNewObjectTokens.UnionWith(bodyNonHeapTokens);
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
                    || generated.Origin.IsAsyncStateMachine))
            {
                owner = declaredOwner;
                ownerSuppressesOpportunities =
                    generated.Origin
                        .DeclaredOwnerSuppressesOpportunities;
            }
            foreach (DirectCall call in body.DirectCalls)
            {
                calls.Add(
                    owner is null
                        ? call
                        : call with
                        {
                            Caller = owner,
                        });
            }
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
            declaredMethods.ToImmutable(),
            methods.ToImmutable(),
            calls.ToImmutable(),
            unsafeEvidence.ToImmutable(),
            bodySignals,
            exceptionTypes,
            nonHeapNewObjectTokens,
            declaredSources,
            declaredSourceSuppressedMethodTokens,
            diagnostics.ToImmutable());
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
