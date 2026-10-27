using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>The image that holds a row present on one side only.</summary>
public enum FindingDiffAnySide
{
    Before,
    After,
}

/// <summary>The axis a census fact is decided on.</summary>
public enum FindingDiffAnyAxis
{
    Api,
    Body,
}

/// <summary>
/// One census fact of a paired Type present on one side only, on the axis the
/// census gives it. <see cref="Key"/> is <see cref="SymbolKeyText"/>'s spelling
/// of the fact's expansion, every <c>Ref</c> replaced by the key it names, so
/// two pairings that interned keys in different orders spell one fact alike;
/// it is a rendering for display and gates, not an identity.
/// </summary>
public readonly record struct FindingDiffAnyFactRow(FindingDiffAnyAxis Axis, FindingDiffAnySide Side, string Key);

/// <summary>One paired method's Body state through the pairing's body reader.</summary>
public readonly record struct FindingDiffAnyBodyRow(
    MethodDefinitionHandle Before,
    MethodDefinitionHandle After,
    FindingDiffAnyState Body);

/// <summary>
/// One paired Type as the rows the Library level compares, with the fold the
/// Library level reports for it. Record equality compares the row arrays by
/// reference; compare <see cref="Facts"/> and <see cref="Bodies"/> as sequences.
/// </summary>
/// <param name="Api">The Library level's API state of the Type: <see cref="FindingDiffAnyState.Changed"/> exactly when an API fact is present on one side only.</param>
/// <param name="Body">The Library level's Body state of the Type: <see cref="FindingDiffAnyState.Changed"/> when a Body fact, or a method with a body, is present on one side only, otherwise the state of the first paired method, in the Body census's method order, whose body is not <see cref="FindingDiffAnyState.Unchanged"/>, which is where the Library level stops.</param>
/// <param name="Facts">Every census fact present on one side only, API facts first, each axis ordered by spelling and then side, an order that does not depend on the pairing's interning table.</param>
/// <param name="Bodies">Every method paired by the Body census, in its method order, with its Body state through the body reader; a method present on one side only is a fact, not a body row.</param>
/// <param name="ApiFactsBefore">The API census size on the before side; with the other three counts, the closed inventory the rows were read from.</param>
public sealed record FindingDiffAnyTypeInventory(
    string FullName,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    ImmutableArray<FindingDiffAnyFactRow> Facts,
    ImmutableArray<FindingDiffAnyBodyRow> Bodies,
    int ApiFactsBefore,
    int ApiFactsAfter,
    int BodyFactsBefore,
    int BodyFactsAfter);

/// <summary>
/// Two images paired once, by Finding Diff Any's Type identity, for Type-scoped
/// questions: <see cref="Inventory"/> returns one paired Type's rows under the
/// pairing's body reader, the walk by default. The pairing is the Type
/// scenario's entry (<c>docs/design/diff-system.md#scenarios</c>): a host
/// that opened an image pair pays the pairing once and each Type as it is
/// asked for. Types present on one side only are not paired; their one-sided
/// form is the Type level's (<c>docs/design/finding-diff-any.md#type</c>),
/// which folds these rows into Member states under its ownership rules.
/// </summary>
public sealed class FindingDiffAnyTypePairing
{
    readonly FindingDiffAny.Side _a;
    readonly FindingDiffAny.Side _b;
    readonly FindingDiffAny.Work _work;
    readonly Dictionary<string, (FindingDiffAny.Unit A, FindingDiffAny.Unit B)> _paired = new(StringComparer.Ordinal);
    readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);
    bool _released;

    public FindingDiffAnyTypePairing(PEReader before, PEReader after, FindingDiffAnyBodyReader? bodyReader = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var interner = new SymbolKeyInterner<FindingDiffAnyPart>();
        _a = new FindingDiffAny.Side(before, interner);
        _b = new FindingDiffAny.Side(after, interner);
        _work = new FindingDiffAny.Work { BodyReader = bodyReader };
        FindingDiffAny.UnitTable unitsB = _b.Units();
        var names = new List<string>();
        foreach ((SymbolKey<FindingDiffAnyPart> key, FindingDiffAny.Unit unitA) in _a.Units().Entries)
        {
            if (!unitsB.TryGetValue(key, out FindingDiffAny.Unit? unitB))
                continue;
            // The Library level pairs rows by key and decides each; a display
            // name two paired rows share selects neither, so the pairing
            // withholds a decision for that name rather than guess a row.
            if (_paired.TryAdd(unitA.FullName, (unitA, unitB)))
                names.Add(unitA.FullName);
            else
                _ambiguous.Add(unitA.FullName);
        }
        Types = names;
    }

    /// <summary>The full names of the Types present in both images, in before-image row order.</summary>
    public IReadOnlyList<string> Types { get; }

    /// <summary>Paired methods whose bodies were read so far, as the Library receipt counts them.</summary>
    public int BodiesCompared => _work.Bodies;

    /// <summary>IL bytes the walk read so far; zero under another body reader.</summary>
    public long IlBytesCompared => _work.IlBytes;

    /// <summary>
    /// The rows of one paired Type. Throws <see cref="ArgumentException"/> for
    /// a name no paired Type has.
    /// </summary>
    public FindingDiffAnyTypeInventory Inventory(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        if (_released)
            throw new InvalidOperationException("The pairing released its sides.");
        if (!_paired.TryGetValue(fullName, out (FindingDiffAny.Unit A, FindingDiffAny.Unit B) units))
            throw new ArgumentException($"'{fullName}' is not a Type present in both images.", nameof(fullName));
        (FindingDiffAny.Unit unitA, FindingDiffAny.Unit unitB) = units;
        if (unitA.Malformed || unitB.Malformed || _ambiguous.Contains(fullName))
            return Indeterminate(fullName);

        var facts = ImmutableArray.CreateBuilder<FindingDiffAnyFactRow>();
        var bodies = ImmutableArray.CreateBuilder<FindingDiffAnyBodyRow>();
        FindingDiffAnyState api = FindingDiffAnyState.Indeterminate;
        FindingDiffAnyState body = FindingDiffAnyState.Indeterminate;
        int apiBefore = 0, apiAfter = 0, bodyBefore = 0, bodyAfter = 0;
        try
        {
            List<SymbolKeySlot> apiCensusA = _a.ApiCensus(unitA);
            List<SymbolKeySlot> apiCensusB = _b.ApiCensus(unitB);
            apiBefore = apiCensusA.Count;
            apiAfter = apiCensusB.Count;
            Differences(apiCensusA, apiCensusB, FindingDiffAnyAxis.Api, facts);
            api = facts.Count == 0 ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed;

            // A Type visible on either side partitions its facts as visible on
            // both, as the Library level does.
            bool ownerVisible = _a.IsVisible(unitA.Owner) || _b.IsVisible(unitB.Owner);
            List<SymbolKeySlot> bodyCensusA = _a.BodyCensus(unitA, ownerVisible, out Dictionary<KeyRef, MethodDefinitionHandle> methodsA);
            List<SymbolKeySlot> bodyCensusB = _b.BodyCensus(unitB, ownerVisible, out Dictionary<KeyRef, MethodDefinitionHandle> methodsB);
            bodyBefore = bodyCensusA.Count;
            bodyAfter = bodyCensusB.Count;
            int apiFacts = facts.Count;
            Differences(bodyCensusA, bodyCensusB, FindingDiffAnyAxis.Body, facts);
            bool bodyFactsDiffer = facts.Count > apiFacts
                || FindingDiffAny.OneSidedBody(_a, methodsA, methodsB) is not null
                || FindingDiffAny.OneSidedBody(_b, methodsB, methodsA) is not null;

            // Every paired method is read, in the order the Body census lists
            // them, where the Library level stops at the first difference; the
            // fold below is the Library level's reading.
            FindingDiffAnyState? first = null;
            foreach ((KeyRef key, MethodDefinitionHandle methodA) in methodsA)
            {
                if (!methodsB.TryGetValue(key, out MethodDefinitionHandle methodB))
                    continue;
                FindingDiffAnyState state;
                try
                {
                    _work.Bodies++;
                    state = FindingDiffAny.BodyEqual(_a, methodA, _b, methodB, _work)
                        ? FindingDiffAnyState.Unchanged
                        : FindingDiffAnyState.Changed;
                }
                catch (Exception ex) when (FindingDiffAny.IsMalformed(ex))
                {
                    _a.Abandon();
                    _b.Abandon();
                    state = FindingDiffAnyState.Indeterminate;
                }
                bodies.Add(new FindingDiffAnyBodyRow(methodA, methodB, state));
                if (state != FindingDiffAnyState.Unchanged)
                    first ??= state;
            }
            body = bodyFactsDiffer ? FindingDiffAnyState.Changed : first ?? FindingDiffAnyState.Unchanged;
        }
        catch (Exception ex) when (FindingDiffAny.IsMalformed(ex))
        {
            _a.Abandon();
            _b.Abandon();
            body = FindingDiffAnyState.Indeterminate;
        }
        finally
        {
            _a.EndUnit();
            _b.EndUnit();
        }
        return new FindingDiffAnyTypeInventory(
            fullName, api, body, facts.ToImmutable(), bodies.ToImmutable(), apiBefore, apiAfter, bodyBefore, bodyAfter);
    }

    /// <summary>Releases the sides' key storage; no further inventory can be read.</summary>
    public void Release()
    {
        if (_released)
            return;
        _released = true;
        _a.Complete();
        _b.Complete();
    }

    static FindingDiffAnyTypeInventory Indeterminate(string fullName)
        => new(
            fullName,
            FindingDiffAnyState.Indeterminate,
            FindingDiffAnyState.Indeterminate,
            [],
            [],
            0,
            0,
            0,
            0);

    // Both censuses are sorted by key bytes, the order the staged keys compare
    // in, so one merge over the two finds every fact present on one side only
    // without resolving a key that has a match.
    void Differences(
        List<SymbolKeySlot> censusA,
        List<SymbolKeySlot> censusB,
        FindingDiffAnyAxis axis,
        ImmutableArray<FindingDiffAnyFactRow>.Builder rows)
    {
        int start = rows.Count;
        Merge(censusA, censusB, axis, rows);
        // The censuses sort by key bytes, which carry the table's ids, so the
        // merge yields rows in a table-dependent order; the rows are reported
        // in the order of their table-free spelling.
        rows.Sort(start, rows.Count - start, FactRowOrder.Instance);
    }

    sealed class FactRowOrder : IComparer<FindingDiffAnyFactRow>
    {
        public static readonly FactRowOrder Instance = new();

        public int Compare(FindingDiffAnyFactRow x, FindingDiffAnyFactRow y)
        {
            int order = string.CompareOrdinal(x.Key, y.Key);
            return order != 0 ? order : x.Side.CompareTo(y.Side);
        }
    }

    void Merge(
        List<SymbolKeySlot> censusA,
        List<SymbolKeySlot> censusB,
        FindingDiffAnyAxis axis,
        ImmutableArray<FindingDiffAnyFactRow>.Builder rows)
    {
        int i = 0;
        int j = 0;
        while (i < censusA.Count && j < censusB.Count)
        {
            StagedSymbolKey<FindingDiffAnyPart> x = _a.Transient.Resolve(censusA[i]);
            StagedSymbolKey<FindingDiffAnyPart> y = _b.Transient.Resolve(censusB[j]);
            int order = x.CompareTo(y);
            if (order == 0)
            {
                i++;
                j++;
                continue;
            }
            if (order < 0)
            {
                rows.Add(new FindingDiffAnyFactRow(axis, FindingDiffAnySide.Before, Spell(x)));
                i++;
            }
            else
            {
                rows.Add(new FindingDiffAnyFactRow(axis, FindingDiffAnySide.After, Spell(y)));
                j++;
            }
        }
        for (; i < censusA.Count; i++)
            rows.Add(new FindingDiffAnyFactRow(axis, FindingDiffAnySide.Before, Spell(_a.Transient.Resolve(censusA[i]))));
        for (; j < censusB.Count; j++)
            rows.Add(new FindingDiffAnyFactRow(axis, FindingDiffAnySide.After, Spell(_b.Transient.Resolve(censusB[j]))));
    }

    // The fact's expansion through the shared table, staged after the census
    // in the before side's transient builder and discarded with it, so that
    // the spelling carries no table id.
    string Spell(StagedSymbolKey<FindingDiffAnyPart> fact)
        => SymbolKeyText.Describe(_a.Interner.Expand(fact.Detach(), _a.Transient));
}
