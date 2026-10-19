using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Exact allocation evidence and its shared method-local Analysis state.
/// </summary>
public sealed class ExactMethodAllocationBody
{
    internal ExactMethodAllocationBody(
        int methodToken,
        MethodIdentity? method,
        bool hasManagedBody,
        ImmutableArray<AllocationOccurrence> allocations,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        MethodBodyAnalysisContext? context = null,
        MethodAllocationFacts? facts = null)
    {
        MethodToken = methodToken;
        Method = method;
        HasManagedBody = hasManagedBody;
        Allocations = allocations;
        Diagnostics = diagnostics;
        Context = context;
        Facts = facts;
        InAssemblyTypeIsException =
            facts?.InAssemblyTypeIsException
            ?? new Dictionary<
                (string Namespace, string Name),
                bool>();
    }

    public int MethodToken { get; }

    public MethodIdentity? Method { get; }

    public bool HasManagedBody { get; }

    public ImmutableArray<AllocationOccurrence> Allocations { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public IReadOnlyDictionary<
        (string Namespace, string Name),
        bool> InAssemblyTypeIsException { get; }

    internal MethodBodyAnalysisContext? Context { get; }

    internal MethodAllocationFacts? Facts { get; }
}

/// <summary>
/// Exact allocation and intrinsic optimization evidence for one MethodDef.
/// </summary>
public sealed record ExactMethodOptimizationBody(
    ExactMethodAllocationBody Allocation,
    ImmutableArray<OptimizationOpportunity> Opportunities)
{
    public bool SuppressesDerivedOpportunities { get; init; }

    public bool RequiresDeclaredOwner { get; init; }

    internal MethodBodyAnalysisContext? ConsumedContext { get; init; }

    internal MethodAllocationFacts? ConsumedFacts { get; init; }
}

/// <summary>
/// Detached exact-method allocation and optimization rows.
/// </summary>
public sealed record ExactMethodOptimizationProducerResult(
    ImmutableArray<ExactMethodOptimizationBody> Bodies);

/// <summary>
/// Exact safety, allocation, and optimization evidence from one Method-source
/// execution.
/// </summary>
public sealed record ExactMethodAnalysisBundleBody(
    ExactMethodAnalysisBody Method,
    ExactMethodOptimizationBody Optimization)
{
    public ImmutableArray<DirectCall> DirectCalls { get; init; } = [];

    public MethodSignals Signals { get; init; } = MethodSignals.None;

    public MethodIdentity? DeclaredOwner { get; init; }

    public bool DeclaredOwnerSuppressesOpportunities { get; init; }

    internal int? DeclaredOwnerToken { get; init; }
}

/// <summary>
/// Detached exact-method Analysis evidence composed from focused producers.
/// </summary>
public sealed record ExactMethodAnalysisBundleProducerResult(
    ImmutableArray<ExactMethodAnalysisBundleBody> Bodies);

/// <summary>
/// Owns exact MethodDef allocation discovery and escape classification.
/// </summary>
public sealed class ExactMethodAllocationProducer :
    MethodDefinitionProducer<
        ExactMethodAllocationBody,
        ImmutableArray<ExactMethodAllocationBody>.Builder,
        ImmutableArray<ExactMethodAllocationBody>>
{
    ExactMethodAllocationProducer()
        : base(
            "Exact Method Allocation",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.MethodAnalysis,
            dependencies: static () =>
            [
                new(
                    ExactMethodAnalysisProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
    }

    public static ExactMethodAllocationProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [
            MethodBodyAnalyzerDeclarations.BoundedFlow,
        ];

    internal override ExactMethodAllocationBody Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAnalysisBody analysis =
            view.FactOf(ExactMethodAnalysisProducer.Instance);
        int token = analysis.MethodToken;
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        diagnostics.AddRange(analysis.Diagnostics);
        if (analysis.Method is not { } method
            || !analysis.HasManagedBody
            || analysis.Context is not { } context)
        {
            return Empty(
                token,
                analysis.Method,
                analysis.HasManagedBody,
                diagnostics);
        }

        MethodAllocationFacts? facts = null;
        try
        {
            facts = MethodAllocationFacts.Create(context);
            using MethodAnalysisQuery query =
                view.BeginMethodAnalysis(
                    method,
                    context.Instructions);
            facts.Collect(query.Resolver);
            if (facts.CollectionFailure is { } failure)
            {
                diagnostics.Add(
                    new(
                        token,
                        ExactMethodAnalysisProducer.MethodLabel(method),
                        "Allocation analysis stopped at "
                            + $"IL_{failure.Offset:x4} "
                            + $"({failure.Operation}): "
                            + failure.Message));
            }
            return new(
                token,
                method,
                hasManagedBody: true,
                facts.ClassifiedOccurrences,
                diagnostics.ToImmutable(),
                context,
                facts);
        }
        catch (Exception ex)
            when (ex
                    is not
                        MethodDefinitionTerminalWorkLimitExceededException
                && LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
        {
            diagnostics.Add(
                new(
                    token,
                    ExactMethodAnalysisProducer.MethodLabel(method),
                    ex.Message));
            return new(
                token,
                method,
                hasManagedBody: true,
                facts?.ClassifiedOccurrences ?? [],
                diagnostics.ToImmutable(),
                context,
                facts);
        }
    }

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

    static ExactMethodAllocationBody Empty(
        int token,
        MethodIdentity? method,
        bool hasManagedBody,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics) =>
        new(
            token,
            method,
            hasManagedBody,
            [],
            diagnostics.ToImmutable());
}

/// <summary>
/// Produces intrinsic optimization rows from the allocation owner's exact
/// context and allocation facts.
/// </summary>
public sealed class ExactMethodOptimizationProducer :
    MethodDefinitionProducer<
        ExactMethodOptimizationBody,
        ImmutableArray<ExactMethodOptimizationBody>.Builder,
        ExactMethodOptimizationProducerResult>
{
    ExactMethodOptimizationProducer()
        : base(
            "Exact Method Optimization",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.MethodAnalysis,
            dependencies: static () =>
            [
                new(
                    ExactMethodAllocationProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
    }

    public static ExactMethodOptimizationProducer Instance { get; } =
        new();

    internal override ExactMethodOptimizationBody Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAllocationBody allocation =
            view.FactOf(ExactMethodAllocationProducer.Instance);
        if (allocation.Method is not { } method
            || allocation.Context is not { } context
            || allocation.Facts is not { } facts
            || (!allocation.Diagnostics.IsEmpty
                && facts.CollectionFailure is null))
        {
            return new(allocation, []);
        }
        if (SuppressOpportunities(view, method))
        {
            return new(allocation, [])
            {
                SuppressesDerivedOpportunities = true,
            };
        }

        bool requiresDeclaredOwner =
            CompilerGeneratedNames.IsLocalFunctionOrLambda(
                method.Name);
        using MethodAnalysisQuery query =
            view.BeginMethodAnalysis(
                method,
                context.Instructions);
        return new(
            allocation,
            OptimizationOpportunityAnalysis.Collect(
                facts,
                query.Resolver))
        {
            ConsumedContext = context,
            ConsumedFacts = facts,
            RequiresDeclaredOwner = requiresDeclaredOwner,
            SuppressesDerivedOpportunities =
                requiresDeclaredOwner,
        };
    }

    static bool SuppressOpportunities(
        scoped MethodDefinitionView view,
        MethodIdentity method)
    {
        MetadataReader reader = view.Reader;
        CustomAttributeHandleCollection attributes =
            view.MethodDefinition.GetCustomAttributes();
        if (LibraryBodyPrimaryMetadataResolver
                .HasGeneratedCodeAttribute(reader, attributes)
            || !CompilerGeneratedNames.IsLocalFunctionOrLambda(
                    method.Name)
                && LibraryBodyPrimaryMetadataResolver
                    .HasCompilerGeneratedAttribute(reader, attributes)
            || LibraryMethodAnalysisRunner.IsBlazorRenderMethod(
                method))
        {
            return true;
        }

        TypeDefinitionHandle current = view.TypeHandle;
        int remaining =
            MetadataSafetyPolicy.MaxRelationshipNodes;
        while (!current.IsNil && remaining-- > 0)
        {
            TypeDefinition type = reader.GetTypeDefinition(current);
            if (LibraryBodyPrimaryMetadataResolver
                .HasGeneratedCodeAttribute(
                    reader,
                    type.GetCustomAttributes()))
            {
                return true;
            }

            current = type.GetDeclaringType();
        }

        return !current.IsNil;
    }

    internal override ImmutableArray<ExactMethodOptimizationBody>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodOptimizationBody>();

    internal override ImmutableArray<ExactMethodOptimizationBody>.Builder
        Accumulate(
            ImmutableArray<ExactMethodOptimizationBody>.Builder accumulator,
            ExactMethodOptimizationBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ExactMethodOptimizationProducerResult Complete(
        ImmutableArray<ExactMethodOptimizationBody>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        new(accumulator.ToImmutable());
}

/// <summary>
/// Composes the focused exact-method producers without rebuilding their
/// same-unit facts.
/// </summary>
public sealed class ExactMethodAnalysisBundleProducer :
    MethodDefinitionProducer<
        ExactMethodAnalysisBundleBody,
        ImmutableArray<ExactMethodAnalysisBundleBody>.Builder,
        ExactMethodAnalysisBundleProducerResult>
{
    readonly bool _readsDeclaredSource;

    ExactMethodAnalysisBundleProducer(
        string name,
        bool readsDeclaredSource)
        : base(
            name,
            version: 1,
            tier: 0,
            layers: 0,
            dependencies: static () =>
            [
                new(
                    ExactMethodAnalysisProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
                new(
                    ExactMethodOptimizationProducer.Instance,
                    ProducerDependencyKind.VisitNeedsVisit),
            ])
    {
        _readsDeclaredSource = readsDeclaredSource;
    }

    public static ExactMethodAnalysisBundleProducer Instance { get; } =
        new(
            "Exact Method Analysis Bundle",
            readsDeclaredSource: false);

    public static ExactMethodAnalysisBundleProducer
        DeclaredSourceInstance { get; } =
        new(
            "Exact Method Analysis Bundle with Declared Source",
            readsDeclaredSource: true);

    internal override ExactMethodAnalysisBundleBody Visit(
        scoped MethodDefinitionView view)
    {
        ExactMethodAnalysisBody method =
            view.FactOf(ExactMethodAnalysisProducer.Instance);
        ExactMethodOptimizationBody optimization =
            view.FactOf(ExactMethodOptimizationProducer.Instance);
        ImmutableArray<DirectCall> directCalls =
            optimization.Allocation.Facts is { } facts
                ?
                [
                    .. method.DirectCalls.Select(
                        call => call with
                        {
                            Multiplicity =
                                facts.MultiplicityAt(
                                    call.ILOffset),
                        }),
                ]
                : method.DirectCalls;
        BodySignals bodySignals =
            method.Context is { } context
                ? BodySignalAnalysis.Collect(
                    context,
                    token =>
                        optimization.Allocation.Allocations.Any(
                            occurrence =>
                                occurrence.Kind
                                    == AllocationKind.Box
                                && occurrence.OperandToken
                                    == token
                                && occurrence
                                    .CountsAsHeapAllocation))
                : default;
        Dictionary<int, MethodSignals> signals =
            MethodSignalAnalysis.Collect(
                directCalls,
                method.SafetyEvidence,
                method.Method is { } identity
                    ? new Dictionary<int, BodySignals>
                    {
                        [identity.MetadataToken] =
                            bodySignals,
                    }
                    : null,
                method.Method is { } allocationMethod
                    ? new Dictionary<
                        int,
                        ImmutableArray<AllocationOccurrence>>
                    {
                        [allocationMethod.MetadataToken] =
                            optimization.Allocation.Allocations,
                    }
                    : null,
                optimization.Allocation.Facts?
                    .InAssemblyTypeIsException);
        return new(method, optimization)
        {
            DirectCalls = directCalls,
            Signals =
                method.Method is { } signalMethod
                    && signals.TryGetValue(
                        signalMethod.MetadataToken,
                        out MethodSignals? value)
                        ? value
                        : MethodSignals.None,
            DeclaredOwnerToken =
                _readsDeclaredSource
                    && view.DeclaredSource
                        is MethodDefinitionDeclaredSource.Generated
                            generated
                    ? MetadataTokens.GetToken(
                        generated.Origin.DeclaredOwner)
                    : null,
        };
    }

    internal override ImmutableArray<ExactMethodAnalysisBundleBody>.Builder
        Seed() =>
        ImmutableArray.CreateBuilder<ExactMethodAnalysisBundleBody>();

    internal override ImmutableArray<ExactMethodAnalysisBundleBody>.Builder
        Accumulate(
            ImmutableArray<ExactMethodAnalysisBundleBody>.Builder accumulator,
            ExactMethodAnalysisBundleBody fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ExactMethodAnalysisBundleProducerResult Complete(
        ImmutableArray<ExactMethodAnalysisBundleBody>.Builder accumulator,
        MethodDefinitionCompletionView completion)
    {
        ImmutableArray<ExactMethodAnalysisBundleBody> bodies =
            accumulator.ToImmutable();
        if (!_readsDeclaredSource)
            return new(bodies);

        Dictionary<int, ExactMethodAnalysisBundleBody> byToken =
            bodies.ToDictionary(
                static body => body.Method.MethodToken);
        return new(
        [
            .. bodies.Select(body =>
            {
                if (body.DeclaredOwnerToken is not int ownerToken
                    || !byToken.TryGetValue(
                        ownerToken,
                        out ExactMethodAnalysisBundleBody? owner)
                    || owner.Method.Method is not { } ownerMethod)
                {
                    return body;
                }

                return body with
                {
                    DeclaredOwner = ownerMethod,
                    DeclaredOwnerSuppressesOpportunities =
                        owner.Optimization
                            .SuppressesDerivedOpportunities,
                    DirectCalls =
                    [
                        .. body.DirectCalls.Select(
                            call => call with
                            {
                                Caller = ownerMethod,
                            }),
                    ],
                };
            }),
        ]);
    }
}
