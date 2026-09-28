using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using InertText;

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

    /// <summary>Tier 1: the method's attribute and implementation flags.</summary>
    Flags = 8,

    /// <summary>Tier 1: in-place comparison of the method's and its type's names.</summary>
    NameComparison = 16,

    /// <summary>Tier 1: in-place match of the method's custom attribute types.</summary>
    AttributeTypeMatch = 32,

    /// <summary>Tier 1: a memoized yes/no walk of the method's signature shape.</summary>
    SignatureShape = 64,

    /// <summary>
    /// Tier 2: identity text (declaring type, signature, anchor, return type,
    /// module) through the gate's budgeted identity decoder, as <c>InertString</c>.
    /// Declaring it arms the gate's identity budget.
    /// </summary>
    IdentityText = 128,
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

    /// <summary>
    /// Runs this producer's whole pass as a closed-query kernel when it is
    /// the only producer visiting. Returns false when it has none.
    /// </summary>
    internal virtual bool RunKernel(
        MethodDefinitionExecution.ProducerState state,
        MetadataReader reader,
        PEReader peReader,
        LibraryMethodAnalysisRunner? lookup,
        MethodRowGate gate,
        out int unitsVisited)
    {
        unitsVisited = 0;
        return false;
    }

    /// <summary>Whether this producer declares a type-scope predicate.</summary>
    internal virtual bool HasTypeScope => false;

    /// <summary>
    /// The type-scope predicate: units in a type outside it are out of this
    /// producer's scope and of every producer guarded by it.
    /// </summary>
    internal virtual bool TypeInScope(MetadataReader reader, TypeDefinition type) => true;

    /// <summary>Whether this producer classifies units for dependents' scope guards.</summary>
    internal virtual bool ClassifiesUnits => false;

    /// <summary>
    /// A scope guard on the source's method-row gate, or null. It adds no
    /// dependency, so a producer guarded only by the gate can run as a kernel.
    /// </summary>
    internal virtual SourceGateGuard? SourceGate => null;

    /// <summary>The unit's class, 0 to 63, derived from its fact; used only when <see cref="ClassifiesUnits"/>.</summary>
    internal virtual int UnitClass(TFact fact) => 0;

    SourceGateGuard? IMethodDefinitionProducer.SourceGate => SourceGate;

    MethodDefinitionExecution.ProducerState IMethodDefinitionProducer.CreateState(
        MethodDefinitionExecution execution,
        ProducerTerminal terminal,
        ImmutableArray<int> dependencies,
        UnitFactRetention retention) =>
        new State(this, execution, terminal, dependencies, retention);

    /// <summary>
    /// A producer's per-execution state, typed by its fact, accumulator, and
    /// result. The executor reaches it through a virtual call, and a kernel
    /// folds in its own loop and sets the accumulator once.
    /// </summary>
    internal sealed class State : MethodDefinitionExecution.ProducerState<TResult>
    {
        readonly MethodDefinitionProducer<TFact, TAccumulator, TResult> _producer;
        TAccumulator _accumulator;

        // The plan decides retention: the current unit's fact in one slot
        // when every reader visits in the same pass, every unit's otherwise.
        readonly bool _keepCurrent;
        readonly Dictionary<int, TFact>? _factsByUnit;
        int _currentToken;
        TFact _currentFact = default!;

        readonly bool _classifies;
        int _classToken;
        int _unitClass;

        public State(
            MethodDefinitionProducer<TFact, TAccumulator, TResult> producer,
            MethodDefinitionExecution execution,
            ProducerTerminal terminal,
            ImmutableArray<int> dependencies,
            UnitFactRetention retention)
            : base(execution, producer, producer.Layers, terminal, dependencies)
        {
            _producer = producer;
            _accumulator = producer.Seed();
            _keepCurrent = retention == UnitFactRetention.CurrentUnit;
            _factsByUnit = retention == UnitFactRetention.AllUnits ? [] : null;
            _classifies = producer.ClassifiesUnits;
            HasTypeScope = producer.HasTypeScope;
            SourceGate = producer.SourceGate;
        }

        public override SourceGateGuard? SourceGate { get; }

        /// <summary>Sets the folded accumulator; a kernel folds in its own loop.</summary>
        internal void SetAccumulator(TAccumulator accumulator) => _accumulator = accumulator;

        public override bool ClassifiesUnits => _classifies;

        public override bool RetainsFacts => _keepCurrent || _factsByUnit is not null;

        public override bool HasTypeScope { get; }

        public override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
            _producer.TypeInScope(reader, type);

        public override bool UnitClassIn(int unitToken, ulong acceptedClasses) =>
            _classToken == unitToken
            && ((acceptedClasses >> _unitClass) & 1) != 0;

        public override bool TryRunKernel(
            MetadataReader reader,
            PEReader peReader,
            LibraryMethodAnalysisRunner? lookup,
            MethodRowGate gate,
            out int unitsVisited) =>
            _producer.RunKernel(this, reader, peReader, lookup, gate, out unitsVisited);

        public override bool Visit(scoped MethodDefinitionView view)
        {
            TFact fact = _producer.Visit(view);
            _accumulator = _producer.Accumulate(_accumulator, fact);
            if (_classifies)
            {
                _classToken = view.Token;
                _unitClass = _producer.UnitClass(fact);
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

            return _producer.Settles(fact);
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

        public override void Complete(MethodDefinitionCompletionView completion) =>
            SetResult(_producer.Complete(_accumulator, completion));
    }
}

internal interface IMethodDefinitionProducer
{
    MethodDefinitionLayers Layers { get; }

    SourceGateGuard? SourceGate { get; }

    MethodDefinitionExecution.ProducerState CreateState(
        MethodDefinitionExecution execution,
        ProducerTerminal terminal,
        ImmutableArray<int> dependencies,
        UnitFactRetention retention);
}

/// <summary>
/// A scoped, read-only borrow of one method-definition unit, valid only while
/// the visit runs (a snapshot-callback borrow). It reads the row through the
/// source's method-row gate, and exposes only the fields, layers, same-unit
/// facts, and results its reader declared.
/// </summary>
public readonly ref struct MethodDefinitionView
{
    const MethodDefinitionLayers DomainLayers =
        MethodDefinitionLayers.Body | MethodDefinitionLayers.ModuleLookup;

    readonly ref MethodDefinitionUnit _unit;
    readonly MethodDefinitionExecution.ProducerState? _producer;
    readonly MethodDefinitionLayers _declared;
    readonly string _owner;

    internal MethodDefinitionView(
        ref MethodDefinitionUnit unit,
        MethodDefinitionExecution.ProducerState producer)
    {
        _unit = ref unit;
        _producer = producer;
        _declared = producer.Layers;
        _owner = producer.Producer.Identity;
    }

    internal MethodDefinitionView(
        ref MethodDefinitionUnit unit,
        MethodRowClassifier classifier)
    {
        _unit = ref unit;
        _producer = null;
        _declared = classifier.Fields;
        _owner = classifier.Identity;
    }

    /// <summary>The unit's MethodDef metadata token.</summary>
    public int Token => MetadataTokens.GetToken(_unit.MethodHandle);

    // Raw rows are for producers with a domain layer, whose own probes read
    // them; every other reader goes through the gate's accessors.

    internal TypeDefinitionHandle TypeHandle
    {
        get
        {
            RequireDomain();
            return _unit.TypeHandle;
        }
    }

    internal TypeDefinition TypeDefinition
    {
        get
        {
            RequireDomain();
            return _unit.TypeDefinition;
        }
    }

    internal MethodDefinitionHandle MethodHandle
    {
        get
        {
            RequireDomain();
            return _unit.MethodHandle;
        }
    }

    internal MethodDefinition MethodDefinition
    {
        get
        {
            RequireDomain();
            return _unit.MethodDefinition;
        }
    }

    /// <summary>Tier 1: the method's attributes.</summary>
    public MethodAttributes Attributes
    {
        get
        {
            Require(MethodDefinitionLayers.Flags);
            return _unit.Gate.Attributes;
        }
    }

    /// <summary>Tier 1: the method's implementation attributes.</summary>
    public MethodImplAttributes ImplAttributes
    {
        get
        {
            Require(MethodDefinitionLayers.Flags);
            return _unit.Gate.ImplAttributes;
        }
    }

    /// <summary>Tier 1: whether the method's name starts with <paramref name="prefix"/>, compared in place.</summary>
    public bool NameStartsWith(string prefix)
    {
        Require(MethodDefinitionLayers.NameComparison);
        return _unit.Gate.NameStartsWith(prefix);
    }

    /// <summary>Tier 1: whether the declaring type's name starts with <paramref name="prefix"/>, compared in place.</summary>
    public bool DeclaringTypeNameStartsWith(string prefix)
    {
        Require(MethodDefinitionLayers.NameComparison);
        return _unit.Gate.DeclaringTypeNameStartsWith(prefix);
    }

    /// <summary>Tier 1: whether a custom attribute on the method has the target type, matched in place.</summary>
    public bool HasAttributeOfType(MetadataTypeNameTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Require(MethodDefinitionLayers.AttributeTypeMatch);
        return _unit.Gate.HasAttributeOfType(target);
    }

    /// <summary>Tier 1: whether the method's return or a parameter type contains a pointer.</summary>
    public bool SignatureHasPointer
    {
        get
        {
            Require(MethodDefinitionLayers.SignatureShape);
            return _unit.Gate.SignatureHasPointer();
        }
    }

    /// <summary>Tier 2: the row's identity text, through the gate's identity budget.</summary>
    public MethodRowIdentity Identity
    {
        get
        {
            Require(MethodDefinitionLayers.IdentityText);
            return _unit.Gate.Identity();
        }
    }

    /// <summary>Tier 2: the row's P/Invoke import module, or null.</summary>
    public InertString? PInvokeModuleName
    {
        get
        {
            Require(MethodDefinitionLayers.IdentityText);
            return _unit.Gate.PInvokeModuleName();
        }
    }

    void Require(MethodDefinitionLayers field)
    {
        if ((_declared & field) == 0)
            throw MethodRowGate.Undeclared(_owner, field);
    }

    void RequireDomain()
    {
        if ((_declared & DomainLayers) == 0)
        {
            throw new ProducerContractException(
                $"Producer '{_owner}' did not declare a domain layer, so it "
                + "reads the row through the method-row gate.");
        }
    }

    MethodDefinitionExecution.ProducerState Producer =>
        _producer ?? throw new ProducerContractException(
            $"Gate classifier '{_owner}' reads only Tier 1 fields.");

    /// <summary>
    /// The module-metadata lookup, an execution-scoped library input. A
    /// producer that did not declare it cannot obtain it.
    /// </summary>
    internal LibraryMethodAnalysisRunner Lookup
    {
        get
        {
            MethodDefinitionExecution.ProducerState producer = Producer;
            if (!producer.HasLookupLayer)
            {
                throw new ProducerContractException(
                    $"Producer '{_owner}' did not "
                    + "declare the module lookup.");
            }

            producer.CountUnit(ref producer.LastLookupUnit, Token, ref producer.LookupUses);
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
        MethodDefinitionExecution.ProducerState producer = Producer;
        if (!producer.HasBodyLayer)
        {
            throw new ProducerContractException(
                $"Producer '{_owner}' did not "
                + "declare the body layer.");
        }

        producer.CountUnit(ref producer.LastBodyUnit, Token, ref producer.BodyAcquisitions);
        return _unit.GetBody();
    }

    /// <summary>The same-unit fact of a declared visit dependency.</summary>
    public TFact FactOf<TFact, TAccumulator, TResult>(
        MethodDefinitionProducer<TFact, TAccumulator, TResult> dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return MethodDefinitionExecution.FactFor(Producer, dependency, Token);
    }

    /// <summary>The completed result of a declared result dependency.</summary>
    public ProducerResult<TResult> ResultOf<TResult>(
        ProducerDeclaration<TResult> dependency)
    {
        MethodDefinitionExecution.ProducerState producer = Producer;
        producer.Require(
            dependency,
            ProducerDependencyKind.VisitNeedsResult);
        return producer.Execution.ResultOf(dependency);
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
    LibraryMethodAnalysisRunner? lookup,
    MethodRowGate gate)
{
    readonly MetadataReader _reader = reader;
    readonly PEReader _peReader = peReader;
    readonly LibraryMethodAnalysisRunner? _lookup = lookup;
    MethodBodyBlock? _body;

    /// <summary>The source's method-row gate, positioned on this unit.</summary>
    public readonly MethodRowGate Gate = gate;

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
        Gate.MoveTo(typeHandle, typeDefinition, methodHandle, MethodDefinition);
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
