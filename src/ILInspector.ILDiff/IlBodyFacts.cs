using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Hashing;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.ILDiff;

// SPIKE (exp/unified-diff-exists, #10227 Q2a): a one-version Body fact per method, under
// Fast Diff's Body equality (canonical IL comparison's Exact with the Body policy), as
// the Annotated Type Map's shape facts need: a function of one image only, so two
// retained tables compare without reopening either image.
//
// Representation, per method with a body:
//   Stream       canonical instructions as varints: the opcode family (ldc.i4 forms and
//                short branches folded, as OpcodeFamily folds them), then one operand:
//                an immediate or slot value (as Immediate spells it), a token operand as
//                a side-local SymbolKey id into this side's SymbolKeyTable, or branch and
//                switch targets as instruction indices (identity alignment).
//   Fingerprint  XxHash128 of the same stream with each token id replaced by its key's
//                own XxHash128, so it is comparable across versions without the table.
// Two facts are Exact when their streams are equal after translating the old side's
// token ids through the key join of the two tables; unequal fingerprints prove Changed.
internal enum BodyFactOutcome : byte
{
    NoBody,
    Available,
    Unavailable,
}

internal readonly record struct BodyFact(BodyFactOutcome Outcome, byte[]? Stream, UInt128 Fingerprint, string? Failure)
{
    public static readonly BodyFact NoBody = new(BodyFactOutcome.NoBody, null, default, null);
}

internal sealed class IlBodyFactProducer
{
    const byte None = 0, Immediate = 1, Slot = 2, Token = 3, Targets = 4;

    readonly PEReader _pe;
    readonly IlBodyExistsComparer _decoder;
    readonly List<UInt128> _keyHashes = [];
    byte[] _stream = new byte[1024];
    byte[] _hashInput = new byte[1024];
    int _streamLength;
    int _hashLength;

    public IlBodyFactProducer(PEReader pe, IlBodyDiffNormalization normalization)
    {
        if ((normalization & IlBodyDiffNormalization.NormalizeVariableLayout) != 0)
            throw new NotSupportedException("SPIKE: the Body policy keeps variable layout.");
        _pe = pe;
        Reader = pe.GetMetadataReader();
        Symbols = new IlBodyDiff.SymbolicIdentity(pe, Reader, normalization, Table);
        _decoder = new IlBodyExistsComparer(Symbols, Symbols, normalization)
        {
            Options = IlExistsSpikeOptions.CheckedDecode | IlExistsSpikeOptions.BlockGraphTopology,
        };
    }

    public MetadataReader Reader { get; }

    public SymbolKeyTable Table { get; } = new();

    public IlBodyDiff.SymbolicIdentity Symbols { get; }

    public long StreamBytes { get; private set; }

    public int Bodies { get; private set; }

    public BodyFact Produce(MethodDefinitionHandle handle)
    {
        MethodDefinition method = Reader.GetMethodDefinition(handle);
        if (method.RelativeVirtualAddress == 0)
            return BodyFact.NoBody;
        MethodBodyBlock body;
        try
        {
            body = _pe.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch (BadImageFormatException ex)
        {
            return new BodyFact(BodyFactOutcome.Unavailable, null, default, "body read failed: " + ex.Message);
        }
        byte[] il = body.GetILBytes() ?? [];
        if (!_decoder.TryCheckedDecode(il, body, "body", out ImmutableArray<DecodedInstruction> instructions, out string? failure))
            return new BodyFact(BodyFactOutcome.Unavailable, null, default, failure);

        _streamLength = 0;
        _hashLength = 0;
        Varint((uint)instructions.Length);
        foreach (DecodedInstruction instruction in instructions)
        {
            Varint((uint)FamilyCode(instruction.OpCode));
            if (IlBodyDiff.SpikeIsTokenOperand(instruction.Operand))
            {
                if (!Symbols.TryOperand(instruction, out int id, out failure))
                    return new BodyFact(BodyFactOutcome.Unavailable, null, default, failure);
                Byte(Token);
                StreamVarint((uint)id);
                HashKey(id);
                continue;
            }
            switch (instruction.Operand)
            {
                case OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget or OperandKind.InlineSwitch:
                {
                    Byte(Targets);
                    ImmutableArray<int> targets = instruction.BranchTargets;
                    Varint((uint)targets.Length);
                    foreach (int target in targets)
                    {
                        int index = IlBodyExistsComparer.IndexAt(instructions, target);
                        // A target that is not an instruction start compares by raw offset and
                        // ends the target comparison (BranchTargetsMatch); completeness admits
                        // none, so this is kept only for fidelity.
                        if (index < 0)
                        {
                            Varint(0);
                            Varint((uint)target);
                            break;
                        }
                        Varint((uint)index + 1);
                    }
                    break;
                }
                default:
                {
                    int tag = IlBodyExistsComparer.Immediate(instruction, variableLayout: false, out long value);
                    if (tag == 0)
                    {
                        Byte(None);
                    }
                    else
                    {
                        Byte(tag == 1 ? Immediate : Slot);
                        Varint64((ulong)((value << 1) ^ (value >> 63)));
                    }
                    break;
                }
            }
        }
        byte[] stream = _stream.AsSpan(0, _streamLength).ToArray();
        UInt128 fingerprint = XxHash128.HashToUInt128(_hashInput.AsSpan(0, _hashLength));
        StreamBytes += stream.Length;
        Bodies++;
        return new BodyFact(BodyFactOutcome.Available, stream, fingerprint, null);
    }

    void HashKey(int id)
    {
        while (_keyHashes.Count <= id)
            _keyHashes.Add(XxHash128.HashToUInt128(Table[_keyHashes.Count].Bytes));
        EnsureHash(16);
        BinaryPrimitives.WriteUInt128LittleEndian(_hashInput.AsSpan(_hashLength), _keyHashes[id]);
        _hashLength += 16;
    }

    void Byte(byte value)
    {
        EnsureStream(1);
        _stream[_streamLength++] = value;
        EnsureHash(1);
        _hashInput[_hashLength++] = value;
    }

    void Varint(uint value) => Varint64(value);

    void Varint64(ulong value)
    {
        EnsureStream(10);
        EnsureHash(10);
        while (value >= 0x80)
        {
            _stream[_streamLength++] = (byte)(value | 0x80);
            _hashInput[_hashLength++] = (byte)(value | 0x80);
            value >>= 7;
        }
        _stream[_streamLength++] = (byte)value;
        _hashInput[_hashLength++] = (byte)value;
    }

    /// <summary>A varint written to the stream only (a side-local id the hash replaces).</summary>
    void StreamVarint(uint value)
    {
        EnsureStream(5);
        while (value >= 0x80)
        {
            _stream[_streamLength++] = (byte)(value | 0x80);
            value >>= 7;
        }
        _stream[_streamLength++] = (byte)value;
    }

    void EnsureStream(int count)
    {
        if (_stream.Length - _streamLength < count)
            Array.Resize(ref _stream, Math.Max(_stream.Length * 2, _streamLength + count));
    }

    void EnsureHash(int count)
    {
        if (_hashInput.Length - _hashLength < count)
            Array.Resize(ref _hashInput, Math.Max(_hashInput.Length * 2, _hashLength + count));
    }

    static readonly Dictionary<ILOpCode, ILOpCode> s_longForms = BuildLongForms();

    static Dictionary<ILOpCode, ILOpCode> BuildLongForms()
    {
        var forms = new Dictionary<ILOpCode, ILOpCode>();
        foreach (ILOpCode opcode in Enum.GetValues<ILOpCode>())
        {
            if (opcode.IsShortBranch())
                forms[opcode] = Enum.Parse<ILOpCode>(opcode.ToString()[..^"_s".Length]);
        }
        return forms;
    }

    /// <summary>
    /// A number per OpcodeFamily spelling under the Body policy: every ldc.i4 form is
    /// ldc.i4, a short branch is its long form, and every other opcode is itself.
    /// </summary>
    internal static int FamilyCode(ILOpCode opcode)
    {
        if (opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1
            or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4
            or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7
            or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4)
        {
            return (int)ILOpCode.Ldc_i4;
        }
        return s_longForms.TryGetValue(opcode, out ILOpCode longForm) ? (int)longForm : (int)opcode;
    }

    /// <summary>
    /// Old-side id to new-side id for every key of the old table, or -1 where the new
    /// table lacks the key: the keyed join of two retained symbol tables.
    /// </summary>
    public static int[] Translate(SymbolKeyTable before, SymbolKeyTable after)
    {
        int[] map = new int[before.Count];
        for (int i = 0; i < map.Length; i++)
            map[i] = after.TryGet(before[i], out int id) ? id : -1;
        return map;
    }

    /// <summary>
    /// Exact under the translated join: equal instruction counts, families, operand
    /// shapes and values, token keys, and target indices.
    /// </summary>
    public static bool StreamsEqual(byte[] before, byte[] after, int[] translate)
    {
        int i = 0, j = 0;
        ulong count = Read(before, ref i);
        if (count != Read(after, ref j))
            return false;
        for (ulong n = 0; n < count; n++)
        {
            if (Read(before, ref i) != Read(after, ref j))
                return false;
            byte tag = before[i++];
            if (tag != after[j++])
                return false;
            switch (tag)
            {
                case None:
                    break;
                case Immediate or Slot:
                    if (Read(before, ref i) != Read(after, ref j))
                        return false;
                    break;
                case Token:
                {
                    ulong id = Read(before, ref i);
                    int mapped = translate[(int)id];
                    if (mapped < 0 || (ulong)mapped != Read(after, ref j))
                        return false;
                    break;
                }
                case Targets:
                {
                    ulong targets = Read(before, ref i);
                    if (targets != Read(after, ref j))
                        return false;
                    for (ulong t = 0; t < targets; t++)
                    {
                        ulong x = Read(before, ref i);
                        ulong y = Read(after, ref j);
                        if (x == 0 || y == 0)
                        {
                            if (x != y || Read(before, ref i) != Read(after, ref j))
                                return false;
                            // BranchTargetsMatch stops at the first unresolved target, and
                            // the producer writes no target after it.
                            break;
                        }
                        if (x != y)
                            return false;
                    }
                    break;
                }
            }
        }
        return true;
    }

    static ulong Read(byte[] stream, ref int position)
    {
        ulong value = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = stream[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
    }
}
