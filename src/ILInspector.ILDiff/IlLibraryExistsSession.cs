using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.ILDiff;

// SPIKE (exp/unified-diff-exists, #10227): Library-scope Body Exists through the
// complete IL diff's own code. One session per image pair holds:
//   - IlAssemblyDiff's method identity map and (identity, occurrence) pairing;
//   - a Type partition over that identity: every method belongs to the nearest
//     declared Type of its declaring chain (a '<'-named Type is generated), keyed
//     by the complete diff's own Type identity string;
//   - optionally the compiler-generated ordinal correspondence, built once;
//   - one IlBodyExistsComparer whose operand resolvers are memoised per side.
// Per Type, CompareType walks the paired methods and stops at the first
// non-Exact body, following Fast Diff's Body contract for one-sided Types,
// methods, and bodies. Nothing in the product calls it.
[Flags]
internal enum IlSessionSpikeOptions
{
    None = 0,
    MapMemo = 1 << 0,
    ResolverMemo = 1 << 1,
    CheckedDecode = 1 << 2,
    KeyCompare = 1 << 3,
    SharedDecode = 1 << 4,
    All = MapMemo | ResolverMemo | CheckedDecode | KeyCompare | SharedDecode,
}

internal enum IlExistsState
{
    Unchanged,
    Changed,
    Indeterminate,
}

internal sealed class IlLibraryExistsSession
{
    internal sealed class Unit(string identity)
    {
        public string Identity { get; } = identity;
        public TypeDefinitionHandle Old { get; set; }
        public TypeDefinitionHandle New { get; set; }
        public bool OldPresent => !Old.IsNil;
        public bool NewPresent => !New.IsNil;
        public List<(string Identity, int Occurrence)> OldMethods { get; } = [];
        public List<(string Identity, int Occurrence)> NewMethods { get; } = [];
        public bool IdentityFailure { get; set; }
        public bool DuplicateRow { get; set; }
        public bool DuplicateMethod { get; set; }
    }

    internal sealed class SessionTimes
    {
        public double MethodMap, Partition, Ordinals, Comparer;
    }

    readonly PEReader _oldPe;
    readonly PEReader _newPe;
    readonly MetadataReader _old;
    readonly MetadataReader _new;
    readonly Dictionary<(string, int), MethodDefinitionHandle> _oldMap;
    readonly Dictionary<(string, int), MethodDefinitionHandle> _newMap;

    public IlBodyExistsComparer Comparer { get; }
    public IlBodyDiffNormalization Normalization { get; }
    public bool LiteDecode { get; set; }
    public List<Unit> Units { get; } = [];
    public SessionTimes Times { get; } = new();

    /// <summary>Methods whose declaring chain folds to no reported Type (&lt;Module&gt;, top-level generated Types).</summary>
    public int UnreportedOldMethods { get; private set; }
    public int UnreportedNewMethods { get; private set; }
    public int IdentityFailures { get; private set; }

    public IlLibraryExistsSession(
        PEReader oldPe,
        PEReader newPe,
        IlBodyDiffNormalization normalization,
        bool liteDecode,
        IlSessionSpikeOptions spike = IlSessionSpikeOptions.None)
    {
        _oldPe = oldPe;
        _newPe = newPe;
        _old = oldPe.GetMetadataReader();
        _new = newPe.GetMetadataReader();
        Normalization = normalization;
        LiteDecode = liteDecode;

        long t = Stopwatch.GetTimestamp();
        bool mapMemo = (spike & IlSessionSpikeOptions.MapMemo) != 0;
        (_oldMap, var oldFailed) = IlAssemblyDiff.SpikeMethodMap(_old, mapMemo);
        (_newMap, var newFailed) = IlAssemblyDiff.SpikeMethodMap(_new, mapMemo);
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
        var (oldCorrespondence, newCorrespondence) =
            (normalization & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0
                ? CompilerGeneratedOrdinalCorrespondence.Build(_old, _new, normalization)
                : (CompilerGeneratedOrdinalCorrespondence.Empty, CompilerGeneratedOrdinalCorrespondence.Empty);
        Times.Ordinals = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        t = Stopwatch.GetTimestamp();
        Comparer = new IlBodyExistsComparer(_old, _new, normalization, oldCorrespondence, newCorrespondence)
        {
            Options = ((spike & IlSessionSpikeOptions.CheckedDecode) != 0 ? IlExistsSpikeOptions.CheckedDecode : 0)
                | ((spike & IlSessionSpikeOptions.KeyCompare) != 0 ? IlExistsSpikeOptions.KeyCompare : 0)
                | ((spike & IlSessionSpikeOptions.SharedDecode) != 0 ? IlExistsSpikeOptions.SharedDecode : 0),
            MemoizeTypes = (spike & IlSessionSpikeOptions.ResolverMemo) != 0,
        };
        Times.Comparer = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
    }

    /// <summary>Folds every Type row to its nearest declared Type and creates the units.</summary>
    Dictionary<TypeDefinitionHandle, Unit?> Partition(MetadataReader reader, Dictionary<string, Unit> byIdentity, bool isOld)
    {
        var owner = new Dictionary<TypeDefinitionHandle, Unit?>();
        Span<TypeDefinitionHandle> chain = stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        var deferred = new List<(TypeDefinitionHandle Type, TypeDefinitionHandle Declared)>();
        foreach (var handle in reader.TypeDefinitions)
        {
            if (MetadataTokens.GetRowNumber(handle) == 1)
            {
                owner[handle] = null; // <Module>
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

    void Assign(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        (string Identity, int Occurrence) key,
        Dictionary<TypeDefinitionHandle, Unit?> owner,
        bool isOld)
    {
        var declaring = reader.GetMethodDefinition(handle).GetDeclaringType();
        if (!owner.TryGetValue(declaring, out var unit) || unit is null)
        {
            if (isOld) UnreportedOldMethods++; else UnreportedNewMethods++;
            return;
        }
        (isOld ? unit.OldMethods : unit.NewMethods).Add(key);
        if (key.Occurrence > 1)
            unit.DuplicateMethod = true;
    }

    static void MarkFailure(MetadataReader reader, MethodDefinitionHandle handle, Dictionary<TypeDefinitionHandle, Unit?> owner)
    {
        if (owner.TryGetValue(reader.GetMethodDefinition(handle).GetDeclaringType(), out var unit) && unit is not null)
            unit.IdentityFailure = true;
    }

    public MethodDefinitionHandle OldHandle((string, int) key) => _oldMap[key];
    public MethodDefinitionHandle NewHandle((string, int) key) => _newMap[key];
    public bool TryNewHandle((string, int) key, out MethodDefinitionHandle handle) => _newMap.TryGetValue(key, out handle);
    public bool TryOldHandle((string, int) key, out MethodDefinitionHandle handle) => _oldMap.TryGetValue(key, out handle);
    public MetadataReader OldReader => _old;
    public MetadataReader NewReader => _new;
    public PEReader OldPe => _oldPe;
    public PEReader NewPe => _newPe;

    public long BodiesCompared { get; private set; }

    /// <summary>
    /// Body Exists for one unit: the first settling fact decides. Presence and
    /// one-sided methods with bodies are checked before any IL is read.
    /// </summary>
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

        // One-sided methods with a body.
        foreach (var key in unit.OldMethods)
        {
            if (_newMap.ContainsKey(key))
                continue;
            if (_old.GetMethodDefinition(_oldMap[key]).RelativeVirtualAddress != 0)
            {
                reason = "method with body on old side only: " + key.Identity;
                return IlExistsState.Changed;
            }
        }
        foreach (var key in unit.NewMethods)
        {
            if (_oldMap.ContainsKey(key))
                continue;
            if (_new.GetMethodDefinition(_newMap[key]).RelativeVirtualAddress != 0)
            {
                reason = "method with body on new side only: " + key.Identity;
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
                reason = "body on one side: " + key.Identity;
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
            var outcome = Comparer.Compare(oldBody, newBody, rawFastPath: false, memoTokens: true, liteDecode: LiteDecode, out _, out string? failure);
            if (outcome == IlExistsOutcome.Changed)
            {
                reason = "IL: " + key.Identity;
                return IlExistsState.Changed;
            }
            if (outcome == IlExistsOutcome.Unavailable)
            {
                indeterminate = true;
                reason ??= "IL unavailable: " + failure;
            }
        }
        if (unit.DuplicateMethod && !indeterminate)
        {
            // IlAssemblyDiff pairs repeated identities by occurrence; Fast Diff
            // calls them Indeterminate. Report the occurrence pairing's verdict
            // but flag it for classification.
            reason = "occurrence-paired duplicate identity";
        }
        return indeterminate ? IlExistsState.Indeterminate : IlExistsState.Unchanged;
    }
}
