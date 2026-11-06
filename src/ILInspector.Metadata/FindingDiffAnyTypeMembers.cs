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
    /// Generated code remains in the residual until lifted-owner adoption.
    /// Like Inventory, this reads every paired body through the selected reader.
    /// </summary>
    public FindingDiffAnyTypeMembers Members(string fullName)
    {
        var fold = new MemberFold(_a, _b);
        FindingDiffAnyTypeInventory inventory = Read(fullName, fold);
        return fold.Result(inventory);
    }

    sealed class MemberFold(FindingDiffAny.Side a, FindingDiffAny.Side b)
    {
        readonly Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> _members = [];
        readonly Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> _ownersA = [];
        readonly Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> _ownersB = [];
        readonly Dictionary<MethodDefinitionHandle, Owner> _methodsA = [];
        readonly Dictionary<MethodDefinitionHandle, Owner> _methodsB = [];
        readonly Owner _residual = new(default);
        bool _apiComplete;
        bool _bodyComplete;

        public void Initialize(FindingDiffAny.Unit unitA, FindingDiffAny.Unit unitB)
        {
            InitializeSide(a, unitA, before: true, _ownersA, _methodsA);
            InitializeSide(b, unitB, before: false, _ownersB, _methodsB);
        }

        void InitializeSide(
            FindingDiffAny.Side side,
            FindingDiffAny.Unit unit,
            bool before,
            Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> owners,
            Dictionary<MethodDefinitionHandle, Owner> methods)
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
            Owner owner = FactOwner(fact, owners);
            if (axis == FindingDiffAnyAxis.Api)
                owner.Api = FindingDiffAnyState.Changed;
            else
                owner.Body = FindingDiffAnyState.Changed;
        }

        Owner FactOwner(SymbolKey<FindingDiffAnyPart> key, Dictionary<SymbolKey<FindingDiffAnyPart>, Owner> owners)
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
                        return _residual;
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
