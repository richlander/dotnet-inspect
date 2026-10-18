using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.ILDiff;

// SPIKE (exp/unified-diff-exists, #10227 Q1, "B2"): IlLibraryExistsSession (B1opt) with
// the complete IL diff's two text identities replaced by SymbolKey ids interned in one
// table shared by both sides:
//   - the method map pairs bodies by (method identity id, occurrence), the symbolic
//     form of IlAssemblyDiff's MethodKey text;
//   - token operands compare by operand identity id, the symbolic form of
//     MetadataOperandResolver's text.
// The Type partition, the one-sided rules, the early-exit comparer, and every B1opt
// optimization are unchanged. Nothing in the product calls it.
internal sealed class IlSymbolicExistsSession
{
    internal sealed class Unit(string identity)
    {
        public string Identity { get; } = identity;
        public TypeDefinitionHandle Old { get; set; }
        public TypeDefinitionHandle New { get; set; }
        public bool OldPresent => !Old.IsNil;
        public bool NewPresent => !New.IsNil;
        public List<(int Identity, int Occurrence)> OldMethods { get; } = [];
        public List<(int Identity, int Occurrence)> NewMethods { get; } = [];
        public bool IdentityFailure { get; set; }
        public bool DuplicateRow { get; set; }
        public bool DuplicateMethod { get; set; }
    }

    readonly PEReader _oldPe;
    readonly PEReader _newPe;
    readonly MetadataReader _old;
    readonly MetadataReader _new;
    readonly Dictionary<(int, int), MethodDefinitionHandle> _oldMap;
    readonly Dictionary<(int, int), MethodDefinitionHandle> _newMap;

    public IlBodyExistsComparer Comparer { get; }
    public IlBodyDiff.SymbolicIdentity OldSymbols { get; }
    public IlBodyDiff.SymbolicIdentity NewSymbols { get; }
    public SymbolKeyTable Table { get; } = new();
    public List<Unit> Units { get; } = [];
    public IlLibraryExistsSession.SessionTimes Times { get; } = new();
    public int UnreportedOldMethods { get; private set; }
    public int UnreportedNewMethods { get; private set; }
    public int IdentityFailures { get; private set; }

    public IlSymbolicExistsSession(PEReader oldPe, PEReader newPe, IlBodyDiffNormalization normalization)
    {
        _oldPe = oldPe;
        _newPe = newPe;
        _old = oldPe.GetMetadataReader();
        _new = newPe.GetMetadataReader();

        long t = Stopwatch.GetTimestamp();
        OldSymbols = new IlBodyDiff.SymbolicIdentity(oldPe, _old, normalization, Table, sharedTable: true);
        NewSymbols = new IlBodyDiff.SymbolicIdentity(newPe, _new, normalization, Table, sharedTable: true);
        (_oldMap, var oldFailed) = MethodMap(_old, OldSymbols);
        (_newMap, var newFailed) = MethodMap(_new, NewSymbols);
        IdentityFailures = oldFailed.Count + newFailed.Count;
        Times.MethodMap = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        t = Stopwatch.GetTimestamp();
        var byIdentity = new Dictionary<string, Unit>(StringComparer.Ordinal);
        var oldOwner = Partition(_old, byIdentity, isOld: true);
        var newOwner = Partition(_new, byIdentity, isOld: false);
        foreach (var (key, handle) in _oldMap)
            Assign(_old, handle, key, oldOwner, isOld: true);
        foreach (var (key, handle) in _newMap)
            Assign(_new, handle, key, newOwner, isOld: false);
        foreach (var handle in oldFailed)
            MarkFailure(_old, handle, oldOwner);
        foreach (var handle in newFailed)
            MarkFailure(_new, handle, newOwner);
        Times.Partition = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        t = Stopwatch.GetTimestamp();
        Comparer = new IlBodyExistsComparer(OldSymbols, NewSymbols, normalization)
        {
            Options = IlExistsSpikeOptions.CheckedDecode | IlExistsSpikeOptions.KeyCompare
                | IlExistsSpikeOptions.SharedDecode | IlExistsSpikeOptions.BlockGraphTopology,
        };
        Times.Comparer = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
    }

    /// <summary>IlAssemblyDiff's method map loop with the symbolic identity.</summary>
    static (Dictionary<(int, int), MethodDefinitionHandle> Methods, List<MethodDefinitionHandle> Failed) MethodMap(
        MetadataReader reader,
        IlBodyDiff.SymbolicIdentity symbols)
    {
        var methods = new Dictionary<(int, int), MethodDefinitionHandle>(reader.MethodDefinitions.Count);
        var occurrences = new Dictionary<int, int>(reader.MethodDefinitions.Count);
        var failed = new List<MethodDefinitionHandle>();
        foreach (var handle in reader.MethodDefinitions)
        {
            if (!symbols.TryMethodIdentity(handle, out int id))
            {
                failed.Add(handle);
                continue;
            }
            int occurrence = occurrences.TryGetValue(id, out int count) ? count + 1 : 1;
            occurrences[id] = occurrence;
            methods.Add((id, occurrence), handle);
        }
        return (methods, failed);
    }

    Dictionary<TypeDefinitionHandle, Unit?> Partition(MetadataReader reader, Dictionary<string, Unit> byIdentity, bool isOld)
    {
        var owner = new Dictionary<TypeDefinitionHandle, Unit?>();
        Span<TypeDefinitionHandle> chain = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        var deferred = new List<(TypeDefinitionHandle Type, TypeDefinitionHandle Declared)>();
        foreach (var handle in reader.TypeDefinitions)
        {
            if (MetadataTokens.GetRowNumber(handle) == 1)
            {
                owner[handle] = null;
                continue;
            }
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(reader, handle, chain, out int length, out _, out _)
                || length == 0)
            {
                owner[handle] = null;
                IdentityFailures++;
                continue;
            }
            int generatedAt = -1;
            for (int i = 0; i < length; i++)
            {
                if (reader.StringComparer.StartsWith(reader.GetTypeDefinition(chain[i]).Name, "<"))
                {
                    generatedAt = i;
                    break;
                }
            }
            if (generatedAt == 0)
            {
                owner[handle] = null;
                continue;
            }
            if (generatedAt > 0)
            {
                deferred.Add((handle, chain[generatedAt - 1]));
                continue;
            }
            if (MetadataIdentityName.TypeDefinition(reader, handle, includeAssembly: false)
                is not MetadataTypeNameResult.Resolved { Value: var identity })
            {
                owner[handle] = null;
                IdentityFailures++;
                continue;
            }
            if (!byIdentity.TryGetValue(identity, out var unit))
            {
                unit = new Unit(identity);
                byIdentity[identity] = unit;
                Units.Add(unit);
            }
            if (isOld ? unit.OldPresent : unit.NewPresent)
                unit.DuplicateRow = true;
            else if (isOld)
                unit.Old = handle;
            else
                unit.New = handle;
            owner[handle] = unit;
        }
        foreach (var (type, declared) in deferred)
            owner[type] = owner.TryGetValue(declared, out var unit) ? unit : null;
        return owner;
    }

    void Assign(MetadataReader reader, MethodDefinitionHandle handle, (int, int) key, Dictionary<TypeDefinitionHandle, Unit?> owner, bool isOld)
    {
        var declaring = reader.GetMethodDefinition(handle).GetDeclaringType();
        if (!owner.TryGetValue(declaring, out var unit) || unit is null)
        {
            if (isOld) UnreportedOldMethods++; else UnreportedNewMethods++;
            return;
        }
        (isOld ? unit.OldMethods : unit.NewMethods).Add(key);
        if (key.Item2 > 1)
            unit.DuplicateMethod = true;
    }

    static void MarkFailure(MetadataReader reader, MethodDefinitionHandle handle, Dictionary<TypeDefinitionHandle, Unit?> owner)
    {
        if (owner.TryGetValue(reader.GetMethodDefinition(handle).GetDeclaringType(), out var unit) && unit is not null)
            unit.IdentityFailure = true;
    }

    public MethodDefinitionHandle OldHandle((int, int) key) => _oldMap[key];
    public bool TryNewHandle((int, int) key, out MethodDefinitionHandle handle) => _newMap.TryGetValue(key, out handle);
    public bool TryOldHandle((int, int) key, out MethodDefinitionHandle handle) => _oldMap.TryGetValue(key, out handle);
    public MethodDefinitionHandle NewHandle((int, int) key) => _newMap[key];
    public IEnumerable<KeyValuePair<(int, int), MethodDefinitionHandle>> OldMap => _oldMap;
    public IEnumerable<KeyValuePair<(int, int), MethodDefinitionHandle>> NewMap => _newMap;
    public MetadataReader OldReader => _old;
    public MetadataReader NewReader => _new;
    public PEReader OldPe => _oldPe;
    public PEReader NewPe => _newPe;
    public long BodiesCompared { get; private set; }

    /// <summary>IlLibraryExistsSession.CompareType over the symbolic map.</summary>
    public IlExistsState CompareType(Unit unit, out string? reason)
    {
        reason = null;
        if (unit.DuplicateRow)
        {
            reason = "duplicate Type rows";
            return IlExistsState.Indeterminate;
        }
        if (!unit.OldPresent || !unit.NewPresent)
        {
            reason = "presence";
            return IlExistsState.Changed;
        }

        bool indeterminate = unit.IdentityFailure;
        if (indeterminate)
            reason = "method identity failure";

        foreach (var key in unit.OldMethods)
        {
            if (_newMap.ContainsKey(key))
                continue;
            if (_old.GetMethodDefinition(_oldMap[key]).RelativeVirtualAddress != 0)
            {
                reason = "method with body on old side only: " + Table[key.Item1];
                return IlExistsState.Changed;
            }
        }
        foreach (var key in unit.NewMethods)
        {
            if (_oldMap.ContainsKey(key))
                continue;
            if (_new.GetMethodDefinition(_newMap[key]).RelativeVirtualAddress != 0)
            {
                reason = "method with body on new side only: " + Table[key.Item1];
                return IlExistsState.Changed;
            }
        }

        foreach (var key in unit.OldMethods)
        {
            if (!_newMap.TryGetValue(key, out var newHandle))
                continue;
            var oldMethod = _old.GetMethodDefinition(_oldMap[key]);
            var newMethod = _new.GetMethodDefinition(newHandle);
            if (oldMethod.RelativeVirtualAddress == 0 && newMethod.RelativeVirtualAddress == 0)
                continue;
            if (oldMethod.RelativeVirtualAddress == 0 || newMethod.RelativeVirtualAddress == 0)
            {
                reason = "body on one side: " + Table[key.Item1];
                return IlExistsState.Changed;
            }
            MethodBodyBlock oldBody, newBody;
            try
            {
                oldBody = _oldPe.GetMethodBody(oldMethod.RelativeVirtualAddress);
                newBody = _newPe.GetMethodBody(newMethod.RelativeVirtualAddress);
            }
            catch (BadImageFormatException ex)
            {
                indeterminate = true;
                reason ??= "body read failed: " + ex.Message;
                continue;
            }
            BodiesCompared++;
            var outcome = Comparer.Compare(oldBody, newBody, rawFastPath: false, memoTokens: true, liteDecode: false, out _, out string? failure);
            if (outcome == IlExistsOutcome.Changed)
            {
                reason = "IL: " + Table[key.Item1];
                return IlExistsState.Changed;
            }
            if (outcome == IlExistsOutcome.Unavailable)
            {
                indeterminate = true;
                reason ??= "IL unavailable: " + failure;
            }
        }
        if (unit.DuplicateMethod && !indeterminate)
            reason = "occurrence-paired duplicate identity";
        return indeterminate ? IlExistsState.Indeterminate : IlExistsState.Unchanged;
    }
}
