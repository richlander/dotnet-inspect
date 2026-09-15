namespace ILInspector.Decompiler.Tests;

// Issues #1766 / #1772 / #1806: a cross-assembly (framework) enum resolves to
// TypeShape.Unknown, so an integer constant flowing into it through a
// conditional arm or a bitwise compound assignment renders as a bare int —
// invalid `int->enum` (CS0266) / `enum |= int` (CS0019) at Full. The printer
// must cast structurally.
public static class EnumCastSamples
{
    // #3011: an enum-typed value shifted (`>>`/`<<`) has no predefined C# shift
    // operator (CS0019), though the IL shifts the enum's underlying integer. The
    // identity `(long)`/`(uint)`/`(ulong)` cast the source carried leaves no IL
    // trace, so the importer sees a bare enum-typed shift left operand; the printer
    // must re-insert the underlying-integer cast. Witness: MySqlConnector's
    // HandshakeResponse41Payload.CreateCapabilitiesPayload — `(int)(clientCapabilities >> 32)`
    // on a long-backed [Flags] enum. Same-assembly enums with a known backing width.
    public static long LongEnumRightShift(CfgLongPriority flags, int n) => (long)flags >> n;

    public static long LongEnumLeftShift(CfgLongPriority flags, int n) => (long)flags << n;

    public static int LongEnumRightShiftToInt(CfgLongPriority flags) => (int)((long)flags >> 32);

    public static ulong ULongEnumRightShift(CfgULong flags, int n) => (ulong)flags >> n;

    public static uint UIntEnumRightShift(CfgFlags flags, int n) => (uint)flags >> n;

    public static int IntEnumRightShift(CfgPriority flags, int n) => (int)flags >> n;

    // #3011 (review): the source may reinterpret the enum to the opposite-signedness
    // same-width integer before shifting — an IL no-op — so the shift opcode
    // (shr vs shr.un), not the enum backing, records the real signedness. An
    // int-backed enum shifted as `(uint)e >> n` emits shr.un; a uint-backed enum
    // shifted as `(int)e >> n` emits shr. The printed cast must follow the opcode.
    public static uint IntEnumUnsignedRightShift(CfgPriority flags, int n) => (uint)flags >> n;

    public static int UIntEnumSignedRightShift(CfgFlags flags, int n) => (int)flags >> n;

    // #3011 (review): a compound shift on an enum lvalue (`flags <<= n`) is also
    // CS0019. An int-backed enum shifted and stored back to itself folds to a
    // compound with a bare enum left operand; the printer must decompose it to a
    // plain cast-back assignment. The unsigned variant confirms the decomposed
    // left-operand cast follows the shr.un opcode, not the enum backing.
    public static CfgPriority IntEnumCompoundLeftShift(CfgPriority flags, int n)
    {
        flags = (CfgPriority)((int)flags << n);
        return flags;
    }

    public static CfgPriority IntEnumCompoundUnsignedRightShift(CfgPriority flags, int n)
    {
        flags = (CfgPriority)((uint)flags >> n);
        return flags;
    }

    // #3011 (review): a typed `ldelem.i4`/`ldind.i4` over an enum array or by-ref
    // loads the enum's primitive storage width, so the shift operand's stack type
    // is the primitive even though the rendered expression (`values[i]`, `e`) is
    // enum-typed and rejects a bare shift (CS0019). The printer must recover the
    // enum from the array element / pointee type and force the reinterpret cast.
    public static int IntEnumArrayRightShift(CfgPriority[] values, int i, int n) => (int)values[i] >> n;

    public static int IntEnumArrayLeftShift(CfgPriority[] values, int i, int n) => (int)values[i] << n;

    public static long LongEnumArrayRightShift(CfgLongPriority[] values, int i, int n) => (long)values[i] >> n;

    public static uint IntEnumArrayUnsignedRightShift(CfgPriority[] values, int i, int n) => (uint)values[i] >> n;

    public static int RefIntEnumLeftShift(ref CfgPriority e, int n) => (int)e << n;

    // #3066 (follow-up to #3011/#3060): a shift on an enum defined in a REFERENCED
    // assembly is CS0019 too, but EnsureTypeMaps (this assembly's type defs only)
    // leaves it Unknown-shaped, so EnumUnderlyingType has no backing width. A bare
    // enum load carries no storage-width hint, so the width is recovered from the
    // compiler-baked shift-count mask (& 31 => 4-byte, & 63 => 8-byte) and the
    // signedness from the opcode. ExternalLong/ExternalULong are 8-byte (mask 63),
    // ExternalUInt is 4-byte (mask 31); the variable count `n` carries the mask.
    public static long ExternalLongRightShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n) => (long)e >> n;

    public static long ExternalLongLeftShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n) => (long)e << n;

    public static ulong ExternalULongRightShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalULong e, int n) => (ulong)e >> n;

    public static uint ExternalUIntRightShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalUInt e, int n) => (uint)e >> n;

    // Opcode-wins mirror across the assembly boundary: a signed shr on a uint-backed
    // referenced enum must reinterpret to `(int)`, not the backing's `(uint)`.
    public static int ExternalUIntSignedRightShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalUInt e, int n) => (int)e >> n;

    // The unsigned-opcode mirror on an 8-byte signed backing: shr.un must reinterpret
    // to `(ulong)`, recovered as 8-byte from the mask, not the backing's `(long)`.
    public static ulong ExternalLongUnsignedRightShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n) => (ulong)e >> n;

    // The compound sibling: a compound shift on a referenced-enum lvalue decomposes
    // to a plain cast-back assignment `e = (ExternalLong)((long)e << (n & 63))`.
    public static ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong ExternalLongCompoundLeftShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n)
    {
        e = (ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong)((long)e << n);
        return e;
    }

    // #3066 soundness: an inner USER mask does not fool width recovery. Roslyn
    // always emits the implicit width mask (& 63 here) as the OUTERMOST mask
    // feeding shr, with any user mask nested inside; so the outer & 63 names the
    // 8-byte backing (stripped), and the user's `& 31` is preserved untouched.
    public static long ExternalLongRightShiftInnerUserMask(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n) => (long)e >> (n & 31);

    // #3066: the ref/array siblings of the direct cross-assembly enum shift. A
    // typed ldelem/ldind masks the referenced enum as its primitive backing width in
    // the operand ResultType, so recognition must consult the array element / by-ref
    // pointee type (IsEnumLikeShiftOperand) — otherwise these render as a bare, uncast
    // `e << n` / `a[i] << n` (CS0019) with the count mask stripped. Both the plain
    // expression and the compound `<<=` decomposition are covered.
    public static long ExternalLongRefLeftShift(ref ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n) => (long)e << n;
    public static long ExternalLongArrayLeftShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong[] a, int i, int n) => (long)a[i] << n;
    public static int ExternalUIntArrayRightShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalUInt[] a, int i, int n) => (int)a[i] >> n;
    public static ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong ExternalLongArrayCompoundLeftShift(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong[] a, int i, int n)
    {
        a[i] = (ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong)((long)a[i] << n);
        return a[i];
    }

    // #3066 x #3011 merge interaction: a cross-assembly enum shift feeding a
    // mixed-sign bitwise parent. The signed `shr` on the referenced 8-byte enum
    // renders as `(long)e >> (n & 63)`; the `ulong` sibling makes the `|` mixed-sign,
    // so the shift must reinterpret to the sibling's width — `(ulong)((long)e >> …)` —
    // for the op to bind (CS0019 otherwise). The rendered-integer WIDTH is recovered
    // from the count mask (ShiftRenderedIntegerType), the same unresolved-backing
    // path as the bare operand; without it the parent reconciliation declines.
    public static ulong ExternalLongSignedShiftOrUnsigned(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n, ulong x) => (ulong)((long)e >> n) | x;

    // The int/uint-backed mirror across the assembly boundary: a signed `shr` on a
    // referenced 4-byte enum reconciled against a `uint` sibling reinterprets to
    // `(uint)`, width recovered from the `& 31` count mask.
    public static uint ExternalUIntSignedShiftOrUnsigned(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalUInt e, int n, uint x) => (uint)((int)e >> n) | x;

    // The int->enum sink mirror: a cross-assembly enum shift RETURNED to the
    // referenced enum renders as its underlying integer, so the sink needs an outer
    // `(ExternalLong)` cast (CS0266). The rendered integer type — recovered from the
    // count mask — drives the enum-spellability test that wraps the shift.
    public static ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong ExternalLongShiftReturn(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong e, int n) => (ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalLong)((long)e >> n);

    // #3011 (review): an enum shift feeding a parent bitwise &/|/^ kept the shift
    // node's enum ResultType, so the printer coerced the *sibling* integer to the
    // enum (`(int)e << n | (E)x`, CS0019). The shift renders as its underlying
    // integer, so the bitwise op is an integer op; a same-width mixed-sign sibling
    // reconciles to one width rather than promoting to the wider signed common type
    // (which changes the value) or failing to bind. Witness: Roslyn
    // MetadataWriter.GetRawToken — `((uint)encoding << 24) | pseudoToken`.
    public static uint IntEnumShiftOrUnsigned(CfgPriority e, uint x) => ((uint)e << 24) | x;

    public static int IntEnumShiftOrSigned(CfgPriority e, int x) => ((int)e << 8) | x;

    public static ulong LongEnumShiftOrUnsigned(CfgLongPriority e, ulong x) => ((ulong)e << 8) | x;

    public static uint IntEnumShiftAndUnsigned(CfgPriority e, uint x) => ((uint)e << 4) & x;

    // Negative case for the #3076 left-shift cast collapse: a signed arithmetic
    // right shift reconciled against an unsigned sibling must KEEP its double cast
    // `(ulong)((long)e >> n)`. Collapsing to `(ulong)e >> n` would switch the
    // arithmetic shift to a logical one and change the value, so the collapse is
    // left-shift only.
    public static ulong LongEnumShiftRightOrUnsigned(CfgLongPriority e, int n, ulong x) => ((ulong)((long)e >> n)) | x;

    // Precedence guard for the #3076 collapse: an enum left shift reconciled inside
    // a mixed-sign ARITHMETIC parent (`+`/`-`/`*`, which bind tighter than `<<`)
    // must keep parentheses around the collapsed shift — `((uint)values[i] << n) + x`,
    // not `(uint)values[i] << n + x` (which parses as `(uint)values[i] << (n + x)`).
    // An enum ARRAY element masks the enum as its primitive width, so the shift's
    // EffectiveType is an integer and MixedSignArithmetic reconciles it (a plain
    // enum field stays enum-typed and never routes here).
    public static uint EnumArrayShiftAddUnsigned(CfgPriority[] values, int i, int n, uint x) => ((uint)values[i] << n) + x;

    // A bitwise CHAIN over an enum shift: the inner `|` inherits the shift's stale
    // enum ResultType while rendering as an integer, so the outer `|` must not
    // coerce the far sibling to the enum (`... | (E)y`, CS0019). The rewritten-
    // integer detection and rendered-type reconciliation recurse through the chain.
    public static uint ChainIntEnumShiftOrUnsigned(CfgPriority e, uint x, uint y) => ((uint)e << 24) | x | y;

    public static ulong ChainLongEnumShiftOrUnsigned(CfgLongPriority e, ulong x, ulong y) => ((ulong)e << 8) | x | y;

    // An enum shift RETURNED or STORED to an enum target: the shift renders as its
    // underlying integer, so the sink is an int->enum conversion needing an outer
    // `(E)` cast (CS0266). The shift's stale enum ResultType would otherwise read
    // as an identity to the target and the cast would be dropped.
    public static CfgPriority EnumShiftReturn(CfgPriority e, int n) => (CfgPriority)((int)e >> n);

    public static CfgLongPriority EnumShiftReturnLong(CfgLongPriority e, int n) => (CfgLongPriority)((long)e >> n);

    public static void EnumShiftStoreArray(CfgPriority[] arr, int n) => arr[0] = (CfgPriority)((int)arr[0] >> n);

    // #3087 defect 1: a bitwise op whose sibling is itself an ENUM value. The shift
    // renders as its underlying integer, so the enum sibling must be coerced to that
    // integer — `(int)e << n | (int)other`, not `(int)e << n | other` (int | E,
    // CS0019). The stale enum ResultType makes the sibling render bare.
    public static CfgPriority EnumShiftOrEnumSibling(CfgPriority e, int n, CfgPriority other) => (CfgPriority)(((int)e << n) | (int)other);

    // #3087 defect 2: an enum shift COMPARED to an enum-constant sibling. The shift
    // renders as int, so the comparison must be integer-vs-integer — the stale enum
    // ResultType instead coerces the sibling to the enum (`(int)e << n == E.High`,
    // int == E, CS0019).
    public static bool EnumShiftCompareEnumConst(CfgPriority e, int n) => ((int)e << n) == (int)CfgPriority.High;

    // #3087 defect 3: an enum shift as a ternary arm flowing into an ENUM target.
    // The shift's ResultType matches the target enum, so the `(CfgPriority)` cast is
    // dropped and the arm renders `(int)e << n` (int) while the other arm is the
    // enum — no common type (CS1503/CS0173). The cast must be kept. A method-argument
    // ternary stays a genuine conditional (it cannot be raised to if/return).
    public static void TakeCfgPriority(CfgPriority value) { }

    public static void EnumShiftTernaryToEnum(bool b, CfgPriority e, int n) => TakeCfgPriority(b ? (CfgPriority)((int)e << n) : CfgPriority.High);

    // #3087 negatives/edges. Mixed-sign: an UNSIGNED enum shift (`shr.un`) rendering
    // as uint whose enum sibling must be coerced to that width/sign — `(uint)other`,
    // not `(int)other` (which would be a mixed-sign bitwise op, CS0019).
    public static uint EnumShiftUnsignedOrEnumSibling(CfgPriority e, int n, CfgPriority other) => ((uint)e >> n) | (uint)other;

    // A comparison whose enum sibling is a RUNTIME value (not a constant): coerced
    // down to the shift's rendered integer — `(int)e << n == (int)other`.
    public static bool EnumShiftCompareEnumValue(CfgPriority e, int n, CfgPriority other) => ((int)e << n) == (int)other;

    // A bitwise CHAIN over an enum shift with an enum FAR sibling: the down-coercion
    // recurses so the enum sibling at the end of `shift | y | other` renders as its
    // underlying integer — `(int)e << 4 | y | (int)other`.
    public static int EnumShiftChainEnumSibling(CfgPriority e, int y, CfgPriority other) => ((int)e << 4) | y | (int)other;

    // #3087 follow-up (adversarial review): an UNSIGNED ordering (`clt.un`) of an
    // enum shift against an enum sibling. The compare cannot round-trip through the
    // enum backing — a signed backing would compare signed and a narrow backing
    // would truncate the widened shift value — so BOTH sides reconcile to the
    // unsigned counterpart of the shift's stack width:
    // `(uint)((int)e << n) < (uint)other`, matching `clt.un` for any backing.
    public static bool EnumShiftUnsignedCompare(CfgFlags e, int n, CfgFlags other) => (uint)((uint)e << n) < (uint)other;

    // #3087 follow-up: the same unsigned ordering with a byte-backed enum. Round-
    // tripping through the byte enum (`(CfgTiny)((int)e << n)`) would re-narrow the
    // 32-bit shift to 8 bits and then compare signed — the unsigned-width spelling
    // `(uint)((int)e << n) < (uint)other` avoids both.
    public static bool EnumShiftUnsignedCompareByte(CfgTiny e, int n, CfgTiny other) => (uint)((byte)e << n) < (uint)other;

    // #3087 follow-up (adversarial review R3, GPT): the unsigned ordering with a
    // PLAIN INTEGER sibling (not an enum). The shift still needs the unsigned
    // reinterpret so the compare is `clt.un` — `(uint)((int)e << n) < (uint)other` —
    // not the sign-widened `((int)e << n) < (uint)other` (`int < uint` promotes to a
    // signed `long` compare) the stale-ResultType fallthrough produced.
    public static bool EnumShiftUnsignedCompareIntSibling(CfgPriority e, int n, int other) => (uint)((int)e << n) < (uint)other;

    // #3087 follow-up (adversarial review R3, GPT): the 8-byte case with a plain
    // `long` sibling. The fallthrough rendered `((long)e << n) < (ulong)other`
    // (`long < ulong`, CS0034 — did not compile); both sides must reconcile to
    // `ulong` — `(ulong)((long)e << n) < (ulong)other`.
    public static bool EnumShiftUnsignedCompareLongSibling(CfgLongPriority e, int n, long other) => (ulong)((long)e << n) < (ulong)other;

    // #3087 follow-up (adversarial review R3, Gemini): a bitwise chain mixing an
    // enum shift with a NARROW (ushort) integer, compared unsigned. IL promotes the
    // short to Int32, so the chain's stack type is wide; treating it as narrow made
    // BitwiseChainRenderedType fall back to the stale enum ResultType and drop the
    // unsigned reinterpret — `((int)e << n | mask) < other` (CS0019 / signed). The
    // chain must reconcile to `(uint)((int)e << n | mask) < (uint)other`.
    public static bool EnumShiftNarrowChainUnsignedCompare(CfgTiny e, int n, ushort mask, CfgTiny other) => (uint)(((int)e << n) | mask) < (uint)other;

    // #3087 follow-up: a bitwise sibling MASKED as its primitive by a typed ldelem —
    // `values[i]` renders enum-typed though its ResultType is the storage width, so
    // it must still be down-coerced — `(int)e << n | (int)values[i]`, not
    // `| values[i]` (int | E, CS0019).
    public static CfgPriority EnumShiftOrArraySibling(CfgPriority e, int n, CfgPriority[] values, int i) => (CfgPriority)(((int)e << n) | (int)values[i]);

    // #3087 follow-up: an enum-CONSTANT sibling whose unsigned-backed value overflows
    // the shift's signed rendered integer (CfgFlags.Top = 0x80000000u > int.MaxValue):
    // the down-coercion needs `unchecked` — `(int)e << n == unchecked((int)CfgFlags.Top)`
    // (a plain `(int)CfgFlags.Top` is CS0221).
    public static bool EnumShiftCompareOverflowConst(CfgFlags e, int n) => ((int)e << n) == unchecked((int)CfgFlags.Top);

    // #3087 follow-up (adversarial review R4, GPT + Gemini): EQUALITY against a
    // plain `uint` sibling. Equality is bit-exact, so it reconciles to the shift's
    // SIGNED stack width — `((int)e << n) == (int)x`. The stale-ResultType
    // fallthrough dropped the sibling cast (`== x`), and `int == uint` widens to a
    // signed 64-bit `ceq` in C# — with `e = (CfgPriority)(-1)`, `x = 0xFFFFFFFF` the
    // IL 32-bit `ceq` is TRUE but `-1L == 4294967295L` is FALSE.
    public static bool EnumShiftEqualsUintSibling(CfgPriority e, int n, uint x) => (uint)((int)e << n) == x;

    // #3087 follow-up (adversarial review R4, GPT + Gemini): SIGNED ordering with a
    // plain integer sibling reconciles to the shift's signed stack width —
    // `((int)e << n) < other` (identity when the sibling already is `int`), never a
    // sign-widened `int < uint` promotion.
    public static bool EnumShiftSignedLessIntSibling(CfgPriority e, int n, int other) => ((int)e << n) < other;

    // #3087 follow-up (adversarial review R4, GPT): a SIGNED ordering after an
    // UNSIGNED shift (`shr.un` renders `uint`). The target is driven by the
    // comparison (signed), not the shift's rendered sign, so the shift is
    // reinterpreted back to `int` — `(int)((uint)e >> n) < (int)other`. Coercing to
    // the shift's `uint` instead would emit an unsigned compare `((uint)e >> n) <
    // (uint)other`, silently changing the ordering.
    public static bool EnumShiftUnsignedShrSignedCompare(CfgPriority e, int n, CfgPriority other) => (int)((uint)e >> n) < (int)other;

    // #3087 follow-up (adversarial review R5, GPT): the reconcile cast the printer
    // INSERTS to reinterpret the shift is not in the IL, so under an enclosing
    // `checked` region a signed->unsigned reinterpret (`(uint)(-1)`) would THROW
    // though the IL (`shl; clt.un`) only reinterprets bits. The inserted cast must be
    // `unchecked`-wrapped — `checked((unchecked((uint)((int)e << n)) < other ...))` —
    // exactly as the enum and plain-integer reconcile paths already are.
    public static int EnumShiftCheckedUnsignedCompare(CfgPriority e, int n, uint other, int y)
        => checked((unchecked((uint)((int)e << n)) < unchecked((uint)other) ? 1 : 0) + y);

    // #3087 follow-up (adversarial review R5, Gemini): a bitwise chain mixing an
    // int enum-shift with an UNSIGNED-backed enum sibling (CfgFlags : uint),
    // compared unsigned. BinaryBody coerces the enum sibling DOWN to the shift's
    // signed stack type (`(int)x`), so the chain renders as `int`; the outer
    // unsigned comparison must therefore keep the `(uint)` reinterpret —
    // `(uint)((int)e << n | (int)x) > other`. Reporting the chain as `uint` (because
    // a uint-backed sibling is present) would drop that cast, silently promoting
    // `int > uint` to a signed 64-bit compare (inverting results for high-bit
    // patterns) or emitting CS0034 at 8-byte width.
    public static bool EnumShiftChainUintEnumSiblingCompare(CfgPriority e, int n, CfgFlags x, uint other)
        => (uint)(((int)e << n) | (int)x) > other;

    // #1766: ternary with enum-constant arms stored to a cross-assembly enum
    // local (StringComparison.Ordinal = 4, OrdinalIgnoreCase = 5).
    public static bool EnumConditional(string name, bool ci)
    {
        System.StringComparison c = ci ? System.StringComparison.Ordinal : System.StringComparison.OrdinalIgnoreCase;
        return name.EndsWith("x", c);
    }

    // #1772: bitwise compound assignment to a cross-assembly [Flags] enum local
    // (AttributeTargets.Class = 4, AttributeTargets.Struct = 8).
    public static System.AttributeTargets EnumFlagsCompound(bool a, bool b)
    {
        System.AttributeTargets result = (System.AttributeTargets)0;
        if (a)
        {
            result |= System.AttributeTargets.Class;
        }
        if (b)
        {
            result |= System.AttributeTargets.Struct;
        }
        return result;
    }

    // #1766 review finding 1: a conditional with one constant arm and one
    // non-constant integer arm into a cross-assembly enum — both arms must be cast
    // (`ci ? (StringComparison)4 : (StringComparison)raw`), not just the constant.
    public static bool EnumConditionalMixedArm(string name, bool ci, int raw)
    {
        System.StringComparison c = ci ? System.StringComparison.Ordinal : (System.StringComparison)raw;
        return name.EndsWith("x", c);
    }

    // #1772 review finding 2: a negative integer constant into a cross-assembly
    // enum (`~AttributeTargets.Class` folds to ldc.i4 -5) must force `unchecked`,
    // since an unsigned- or narrow-backed enum would reject `(Enum)(-5)` (CS0221).
    public static System.AttributeTargets EnumFlagsCompoundNegative(System.AttributeTargets seed)
    {
        seed &= ~System.AttributeTargets.Class;
        return seed;
    }

    // #1806: the fallback of `Nullable<cross-assembly enum> ?? enumConstant`
    // needs the same structural enum cast as conditional arms.
    // #2302: a same-width cross-signedness constant arm at a numeric join. A `uint`
    // coalesce fallback of `uint.MaxValue` lowers to `ldc.i4.m1`, so a bare `-1` arm
    // at the `uint` join is CS0019 (`??`) / CS0029; it must render the target-aware
    // `unchecked((uint)(-1))`. Mirrors the enum coalesce/conditional arm contract for
    // the plain numeric-primitive join.
    public static uint CrossSignCoalesceConstant(uint? value)
        => value ?? uint.MaxValue;

    public static System.StringComparison EnumCoalesce(System.StringComparison? value)
        => value ?? System.StringComparison.Ordinal;

    // #1806: switch-expression arms yielding cross-assembly enum constants need
    // target-aware casts, including the direct-return multi-line switch form.
    public static System.StringComparison EnumSwitchExpression(int value)
        => value switch
        {
            0 => System.StringComparison.Ordinal,
            _ => System.StringComparison.OrdinalIgnoreCase,
        };

    public static CfgPriority SameAssemblyEnumCoalesce(CfgPriority? value)
        => value ?? CfgPriority.High;

    public static CfgPriority SameAssemblyEnumSwitchExpression(int value)
        => value switch
        {
            0 => CfgPriority.High,
            _ => CfgPriority.Critical,
        };

    // #2076: a conditional whose reused stack slot merges an integer constant and a
    // same-assembly unsigned-backed enum (CfgFlags : uint). CfgFlags.Top =
    // 0x80000000u is emitted as `ldc.i4` int.MinValue (negative as a signed int),
    // so the importer cannot type the slot join. The slot-diamond fold must anchor
    // the enum type and the printer must emit `unchecked((CfgFlags)(-2147483648))`;
    // a bare or checked cast is CS0221, and a lost cast is CS0029.
    public static bool UnsignedEnumConditionalArm(bool c, CfgFlags e)
    {
        CfgFlags x = c ? CfgFlags.Top : e;
        return x == CfgFlags.None;
    }

    // #2076 (review): a retyped same-assembly unsigned-enum constant with no named
    // member — (CfgFlags)uint.MaxValue folds to `ldc.i4.m1` (-1) — must render an
    // `unchecked` cast in comparison, bitwise, and coalesce positions, not a bare
    // `(CfgFlags)(-1)` (CS0221).
    public static bool UnsignedEnumConstantComparison(CfgFlags f) => f == (CfgFlags)uint.MaxValue;

    public static CfgFlags UnsignedEnumConstantBitwise(CfgFlags f) => f & (CfgFlags)uint.MaxValue;

    public static CfgFlags UnsignedEnumConstantCoalesce(CfgFlags? f) => f ?? (CfgFlags)uint.MaxValue;

    // Slice-4 adversarial review (GPT-5.5): a byte-backed enum joined with a
    // full int. The join's semantic type is int (the enum was widened into
    // it); typing it as the enum re-narrows the int path — `(CfgTiny)x` turns
    // 300 into 44 and flips the boxed type from int to CfgTiny.
    public static object ByteEnumOrIntBox(bool c, CfgTiny e, int x)
    {
        int y = c ? (int)e : x;
        return y;
    }

    // The sound half of the join rule: an int-backed enum meeting an int of
    // exactly its underlying type is a pure reinterpretation, and the
    // enum-typed slot must render legally at every int sink it reaches.
    public static int IntEnumJoinThroughSlot(bool c, CfgPriority e)
    {
        int x = c ? (int)e : 1;
        System.Console.WriteLine(x);
        return x;
    }

    // Slice-4 cross-check review (Opus 4.8, verified against real IL): a
    // byte-backed enum arm in an int-typed switch dispatch. The raised switch
    // expression must cast the enum arm (`0 => (int)e`) and the statement
    // form's int store must cast at the sink — bare renders are
    // CS0029/CS0266 while graded Full.
    public static int SwitchEnumOrInt(int k, CfgTiny e, int x)
    {
        int r;
        switch (k)
        {
            case 0: r = (int)e; break;
            case 1: r = x; break;
            case 2: r = 300; break;
            default: r = 9; break;
        }
        return r;
    }

    // #2076 (review): long-backed enum constants in array-element and box
    // positions. The `stelem.i8` element type and `box` drop the enum type, so a
    // long constant printed bare is CS0266/CS0029 unless cast.
    public static CfgLongPriority[] LongEnumArray() => new[] { CfgLongPriority.High, (CfgLongPriority)5000000000L };

    public static System.Enum LongEnumBoxed() => CfgLongPriority.High;

    // #2076 (review): an unsigned long-backed enum value lowers as
    // `ldc.i4.m1; conv.i8`, so the enum cast's operand is a `Convert(long, ...)`.
    // The overflow decision must see through it and wrap `unchecked((CfgULong)(...))`.
    public static System.Enum ULongEnumBoxedMax() => CfgULong.All;

    public static CfgULong[] ULongEnumArrayMax() => new[] { CfgULong.All };

    // #2076 (review): a cross-assembly enum array (StringComparison resolves to
    // TypeShape.Unknown). The `stelem.i4` storage type must not drop the element
    // below its enum type — a bare `[0] = 4;` is CS0266.
    public static System.StringComparison[] CrossAssemblyEnumArray()
        => new[] { (System.StringComparison)4, System.StringComparison.OrdinalIgnoreCase };
}
