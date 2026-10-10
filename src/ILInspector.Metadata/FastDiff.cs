using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>Whether one axis of a Type differs between two Library images.</summary>
public enum FastDiffState
{
    /// <summary>Every fact the axis compares is equal.</summary>
    Unchanged,

    /// <summary>A compared fact differs. The complete diff may present it differently.</summary>
    Changed,

    /// <summary>The axis could not be decided, for example because a row could not be decoded.</summary>
    Indeterminate,

    /// <summary>The comparison was asked not to decide this axis.</summary>
    NotCompared,
}

/// <summary>The axes one comparison decides.</summary>
public enum FastDiffAxes
{
    /// <summary>Both the API and the Body axis.</summary>
    ApiAndBody,

    /// <summary>The API axis only; every Body state is <see cref="FastDiffState.NotCompared"/>.</summary>
    Api,
}

/// <summary>
/// The API and Body states of one Type.
/// </summary>
/// <param name="FullName">
/// The <c>ApiType.FullName</c> spelling: <c>.</c> between nested names and the
/// metadata backtick arity. It is a display name and need not be unique.
/// </param>
/// <param name="Identifier">
/// The injective <see cref="MetadataTypeDefinitionName.ToEscapedFullName"/>
/// spelling, which hosts use to join navigation Types. A Type whose name cannot
/// be read is identified by its row token, prefixed with <c>!</c>.
/// </param>
public sealed record FastDiffTypeState(
    string FullName,
    FastDiffState Api,
    FastDiffState Body,
    string Identifier);

/// <summary>The work one comparison performed.</summary>
public sealed record FastDiffReceipt(
    int Types,
    int BodiesCompared,
    long IlBytesCompared);

public sealed record FastDiffResult(
    ImmutableArray<FastDiffTypeState> Types,
    FastDiffReceipt Receipt);

/// <summary>
/// Decides, for every Type of one Library image pair, whether its API and its
/// implementation differ, without building either complete diff.
/// </summary>
/// <remarks>
/// <para>
/// The API axis compares the Public API facts of the Type and its members:
/// Type flags, base Type, interfaces, generic
/// parameters and constraints, custom attributes, layout, member signatures
/// and flags, parameter names and defaults, constants, and explicit interface
/// implementations. The Body axis compares the implementation of every
/// member, public or not: IL, exception regions, locals, stack size, and the
/// metadata of non-public members and compiler-generated nested Types.
/// </para>
/// <para>
/// Tokens compare by symbolic name, resolved once per side, never by token
/// number or referenced assembly identity. Compiler-generated nested Types
/// (state machines, closures, local-function holders) belong to their nearest
/// declared Type. Top-level compiler-generated Types are not reported; a
/// body that references them still compares their names.
/// </para>
/// <para>
/// <c>Unchanged</c> is sound for the compared facts. <c>Changed</c> may
/// over-report: a reordered member, a renumbered compiler-generated name, or
/// an IL encoding that canonical comparison treats as equal. A row that cannot
/// be decoded makes the unsettled axes of its Type <c>Indeterminate</c>.
/// </para>
/// </remarks>
public static class FastDiff
{
    public static FastDiffResult Compare(
        PEReader before,
        PEReader after,
        FastDiffAxes axes = FastDiffAxes.ApiAndBody,
        CancellationToken cancellationToken = default)
    {
        var comparison = new FastDiffComparison(axes);
        comparison.Step(before, after, Timeout.InfiniteTimeSpan, cancellationToken);
        return comparison.Result!;
    }

    /// <summary>
    /// Returns the first compared fact that differs on each axis of one Type,
    /// or null for an axis whose facts are equal. A body difference inside IL
    /// is named by its method.
    /// </summary>
    public static (string? Api, string? Body) Explain(
        PEReader before,
        PEReader after,
        string fullName)
    {
        try
        {
            return ExplainCore(before, after, fullName);
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            return ("malformed: " + ex.Message, "malformed: " + ex.Message);
        }
    }

    static (string? Api, string? Body) ExplainCore(
        PEReader before,
        PEReader after,
        string fullName)
    {
        var a = new Side(before);
        var b = new Side(after);
        Unit? unitA = a.Units().Values.FirstOrDefault(unit => unit.FullName == fullName);
        Unit? unitB = b.Units().Values.FirstOrDefault(unit => unit.FullName == fullName);
        if (unitA is null || unitB is null)
            return ("one-sided", "one-sided");

        string? api = FirstDifference(a.ApiCensus(unitA), b.ApiCensus(unitB));
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        string? body = FirstDifference(
            a.BodyCensus(unitA, ownerVisible, out var methodsA),
            b.BodyCensus(unitB, ownerVisible, out var methodsB));
        if (body is null)
        {
            var work = new Work();
            foreach ((SymbolKey key, MethodDefinitionHandle methodA) in methodsA)
            {
                if (methodsB.TryGetValue(key, out MethodDefinitionHandle methodB)
                    && !BodyEqual(a, methodA, b, methodB, work))
                {
                    body = "IL " + key;
                    break;
                }
            }
        }
        return (api, body);
    }

    static string? FirstDifference(List<SymbolKey> before, List<SymbolKey> after)
    {
        string? onlyBefore = before.Except(after).Select(fact => fact.ToString()).FirstOrDefault();
        string? onlyAfter = after.Except(before).Select(fact => fact.ToString()).FirstOrDefault();
        return onlyBefore is null && onlyAfter is null
            ? null
            : $"- {onlyBefore}\n+ {onlyAfter}";
    }

    internal static FastDiffTypeState OneSided(Side side, Unit unit, FastDiffAxes axes)
    {
        FastDiffState presence = axes is FastDiffAxes.Api
            ? FastDiffState.NotCompared
            : FastDiffState.Changed;
        if (unit.Malformed)
        {
            return new FastDiffTypeState(
                unit.FullName,
                FastDiffState.Indeterminate,
                axes is FastDiffAxes.Api ? FastDiffState.NotCompared : FastDiffState.Indeterminate,
                unit.Identifier);
        }
        FastDiffState api;
        try
        {
            api = side.IsVisible(unit.Owner) ? FastDiffState.Changed : FastDiffState.Unchanged;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            side.Keys.Reset();
            api = FastDiffState.Indeterminate;
        }
        return new FastDiffTypeState(unit.FullName, api, presence, unit.Identifier);
    }

    internal static FastDiffTypeState CompareUnit(
        Side a,
        Unit unitA,
        Side b,
        Unit unitB,
        FastDiffAxes axes,
        Work work)
    {
        FastDiffState api = FastDiffState.Indeterminate;
        FastDiffState body = axes is FastDiffAxes.Api
            ? FastDiffState.NotCompared
            : FastDiffState.Indeterminate;
        if (unitA.Malformed || unitB.Malformed)
            return new FastDiffTypeState(unitA.FullName, api, body, unitA.Identifier);
        try
        {
            api = a.ApiCensus(unitA).SequenceEqual(b.ApiCensus(unitB))
                ? FastDiffState.Unchanged
                : FastDiffState.Changed;
            if (axes is FastDiffAxes.Api)
                return new FastDiffTypeState(unitA.FullName, api, body, unitA.Identifier);
            body = BodiesEqual(a, unitA, b, unitB, work)
                ? FastDiffState.Unchanged
                : FastDiffState.Changed;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            a.Keys.Reset();
            b.Keys.Reset();
        }
        return new FastDiffTypeState(unitA.FullName, api, body, unitA.Identifier);
    }

    static bool BodiesEqual(Side a, Unit unitA, Side b, Unit unitB, Work work)
    {
        // A Type visible on either side partitions its facts as visible on
        // both, so a visibility change is an API fact only.
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        if (!a.BodyCensus(unitA, ownerVisible, out Dictionary<SymbolKey, MethodDefinitionHandle> methodsA)
                .SequenceEqual(b.BodyCensus(unitB, ownerVisible, out Dictionary<SymbolKey, MethodDefinitionHandle> methodsB)))
        {
            return false;
        }

        // Equal censuses declare the same non-public members. A public method
        // on one side only is an API fact; its body is not compared.
        foreach ((SymbolKey key, MethodDefinitionHandle methodA) in methodsA)
        {
            if (!methodsB.TryGetValue(key, out MethodDefinitionHandle methodB))
                continue;
            work.Bodies++;
            if (!BodyEqual(a, methodA, b, methodB, work))
                return false;
        }
        return true;
    }

    static bool BodyEqual(
        Side a,
        MethodDefinitionHandle handleA,
        Side b,
        MethodDefinitionHandle handleB,
        Work work)
    {
        MethodDefinition methodA = a.Md.GetMethodDefinition(handleA);
        MethodDefinition methodB = b.Md.GetMethodDefinition(handleB);
        if ((methodA.RelativeVirtualAddress == 0) != (methodB.RelativeVirtualAddress == 0))
            return false;
        if (methodA.RelativeVirtualAddress == 0)
            return true;

        MethodBodyBlock bodyA = a.Pe.GetMethodBody(methodA.RelativeVirtualAddress);
        MethodBodyBlock bodyB = b.Pe.GetMethodBody(methodB.RelativeVirtualAddress);
        if (bodyA.MaxStack != bodyB.MaxStack
            || bodyA.LocalVariablesInitialized != bodyB.LocalVariablesInitialized
            || bodyA.Size != bodyB.Size
            || bodyA.LocalSignature.IsNil != bodyB.LocalSignature.IsNil
            || (!bodyA.LocalSignature.IsNil
                && a.Key(bodyA.LocalSignature) != b.Key(bodyB.LocalSignature)))
        {
            return false;
        }

        ImmutableArray<ExceptionRegion> regionsA = bodyA.ExceptionRegions;
        ImmutableArray<ExceptionRegion> regionsB = bodyB.ExceptionRegions;
        if (regionsA.Length != regionsB.Length)
            return false;
        for (int i = 0; i < regionsA.Length; i++)
        {
            ExceptionRegion x = regionsA[i];
            ExceptionRegion y = regionsB[i];
            if (x.Kind != y.Kind
                || x.TryOffset != y.TryOffset
                || x.TryLength != y.TryLength
                || x.HandlerOffset != y.HandlerOffset
                || x.HandlerLength != y.HandlerLength
                || x.FilterOffset != y.FilterOffset
                || x.CatchType.IsNil != y.CatchType.IsNil
                || (!x.CatchType.IsNil && a.Key(x.CatchType) != b.Key(y.CatchType)))
            {
                return false;
            }
        }

        BlobReader readerA = bodyA.GetILReader();
        BlobReader readerB = bodyB.GetILReader();
        work.IlBytes += readerA.Length;
        while (readerA.RemainingBytes > 0)
        {
            int opCode = readerA.ReadByte();
            if (opCode != readerB.ReadByte())
                return false;
            if (opCode == 0xFE)
            {
                opCode = 0xFE00 | readerA.ReadByte();
                if (opCode != (0xFE00 | readerB.ReadByte()))
                    return false;
            }

            switch (OperandOf((ILOpCode)opCode))
            {
                case Operand.None:
                    break;
                case Operand.Int8:
                    if (readerA.ReadByte() != readerB.ReadByte())
                        return false;
                    break;
                case Operand.Int16:
                    if (readerA.ReadInt16() != readerB.ReadInt16())
                        return false;
                    break;
                case Operand.Int32:
                    if (readerA.ReadInt32() != readerB.ReadInt32())
                        return false;
                    break;
                case Operand.Int64:
                    if (readerA.ReadInt64() != readerB.ReadInt64())
                        return false;
                    break;
                case Operand.Token:
                    if (a.ReferenceKey(MetadataTokens.EntityHandle(readerA.ReadInt32()))
                        != b.ReferenceKey(MetadataTokens.EntityHandle(readerB.ReadInt32())))
                    {
                        return false;
                    }
                    break;
                case Operand.String:
                    if (a.Md.GetUserString(MetadataTokens.UserStringHandle(readerA.ReadInt32() & 0xFFFFFF))
                        != b.Md.GetUserString(MetadataTokens.UserStringHandle(readerB.ReadInt32() & 0xFFFFFF)))
                    {
                        return false;
                    }
                    break;
                case Operand.Switch:
                    int count = readerA.ReadInt32();
                    if (count != readerB.ReadInt32())
                        return false;
                    for (int i = 0; i < count; i++)
                    {
                        if (readerA.ReadInt32() != readerB.ReadInt32())
                            return false;
                    }
                    break;
            }
        }
        return true;
    }

    enum Operand
    {
        None,
        Int8,
        Int16,
        Int32,
        Int64,
        Token,
        String,
        Switch,
    }

    // The `no.` prefix (0xFE 0x19) takes a one-byte operand and has no ILOpCode member.
    const ILOpCode NoPrefix = (ILOpCode)0xFE19;

    // Every defined opcode, indexed by its second byte for the 0xFE page.
    static readonly bool[] KnownOneByte = new bool[256];
    static readonly bool[] KnownTwoByte = new bool[256];

    static FastDiff()
    {
        foreach (ILOpCode opCode in Enum.GetValues<ILOpCode>())
        {
            int value = (int)opCode;
            if (value <= 0xFF)
                KnownOneByte[value] = true;
            else if ((value & 0xFF00) == 0xFE00)
                KnownTwoByte[value & 0xFF] = true;
        }
    }

    static bool IsKnown(ILOpCode opCode)
    {
        int value = (int)opCode;
        return value <= 0xFF ? KnownOneByte[value] : KnownTwoByte[value & 0xFF];
    }

    static Operand OperandOf(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned or NoPrefix
            or ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s
            or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s
            or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s
            or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s
            or ILOpCode.Blt_un_s or ILOpCode.Leave_s => Operand.Int8,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => Operand.Int16,
        ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
            or ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue
            or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt
            or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un
            or ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un
            or ILOpCode.Blt_un or ILOpCode.Leave => Operand.Int32,
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => Operand.Int64,
        ILOpCode.Ldstr => Operand.String,
        ILOpCode.Switch => Operand.Switch,
        ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt
            or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Newobj
            or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Unbox
            or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
            or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
            or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr
            or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
            or ILOpCode.Unbox_any or ILOpCode.Refanyval or ILOpCode.Mkrefany
            or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
            or ILOpCode.Initobj or ILOpCode.Constrained or ILOpCode.Sizeof => Operand.Token,
        _ when IsKnown(opCode) => Operand.None,
        _ => throw new BadImageFormatException($"Unknown IL opcode 0x{(int)opCode:X}."),
    };

    static bool IsMalformed(Exception ex)
        => ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or ArgumentException
            or IndexOutOfRangeException;

    /// <summary>The Type units of one side, read row by row.</summary>
    internal sealed class UnitTable
    {
        public OrderedDictionary<SymbolKey, Unit> Units { get; } = [];

        public List<(TypeDefinitionHandle Type, TypeDefinitionHandle Owner)> Generated { get; } = [];

        // Row 1 is the <Module> pseudo-Type.
        public int NextRow { get; set; } = 2;
    }

    internal sealed class Work
    {
        public int Bodies;
        public long IlBytes;
    }

    /// <param name="Malformed">
    /// The Type's declaring chain could not be read, so neither axis can be
    /// decided.
    /// </param>
    internal sealed record Unit(
        string FullName,
        string Identifier,
        TypeDefinitionHandle Owner,
        List<TypeDefinitionHandle> Generated,
        bool Malformed = false);

    internal sealed class Side
    {
        readonly Guid _mvid;
        readonly Dictionary<int, SymbolKey> _keys = [];
        readonly Dictionary<int, string> _displayNames = [];
        readonly HashSet<SymbolKey> _duplicateTypeKeys = [];
        readonly SignatureKeys _signatures;
        readonly Dictionary<TypeDefinitionHandle, HashSet<MethodDefinitionHandle>> _explicitImplementations = [];

        /// <summary>
        /// Metadata as stored, without Windows Runtime projection, so every
        /// name is a <c>#Strings</c> heap entry.
        /// </summary>
        const MetadataReaderOptions StoredMetadata = MetadataReaderOptions.None;

        public Side(PEReader pe)
        {
            Pe = pe;
            Md = MetadataFormatAdmission.GetMetadataReader(pe, StoredMetadata);
            _mvid = Md.GetGuid(Md.GetModuleDefinition().Mvid);
            Keys = new SymbolKey.Builder(new SymbolKey.Utf8Names(pe, Md));
            _signatures = new SignatureKeys(this);
        }

        /// <summary>The only way this side spells a compared key or fact.</summary>
        public SymbolKey.Builder Keys { get; }

        /// <summary>
        /// Rebinds this side to a reader over the same image. Every retained
        /// fact is a handle, a key, or a string, so it stays valid for any
        /// reader of that image.
        /// </summary>
        public void Bind(PEReader pe)
        {
            if (ReferenceEquals(pe, Pe))
                return;
            MetadataReader md = MetadataFormatAdmission.GetMetadataReader(pe, StoredMetadata);
            if (md.GetGuid(md.GetModuleDefinition().Mvid) != _mvid)
                throw new InvalidOperationException("A Fast Diff comparison continues over the same images.");
            Pe = pe;
            Md = md;
            Keys.Names = new SymbolKey.Utf8Names(pe, md);
        }

        /// <summary>
        /// The methods of one Type that implement an interface member
        /// explicitly, read when a unit of that Type is compared.
        /// </summary>
        HashSet<MethodDefinitionHandle> ExplicitImplementations(TypeDefinitionHandle type)
        {
            if (_explicitImplementations.TryGetValue(type, out var methods))
                return methods;
            methods = [];
            foreach (MethodImplementationHandle handle in Md.GetTypeDefinition(type).GetMethodImplementations())
            {
                if (Md.GetMethodImplementation(handle).MethodBody is
                    { Kind: HandleKind.MethodDefinition } body)
                {
                    methods.Add((MethodDefinitionHandle)body);
                }
            }
            _explicitImplementations[type] = methods;
            return methods;
        }

        public PEReader Pe { get; private set; }

        public MetadataReader Md { get; private set; }

        public OrderedDictionary<SymbolKey, Unit> Units()
        {
            var table = new UnitTable();
            ReadUnits(table, static () => false);
            return table.Units;
        }

        /// <summary>
        /// Reads Type rows into <paramref name="table"/> until the table is
        /// complete or <paramref name="overBudget"/> reports true at a row
        /// boundary. Returns true when the table is complete.
        /// </summary>
        public bool ReadUnits(UnitTable table, Func<bool> overBudget)
        {
            OrderedDictionary<SymbolKey, Unit> units = table.Units;
            List<(TypeDefinitionHandle Type, TypeDefinitionHandle Owner)> generated = table.Generated;
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            int rows = Md.TypeDefinitions.Count;
            for (; table.NextRow <= rows; table.NextRow++)
            {
                if (overBudget())
                    return false;
                TypeDefinitionHandle handle = MetadataTokens.TypeDefinitionHandle(table.NextRow);

                try
                {
                    if (!TryDeclaringChain(handle, chain, out int length))
                    {
                        AddMalformed(units, handle);
                        continue;
                    }

                    // A Type with a compiler-generated Type in its declaring chain
                    // belongs to the nearest declared Type above that generated
                    // Type; under a top-level generated Type it belongs to none.
                    int generatedAt = -1;
                    for (int i = 0; i < length; i++)
                    {
                        if (IsGeneratedName(Md.GetTypeDefinition(chain[i])))
                        {
                            generatedAt = i;
                            break;
                        }
                    }
                    if (generatedAt < 0)
                    {
                        SymbolKey key = TypeKey(handle);
                        if (units.TryGetValue(key, out Unit? existing))
                        {
                            // Two rows that spell one name cannot be told apart, so
                            // neither is decided.
                            units[key] = existing with { Malformed = true };
                            _duplicateTypeKeys.Add(key);
                            AddMalformed(units, handle);
                            continue;
                        }
                        units[key] = new Unit(
                            DisplayName(handle), EscapedName(handle), handle, []);
                        continue;
                    }
                    if (generatedAt > 0)
                        generated.Add((handle, chain[generatedAt - 1]));
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    // A name or row that cannot be read affects only its own Type.
                    Keys.Reset();
                    AddMalformed(units, handle);
                }
            }
            foreach ((TypeDefinitionHandle type, TypeDefinitionHandle owner) in generated)
            {
                try
                {
                    if (units.TryGetValue(TypeKey(owner), out Unit? unit))
                        unit.Generated.Add(type);
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    // The owner's own row is malformed and already reported.
                    Keys.Reset();
                }
            }
            generated.Clear();
            return true;
        }

        void AddMalformed(OrderedDictionary<SymbolKey, Unit> units, TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            int mark = Keys.Begin();
            SymbolKey row = Keys.Mark(SymbolPart.Malformed).Int(token).End(mark);
            units[row] = new Unit(SafeName(handle), $"!{token:X8}", handle, [], Malformed: true);
        }

        /// <summary>
        /// Writes the declaring chain of a Type, outermost first, through the
        /// bounded shared traversal.
        /// </summary>
        bool TryDeclaringChain(TypeDefinitionHandle handle, Span<TypeDefinitionHandle> chain, out int length)
            => MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                    Md, handle, chain, out length, out _, out _)
                && length > 0;

        string SafeName(TypeDefinitionHandle handle)
        {
            try
            {
                return DisplayName(handle);
            }
            catch (Exception ex) when (IsMalformed(ex))
            {
                return $"<row 0x{MetadataTokens.GetToken(handle):X8}>";
            }
        }

        bool IsGeneratedName(TypeDefinition type)
            => Md.StringComparer.StartsWith(type.Name, "<");

        /// <remarks>
        /// Visibility is the Type's own row, not its declaring chain: the
        /// Public API surface admits a public nested Type of a non-public Type,
        /// so the API axis must compare it.
        /// </remarks>
        public bool IsVisible(TypeDefinitionHandle handle)
            => (Md.GetTypeDefinition(handle).Attributes & TypeAttributes.VisibilityMask)
                    is TypeAttributes.Public
                    or TypeAttributes.NestedPublic
                    or TypeAttributes.NestedFamily
                    or TypeAttributes.NestedFamORAssem;

        bool IsVisible(MethodDefinitionHandle handle, MethodDefinition method)
            => IsVisibleAccess((int)(method.Attributes & MethodAttributes.MemberAccessMask))
                || ExplicitImplementations(method.GetDeclaringType()).Contains(handle);

        bool IsVisible(FieldDefinition field)
            => IsVisibleAccess((int)(field.Attributes & FieldAttributes.FieldAccessMask));

        // Public, Family, and FamORAssem share their encoding across method and field access.
        static bool IsVisibleAccess(int access)
            => access is (int)MethodAttributes.Public
                or (int)MethodAttributes.Family
                or (int)MethodAttributes.FamORAssem;

        /// <summary>
        /// Facts of visible Types and members that the API diff does not
        /// present open with <see cref="SymbolPart.NonApi"/> and belong to the
        /// Body axis. Custom attributes stay API facts: compiler-emitted ones
        /// such as nullable annotations shape the rendered signatures the API
        /// diff compares.
        /// </summary>
        static bool IsNonApi(SymbolKey fact) => fact.Kind is SymbolPart.NonApi;

        public List<SymbolKey> ApiCensus(Unit unit)
        {
            var census = new List<SymbolKey>();
            bool visible = IsVisible(unit.Owner);
            int mark = Keys.Begin();
            census.Add(Keys.Mark(visible ? SymbolPart.Visible : SymbolPart.Hidden).End(mark));
            if (!visible)
                return census;
            census.AddRange(VisibleFacts(unit).Where(fact => !IsNonApi(fact)));
            census.Sort();
            return census;
        }

        List<SymbolKey> VisibleFacts(Unit unit)
        {
            var facts = new List<SymbolKey>();
            TypeFacts(facts, unit.Owner);
            MemberFacts(facts, unit.Owner, api: true, out _);
            return facts;
        }

        public List<SymbolKey> BodyCensus(
            Unit unit,
            bool visible,
            out Dictionary<SymbolKey, MethodDefinitionHandle> methods)
        {
            var census = new List<SymbolKey>();
            methods = [];
            if (!visible)
                TypeFacts(census, unit.Owner);
            else
                census.AddRange(VisibleFacts(unit).Where(IsNonApi));
            MemberFacts(census, unit.Owner, api: !visible ? null : false, out var ownerMethods);
            foreach ((SymbolKey key, MethodDefinitionHandle method) in ownerMethods)
                AddMethod(methods, key, method);
            foreach (TypeDefinitionHandle generated in unit.Generated)
            {
                TypeFacts(census, generated);
                MemberFacts(census, generated, api: null, out var generatedMethods);
                foreach ((SymbolKey key, MethodDefinitionHandle method) in generatedMethods)
                    AddMethod(methods, key, method);
            }
            census.Sort();
            return census;
        }

        // Two method rows that spell one key, which valid metadata does not
        // allow, cannot be told apart, so the body is not decided.
        static void AddMethod(
            Dictionary<SymbolKey, MethodDefinitionHandle> methods,
            SymbolKey key,
            MethodDefinitionHandle method)
        {
            if (!methods.TryAdd(key, method))
                throw new BadImageFormatException($"Two methods share the compared key {key}.");
        }

        void TypeFacts(List<SymbolKey> census, TypeDefinitionHandle handle)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            SymbolKey name = TypeKey(handle);
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.TypeFact).Key(name).Int((int)(type.Attributes & ~TypeAttributes.BeforeFieldInit));
            OptionalKey(type.BaseType);
            census.Add(Keys.End(mark));
            // beforefieldinit only changes when a static constructor runs.
            if ((type.Attributes & TypeAttributes.BeforeFieldInit) != 0
                && type.GetMethods().Any(method =>
                    Md.StringComparer.Equals(Md.GetMethodDefinition(method).Name, ".cctor")))
            {
                mark = Keys.Begin();
                census.Add(Keys.Mark(SymbolPart.NonApi).Mark(SymbolPart.BeforeFieldInit).Key(name).End(mark));
            }
            foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
            {
                InterfaceImplementation value = Md.GetInterfaceImplementation(implementation);
                mark = Keys.Begin();
                SymbolKey fact = Keys.Mark(SymbolPart.InterfaceFact).Key(name).Key(Key(value.Interface)).End(mark);
                census.Add(fact);
                Attributes(census, value.GetCustomAttributes(), fact);
            }
            GenericParameters(census, type.GetGenericParameters(), name);
            Attributes(census, type.GetCustomAttributes(), name);
            // A nested Type inherits its declaring Type's nullable context
            // without an attribute row of its own, and that context shapes
            // the rendered signatures of every member that does not override it.
            mark = Keys.Begin();
            census.Add(Keys.Mark(SymbolPart.NullableContext).Key(name)
                .Int(NullabilityReader.GetTypeNullableContext(Md, handle)).End(mark));
            TypeLayout layout = type.GetLayout();
            if (!layout.IsDefault)
            {
                mark = Keys.Begin();
                census.Add(Keys.Mark(SymbolPart.Layout).Key(name).Int(layout.PackingSize).Int(layout.Size).End(mark));
            }
            foreach (MethodImplementationHandle implementation in type.GetMethodImplementations())
            {
                MethodImplementation value = Md.GetMethodImplementation(implementation);
                mark = Keys.Begin();
                census.Add(Keys.Mark(SymbolPart.MethodImplementation).Key(name)
                    .Key(Key(value.MethodBody)).Key(Key(value.MethodDeclaration)).End(mark));
            }
        }

        /// <param name="api">
        /// True for the public members only, false for the non-public members,
        /// and null for every member.
        /// </param>
        void MemberFacts(
            List<SymbolKey> census,
            TypeDefinitionHandle handle,
            bool? api,
            out List<(SymbolKey Key, MethodDefinitionHandle Method)> methods)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            SymbolKey name = TypeKey(handle);
            methods = [];
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = Md.GetFieldDefinition(fieldHandle);
                if (api is { } wanted && IsVisible(field) != wanted)
                    continue;
                SymbolKey key = Key(fieldHandle);
                int mark = Keys.Begin();
                Keys.Mark(SymbolPart.FieldFact).Key(key).Int((int)field.Attributes);
                Constant(field.GetDefaultValue());
                census.Add(Keys.Int(field.GetOffset()).End(mark));
                Attributes(census, field.GetCustomAttributes(), key);
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = Md.GetMethodDefinition(methodHandle);
                SymbolKey key = Key(methodHandle);
                methods.Add((key, methodHandle));
                if (api is { } wanted && IsVisible(methodHandle, method) != wanted)
                    continue;
                int mark = Keys.Begin();
                census.Add(Keys.Mark(SymbolPart.MethodFact).Key(key)
                    .Int((int)method.Attributes).Int((int)method.ImplAttributes).End(mark));
                GenericParameters(census, method.GetGenericParameters(), key);
                Attributes(census, method.GetCustomAttributes(), key);
                foreach (ParameterHandle parameterHandle in method.GetParameters())
                {
                    Parameter parameter = Md.GetParameter(parameterHandle);
                    mark = Keys.Begin();
                    SymbolKey parameterKey = Keys.Mark(SymbolPart.Parameter).Key(key)
                        .Int(parameter.SequenceNumber).End(mark);
                    mark = Keys.Begin();
                    Keys.Mark(SymbolPart.ParameterFact).Key(parameterKey)
                        .Name(parameter.Name).Int((int)parameter.Attributes);
                    Constant(parameter.GetDefaultValue());
                    census.Add(Keys.End(mark));
                    Attributes(census, parameter.GetCustomAttributes(), parameterKey);
                }
                MethodImport import = method.GetImport();
                if (!import.Module.IsNil)
                {
                    mark = Keys.Begin();
                    census.Add(Keys.Mark(SymbolPart.Import).Key(key).Name(import.Name)
                        .Name(Md.GetModuleReference(import.Module).Name).Int((int)import.Attributes).End(mark));
                }
            }
            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = Md.GetPropertyDefinition(propertyHandle);
                PropertyAccessors accessors = property.GetAccessors();
                if (api is { } wanted
                    && AccessorVisible(accessors.Getter, accessors.Setter, accessors.Others) != wanted)
                {
                    continue;
                }
                SymbolKey key = PropertyKey(name, property);
                int mark = Keys.Begin();
                Keys.Mark(SymbolPart.PropertyFact).Key(key).Int((int)property.Attributes);
                Constant(property.GetDefaultValue());
                Accessor(accessors.Getter);
                Accessor(accessors.Setter);
                census.Add(Keys.End(mark));
                Attributes(census, property.GetCustomAttributes(), key);
            }
            foreach (EventDefinitionHandle eventHandle in type.GetEvents())
            {
                EventDefinition value = Md.GetEventDefinition(eventHandle);
                EventAccessors accessors = value.GetAccessors();
                if (api is { } wanted
                    && AccessorVisible(accessors.Adder, accessors.Remover, accessors.Others) != wanted)
                {
                    continue;
                }
                // The Event marker keeps an event from spelling the key of its backing field.
                SymbolKey eventType = Key(value.Type);
                int mark = Keys.Begin();
                SymbolKey key = Keys.Mark(SymbolPart.Event).Key(name).Name(value.Name).Key(eventType).End(mark);
                mark = Keys.Begin();
                Keys.Mark(SymbolPart.EventFact).Key(key).Int((int)value.Attributes);
                Accessor(accessors.Adder);
                Accessor(accessors.Remover);
                Accessor(accessors.Raiser);
                census.Add(Keys.End(mark));
                Attributes(census, value.GetCustomAttributes(), key);
            }
        }

        bool AccessorVisible(
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            foreach (MethodDefinitionHandle accessor in (MethodDefinitionHandle[])[first, second, .. others])
            {
                if (!accessor.IsNil && IsVisible(accessor, Md.GetMethodDefinition(accessor)))
                    return true;
            }
            return false;
        }

        void Accessor(MethodDefinitionHandle accessor) => OptionalKey(accessor);

        /// <summary>Writes the key of <paramref name="handle"/>, or <see cref="SymbolPart.Absent"/>.</summary>
        void OptionalKey(EntityHandle handle)
        {
            if (handle.IsNil)
            {
                Keys.Mark(SymbolPart.Absent);
                return;
            }
            SymbolKey key = Key(handle);
            Keys.Key(key);
        }

        void GenericParameters(List<SymbolKey> census, GenericParameterHandleCollection parameters, SymbolKey owner)
        {
            foreach (GenericParameterHandle handle in parameters)
            {
                GenericParameter parameter = Md.GetGenericParameter(handle);
                int mark = Keys.Begin();
                SymbolKey key = Keys.Mark(SymbolPart.GenericParameter).Key(owner).Int(parameter.Index).End(mark);
                mark = Keys.Begin();
                census.Add(Keys.Mark(SymbolPart.GenericParameterFact).Key(key)
                    .Name(parameter.Name).Int((int)parameter.Attributes).End(mark));
                foreach (GenericParameterConstraintHandle constraintHandle in parameter.GetConstraints())
                {
                    GenericParameterConstraint constraint = Md.GetGenericParameterConstraint(constraintHandle);
                    SymbolKey constraintType = Key(constraint.Type);
                    mark = Keys.Begin();
                    SymbolKey fact = Keys.Mark(SymbolPart.Constraint).Key(key).Key(constraintType).End(mark);
                    census.Add(fact);
                    Attributes(census, constraint.GetCustomAttributes(), fact);
                }
                Attributes(census, parameter.GetCustomAttributes(), key);
            }
        }

        void Attributes(List<SymbolKey> census, CustomAttributeHandleCollection attributes, SymbolKey owner)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = Md.GetCustomAttribute(handle);
                SymbolKey constructor = Key(attribute.Constructor);
                int mark = Keys.Begin();
                Keys.Mark(SymbolPart.Attribute).Key(owner).Key(constructor);
                Blob(attribute.Value);
                census.Add(Keys.End(mark));
            }
        }

        /// <summary>Writes a constant's Type code and value, or <see cref="SymbolPart.Absent"/>.</summary>
        void Constant(ConstantHandle handle)
        {
            if (handle.IsNil)
            {
                Keys.Mark(SymbolPart.Absent);
                return;
            }
            Constant constant = Md.GetConstant(handle);
            Keys.Mark(SymbolPart.Constant).Int((int)constant.TypeCode);
            Blob(constant.Value);
        }

        void Blob(BlobHandle handle)
        {
            BlobReader reader = Md.GetBlobReader(handle);
            Keys.Blob(reader);
        }

        SymbolKey PropertyKey(SymbolKey type, PropertyDefinition property)
        {
            Guard(property.Signature, SignatureBlobGuard.Kind.Property);
            MethodSignature<SymbolKey> signature = property.DecodeSignature(_signatures, null);
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.Property).Key(type).Name(property.Name).Int(signature.Header.IsInstance ? 1 : 0);
            Signature(signature);
            return Keys.End(mark);
        }

        /// <summary>
        /// Writes a signature's parameters and return Type, with
        /// <see cref="SymbolPart.Vararg"/> where the required parameters end.
        /// </summary>
        public void Signature(MethodSignature<SymbolKey> signature)
        {
            ImmutableArray<SymbolKey> types = signature.ParameterTypes;
            for (int i = 0; i < types.Length; i++)
            {
                if (i == signature.RequiredParameterCount)
                    Keys.Mark(SymbolPart.Vararg);
                Keys.Key(types[i]);
            }
            Keys.Mark(SymbolPart.Return).Key(signature.ReturnType);
        }

        /// <summary>The symbolic name of a Type: its namespace and each name of its declaring chain.</summary>
        public SymbolKey TypeKey(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_keys.TryGetValue(token, out SymbolKey key))
                return key;
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!TryDeclaringChain(handle, chain, out int length))
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.Type);
            TypeDefinition root = Md.GetTypeDefinition(chain[0]);
            if (!root.Namespace.IsNil)
                Keys.Mark(SymbolPart.Namespace).Name(root.Namespace);
            for (int i = 0; i < length; i++)
                Keys.Name(Md.GetTypeDefinition(chain[i]).Name);
            _keys[token] = key = Keys.End(mark);
            return key;
        }

        string DisplayName(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (!_displayNames.TryGetValue(token, out string? name))
                _displayNames[token] = name = DefinitionName(handle);
            return name;
        }

        /// <summary>The injective escaped name of a Type.</summary>
        string EscapedName(TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!TryDeclaringChain(handle, chain, out int length))
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            TypeDefinition root = Md.GetTypeDefinition(chain[0]);
            var segments = ImmutableArray.CreateBuilder<string>(length);
            for (int i = 0; i < length; i++)
                segments.Add(Md.GetString(Md.GetTypeDefinition(chain[i]).Name));
            return MetadataTypeDefinitionName.Create(
                    root.Namespace.IsNil ? "" : Md.GetString(root.Namespace),
                    segments.MoveToImmutable())
                is MetadataTypeDefinitionNameResult.Valid { Name: var name }
                ? name.ToEscapedFullName()
                : throw new BadImageFormatException("The Type name cannot be represented.");
        }

        /// <summary>The display name of a Type, with <c>.</c> between nested names.</summary>
        string DefinitionName(TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!TryDeclaringChain(handle, chain, out int length))
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            TypeDefinition root = Md.GetTypeDefinition(chain[0]);
            var name = new System.Text.StringBuilder();
            if (!root.Namespace.IsNil)
                name.Append(Md.GetString(root.Namespace)).Append('.');
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                    name.Append('.');
                name.Append(Md.GetString(Md.GetTypeDefinition(chain[i]).Name));
            }
            return name.ToString();
        }

        /// <summary>
        /// The key of an entity a fact refers to, rather than declares. A
        /// reference to a Type row that shares its key with another row, or to
        /// a compiler-controlled (<c>PrivateScope</c>) method or field, which
        /// ECMA-335 lets share a name and signature, cannot say which
        /// declaration it names, so the referring fact is not decided.
        /// </summary>
        public SymbolKey ReferenceKey(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    RejectDuplicate((TypeDefinitionHandle)handle);
                    break;
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition method = Md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    if ((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.PrivateScope)
                        throw new BadImageFormatException("A reference names a compiler-controlled method.");
                    RejectDuplicate(method.GetDeclaringType());
                    break;
                }
                case HandleKind.FieldDefinition:
                {
                    FieldDefinition field = Md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.PrivateScope)
                        throw new BadImageFormatException("A reference names a compiler-controlled field.");
                    RejectDuplicate(field.GetDeclaringType());
                    break;
                }
            }
            return Key(handle);
        }

        void RejectDuplicate(TypeDefinitionHandle handle)
        {
            if (_duplicateTypeKeys.Count > 0 && _duplicateTypeKeys.Contains(TypeKey(handle)))
                throw new BadImageFormatException("A reference names one of two Type rows that share a key.");
        }

        /// <summary>The symbolic name of a metadata entity, resolved once per side.</summary>
        public SymbolKey Key(EntityHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_keys.TryGetValue(token, out SymbolKey key))
                return key;
            key = handle.Kind switch
            {
                HandleKind.TypeDefinition => TypeKey((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => TypeReferenceKey((TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => TypeSpecificationKey((TypeSpecificationHandle)handle),
                HandleKind.MethodDefinition => MethodKey((MethodDefinitionHandle)handle),
                HandleKind.FieldDefinition => FieldKey((FieldDefinitionHandle)handle),
                HandleKind.MemberReference => MemberReferenceKey((MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => MethodSpecificationKey((MethodSpecificationHandle)handle),
                HandleKind.StandaloneSignature => StandaloneKey((StandaloneSignatureHandle)handle),
                _ => throw new BadImageFormatException(
                    $"Unsupported metadata token 0x{token:X8} in a compared fact."),
            };
            _keys[token] = key;
            return key;
        }

        /// <summary>
        /// Rejects a signature blob whose shape could exhaust the stack while
        /// decoding; the unit that needs it becomes Indeterminate.
        /// </summary>
        void Guard(BlobHandle signature, SignatureBlobGuard.Kind kind)
        {
            if (!SignatureBlobGuard.IsSafeToDecode(Md, signature, kind))
                throw new BadImageFormatException("The signature blob is too deep to decode.");
        }

        SymbolKey TypeSpecificationKey(TypeSpecificationHandle handle)
        {
            if (!TypeSpecGuard.TryEnter(Md, handle, out TypeSpecGuard.Scope scope))
                throw new BadImageFormatException("The Type specification exceeds the decode budget.");
            using (scope)
            {
                TypeSpecification specification = Md.GetTypeSpecification(handle);
                Guard(specification.Signature, SignatureBlobGuard.Kind.TypeSpecification);
                return specification.DecodeSignature(_signatures, null);
            }
        }

        /// <summary>
        /// The symbolic name of a referenced Type, spelled as the key of a
        /// definition of that name, so a reference and a definition compare
        /// by name rather than by assembly.
        /// </summary>
        SymbolKey TypeReferenceKey(TypeReferenceHandle handle)
        {
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    Md, handle, chain, out int length, out _, out var rejection)
                || length == 0)
            {
                throw new BadImageFormatException(
                    rejection?.Detail ?? "The Type reference has an invalid resolution-scope chain.");
            }
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.Type);
            TypeReference root = Md.GetTypeReference(chain[0]);
            if (!root.Namespace.IsNil)
                Keys.Mark(SymbolPart.Namespace).Name(root.Namespace);
            for (int i = 0; i < length; i++)
                Keys.Name(Md.GetTypeReference(chain[i]).Name);
            return Keys.End(mark);
        }

        SymbolKey MethodKey(MethodDefinitionHandle handle)
        {
            MethodDefinition method = Md.GetMethodDefinition(handle);
            Guard(method.Signature, SignatureBlobGuard.Kind.Method);
            MethodSignature<SymbolKey> signature = method.DecodeSignature(_signatures, null);
            return MethodKey(TypeKey(method.GetDeclaringType()), method.Name, signature);
        }

        SymbolKey MethodKey(SymbolKey parent, StringHandle name, MethodSignature<SymbolKey> signature)
        {
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.Method).Key(parent).Name(name)
                .Int(signature.GenericParameterCount).Int(signature.Header.RawValue);
            Signature(signature);
            return Keys.End(mark);
        }

        SymbolKey FieldKey(FieldDefinitionHandle handle)
        {
            FieldDefinition field = Md.GetFieldDefinition(handle);
            Guard(field.Signature, SignatureBlobGuard.Kind.Field);
            return FieldKey(TypeKey(field.GetDeclaringType()), field.Name, field.DecodeSignature(_signatures, null));
        }

        SymbolKey FieldKey(SymbolKey parent, StringHandle name, SymbolKey type)
        {
            int mark = Keys.Begin();
            return Keys.Mark(SymbolPart.Field).Key(parent).Name(name).Key(type).End(mark);
        }

        SymbolKey MemberReferenceKey(MemberReferenceHandle handle)
        {
            MemberReference member = Md.GetMemberReference(handle);
            SymbolKey parent = ReferenceKey(member.Parent);
            if (member.GetKind() == MemberReferenceKind.Field)
            {
                Guard(member.Signature, SignatureBlobGuard.Kind.Field);
                return FieldKey(parent, member.Name, member.DecodeFieldSignature(_signatures, null));
            }
            Guard(member.Signature, SignatureBlobGuard.Kind.Method);
            return MethodKey(parent, member.Name, member.DecodeMethodSignature(_signatures, null));
        }

        SymbolKey MethodSpecificationKey(MethodSpecificationHandle handle)
        {
            MethodSpecification specification = Md.GetMethodSpecification(handle);
            Guard(specification.Signature, SignatureBlobGuard.Kind.MethodSpecification);
            SymbolKey method = ReferenceKey(specification.Method);
            ImmutableArray<SymbolKey> arguments = specification.DecodeSignature(_signatures, null);
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.MethodSpecification).Key(method);
            foreach (SymbolKey argument in arguments)
                Keys.Key(argument);
            return Keys.End(mark);
        }

        SymbolKey StandaloneKey(StandaloneSignatureHandle handle)
        {
            StandaloneSignature signature = Md.GetStandaloneSignature(handle);
            int mark;
            if (signature.GetKind() == StandaloneSignatureKind.LocalVariables)
            {
                Guard(signature.Signature, SignatureBlobGuard.Kind.LocalVariables);
                ImmutableArray<SymbolKey> locals = signature.DecodeLocalSignature(_signatures, null);
                mark = Keys.Begin();
                Keys.Mark(SymbolPart.LocalSignature);
                foreach (SymbolKey local in locals)
                    Keys.Key(local);
                return Keys.End(mark);
            }
            Guard(signature.Signature, SignatureBlobGuard.Kind.StandaloneMethod);
            MethodSignature<SymbolKey> method = signature.DecodeMethodSignature(_signatures, null);
            mark = Keys.Begin();
            Keys.Mark(SymbolPart.StandaloneSignature).Int(method.Header.RawValue);
            Signature(method);
            return Keys.End(mark);
        }
    }

    sealed class SignatureKeys(Side side) : ISignatureTypeProvider<SymbolKey, object?>
    {
        readonly SymbolKey[] _primitives = new SymbolKey[256];

        SymbolKey.Builder Keys => side.Keys;

        public SymbolKey GetArrayType(SymbolKey elementType, ArrayShape shape)
        {
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.Array).Key(elementType).Int(shape.Rank).Int(shape.Sizes.Length);
            foreach (int size in shape.Sizes)
                Keys.Int(size);
            Keys.Int(shape.LowerBounds.Length);
            foreach (int bound in shape.LowerBounds)
                Keys.Int(bound);
            return Keys.End(mark);
        }

        public SymbolKey GetByReferenceType(SymbolKey elementType) => Wrap(SymbolPart.ByReference, elementType);

        public SymbolKey GetFunctionPointerType(MethodSignature<SymbolKey> signature)
        {
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.FunctionPointer).Int(signature.Header.RawValue);
            side.Signature(signature);
            return Keys.End(mark);
        }

        public SymbolKey GetGenericInstantiation(SymbolKey genericType, ImmutableArray<SymbolKey> typeArguments)
        {
            int mark = Keys.Begin();
            Keys.Mark(SymbolPart.GenericInstance).Key(genericType);
            foreach (SymbolKey argument in typeArguments)
                Keys.Key(argument);
            return Keys.End(mark);
        }

        public SymbolKey GetGenericMethodParameter(object? genericContext, int index)
        {
            int mark = Keys.Begin();
            return Keys.Mark(SymbolPart.MethodParameter).Int(index).End(mark);
        }

        public SymbolKey GetGenericTypeParameter(object? genericContext, int index)
        {
            int mark = Keys.Begin();
            return Keys.Mark(SymbolPart.TypeParameter).Int(index).End(mark);
        }

        public SymbolKey GetModifiedType(SymbolKey modifier, SymbolKey unmodifiedType, bool isRequired)
        {
            int mark = Keys.Begin();
            return Keys.Mark(isRequired ? SymbolPart.RequiredModifier : SymbolPart.OptionalModifier)
                .Key(modifier).Key(unmodifiedType).End(mark);
        }

        public SymbolKey GetPinnedType(SymbolKey elementType) => Wrap(SymbolPart.Pinned, elementType);

        public SymbolKey GetPointerType(SymbolKey elementType) => Wrap(SymbolPart.Pointer, elementType);

        public SymbolKey GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            ref SymbolKey key = ref _primitives[(byte)typeCode];
            if (key.IsDefault)
            {
                int mark = Keys.Begin();
                key = Keys.Mark(SymbolPart.Primitive).Int((byte)typeCode).End(mark);
            }
            return key;
        }

        public SymbolKey GetSZArrayType(SymbolKey elementType) => Wrap(SymbolPart.SZArray, elementType);

        public SymbolKey GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => side.ReferenceKey(handle);

        public SymbolKey GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => side.Key(handle);

        public SymbolKey GetTypeFromSpecification(
            MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => side.Key(handle);

        SymbolKey Wrap(SymbolPart part, SymbolKey elementType)
        {
            int mark = Keys.Begin();
            return Keys.Mark(part).Key(elementType).End(mark);
        }
    }
}

/// <summary>
/// One Fast Diff over an image pair, advanced in bounded steps so a
/// single-threaded host can return to its event loop between them.
/// </summary>
/// <remarks>
/// Each step takes readers over the same two images and stops at the first
/// Type row or compared Type after its budget elapses, having made progress
/// on at least one. The retained state holds handles, keys, and strings, never a
/// reader, so a host can re-enter its image callbacks for every step.
/// Cancellation is observed at the same boundaries and leaves the comparison
/// resumable; any other exception leaves it unusable.
/// </remarks>
public sealed class FastDiffComparison(FastDiffAxes axes = FastDiffAxes.ApiAndBody)
{
    readonly FastDiff.UnitTable _unitsA = new();
    readonly FastDiff.UnitTable _unitsB = new();
    readonly ImmutableArray<FastDiffTypeState>.Builder _states =
        ImmutableArray.CreateBuilder<FastDiffTypeState>();
    readonly FastDiff.Work _work = new();
    FastDiff.Side? _a;
    FastDiff.Side? _b;
    bool _readA;
    bool _readB;
    List<FastDiff.Unit>? _onlyB;
    int _next;

    public FastDiffAxes Axes { get; } = axes;

    /// <summary>The complete result, once a step has finished the comparison.</summary>
    public FastDiffResult? Result { get; private set; }

    /// <summary>
    /// Advances the comparison for about <paramref name="budget"/>, or to
    /// completion for <see cref="Timeout.InfiniteTimeSpan"/>. Returns true when
    /// <see cref="Result"/> is available.
    /// </summary>
    public bool Step(
        PEReader before,
        PEReader after,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (Result is not null)
            return true;

        if (_a is null || _b is null)
        {
            _a = new FastDiff.Side(before);
            _b = new FastDiff.Side(after);
        }
        else
        {
            _a.Bind(before);
            _b.Bind(after);
        }

        long started = Stopwatch.GetTimestamp();
        bool progressed = false;
        bool OverBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!progressed)
            {
                progressed = true;
                return false;
            }
            return budget != Timeout.InfiniteTimeSpan
                && Stopwatch.GetElapsedTime(started) >= budget;
        }

        if (!_readA && !(_readA = _a.ReadUnits(_unitsA, OverBudget)))
            return false;
        if (!_readB && !(_readB = _b.ReadUnits(_unitsB, OverBudget)))
            return false;

        OrderedDictionary<SymbolKey, FastDiff.Unit> unitsA = _unitsA.Units;
        OrderedDictionary<SymbolKey, FastDiff.Unit> unitsB = _unitsB.Units;
        _onlyB ??= [.. unitsB.Where(pair => !unitsA.ContainsKey(pair.Key)).Select(pair => pair.Value)];
        int total = unitsA.Count + _onlyB.Count;
        for (; _next < total; _next++)
        {
            if (OverBudget())
                return false;
            if (_next < unitsA.Count)
            {
                (SymbolKey key, FastDiff.Unit unitA) = unitsA.GetAt(_next);
                _states.Add(unitsB.TryGetValue(key, out FastDiff.Unit? unitB)
                    ? FastDiff.CompareUnit(_a, unitA, _b, unitB, Axes, _work)
                    : FastDiff.OneSided(_a, unitA, Axes));
            }
            else
            {
                _states.Add(FastDiff.OneSided(_b, _onlyB[_next - unitsA.Count], Axes));
            }
        }

        _states.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
        Result = new FastDiffResult(
            _states.ToImmutable(),
            new FastDiffReceipt(_states.Count, _work.Bodies, _work.IlBytes));
        return true;
    }
}
