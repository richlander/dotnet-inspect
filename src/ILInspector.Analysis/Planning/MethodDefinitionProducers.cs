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

    /// <summary>
    /// The module-metadata lookup: a library-scope input, not a layer of the
    /// unit, that resolves tokens and same-image correspondence outside the
    /// unit. Its budget and caches are execution-scoped.
    /// </summary>
    ModuleLookup = 4,
}

/// <summary>
/// A producer over method-definition units, the first unit kind adopted under
/// <c>docs/design/library-body-analysis-service.md#producer-planning-adoption</c>.
/// The declaration is stateless; per-execution state lives in the run the
/// executor creates. Unit facts fold into an accumulator as they are visited,
/// so no execution retains facts its terminal does not need.
/// </summary>
public abstract class MethodDefinitionProducer<TFact, TAccumulator, TResult>
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

    /// <summary>The accumulator before any unit is visited; called once per execution.</summary>
    internal abstract TAccumulator Seed();

    /// <summary>Folds one visited unit's fact into the accumulator.</summary>
    internal abstract TAccumulator Accumulate(TAccumulator accumulator, TFact fact);

    /// <summary>Produces the published result from the folded accumulator.</summary>
    internal abstract TResult Complete(
        TAccumulator accumulator,
        MethodDefinitionCompletionView completion);

    /// <summary>Whether a unit fact settles an Exists terminal for this producer.</summary>
    internal virtual bool Settles(TFact fact) => false;

    /// <summary>Whether this producer classifies units for dependents' scope guards.</summary>
    internal virtual bool ClassifiesUnits => false;

    /// <summary>The unit's class, 0 to 63, derived from its fact; used only when <see cref="ClassifiesUnits"/>.</summary>
    internal virtual int UnitClass(TFact fact) => 0;

    IMethodDefinitionProducerRun IMethodDefinitionProducer.CreateRun(
        UnitFactRetention retention) =>
        new Run(this, retention);

    internal sealed class Run(
        MethodDefinitionProducer<TFact, TAccumulator, TResult> producer,
        UnitFactRetention retention)
        : IMethodDefinitionProducerRun
    {
        TAccumulator _accumulator = producer.Seed();

        // The plan decides retention: the current unit's fact in one slot
        // when every reader visits in the same pass, every unit's otherwise.
        readonly bool _keepCurrent = retention == UnitFactRetention.CurrentUnit;
        readonly Dictionary<int, TFact>? _factsByUnit =
            retention == UnitFactRetention.AllUnits ? [] : null;
        int _currentToken;
        TFact _currentFact = default!;

        readonly bool _classifies = producer.ClassifiesUnits;
        int _classToken;
        int _unitClass;

        public ProducerDeclaration Producer => producer;

        public bool ClassifiesUnits => _classifies;

        public bool UnitClassIn(int unitToken, ulong acceptedClasses) =>
            _classToken == unitToken
            && ((acceptedClasses >> _unitClass) & 1) != 0;

        public bool Visit(scoped MethodDefinitionView view)
        {
            TFact fact = producer.Visit(view);
            _accumulator = producer.Accumulate(_accumulator, fact);
            if (_classifies)
            {
                _classToken = view.Token;
                _unitClass = producer.UnitClass(fact);
            }

            if (_keepCurrent)
            {
                _currentToken = view.Token;
                _currentFact = fact;
            }
            else if (_factsByUnit is not null)
            {
                _factsByUnit[view.Token] = fact;
            }

            return producer.Settles(fact);
        }

        public bool TryGetFact(int unitToken, out TFact fact)
        {
            if (_keepCurrent && _currentToken == unitToken && unitToken != 0)
            {
                fact = _currentFact;
                return true;
            }

            if (_factsByUnit is not null
                && _factsByUnit.TryGetValue(unitToken, out TFact? value))
            {
                fact = value;
                return true;
            }

            fact = default!;
            return false;
        }

        public object? Complete(MethodDefinitionCompletionView completion) =>
            producer.Complete(_accumulator, completion);
    }
}

internal interface IMethodDefinitionProducer
{
    MethodDefinitionLayers Layers { get; }

    IMethodDefinitionProducerRun CreateRun(UnitFactRetention retention);
}

internal interface IMethodDefinitionProducerRun
{
    ProducerDeclaration Producer { get; }

    bool ClassifiesUnits { get; }

    /// <summary>Whether this run classified <paramref name="unitToken"/> into one of <paramref name="acceptedClasses"/>.</summary>
    bool UnitClassIn(int unitToken, ulong acceptedClasses);

    bool Visit(scoped MethodDefinitionView view);

    object? Complete(MethodDefinitionCompletionView completion);
}

/// <summary>
/// A scoped, read-only borrow of one method-definition unit, valid only while
/// the visit runs (a snapshot-callback borrow). It exposes only the layers,
/// same-unit facts, and results the visiting producer declared.
/// </summary>
public readonly ref struct MethodDefinitionView
{
    readonly ref MethodDefinitionUnit _unit;
    readonly MethodDefinitionExecution.ProducerState _producer;

    internal MethodDefinitionView(
        ref MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState producer)
    {
        _unit = ref unit;
        _producer = producer;
    }

    /// <summary>The unit's MethodDef metadata token.</summary>
    public int Token => MetadataTokens.GetToken(_unit.MethodHandle);

    internal TypeDefinitionHandle TypeHandle => _unit.TypeHandle;

    internal TypeDefinition TypeDefinition => _unit.TypeDefinition;

    internal MethodDefinitionHandle MethodHandle => _unit.MethodHandle;

    internal MethodDefinition MethodDefinition => _unit.MethodDefinition;

    /// <summary>
    /// The module-metadata lookup, an execution-scoped library input. A
    /// producer that did not declare it cannot obtain it.
    /// </summary>
    internal LibraryMethodAnalysisRunner Lookup
    {
        get
        {
            if (!_producer.HasLookupLayer)
            {
                throw new ProducerContractException(
                    $"Producer '{_producer.Run.Producer.Identity}' did not "
                    + "declare the module lookup.");
            }

            _producer.CountUnit(ref _producer.LastLookupUnit, Token, ref _producer.LookupUses);
            return _unit.Lookup;
        }
    }

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
        if (!_producer.HasBodyLayer)
        {
            throw new ProducerContractException(
                $"Producer '{_producer.Run.Producer.Identity}' did not "
                + "declare the body layer.");
        }

        _producer.CountUnit(ref _producer.LastBodyUnit, Token, ref _producer.BodyAcquisitions);
        return _unit.GetBody();
    }

    /// <summary>The same-unit fact of a declared visit dependency.</summary>
    public TFact FactOf<TFact, TAccumulator, TResult>(
        MethodDefinitionProducer<TFact, TAccumulator, TResult> dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return MethodDefinitionExecution.FactFor(_producer, dependency, Token);
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

/// <summary>
/// The executor's cursor over method definitions. It is a struct local to the
/// visiting loop, so advancing it writes only to the stack; views borrow it by
/// reference for one visit.
/// </summary>
internal struct MethodDefinitionUnit(
    MetadataReader reader,
    PEReader peReader,
    LibraryMethodAnalysisRunner? lookup)
{
    readonly MetadataReader _reader = reader;
    readonly PEReader _peReader = peReader;
    readonly LibraryMethodAnalysisRunner? _lookup = lookup;
    MethodBodyBlock? _body;

    public TypeDefinitionHandle TypeHandle { get; private set; }

    public TypeDefinition TypeDefinition { get; private set; }

    public MethodDefinitionHandle MethodHandle { get; private set; }

    public MethodDefinition MethodDefinition { get; private set; }

    /// <summary>The module lookup; present whenever a planned producer declared it.</summary>
    public readonly LibraryMethodAnalysisRunner Lookup =>
        _lookup ?? throw new InvalidOperationException(
            "The module lookup was not built because no planned producer declared it.");

    public void MoveTo(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle)
    {
        TypeHandle = typeHandle;
        TypeDefinition = typeDefinition;
        MethodHandle = methodHandle;
        MethodDefinition = _reader.GetMethodDefinition(methodHandle);
        _body = null;
    }

    public MethodBodyBlock GetBody() =>
        _body ??= _peReader.GetMethodBody(
            MethodDefinition.RelativeVirtualAddress);

    public readonly string Label =>
        LibraryMethodAnalysisRunner.MethodLabel(
            _reader,
            TypeHandle,
            MethodHandle);
}
