using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using InertText;
using Inspector.Findings;

namespace ILInspector.Metadata;

/// <summary>The axis a declaration fact is censused on (<c>docs/design/finding-diff-any.md#axes</c>).</summary>
public enum DeclarationAxis
{
    /// <summary>The Public API facts of a visible Type and its visible Members.</summary>
    Api,

    /// <summary>The declaration facts of every other row: non-public Members, hidden Types, implementation facts, and generated nested Types.</summary>
    Body,
}

/// <summary>
/// The kinds of declaration fact Finding Diff Any's census records
/// (<c>docs/design/declaration-view.md#facts</c>). A constant and a property's
/// accessors are payload of their row fact, not kinds of their own.
/// </summary>
public enum DeclarationFactKind
{
    /// <summary>Whether the Type is visible on this side; the one fact of a hidden Type's API census.</summary>
    TypeVisibility,
    TypeRow,
    BeforeFieldInit,
    Interface,
    NullableContext,
    Layout,
    MethodImplementation,
    FieldRow,
    MethodRow,
    Parameter,
    Import,
    PropertyRow,
    EventRow,
    GenericParameter,
    Constraint,
    Attribute,
}

/// <summary>
/// One declaration fact: the atom of the declaration census cursor
/// (<c>docs/design/finding-census.md#the-cursor</c>). Its identity is the whole
/// census key Finding Diff Any compares, expanded so that it names every Type
/// in full and compares across images without a shared interning table; two
/// facts are equal exactly when their keys are byte-equal. <see cref="Text"/>
/// is the key's diagnostic spelling, projected only when the fact is sealed
/// into an inspection, never on the Exists path.
/// </summary>
public sealed class DeclarationFact
{
    readonly SymbolKey<FindingDiffAnyPart> _key;
    string? _identityKey;

    internal DeclarationFact(DeclarationFactKind kind, DeclarationAxis axis, SymbolKey<FindingDiffAnyPart> key, string? text)
    {
        Kind = kind;
        Axis = axis;
        _key = key;
        Text = text;
    }

    public DeclarationFactKind Kind { get; }

    public DeclarationAxis Axis { get; }

    /// <summary>The fact's expanded census key: its exact identity, as bytes.</summary>
    public OpaqueByteRange Key => _key.Range;

    /// <summary>The key's bytes as a lossless hexadecimal string, the <see cref="FindingKey.IdentityKey"/> of its <see cref="Finding{T}"/>.</summary>
    public string IdentityKey => _identityKey ??= Hex(_key.Range);

    /// <summary>The key's declaration-level spelling, or null when the fact was read for Exists alone.</summary>
    public string? Text { get; }

    internal SymbolKey<FindingDiffAnyPart> SymbolKey => _key;

    internal int CompareTo(DeclarationFact other) => _key.CompareTo(other._key);

    static string Hex(OpaqueByteRange range)
    {
        Span<byte> bytes = range.Length <= 512 ? stackalloc byte[range.Length] : new byte[range.Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = range.ByteAt(i);
        return Convert.ToHexString(bytes);
    }
}

/// <summary>One Type unit of a side: the row whose declaration census the cursor reads.</summary>
public sealed record DeclarationCensusType(
    string FullName,
    string Identifier,
    TypeDefinitionHandle Type,
    bool IsMalformed);

/// <summary>
/// One image of a declaration census: Finding Diff Any's side, whose fact
/// builders stage each unit's census facts, read here through a cursor
/// instead of compared slot by slot. A side is used from one thread at a time
/// and serves one cursor at a time; its Type units are read once, on first use.
/// </summary>
public sealed class DeclarationCensusSide : IDisposable
{
    readonly FindingDiffAny.Side _side;
    readonly SymbolKeyBuilder<FindingDiffAnyPart> _scratch;
    FindingDiffAny.UnitTable? _units;
    Dictionary<TypeDefinitionHandle, FindingDiffAny.Unit>? _byType;
    bool _disposed;

    DeclarationCensusSide(PEReader image, SymbolKeyInterner<FindingDiffAnyPart> interner)
    {
        Image = image;
        _side = new FindingDiffAny.Side(image, interner);
        _scratch = new SymbolKeyBuilder<FindingDiffAnyPart>(4 * 1024) { Names = _side.Names };
    }

    /// <summary>A side over one image with an interning table of its own.</summary>
    public static DeclarationCensusSide Create(PEReader image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new DeclarationCensusSide(image, new SymbolKeyInterner<FindingDiffAnyPart>());
    }

    /// <summary>
    /// Two sides over one image pair sharing one interning table, as Finding
    /// Diff Any's comparison does, so a Type a spelling names once is one id
    /// on both sides. Lockstep compares expanded keys and does not need the
    /// sharing; it saves the table's size for a comparison that reads many Types.
    /// The table is not thread-safe, so a pair is driven from one thread, as
    /// Finding Diff Any's comparison drives its two sides.
    /// </summary>
    public static (DeclarationCensusSide Before, DeclarationCensusSide After) CreatePair(PEReader before, PEReader after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var interner = new SymbolKeyInterner<FindingDiffAnyPart>();
        return (new DeclarationCensusSide(before, interner), new DeclarationCensusSide(after, interner));
    }

    public PEReader Image { get; }

    public MetadataReader Reader => _side.Md;

    /// <summary>The Type units of the image, in row order, as Finding Diff Any partitions them.</summary>
    public IEnumerable<DeclarationCensusType> Types
    {
        get
        {
            ThrowIfDisposed();
            foreach ((_, FindingDiffAny.Unit unit) in Units().Entries)
                yield return new DeclarationCensusType(unit.FullName, unit.Identifier, unit.Owner, unit.Malformed);
        }
    }

    /// <summary>Whether the Type is visible on this side: public, or protected within an externally reachable chain.</summary>
    public bool IsVisible(TypeDefinitionHandle type)
    {
        ThrowIfDisposed();
        return _side.IsVisible(type);
    }

    internal FindingDiffAny.Side Side => _side;

    internal SymbolKeyBuilder<FindingDiffAnyPart> Scratch => _scratch;

    internal bool TryFindUnit(TypeDefinitionHandle type, out FindingDiffAny.Unit unit)
    {
        ThrowIfDisposed();
        Units();
        return _byType!.TryGetValue(type, out unit!);
    }

    FindingDiffAny.UnitTable Units()
    {
        if (_units is null)
        {
            _units = _side.Units();
            var byType = new Dictionary<TypeDefinitionHandle, FindingDiffAny.Unit>(_units.Count);
            foreach ((_, FindingDiffAny.Unit unit) in _units.Entries)
                byType[unit.Owner] = unit;
            _byType = byType;
        }
        return _units;
    }

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Completes the side: its builders release their storage and the interning table reference is dropped.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _scratch.Release();
        _side.Complete();
    }
}

/// <summary>
/// The declaration census cursor (<c>docs/design/finding-census.md#the-cursor</c>,
/// adoption step 3): the facts Finding Diff Any's side stages for one Type unit
/// on one axis, each expanded to a side-independent key, yielded in ascending
/// key order. The kind's laziness is per unit, as the design states: the
/// cursor reads the unit's facts before its first atom, because its canonical
/// order is a sort over them; Exists over two cursors then stops at the first
/// differing position. A unit that cannot be read ends <c>Failed</c>; a Type
/// that is not a unit (a compiler-generated nested Type, which belongs to its
/// declaring Type's unit) is <c>Absent</c>.
/// </summary>
public sealed class DeclarationCensusCursor : IFindingCursor<DeclarationFact>
{
    readonly FindingSubject? _subject;
    readonly DeclarationAxis _axis;
    readonly ImmutableArray<DeclarationFact> _facts;
    int _position;
    DeclarationFact? _current;
    FindingCursorTerminal? _terminal;

    DeclarationCensusCursor(FindingSubject? subject, DeclarationAxis axis, ImmutableArray<DeclarationFact> facts, FindingCursorTerminal? terminal)
    {
        _subject = subject;
        _axis = axis;
        _facts = facts;
        _terminal = terminal;
    }

    public DeclarationAxis Axis => _axis;

    public DeclarationFact? Current => _current;

    public int Position => _position;

    public FindingCursorTerminal? Terminal => _terminal;

    /// <summary>
    /// A cursor over one Type's facts on one axis, for Exists: its facts carry
    /// no spelling. <paramref name="ownerVisible"/> is the Body axis's
    /// partition, visible on either side of a pair as Finding Diff Any's Type
    /// level states; null reads this side's visibility alone.
    /// </summary>
    public static DeclarationCensusCursor Create(
        DeclarationCensusSide side,
        TypeDefinitionHandle type,
        DeclarationAxis axis,
        bool? ownerVisible = null)
        => Create(side, type, axis, subject: null, ownerVisible, spell: false);

    internal static DeclarationCensusCursor Create(
        DeclarationCensusSide side,
        TypeDefinitionHandle type,
        DeclarationAxis axis,
        FindingSubject? subject,
        bool? ownerVisible,
        bool spell)
    {
        ArgumentNullException.ThrowIfNull(side);
        if (type.IsNil)
            throw new ArgumentException("The cursor needs a Type definition.", nameof(type));
        if (!side.TryFindUnit(type, out FindingDiffAny.Unit? unit))
        {
            return new DeclarationCensusCursor(
                subject,
                axis,
                [],
                FindingCursorTerminal.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    "The Type is not a census unit: a compiler-generated Type belongs to its declaring Type's unit."));
        }
        if (unit.Malformed)
        {
            return new DeclarationCensusCursor(
                subject,
                axis,
                [],
                FindingCursorTerminal.Failed(
                    FindingCursorFailureStage.Identity,
                    0,
                    "The Type's row or name could not be read, or two Type rows share its key."));
        }

        FindingDiffAny.Side inner = side.Side;
        try
        {
            List<SymbolKeySlot> slots = axis == DeclarationAxis.Api
                ? inner.ApiCensus(unit)
                : inner.BodyCensus(unit, ownerVisible ?? inner.IsVisible(unit.Owner), out _);
            var facts = new DeclarationFact[slots.Count];
            for (int i = 0; i < slots.Count; i++)
                facts[i] = Materialize(side, slots[i], axis, spell);
            Array.Sort(facts, static (x, y) => x.CompareTo(y));
            return new DeclarationCensusCursor(subject, axis, ImmutableCollectionsMarshal.AsImmutableArray(facts), terminal: null);
        }
        catch (Exception ex) when (FindingDiffAny.IsMalformed(ex))
        {
            inner.Abandon();
            side.Scratch.Abandon();
            return new DeclarationCensusCursor(
                subject,
                axis,
                [],
                FindingCursorTerminal.Failed(FindingCursorFailureStage.Reading, 0, ex.Message));
        }
        finally
        {
            inner.EndUnit();
            side.Scratch.Reset();
        }
    }

    // One staged slot becomes one fact: the slot's key is detached, expanded so
    // that every Ref part is the named Type's full key, and detached again as
    // the fact's identity; the kind is read from the key's leading markers.
    static DeclarationFact Materialize(DeclarationCensusSide side, SymbolKeySlot slot, DeclarationAxis axis, bool spell)
    {
        FindingDiffAny.Side inner = side.Side;
        SymbolKey<FindingDiffAnyPart> staged = inner.Transient.Resolve(slot).Detach();
        StagedSymbolKey<FindingDiffAnyPart> expanded = inner.Interner.Expand(staged, side.Scratch);
        string? text = spell ? SymbolKeyText.Describe(expanded) : null;
        return new DeclarationFact(KindOf(staged), axis, expanded.Detach(), text);
    }

    // A fact the walk stages always leads with a kind marker this table names;
    // a marker it does not is a defect of this cursor, not of the image, and
    // surfaces rather than ending the unit Failed.
    static DeclarationFactKind KindOf(SymbolKey<FindingDiffAnyPart> key)
    {
        SymbolKeyPartReader<FindingDiffAnyPart> parts = key.Parts;
        if (!parts.Read() || parts.Kind != SymbolKeyPartKind.Marker)
            throw new UnreachableException("A census fact begins with its kind marker.");
        FindingDiffAnyPart marker = parts.Marker;
        if (marker == FindingDiffAnyPart.NonApi)
        {
            if (!parts.Read() || parts.Kind != SymbolKeyPartKind.Marker)
                throw new UnreachableException("A non-API fact carries its kind marker after the NonApi marker.");
            marker = parts.Marker;
        }
        return marker switch
        {
            FindingDiffAnyPart.Visible or FindingDiffAnyPart.Hidden => DeclarationFactKind.TypeVisibility,
            FindingDiffAnyPart.TypeFact => DeclarationFactKind.TypeRow,
            FindingDiffAnyPart.BeforeFieldInit => DeclarationFactKind.BeforeFieldInit,
            FindingDiffAnyPart.InterfaceFact => DeclarationFactKind.Interface,
            FindingDiffAnyPart.NullableContext => DeclarationFactKind.NullableContext,
            FindingDiffAnyPart.Layout => DeclarationFactKind.Layout,
            FindingDiffAnyPart.MethodImplementation => DeclarationFactKind.MethodImplementation,
            FindingDiffAnyPart.FieldFact => DeclarationFactKind.FieldRow,
            FindingDiffAnyPart.MethodFact => DeclarationFactKind.MethodRow,
            FindingDiffAnyPart.ParameterFact => DeclarationFactKind.Parameter,
            FindingDiffAnyPart.Import => DeclarationFactKind.Import,
            FindingDiffAnyPart.PropertyFact => DeclarationFactKind.PropertyRow,
            FindingDiffAnyPart.EventFact => DeclarationFactKind.EventRow,
            FindingDiffAnyPart.GenericParameterFact => DeclarationFactKind.GenericParameter,
            FindingDiffAnyPart.Constraint => DeclarationFactKind.Constraint,
            FindingDiffAnyPart.Attribute => DeclarationFactKind.Attribute,
            _ => throw new UnreachableException($"Unknown census fact marker {marker}."),
        };
    }

    public bool Advance()
    {
        if (_terminal is not null)
            return false;
        if (_position < _facts.Length)
        {
            _current = _facts[_position];
            _position++;
            return true;
        }
        _current = null;
        _terminal = FindingCursorTerminal.Complete(_facts.Length);
        return false;
    }

    /// <summary>
    /// Drains the cursor into the kind's inspection: each fact a
    /// <see cref="Finding{T}"/> whose identity key is the fact's key bytes,
    /// then the terminal lowered to <see cref="FindingInspection{T}"/>.
    /// </summary>
    public FindingInspection<DeclarationFact> Drain()
    {
        if (_subject is null)
            throw new InvalidOperationException("Draining a cursor into an inspection requires its subject.");
        if (_position != 0)
            throw new InvalidOperationException("A cursor is drained from its first position.");

        var atoms = ImmutableArray.CreateBuilder<Finding<DeclarationFact>>(_facts.Length);
        while (Advance())
        {
            DeclarationFact fact = _current!;
            atoms.Add(new Finding<DeclarationFact>(
                _subject,
                DeclarationFindings.FactDescriptor,
                new FindingKey(fact.IdentityKey),
                fact));
        }

        FindingCursorTerminal terminal = _terminal!;
        return terminal.Kind switch
        {
            FindingCursorTerminalKind.Complete => new FindingInspection<DeclarationFact>.Complete(atoms.ToImmutable()),
            FindingCursorTerminalKind.Absent => new FindingInspection<DeclarationFact>.Absent(terminal.AbsenceKind!.Value, terminal.Reason),
            _ => new FindingInspection<DeclarationFact>.Failed(
                new InspectionError(_subject, DeclarationFindings.InspectionDescriptor, terminal.Reason!)),
        };
    }
}

/// <summary>Byte equality of two declaration facts; a census fact has no undecided or deferred part.</summary>
public readonly struct DeclarationFactEquality : IFindingAtomEquality<DeclarationFact>
{
    public FindingAtomEquality Compare(DeclarationFact oldAtom, DeclarationFact newAtom)
        => oldAtom.SymbolKey.Equals(newAtom.SymbolKey) ? FindingAtomEquality.Equal : FindingAtomEquality.Unequal;
}

/// <summary>
/// The declaration kind's scenarios over the census cursor: <see cref="Inspect"/>
/// drains one Type's facts into a sealed inspection, <see cref="Compare"/>
/// matches two inspections as an identity set, and <see cref="Exists"/> runs
/// lockstep over two cursors to the first differing fact.
/// </summary>
public static class DeclarationFindings
{
    public static readonly FindingDescriptor FactDescriptor = new("declaration.fact", "Declaration fact");

    public static readonly FindingDescriptor InspectionDescriptor = new("declaration.inspect", "Declaration inspection");

    /// <summary>One Type's facts on one axis, spelled at sealing.</summary>
    public static FindingInspection<DeclarationFact> Inspect(
        DeclarationCensusSide side,
        TypeDefinitionHandle type,
        DeclarationAxis axis,
        FindingSubject subject,
        bool? ownerVisible = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return DeclarationCensusCursor.Create(side, type, axis, subject, ownerVisible, spell: true).Drain();
    }

    /// <summary>
    /// Two inspections matched as an identity set: a fact is <c>Present</c>
    /// when its key is on both sides and <c>Added</c> or <c>Removed</c>
    /// otherwise. The row-tier soft key that pairs a changed slot as
    /// <c>Changed</c> is the declaration view's (DV1).
    /// </summary>
    public static FindingComparison<DeclarationFact> Compare(
        FindingInspection<DeclarationFact> oldInspection,
        FindingInspection<DeclarationFact> newInspection)
        => FindingComparison.Compare(
            oldInspection,
            newInspection,
            new FindingMatchOptions { MatchMode = FindingMatchMode.IdentitySet });

    /// <summary>
    /// Whether one Type's facts on one axis differ between two images, decided
    /// at the first differing fact. On the Body axis the partition is visible
    /// on either side, as Finding Diff Any's Type level states.
    /// </summary>
    public static FindingExistsResult Exists(
        DeclarationCensusSide before,
        TypeDefinitionHandle beforeType,
        DeclarationCensusSide after,
        TypeDefinitionHandle afterType,
        DeclarationAxis axis)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        bool? ownerVisible = null;
        if (axis == DeclarationAxis.Body
            && before.TryFindUnit(beforeType, out FindingDiffAny.Unit? beforeUnit) && !beforeUnit.Malformed
            && after.TryFindUnit(afterType, out FindingDiffAny.Unit? afterUnit) && !afterUnit.Malformed)
        {
            // The partition is read only for two well-formed units; a Type
            // that is not a unit, or one whose row is malformed, ends at its
            // cursor's terminal on this axis as on the API axis.
            try
            {
                ownerVisible = before.IsVisible(beforeType) || after.IsVisible(afterType);
            }
            catch (Exception ex) when (FindingDiffAny.IsMalformed(ex))
            {
                return new FindingExistsResult(FindingExistsDecision.Indeterminate, 0, 0);
            }
        }
        DeclarationCensusCursor oldCursor = DeclarationCensusCursor.Create(before, beforeType, axis, ownerVisible);
        DeclarationCensusCursor newCursor = DeclarationCensusCursor.Create(after, afterType, axis, ownerVisible);
        return FindingExists.Decide(oldCursor, newCursor, default(DeclarationFactEquality));
    }
}
