using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One recipe's ephemeral proposal. It may hold IR references — it is never
/// published — and is consumed by <see cref="ClassicInverseAccountant"/>, which
/// either turns it into a detached plan or declines.
/// </summary>
internal sealed class ClassicInverseCandidate
{
    readonly List<ClassicInverseClaim> _claims = [];
    readonly Dictionary<IrNode, string> _protocol =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, ClassicInverseContainerDeclaration> _containers =
        new(ReferenceEqualityComparer.Instance);
    readonly List<ClassicInverseControlRegion> _controlRegions = [];
    readonly Dictionary<string, int> _hoistedLocals = new(StringComparer.Ordinal);
    readonly Dictionary<string, TypeRef> _hoistedTypes = new(StringComparer.Ordinal);
    readonly Dictionary<int, int> _localRemap = [];
    readonly Dictionary<string, int> _parameterFields = new(StringComparer.Ordinal);
    readonly Dictionary<int, IrNode> _localValues = [];

    internal ClassicInverseCandidate(string recipe) => Recipe = recipe;

    internal string Recipe { get; }

    /// <summary>Freshly built output statements; never aliased from the request.</summary>
    internal List<IrNode> Statements { get; } = [];

    internal ImmutableArray<TypeRef> Locals { get; set; } = [];

    internal ImmutableArray<string?> LocalNames { get; set; } = [];

    internal IReadOnlyList<ClassicInverseClaim> Claims => _claims;

    internal IReadOnlyDictionary<IrNode, string> DeclaredProtocol => _protocol;

    internal IReadOnlyDictionary<IrNode, ClassicInverseContainerDeclaration>
        DeclaredContainers => _containers;

    internal IReadOnlyList<ClassicInverseControlRegion> ControlRegions =>
        _controlRegions;

    /// <summary>Hoisted state-machine field name to the output local it became.</summary>
    internal IReadOnlyDictionary<string, int> HoistedLocals => _hoistedLocals;

    /// <summary>Execution-body local slot to the output local it became.</summary>
    internal IReadOnlyDictionary<int, int> LocalRemap => _localRemap;

    /// <summary>Kickoff parameter-transfer field name to its output argument index.</summary>
    internal IReadOnlyDictionary<string, int> ParameterFields => _parameterFields;

    /// <summary>
    /// Execution-body local slots whose value is realized by one output node
    /// rather than by another local — an awaited temporary, or a conditional
    /// merge slot. A read of the slot must correspond to that exact output node.
    /// </summary>
    internal IReadOnlyDictionary<int, IrNode> LocalValueRealizations => _localValues;

    internal void MapLocalValue(int sourceIndex, IrNode output)
    {
        if (!_localValues.TryAdd(sourceIndex, output))
            Sound = false;
    }

    /// <summary>The execution-body local the shell's completion call reads, or -1.</summary>
    internal int ResultLocal { get; set; } = -1;

    /// <summary>False once a recipe hits a state its own rules do not allow.</summary>
    internal bool Sound { get; private set; } = true;

    internal void Unsound() => Sound = false;

    internal void MapParameterField(string field, int argumentIndex)
        => _parameterFields[field] = argumentIndex;

    internal void MapHoistedLocal(string field, int localIndex, TypeRef type)
    {
        if (!_hoistedLocals.TryAdd(field, localIndex))
            Sound = false;
        _hoistedTypes[field] = type;
    }

    /// <summary>The output type of a hoisted state-machine local.</summary>
    internal IReadOnlyDictionary<string, TypeRef> HoistedTypes => _hoistedTypes;

    internal void MapLocal(int sourceIndex, int outputIndex)
    {
        if (!_localRemap.TryAdd(sourceIndex, outputIndex))
            Sound = false;
    }

    /// <summary>
    /// Records that <paramref name="source"/> is realized by
    /// <paramref name="output"/>. A source or output node may be claimed once;
    /// a second claim makes the candidate unsound so the core declines rather
    /// than silently duplicating or swapping a realized effect.
    /// </summary>
    internal void Claim(
        IrNode source,
        IrNode output,
        ClassicInverseRealizationRule rule)
    {
        if (_claims.Any(c =>
                ReferenceEquals(c.Source, source)
                || ReferenceEquals(c.Output, output)))
        {
            Sound = false;
            return;
        }
        _claims.Add(new ClassicInverseClaim(source, output, rule));
    }

    /// <summary>Declares one node as exact lowering scaffolding, owning its subtree.</summary>
    internal void DeclareProtocol(IrNode node, string rule)
    {
        if (!_protocol.TryAdd(node, rule))
            Sound = false;
    }

    /// <summary>Declares how a structured container on a consumed node's path is accounted.</summary>
    internal void DeclareContainer(
        IrNode container,
        ClassicInverseAncestorKind kind,
        string rule,
        IrNode? outputContext)
    {
        if (!_containers.TryAdd(
                container,
                new ClassicInverseContainerDeclaration(kind, rule, outputContext)))
        {
            Sound = false;
        }
    }

    /// <summary>
    /// Declares that every claim whose source lies inside
    /// <paramref name="sourceRoots"/> executes under one reproduced control
    /// context, and must therefore realize inside
    /// <paramref name="outputContext"/>. This is the flat-IR counterpart of a
    /// structured ancestor: the classic execution body carries its user loops
    /// and conditions as branches, not as tree ancestors.
    /// </summary>
    internal void DeclareControlRegion(
        string rule,
        IEnumerable<IrNode> sourceRoots,
        IrNode outputContext)
        => _controlRegions.Add(new ClassicInverseControlRegion(
            rule,
            [.. sourceRoots],
            outputContext));
}

internal sealed record ClassicInverseClaim(
    IrNode Source,
    IrNode Output,
    ClassicInverseRealizationRule Rule);

internal sealed record ClassicInverseContainerDeclaration(
    ClassicInverseAncestorKind Kind,
    string Rule,
    IrNode? OutputContext);

internal sealed record ClassicInverseControlRegion(
    string Rule,
    ImmutableArray<IrNode> SourceRoots,
    IrNode OutputContext);
