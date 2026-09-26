using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

/// <summary>Layers of a method-definition unit a producer may request.</summary>
[Flags]
public enum MethodDefinitionLayers
{
    /// <summary>The definition's metadata, signature, and declaring type. Always visited.</summary>
    Declaration = 1,

    /// <summary>
    /// The managed IL body and local signature, acquired on demand during the
    /// visit, after the declaration.
    /// </summary>
    Body = 2,
}

/// <summary>
/// A producer over method-definition units, the first unit kind adopted under
/// <c>docs/design/library-body-analysis-service.md#producer-planning-adoption</c>.
/// The declaration is stateless; per-execution state lives in the run the
/// executor creates.
/// </summary>
public abstract class MethodDefinitionProducer<TFact, TResult>
    : ProducerDeclaration<TResult>, IMethodDefinitionProducer
{
    private protected MethodDefinitionProducer(
        string identity,
        int version,
        int tier,
        MethodDefinitionLayers layers,
        Func<IReadOnlyList<ProducerDependency>>? dependencies = null,
        string? parameters = null)
        : base(identity, version, tier, dependencies, parameters)
    {
        Layers = layers | MethodDefinitionLayers.Declaration;
    }

    /// <summary>The layers this producer may acquire; the declaration layer is always included.</summary>
    public MethodDefinitionLayers Layers { get; }

    /// <summary>Visits one unit and returns its fact. Throwing a recoverable failure fails the producer at this unit.</summary>
    internal abstract TFact Visit(scoped MethodDefinitionView view);

    /// <summary>Folds the facts of the visited units into the published result.</summary>
    internal abstract TResult Complete(
        IReadOnlyList<TFact> facts,
        MethodDefinitionCompletionView completion);

    /// <summary>Whether a unit fact settles an Exists terminal for this producer.</summary>
    internal virtual bool Settles(TFact fact) => false;

    IMethodDefinitionProducerRun IMethodDefinitionProducer.CreateRun() =>
        new Run(this);

    sealed class Run(MethodDefinitionProducer<TFact, TResult> producer)
        : IMethodDefinitionProducerRun
    {
        readonly List<TFact> _facts = [];
        readonly Dictionary<int, TFact> _factsByUnit = [];

        public ProducerDeclaration Producer => producer;

        public bool Visit(scoped MethodDefinitionView view)
        {
            TFact fact = producer.Visit(view);
            _facts.Add(fact);
            _factsByUnit[view.Token] = fact;
            return producer.Settles(fact);
        }

        public bool TryGetFact(int unitToken, out object? fact)
        {
            bool found = _factsByUnit.TryGetValue(unitToken, out TFact? value);
            fact = value;
            return found;
        }

        public object? Complete(MethodDefinitionCompletionView completion) =>
            producer.Complete(_facts, completion);
    }
}

internal interface IMethodDefinitionProducer
{
    MethodDefinitionLayers Layers { get; }

    IMethodDefinitionProducerRun CreateRun();
}

internal interface IMethodDefinitionProducerRun
{
    ProducerDeclaration Producer { get; }

    bool Visit(scoped MethodDefinitionView view);

    bool TryGetFact(int unitToken, out object? fact);

    object? Complete(MethodDefinitionCompletionView completion);
}

/// <summary>
/// A scoped, read-only borrow of one method-definition unit, valid only while
/// the visit runs (a snapshot-callback borrow). It exposes only the layers,
/// same-unit facts, and results the visiting producer declared.
/// </summary>
public readonly ref struct MethodDefinitionView
{
    readonly MethodDefinitionUnit _unit;
    readonly MethodDefinitionExecution.ProducerState _producer;

    internal MethodDefinitionView(
        MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState producer)
    {
        _unit = unit;
        _producer = producer;
    }

    /// <summary>The unit's MethodDef metadata token.</summary>
    public int Token => MetadataTokens.GetToken(_unit.MethodHandle);

    internal TypeDefinitionHandle TypeHandle => _unit.TypeHandle;

    internal TypeDefinition TypeDefinition => _unit.TypeDefinition;

    internal MethodDefinitionHandle MethodHandle => _unit.MethodHandle;

    internal MethodDefinition MethodDefinition => _unit.MethodDefinition;

    /// <summary>The module-metadata lookup, an execution-scoped library input.</summary>
    internal LibraryMethodAnalysisRunner Lookup => _unit.Lookup;

    /// <summary>Whether the unit has a managed IL body to acquire.</summary>
    internal bool HasManagedBody =>
        _unit.MethodDefinition.RelativeVirtualAddress != 0
        && LibraryMethodAnalysisRunner.HasManagedIlBody(
            _unit.MethodDefinition.ImplAttributes);

    /// <summary>
    /// Acquires the body layer on demand. A producer that did not declare the
    /// body layer cannot obtain it.
    /// </summary>
    internal MethodBodyBlock GetBody()
    {
        if (!_producer.Declaration.Layers.HasFlag(
                MethodDefinitionLayers.Body))
        {
            throw new ProducerContractException(
                $"Producer '{_producer.Run.Producer.Identity}' did not "
                + "declare the body layer.");
        }

        _producer.BodyAcquisitions++;
        return _unit.GetBody();
    }

    /// <summary>The same-unit fact of a declared visit dependency.</summary>
    public TFact FactOf<TFact, TResult>(
        MethodDefinitionProducer<TFact, TResult> dependency)
    {
        _producer.Require(
            dependency,
            ProducerDependencyKind.VisitNeedsVisit);
        return (TFact)_producer.Execution.FactFor(dependency, Token)!;
    }

    /// <summary>The completed result of a declared result dependency.</summary>
    public ProducerResult<TResult> ResultOf<TResult>(
        ProducerDeclaration<TResult> dependency)
    {
        _producer.Require(
            dependency,
            ProducerDependencyKind.VisitNeedsResult);
        return _producer.Execution.ResultOf(dependency);
    }
}

/// <summary>A scoped view for a producer's completion: its declared result dependencies only.</summary>
public readonly ref struct MethodDefinitionCompletionView
{
    readonly MethodDefinitionExecution.ProducerState _producer;

    internal MethodDefinitionCompletionView(
        MethodDefinitionExecution.ProducerState producer) =>
        _producer = producer;

    public ProducerResult<TResult> ResultOf<TResult>(
        ProducerDeclaration<TResult> dependency)
    {
        _producer.Require(
            dependency,
            ProducerDependencyKind.CompletionNeedsResult,
            ProducerDependencyKind.VisitNeedsResult);
        return _producer.Execution.ResultOf(dependency);
    }
}

internal sealed class MethodDefinitionUnit(
    MetadataReader reader,
    PEReader peReader,
    LibraryMethodAnalysisRunner lookup)
{
    MethodBodyBlock? _body;

    public TypeDefinitionHandle TypeHandle { get; private set; }

    public TypeDefinition TypeDefinition { get; private set; }

    public MethodDefinitionHandle MethodHandle { get; private set; }

    public MethodDefinition MethodDefinition { get; private set; }

    public LibraryMethodAnalysisRunner Lookup => lookup;

    public void MoveTo(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle)
    {
        TypeHandle = typeHandle;
        TypeDefinition = typeDefinition;
        MethodHandle = methodHandle;
        MethodDefinition = reader.GetMethodDefinition(methodHandle);
        _body = null;
    }

    public MethodBodyBlock GetBody() =>
        _body ??= peReader.GetMethodBody(
            MethodDefinition.RelativeVirtualAddress);

    public string Label =>
        LibraryMethodAnalysisRunner.MethodLabel(
            reader,
            TypeHandle,
            MethodHandle);
}
