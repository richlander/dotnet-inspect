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
    MethodIdentity? TargetedDeclaredOwner,
    bool TargetedDeclaredOwnerSuppressesOpportunities,
    bool KeepPhysicalIdentity,
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
                new(
                    ExactMethodAllocationProducer.Instance,
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
            view.FactOf(ExactMethodAllocationProducer.Instance);
        ImmutableArray<DirectCall> directCalls =
            ExactMethodAnalysisProducer.CollectDirectCalls(
                method,
                allocation.Facts,
                out _);
        BodySignals? bodySignals = null;
        MethodIdentity? targetedDeclaredOwner = null;
        bool targetedDeclaredOwnerSuppressesOpportunities = false;
        bool keepPhysicalIdentity = false;

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
        if (method.Method is { } candidate
            && (CompilerGeneratedNames.IsLocalFunctionOrLambda(
                    candidate.Name)
                || candidate.Name == "MoveNext"))
        {
            ExactMethodDeclaredSource declaredSource =
                view.ResolveDeclaredSource(candidate);
            if (declaredSource.IsAuthenticated
                && declaredSource.Owner is { } owner
                && (CompilerGeneratedNames.IsLocalFunctionOrLambda(
                        candidate.Name)
                    || LibraryBodyAsyncSiblingSignatureMatcher
                        .IsAsyncReturnType(owner.ReturnType)
                    || owner.ReturnType.Equals(
                        TypeRef.CoreLib("System", "Void"))))
            {
                targetedDeclaredOwner = declaredSource.Owner;
                targetedDeclaredOwnerSuppressesOpportunities =
                    declaredSource.SuppressesOpportunities;
            }
            else if (candidate.Name == "MoveNext")
            {
                keepPhysicalIdentity = true;
            }
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
            targetedDeclaredOwner,
            targetedDeclaredOwnerSuppressesOpportunities,
            keepPhysicalIdentity,
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
            MethodIdentity? owner =
                body.TargetedDeclaredOwner
                ?? (!body.KeepPhysicalIdentity
                    && completion.DeclaredSourceOf(
                        MetadataTokens.MethodDefinitionHandle(
                            method.MetadataToken))
                        is MethodDefinitionDeclaredSource.Generated generated
                    && methodsByToken.TryGetValue(
                        MetadataTokens.GetToken(
                            generated.Origin.DeclaredOwner),
                        out MethodIdentity? declaredOwner)
                        ? declaredOwner
                        : null);
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
                if (body.TargetedDeclaredOwnerSuppressesOpportunities)
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
