using System.Buffers;
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
            foreach ((string key, MethodDefinitionHandle methodA) in methodsA)
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

    static string? FirstDifference(List<string> before, List<string> after)
    {
        var onlyBefore = before.Except(after, StringComparer.Ordinal).FirstOrDefault();
        var onlyAfter = after.Except(before, StringComparer.Ordinal).FirstOrDefault();
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
            api = a.ApiCensus(unitA).SequenceEqual(b.ApiCensus(unitB), StringComparer.Ordinal)
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
        }
        return new FastDiffTypeState(unitA.FullName, api, body, unitA.Identifier);
    }

    static bool BodiesEqual(Side a, Unit unitA, Side b, Unit unitB, Work work)
    {
        // A Type visible on either side partitions its facts as visible on
        // both, so a visibility change is an API fact only.
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        if (!a.BodyCensus(unitA, ownerVisible, out Dictionary<string, MethodDefinitionHandle> methodsA)
                .SequenceEqual(
                    b.BodyCensus(unitB, ownerVisible, out Dictionary<string, MethodDefinitionHandle> methodsB),
                    StringComparer.Ordinal))
        {
            return false;
        }

        // Equal censuses declare the same non-public members. A public method
        // on one side only is an API fact; its body is not compared.
        foreach ((string key, MethodDefinitionHandle methodA) in methodsA)
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

    static readonly SearchValues<char> KeySeparators = SearchValues.Create("\\./:`#(),<>[]&*! ");

    internal static string EscapeKeyName(string name)
    {
        if (name.AsSpan().IndexOfAny(KeySeparators) < 0)
            return name;
        var escaped = new System.Text.StringBuilder(name.Length + 8);
        foreach (char c in name)
        {
            if (KeySeparators.Contains(c))
                escaped.Append('\\');
            escaped.Append(c);
        }
        return escaped.ToString();
    }

    /// <summary>
    /// A signature's parameter list, with the vararg sentinel spelled as the
    /// reserved <c>#...</c> where the required parameters end.
    /// </summary>
    internal static string Parameters(MethodSignature<string> signature)
    {
        ImmutableArray<string> types = signature.ParameterTypes;
        int required = signature.RequiredParameterCount;
        return required >= types.Length
            ? string.Join(",", types)
            : string.Join(",", [.. types.Take(required), "#...", .. types.Skip(required)]);
    }

    static bool IsMalformed(Exception ex)
        => ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or ArgumentException
            or IndexOutOfRangeException;

    /// <summary>The Type units of one side, read row by row.</summary>
    internal sealed class UnitTable
    {
        public OrderedDictionary<string, Unit> Units { get; } = new(StringComparer.Ordinal);

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
        readonly Dictionary<int, string> _keys = [];
        readonly Dictionary<int, string> _referenceKeys = [];
        readonly Dictionary<int, string> _displayNames = [];
        readonly HashSet<string> _duplicateTypeKeys = new(StringComparer.Ordinal);
        readonly SignatureKeys _signatures;
        readonly Dictionary<TypeDefinitionHandle, HashSet<MethodDefinitionHandle>> _explicitImplementations = [];

        public Side(PEReader pe)
        {
            Pe = pe;
            Md = MetadataFormatAdmission.GetMetadataReader(pe);
            _mvid = Md.GetGuid(Md.GetModuleDefinition().Mvid);
            _signatures = new SignatureKeys(this);
        }

        /// <summary>
        /// Rebinds this side to a reader over the same image. Every retained
        /// fact is a handle or a string, so it stays valid for any reader of
        /// that image.
        /// </summary>
        public void Bind(PEReader pe)
        {
            if (ReferenceEquals(pe, Pe))
                return;
            MetadataReader md = MetadataFormatAdmission.GetMetadataReader(pe);
            if (md.GetGuid(md.GetModuleDefinition().Mvid) != _mvid)
                throw new InvalidOperationException("A Fast Diff comparison continues over the same images.");
            Pe = pe;
            Md = md;
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

        public OrderedDictionary<string, Unit> Units()
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
            OrderedDictionary<string, Unit> units = table.Units;
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
                        string key = TypeKey(handle);
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
                }
            }
            generated.Clear();
            return true;
        }

        void AddMalformed(OrderedDictionary<string, Unit> units, TypeDefinitionHandle handle)
        {
            string row = $"!{MetadataTokens.GetToken(handle):X8}";
            units[row] = new Unit(SafeName(handle), row, handle, [], Malformed: true);
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
        /// present carry this prefix and belong to the Body axis. Custom
        /// attributes stay API facts: compiler-emitted ones such as nullable
        /// annotations shape the rendered signatures the API diff compares.
        /// </summary>
        const string NonApiFact = "~";

        public List<string> ApiCensus(Unit unit)
        {
            var census = new List<string>();
            bool visible = IsVisible(unit.Owner);
            census.Add(visible ? "visible" : "hidden");
            if (!visible)
                return census;
            census.AddRange(VisibleFacts(unit).Where(fact => !fact.StartsWith(NonApiFact, StringComparison.Ordinal)));
            census.Sort(StringComparer.Ordinal);
            return census;
        }

        List<string> VisibleFacts(Unit unit)
        {
            var facts = new List<string>();
            TypeFacts(facts, unit.Owner);
            MemberFacts(facts, unit.Owner, api: true, out _);
            return facts;
        }

        public List<string> BodyCensus(
            Unit unit,
            bool visible,
            out Dictionary<string, MethodDefinitionHandle> methods)
        {
            var census = new List<string>();
            methods = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
            if (!visible)
                TypeFacts(census, unit.Owner);
            else
                census.AddRange(VisibleFacts(unit).Where(fact => fact.StartsWith(NonApiFact, StringComparison.Ordinal)));
            MemberFacts(census, unit.Owner, api: !visible ? null : false, out var ownerMethods);
            foreach ((string key, MethodDefinitionHandle method) in ownerMethods)
                AddMethod(methods, key, method);
            foreach (TypeDefinitionHandle generated in unit.Generated)
            {
                TypeFacts(census, generated);
                MemberFacts(census, generated, api: null, out var generatedMethods);
                foreach ((string key, MethodDefinitionHandle method) in generatedMethods)
                    AddMethod(methods, key, method);
            }
            census.Sort(StringComparer.Ordinal);
            return census;
        }

        // Two method rows that spell one key, which valid metadata does not
        // allow, cannot be told apart, so the body is not decided.
        static void AddMethod(
            Dictionary<string, MethodDefinitionHandle> methods,
            string key,
            MethodDefinitionHandle method)
        {
            if (!methods.TryAdd(key, method))
                throw new BadImageFormatException($"Two methods share the compared key {key}.");
        }

        void TypeFacts(List<string> census, TypeDefinitionHandle handle)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            string name = TypeKey(handle);
            census.Add(
                $"T {name} {(int)(type.Attributes & ~TypeAttributes.BeforeFieldInit)} "
                    + (type.BaseType.IsNil ? "" : Key(type.BaseType)));
            // beforefieldinit only changes when a static constructor runs.
            if ((type.Attributes & TypeAttributes.BeforeFieldInit) != 0
                && type.GetMethods().Any(method =>
                    Md.StringComparer.Equals(Md.GetMethodDefinition(method).Name, ".cctor")))
            {
                census.Add($"{NonApiFact}beforefieldinit {name}");
            }
            foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
            {
                InterfaceImplementation value = Md.GetInterfaceImplementation(implementation);
                census.Add($"I {name} {Key(value.Interface)}");
                Attributes(census, value.GetCustomAttributes(), $"I {name} {Key(value.Interface)}");
            }
            GenericParameters(census, type.GetGenericParameters(), name);
            Attributes(census, type.GetCustomAttributes(), name);
            // A nested Type inherits its declaring Type's nullable context
            // without an attribute row of its own, and that context shapes
            // the rendered signatures of every member that does not override it.
            census.Add($"N? {name} {NullabilityReader.GetTypeNullableContext(Md, handle)}");
            TypeLayout layout = type.GetLayout();
            if (!layout.IsDefault)
                census.Add($"L {name} {layout.PackingSize} {layout.Size}");
            foreach (MethodImplementationHandle implementation in type.GetMethodImplementations())
            {
                MethodImplementation value = Md.GetMethodImplementation(implementation);
                census.Add($"X {name} {Key(value.MethodBody)} {Key(value.MethodDeclaration)}");
            }
        }

        /// <param name="api">
        /// True for the public members only, false for the non-public members,
        /// and null for every member.
        /// </param>
        void MemberFacts(
            List<string> census,
            TypeDefinitionHandle handle,
            bool? api,
            out List<(string Key, MethodDefinitionHandle Method)> methods)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            string name = TypeKey(handle);
            methods = [];
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = Md.GetFieldDefinition(fieldHandle);
                if (api is { } wanted && IsVisible(field) != wanted)
                    continue;
                string key = Key(fieldHandle);
                census.Add($"F {key} {(int)field.Attributes} {Constant(field.GetDefaultValue())} {field.GetOffset()}");
                Attributes(census, field.GetCustomAttributes(), key);
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = Md.GetMethodDefinition(methodHandle);
                string key = Key(methodHandle);
                methods.Add((key, methodHandle));
                if (api is { } wanted && IsVisible(methodHandle, method) != wanted)
                    continue;
                census.Add($"M {key} {(int)method.Attributes} {(int)method.ImplAttributes}");
                GenericParameters(census, method.GetGenericParameters(), key);
                Attributes(census, method.GetCustomAttributes(), key);
                foreach (ParameterHandle parameterHandle in method.GetParameters())
                {
                    Parameter parameter = Md.GetParameter(parameterHandle);
                    string parameterKey = $"{key}#{parameter.SequenceNumber}";
                    census.Add(
                        $"A {parameterKey} {KeyName(parameter.Name)} {(int)parameter.Attributes} "
                            + Constant(parameter.GetDefaultValue()));
                    Attributes(census, parameter.GetCustomAttributes(), parameterKey);
                }
                MethodImport import = method.GetImport();
                if (!import.Module.IsNil)
                {
                    census.Add(
                        $"N {key} {KeyName(import.Name)} "
                            + $"{KeyName(Md.GetModuleReference(import.Module).Name)} {(int)import.Attributes}");
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
                string key =
                    $"{name}::{KeyName(property.Name)}:{PropertySignature(property)}";
                census.Add(
                    $"P {key} {(int)property.Attributes} {Constant(property.GetDefaultValue())} "
                        + $"{AccessorKey(accessors.Getter)} {AccessorKey(accessors.Setter)}");
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
                // An unescaped '!' cannot occur in an escaped name, so an event never
                // spells the key of its backing field.
                string key = $"{name}::{KeyName(value.Name)}!event:{Key(value.Type)}";
                census.Add(
                    $"E {key} {(int)value.Attributes} {AccessorKey(accessors.Adder)} "
                        + $"{AccessorKey(accessors.Remover)} {AccessorKey(accessors.Raiser)}");
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

        string AccessorKey(MethodDefinitionHandle accessor)
            => accessor.IsNil ? "-" : Key(accessor);

        void GenericParameters(List<string> census, GenericParameterHandleCollection parameters, string owner)
        {
            foreach (GenericParameterHandle handle in parameters)
            {
                GenericParameter parameter = Md.GetGenericParameter(handle);
                string key = $"{owner}<{parameter.Index}";
                census.Add($"G {key} {KeyName(parameter.Name)} {(int)parameter.Attributes}");
                foreach (GenericParameterConstraintHandle constraintHandle in parameter.GetConstraints())
                {
                    GenericParameterConstraint constraint = Md.GetGenericParameterConstraint(constraintHandle);
                    census.Add($"C {key} {Key(constraint.Type)}");
                    Attributes(census, constraint.GetCustomAttributes(), $"C {key} {Key(constraint.Type)}");
                }
                Attributes(census, parameter.GetCustomAttributes(), key);
            }
        }

        void Attributes(List<string> census, CustomAttributeHandleCollection attributes, string owner)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = Md.GetCustomAttribute(handle);
                census.Add(
                    $"@ {owner} {Key(attribute.Constructor)} "
                        + Convert.ToHexString(Md.GetBlobBytes(attribute.Value)));
            }
        }

        string Constant(ConstantHandle handle)
        {
            if (handle.IsNil)
                return "-";
            Constant constant = Md.GetConstant(handle);
            return $"{(int)constant.TypeCode}:{Convert.ToHexString(Md.GetBlobBytes(constant.Value))}";
        }

        string PropertySignature(PropertyDefinition property)
        {
            Guard(property.Signature, SignatureBlobGuard.Kind.Property);
            MethodSignature<string> signature = property.DecodeSignature(_signatures, null);
            return $"{signature.Header.IsInstance}({string.Join(",", signature.ParameterTypes)})[{signature.ReturnType}]";
        }

        /// <summary>
        /// A metadata name as it appears in a compared key, with every
        /// character the key grammar uses as a separator escaped, so distinct
        /// names never spell the same key.
        /// </summary>
        string KeyName(StringHandle handle) => EscapeKeyName(Md.GetString(handle));

        /// <summary>The symbolic name of a Type, with <c>/</c> between nested names.</summary>
        public string TypeKey(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (!_keys.TryGetValue(token, out string? key))
                _keys[token] = key = DefinitionName(handle, '/', KeyName);
            return key;
        }

        string DisplayName(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (!_displayNames.TryGetValue(token, out string? name))
                _displayNames[token] = name = DefinitionName(handle, '.', Md.GetString);
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

        string DefinitionName(TypeDefinitionHandle handle, char nestedSeparator, Func<StringHandle, string> read)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!TryDeclaringChain(handle, chain, out int length))
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            TypeDefinition root = Md.GetTypeDefinition(chain[0]);
            var name = new System.Text.StringBuilder();
            if (!root.Namespace.IsNil)
                name.Append(read(root.Namespace)).Append('.');
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                    name.Append(nestedSeparator);
                name.Append(read(Md.GetTypeDefinition(chain[i]).Name));
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
        /// <remarks>
        /// The duplicate Type keys are complete before any reference is read,
        /// so a token's decision is made once and a repeat costs one lookup.
        /// </remarks>
        public string ReferenceKey(EntityHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_referenceKeys.TryGetValue(token, out string? key))
                return key;
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
            key = Key(handle);
            _referenceKeys[token] = key;
            return key;
        }

        void RejectDuplicate(TypeDefinitionHandle handle)
        {
            if (_duplicateTypeKeys.Count > 0 && _duplicateTypeKeys.Contains(TypeKey(handle)))
                throw new BadImageFormatException("A reference names one of two Type rows that share a key.");
        }

        /// <summary>The symbolic name of a metadata entity, resolved once per side.</summary>
        public string Key(EntityHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_keys.TryGetValue(token, out string? key))
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

        string TypeSpecificationKey(TypeSpecificationHandle handle)
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

        string TypeReferenceKey(TypeReferenceHandle handle)
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
            TypeReference root = Md.GetTypeReference(chain[0]);
            var name = new System.Text.StringBuilder();
            if (!root.Namespace.IsNil)
                name.Append(KeyName(root.Namespace)).Append('.');
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                    name.Append('/');
                name.Append(KeyName(Md.GetTypeReference(chain[i]).Name));
            }
            return name.ToString();
        }

        string MethodKey(MethodDefinitionHandle handle)
        {
            MethodDefinition method = Md.GetMethodDefinition(handle);
            Guard(method.Signature, SignatureBlobGuard.Kind.Method);
            MethodSignature<string> signature = method.DecodeSignature(_signatures, null);
            return $"{TypeKey(method.GetDeclaringType())}::{KeyName(method.Name)}"
                + $"`{signature.GenericParameterCount}#{signature.Header.RawValue}"
                + $"({Parameters(signature)})[{signature.ReturnType}]";
        }

        string FieldKey(FieldDefinitionHandle handle)
        {
            FieldDefinition field = Md.GetFieldDefinition(handle);
            Guard(field.Signature, SignatureBlobGuard.Kind.Field);
            return $"{TypeKey(field.GetDeclaringType())}::{KeyName(field.Name)}:"
                + field.DecodeSignature(_signatures, null);
        }

        string MemberReferenceKey(MemberReferenceHandle handle)
        {
            MemberReference member = Md.GetMemberReference(handle);
            string parent = ReferenceKey(member.Parent);
            if (member.GetKind() == MemberReferenceKind.Field)
            {
                Guard(member.Signature, SignatureBlobGuard.Kind.Field);
                return $"{parent}::{KeyName(member.Name)}:{member.DecodeFieldSignature(_signatures, null)}";
            }
            Guard(member.Signature, SignatureBlobGuard.Kind.Method);
            MethodSignature<string> signature = member.DecodeMethodSignature(_signatures, null);
            return $"{parent}::{KeyName(member.Name)}`{signature.GenericParameterCount}#{signature.Header.RawValue}"
                + $"({Parameters(signature)})[{signature.ReturnType}]";
        }

        string MethodSpecificationKey(MethodSpecificationHandle handle)
        {
            MethodSpecification specification = Md.GetMethodSpecification(handle);
            Guard(specification.Signature, SignatureBlobGuard.Kind.MethodSpecification);
            return ReferenceKey(specification.Method)
                + "<" + string.Join(",", specification.DecodeSignature(_signatures, null)) + ">";
        }

        string StandaloneKey(StandaloneSignatureHandle handle)
        {
            StandaloneSignature signature = Md.GetStandaloneSignature(handle);
            if (signature.GetKind() == StandaloneSignatureKind.LocalVariables)
            {
                Guard(signature.Signature, SignatureBlobGuard.Kind.LocalVariables);
                return "L(" + string.Join(",", signature.DecodeLocalSignature(_signatures, null)) + ")";
            }
            Guard(signature.Signature, SignatureBlobGuard.Kind.StandaloneMethod);
            MethodSignature<string> method = signature.DecodeMethodSignature(_signatures, null);
            return $"S{method.Header.RawValue}({Parameters(method)})[{method.ReturnType}]";
        }
    }

    sealed class SignatureKeys(Side side) : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape)
            => $"{elementType}[{shape.Rank}:{string.Join(",", shape.Sizes)}:{string.Join(",", shape.LowerBounds)}]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature)
            => $"#fnptr{signature.Header.RawValue}({Parameters(signature)})[{signature.ReturnType}]";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
        public string GetPinnedType(string elementType) => elementType + " pinned";
        public string GetPointerType(string elementType) => elementType + "*";
        // A leading '#' is escaped in every name, so no Type spells a primitive.
        static readonly string[] PrimitiveKeys = CreatePrimitiveKeys();

        static string[] CreatePrimitiveKeys()
        {
            var keys = new string[256];
            foreach (PrimitiveTypeCode code in Enum.GetValues<PrimitiveTypeCode>())
                keys[(byte)code] = "#" + code;
            return keys;
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            => PrimitiveKeys[(byte)typeCode] ?? "#" + typeCode;
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => side.ReferenceKey(handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => side.Key(handle);
        public string GetTypeFromSpecification(
            MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => side.Key(handle);
    }
}

/// <summary>
/// One Fast Diff over an image pair, advanced in bounded steps so a
/// single-threaded host can return to its event loop between them.
/// </summary>
/// <remarks>
/// Each step takes readers over the same two images and stops at the first
/// Type row or compared Type after its budget elapses, having made progress
/// on at least one. The retained state holds handles and strings, never a
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

        OrderedDictionary<string, FastDiff.Unit> unitsA = _unitsA.Units;
        OrderedDictionary<string, FastDiff.Unit> unitsB = _unitsB.Units;
        _onlyB ??= [.. unitsB.Where(pair => !unitsA.ContainsKey(pair.Key)).Select(pair => pair.Value)];
        int total = unitsA.Count + _onlyB.Count;
        for (; _next < total; _next++)
        {
            if (OverBudget())
                return false;
            if (_next < unitsA.Count)
            {
                (string key, FastDiff.Unit unitA) = unitsA.GetAt(_next);
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
