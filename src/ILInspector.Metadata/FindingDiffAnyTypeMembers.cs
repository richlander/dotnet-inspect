using System.Collections.Immutable;
using System.Reflection.Metadata;
using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// A declared Member identified by its expanded census key. Handles select the
/// declaration in each image; a nil handle means that side has no declaration.
/// </summary>
public sealed record FindingDiffAnyMemberState(
    OpaqueByteRange Key,
    EntityHandle Before,
    EntityHandle After,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body);

/// <summary>
/// The paired-Type Member partition (docs/design/finding-diff-any.md#type).
/// Api and Body retain the Library fold; each Member and the residual retain
/// their own states. FindingDiffAnyTypeMemberTests gates the partition.
/// </summary>
public sealed record FindingDiffAnyTypeMembers(
    string FullName,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    ImmutableArray<FindingDiffAnyMemberState> Members,
    FindingDiffAnyState ResidualApi,
    FindingDiffAnyState ResidualBody);

public sealed partial class FindingDiffAnyTypePairing
{
    /// <summary>
    /// Folds one paired Type's census into declared Members and a residual.
    /// Generated code remains residual without authenticated owner inputs.
    /// Like Inventory, this reads every paired body through the selected reader.
    /// </summary>
    public FindingDiffAnyTypeMembers Members(string fullName)
        => Members(fullName, null, null);

    /// <summary>
    /// Folds with authenticated generated-method owners supplied by Analysis.
    /// Omitted owners and generated Types without one unanimous owner remain
    /// residual. Each map belongs to its corresponding endpoint image.
    /// </summary>
    public FindingDiffAnyTypeMembers Members(
        string fullName,
        IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? beforeGeneratedOwners,
        IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? afterGeneratedOwners)
    {
        var fold = new MemberFold(_a, _b, beforeGeneratedOwners, afterGeneratedOwners);
        FindingDiffAnyTypeInventory inventory = Read(fullName, fold);
        return fold.Result(inventory);
    }

    /// <summary>Prepares one paired Type and advances its bodies at Member boundaries.</summary>
    public MemberComparison BeginMembers(
        string fullName,
        IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? beforeGeneratedOwners = null,
        IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? afterGeneratedOwners = null)
        => new(this, fullName, beforeGeneratedOwners, afterGeneratedOwners);

    /// <summary>The pairing and its endpoint images must remain alive until completion.</summary>
    public sealed class MemberComparison
    {
        readonly FindingDiffAnyTypePairing _pairing;
        readonly MemberFold _fold;
        readonly FindingDiffAnyTypeInventory _inventory;
        readonly List<(MethodDefinitionHandle Before, MethodDefinitionHandle After, int Ordinal)[]> _groups;
        int _next;
        FindingDiffAnyState? _first;
        int _firstOrdinal = int.MaxValue;
        public FindingDiffAnyTypeMembers? Result { get; private set; }

        internal MemberComparison(FindingDiffAnyTypePairing pairing, string name,
            IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? before,
            IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? after)
        {
            _pairing = pairing;
            _fold = new(pairing._a, pairing._b, before, after);
            List<(MethodDefinitionHandle Before, MethodDefinitionHandle After)> bodies = [];
            _inventory = pairing.Read(name, _fold, bodies);
            _groups = bodies.Select((body, ordinal) => (body.Before, body.After, Ordinal: ordinal)).GroupBy(body => _fold.BodyOwner(body.Before)).Select(group => group.ToArray()).ToList();
        }

        /// <summary>Completes at least one Member group, then observes the time budget.</summary>
        public bool Step(TimeSpan budget, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
            cancellationToken.ThrowIfCancellationRequested();
            if (Result is not null)
                return true;
            if (_pairing._released)
                throw new InvalidOperationException("The pairing released its sides.");
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            while (_next < _groups.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var (before, after, ordinal) in _groups[_next++])
                {
                    FindingDiffAnyState state;
                    try
                    {
                        _pairing._work.Bodies++;
                        state = FindingDiffAny.BodyEqual(_pairing._a, before, _pairing._b, after, _pairing._work)
                            ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed;
                    }
                    catch (Exception ex) when (FindingDiffAny.IsMalformed(ex))
                    {
                        _pairing._a.Abandon();
                        _pairing._b.Abandon();
                        state = FindingDiffAnyState.Indeterminate;
                    }
                    _fold.Body(before, after, state);
                    if (state != FindingDiffAnyState.Unchanged && ordinal < _firstOrdinal)
                    {
                        _first = state;
                        _firstOrdinal = ordinal;
                    }
                }
                if (_next < _groups.Count && System.Diagnostics.Stopwatch.GetElapsedTime(start) >= budget)
                    return false;
            }
            FindingDiffAnyState body = _inventory.Body == FindingDiffAnyState.Unchanged
                ? _first ?? FindingDiffAnyState.Unchanged : _inventory.Body;
            Result = _fold.Result(_inventory with { Body = body });
            return true;
        }
    }

    sealed class MemberFold(
        FindingDiffAny.Side a,
        FindingDiffAny.Side b,
        IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? generatedA,
        IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? generatedB)
    {
        readonly Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> _members = [];
        readonly Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> _ownersA = [];
        readonly Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> _ownersB = [];
        readonly Dictionary<MethodDefinitionHandle, Owner> _methodsA = [];
        readonly Dictionary<MethodDefinitionHandle, Owner> _methodsB = [];
        readonly Owner _residual = new(default);
        readonly Dictionary<int, Owner> _typesA = [];
        readonly Dictionary<int, Owner> _typesB = [];
        bool _apiComplete;
        bool _bodyComplete;

        public void Initialize(FindingDiffAny.Unit unitA, FindingDiffAny.Unit unitB)
        {
            InitializeSide(a, unitA, before: true, _ownersA, _methodsA, _typesA, generatedA);
            InitializeSide(b, unitB, before: false, _ownersB, _methodsB, _typesB, generatedB);
        }

        void InitializeSide(
            FindingDiffAny.Side side,
            FindingDiffAny.Unit unit,
            bool before,
            Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> owners,
            Dictionary<MethodDefinitionHandle, Owner> methods,
            Dictionary<int, Owner> types,
            IReadOnlyDictionary<MethodDefinitionHandle, MethodDefinitionHandle>? generatedOwners)
        {
            Dictionary<EntityHandle, EntityHandle> declarations = ApiSurfaceExtractor.DeclaredMemberOwners(side.Md, unit.Owner);
            foreach (EntityHandle declaration in declarations.Values.Distinct())
            {
                SymbolKey<FindingDiffAnyPart> key = side.MemberKey(unit.Owner, declaration);
                if (!_members.TryGetValue(key, out Owner? owner))
                    _members.Add(key, owner = new Owner(key));
                if (before)
                    owner.Before = declaration;
                else
                    owner.After = declaration;
            }
            foreach ((EntityHandle row, EntityHandle declaration) in declarations)
            {
                Owner owner = _members[side.MemberKey(unit.Owner, declaration)];
                owners[side.MemberKey(unit.Owner, row)] = owner;
                if (row.Kind == HandleKind.MethodDefinition)
                    methods[(MethodDefinitionHandle)row] = owner;
            }
            if (generatedOwners is null)
                return;
            foreach ((MethodDefinitionHandle method, MethodDefinitionHandle source) in generatedOwners)
            {
                if (!methods.TryGetValue(source, out Owner? owner))
                    continue;
                TypeDefinitionHandle declaringType = side.Md.GetMethodDefinition(method).GetDeclaringType();
                owners[side.MemberKey(declaringType, method)] = owner;
                methods[method] = owner;
            }
            foreach (TypeDefinitionHandle generatedType in unit.Generated)
            {
                Owner? owner = null;
                bool unanimous = true;
                foreach (MethodDefinitionHandle method in side.Md.GetTypeDefinition(generatedType).GetMethods())
                {
                    if (!methods.TryGetValue(method, out Owner? candidate)
                        || owner is not null && owner != candidate)
                    {
                        unanimous = false;
                        break;
                    }
                    owner = candidate;
                }
                if (!unanimous || owner is null)
                    continue;
                types[side.TypeKey(generatedType).Id] = owner;
                TypeDefinition generated = side.Md.GetTypeDefinition(generatedType);
                foreach (EntityHandle row in generated.GetFields().Select(field => (EntityHandle)field)
                    .Concat(generated.GetProperties().Select(property => (EntityHandle)property))
                    .Concat(generated.GetEvents().Select(@event => (EntityHandle)@event)))
                {
                    owners[side.MemberKey(generatedType, row)] = owner;
                }
            }
        }

        public void CompleteApi(FindingDiffAnyState state)
        {
            _apiComplete = state != FindingDiffAnyState.Indeterminate;
            if (state != FindingDiffAnyState.Changed)
            {
                foreach (Owner owner in _members.Values.Append(_residual))
                    owner.Api = FindingDiffAnyState.Unchanged;
            }
        }
        public void CompleteBodyCensus() => _bodyComplete = true;

        public void Fact(SymbolKey<FindingDiffAnyPart> fact, FindingDiffAnySide side, FindingDiffAnyAxis axis)
        {
            Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> owners = side == FindingDiffAnySide.Before ? _ownersA : _ownersB;
            Owner owner = FactOwner(fact, owners, side == FindingDiffAnySide.Before ? _typesA : _typesB);
            if (axis == FindingDiffAnyAxis.Api)
                owner.Api = FindingDiffAnyState.Changed;
            else
                owner.Body = FindingDiffAnyState.Changed;
        }

        Owner FactOwner(SymbolKey<FindingDiffAnyPart> key, Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> owners, Dictionary<int, Owner> types)
        {
            // Only the first nested key is the row described by a fact. Other
            // keys can be referenced declarations (attribute constructors,
            // interface Types, MethodImpl bodies), which never own that fact.
            while (true)
            {
                if (owners.TryGetValue(key, out Owner? owner))
                    return owner;
                if (key.Kind is FindingDiffAnyPart.Method or FindingDiffAnyPart.Field
                    or FindingDiffAnyPart.Property or FindingDiffAnyPart.Event
                    or FindingDiffAnyPart.Type or FindingDiffAnyPart.Primitive)
                {
                    return _residual;
                }
                var parts = key.Parts;
                bool nested = false;
                while (parts.Read())
                {
                    // A named Type owner is interned as Ref. It is the owner
                    // part just as a nested Key is; later parts are payload.
                    if (parts.Kind == SymbolKeyPartKind.Ref)
                        return types.GetValueOrDefault(parts.Id) ?? _residual;
                    if (parts.Kind != SymbolKeyPartKind.Key)
                        continue;
                    key = parts.Key;
                    nested = true;
                    break;
                }
                if (!nested)
                    return _residual;
            }
        }

        public void OneSidedBodies(
            Dictionary<KeyRef, MethodDefinitionHandle> methodsA,
            Dictionary<KeyRef, MethodDefinitionHandle> methodsB)
        {
            OneSide(a, methodsA, methodsB, _methodsA);
            OneSide(b, methodsB, methodsA, _methodsB);
        }

        void OneSide(FindingDiffAny.Side side,
            Dictionary<KeyRef, MethodDefinitionHandle> present,
            Dictionary<KeyRef, MethodDefinitionHandle> other,
            Dictionary<MethodDefinitionHandle, Owner> owners)
        {
            foreach ((KeyRef key, MethodDefinitionHandle method) in present)
            {
                if (!other.ContainsKey(key) && side.Md.GetMethodDefinition(method).RelativeVirtualAddress != 0)
                    MethodOwner(method, owners).Body = FindingDiffAnyState.Changed;
            }
        }

        Owner MethodOwner(MethodDefinitionHandle method, Dictionary<MethodDefinitionHandle, Owner> owners)
            => owners.GetValueOrDefault(method) ?? _residual;

        public object BodyOwner(MethodDefinitionHandle method) => MethodOwner(method, _methodsA);

        public void Body(MethodDefinitionHandle before, MethodDefinitionHandle after, FindingDiffAnyState state)
        {
            SetBody(MethodOwner(before, _methodsA), state);
            SetBody(MethodOwner(after, _methodsB), state);
        }

        static void SetBody(Owner owner, FindingDiffAnyState state)
        {
            // Changed evidence settles the owner's existence check. A malformed
            // body does not erase another body or declaration's known change.
            if (state == FindingDiffAnyState.Changed || owner.Body == FindingDiffAnyState.Unchanged)
                owner.Body = state;
        }

        public FindingDiffAnyTypeMembers Result(FindingDiffAnyTypeInventory inventory)
        {
            foreach (Owner owner in _members.Values.Append(_residual))
            {
                if (!_apiComplete && owner.Api != FindingDiffAnyState.Changed)
                    owner.Api = FindingDiffAnyState.Indeterminate;
                if (!_bodyComplete && owner.Body != FindingDiffAnyState.Changed)
                    owner.Body = FindingDiffAnyState.Indeterminate;
            }
            var members = ImmutableArray.CreateBuilder<FindingDiffAnyMemberState>(_members.Count);
            var scratch = new SymbolKeyBuilder<FindingDiffAnyPart>();
            foreach (Owner owner in _members.Values)
            {
                members.Add(new FindingDiffAnyMemberState(
                    a.Interner.Expand(owner.Key, scratch).Detach().Range,
                    owner.Before, owner.After, owner.Api, owner.Body));
                scratch.Reset();
            }
            // Read ends the unit before returning, so Member keys are detached
            // at admission, and their Ref parts use the pairing's retained table.
            return new(inventory.FullName, inventory.Api, inventory.Body, members.ToImmutable(), _residual.Api, _residual.Body);
        }

        sealed class Owner(SymbolKey<FindingDiffAnyPart> key)
        {
            public SymbolKey<FindingDiffAnyPart> Key { get; } = key;
            public EntityHandle Before;
            public EntityHandle After;
            public FindingDiffAnyState Api;
            public FindingDiffAnyState Body;
        }
    }
}
