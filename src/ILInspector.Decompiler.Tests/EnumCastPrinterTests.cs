using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Issues #1766 / #1772: a cross-assembly (framework) enum resolves to
// TypeShape.Unknown, so an integer constant flowing into it renders as a bare
// int — `int->enum` in a conditional arm (CS0266) or `enum |= int` in a bitwise
// compound (CS0019) — while the method is still graded Full. The printer must
// cast the integer to the enum structurally.
public class EnumCastPrinterTests
{
    // #3011: an enum-typed value shifted has no predefined C# shift operator
    // (CS0019); the printer reinterprets the enum left operand to its underlying
    // integer so the shift type-checks and the shr/shr.un opcode round-trips.
    [Fact]
    public void EnumRightShift_LongBackedEnum_CastsLeftOperandToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.LongEnumRightShift));

        Assert.Contains("(long)flags >>", body);
        Assert.DoesNotContain(" flags >>", body);
        // #3011 (review): the shift-count width mask is keyed off the enum backing
        // (63 for the 8-byte underlying), so it strips instead of double-masking.
        Assert.DoesNotContain("& 63", body);
        AssertCompiles("public static long M(CfgLongPriority flags, int n)", body, "public enum CfgLongPriority : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void EnumLeftShift_LongBackedEnum_CastsLeftOperandToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.LongEnumLeftShift));

        Assert.Contains("(long)flags <<", body);
        AssertCompiles("public static long M(CfgLongPriority flags, int n)", body, "public enum CfgLongPriority : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void EnumRightShiftToInt_LongBackedEnum_MirrorsWitness()
    {
        // The reported MySqlConnector shape: `(int)((long)clientCapabilities >> 32)`.
        string body = RenderFixture(nameof(EnumCastSamples.LongEnumRightShiftToInt));

        Assert.Contains("(long)flags >> 32", body);
        AssertCompiles("public static int M(CfgLongPriority flags)", body, "public enum CfgLongPriority : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void EnumRightShift_ULongBackedEnum_KeepsUnsignedShiftFaithful()
    {
        string body = RenderFixture(nameof(EnumCastSamples.ULongEnumRightShift));

        Assert.Contains("(ulong)flags >>", body);
        AssertCompiles("public static ulong M(CfgULong flags, int n)", body, "public enum CfgULong : ulong { None = 0, All = 18446744073709551615UL }");
    }

    [Fact]
    public void EnumRightShift_UIntBackedEnum_KeepsUnsignedShiftFaithful()
    {
        string body = RenderFixture(nameof(EnumCastSamples.UIntEnumRightShift));

        Assert.Contains("(uint)flags >>", body);
        AssertCompiles("public static uint M(CfgFlags flags, int n)", body, "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    [Fact]
    public void EnumRightShift_IntBackedEnum_CastsToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumRightShift));

        Assert.Contains("(int)flags >>", body);
        Assert.DoesNotContain(" flags >>", body);
        // The 4-byte backing masks the count by 31; keyed off the underlying it
        // strips rather than re-spelling `n & 31` (which double-masks on recompile).
        Assert.DoesNotContain("& 31", body);
        AssertCompiles("public static int M(CfgPriority flags, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // The shift opcode's signedness, not the enum backing's, drives the cast: a
    // same-width signedness reinterpret in the source leaves no IL trace, so the
    // enum backing is a misleading signedness signal. shr.un on an int-backed enum
    // must render `(uint)`, and shr on a uint-backed enum must render `(int)`,
    // else the recompiled shift flips opcode and silently changes the result.
    [Fact]
    public void EnumRightShift_IntBackedButUnsignedOpcode_CastsToUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumUnsignedRightShift));

        Assert.Contains("(uint)flags >>", body);
        Assert.DoesNotContain("(int)flags >>", body);
        AssertCompiles("public static uint M(CfgPriority flags, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void EnumRightShift_UIntBackedButSignedOpcode_CastsToSigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.UIntEnumSignedRightShift));

        Assert.Contains("(int)flags >>", body);
        Assert.DoesNotContain("(uint)flags >>", body);
        AssertCompiles("public static int M(CfgFlags flags, int n)", body, "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    // A compound shift on an enum lvalue (`flags <<= n`) is CS0019 just like the
    // expression form: C# has no compound shift operator on an enum. The printer
    // decomposes it to a plain assignment that reinterprets the enum and casts the
    // shift result back — `flags = (CfgPriority)((int)flags << n)`.
    [Fact]
    public void EnumCompoundLeftShift_IntBackedEnum_DecomposesToCastBackAssignment()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumCompoundLeftShift));

        Assert.Contains("flags = (CfgPriority)((int)flags <<", body);
        Assert.DoesNotContain("flags <<=", body);
        Assert.DoesNotContain("& 31", body);
        AssertCompiles("public static CfgPriority M(CfgPriority flags, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // The decomposed compound's left-operand cast follows the shift opcode: an
    // int-backed enum shifted as shr.un must reinterpret to `(uint)`, not `(int)`.
    [Fact]
    public void EnumCompoundRightShift_IntBackedButUnsignedOpcode_CastsToUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumCompoundUnsignedRightShift));

        Assert.Contains("flags = (CfgPriority)((uint)flags >>", body);
        Assert.DoesNotContain("flags >>=", body);
        Assert.DoesNotContain("(int)flags >>", body);
        AssertCompiles("public static CfgPriority M(CfgPriority flags, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3011 (review): a typed `ldelem.i4`/`ldind.i4` over an enum array or by-ref
    // masks the operand's stack type as the primitive storage width, but the
    // rendered `values[i]`/`e` is enum-typed and still rejects a bare shift. The
    // printer recovers the enum from the array element / pointee type and forces
    // the reinterpret cast (which `CoerceText` would drop as an int→int identity).
    [Fact]
    public void EnumArrayRightShift_IntBackedEnum_CastsElementToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumArrayRightShift));

        Assert.Contains("(int)values[i] >>", body);
        Assert.DoesNotContain(" values[i] >>", body);
        Assert.DoesNotContain("& 31", body);
        AssertCompiles("public static int M(CfgPriority[] values, int i, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void EnumArrayLeftShift_IntBackedEnum_CastsElementToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumArrayLeftShift));

        Assert.Contains("(int)values[i] <<", body);
        Assert.DoesNotContain(" values[i] <<", body);
        AssertCompiles("public static int M(CfgPriority[] values, int i, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // Width recovery for the array element: a long-backed enum array casts to long
    // and strips the 6-bit (& 63) count mask, not the 5-bit int mask.
    [Fact]
    public void EnumArrayRightShift_LongBackedEnum_CastsElementToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.LongEnumArrayRightShift));

        Assert.Contains("(long)values[i] >>", body);
        Assert.DoesNotContain("& 63", body);
        AssertCompiles("public static long M(CfgLongPriority[] values, int i, int n)", body, "public enum CfgLongPriority : long { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // Signedness still follows the opcode for the recovered element cast: shr.un on
    // an int-backed enum array reinterprets to `(uint)`, not the backing's `(int)`.
    [Fact]
    public void EnumArrayRightShift_IntBackedButUnsignedOpcode_CastsElementToUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumArrayUnsignedRightShift));

        Assert.Contains("(uint)values[i] >>", body);
        Assert.DoesNotContain("(int)values[i] >>", body);
        AssertCompiles("public static uint M(CfgPriority[] values, int i, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // The by-ref sibling: a `ref` enum loaded through `ldind.i4` is masked as the
    // primitive too, so the pointee-recovered cast reinterprets `e`.
    [Fact]
    public void RefEnumLeftShift_IntBackedEnum_CastsPointeeToUnderlying()
    {
        string body = RenderFixture(nameof(EnumCastSamples.RefIntEnumLeftShift));

        Assert.Contains("(int)", body);
        Assert.Contains("<<", body);
        Assert.DoesNotContain(" e <<", body);
        AssertCompiles("public static int M(ref CfgPriority e, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // An enum shift feeding a parent bitwise &/|/^ must not coerce the *sibling*
    // integer to the enum (`(int)e << n | (E)x`, CS0019). The shift renders as its
    // underlying integer, so the bitwise op is integer; a mixed-sign same-width
    // sibling reconciles to one width (uint|uint) rather than promoting to the wider
    // signed common type. Witness: Roslyn MetadataWriter.GetRawToken.
    [Fact]
    public void EnumShiftInBitwiseOr_UnsignedSibling_ReconcilesToOneWidth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumShiftOrUnsigned));

        Assert.Contains("(uint)e << 24 | x", body);
        Assert.DoesNotContain("(int)e << 24", body);
        Assert.DoesNotContain("(CfgPriority)x", body);
        AssertCompiles("public static uint M(CfgPriority e, uint x)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // Same-sign (int shift | int) binds bare — no reconciliation, no enum coercion.
    [Fact]
    public void EnumShiftInBitwiseOr_SignedSibling_BindsAsInteger()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumShiftOrSigned));

        Assert.Contains("(int)e << 8 | x", body);
        Assert.DoesNotContain("(CfgPriority)x", body);
        AssertCompiles("public static int M(CfgPriority e, int x)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // A long-backed enum shift | ulong sibling has no C# common type (CS0019) unless
    // the signed shift is reinterpreted to ulong — the mixed-sign reconciliation the
    // stale enum ResultType would otherwise suppress.
    [Fact]
    public void EnumShiftInBitwiseOr_LongEnumUnsignedSibling_ReconcilesToOneWidth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.LongEnumShiftOrUnsigned));

        Assert.Contains("(ulong)e << 8 | x", body);
        Assert.DoesNotContain("(long)e << 8", body);
        AssertCompiles("public static ulong M(CfgLongPriority e, ulong x)", body, "public enum CfgLongPriority : long { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void EnumShiftInBitwiseAnd_UnsignedSibling_ReconcilesToOneWidth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.IntEnumShiftAndUnsigned));

        Assert.Contains("(uint)e << 4 & x", body);
        Assert.DoesNotContain("(int)e << 4", body);
        Assert.DoesNotContain("(CfgPriority)x", body);
        AssertCompiles("public static uint M(CfgPriority e, uint x)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3076 is a LEFT-shift-only collapse: a signed arithmetic right shift keeps its
    // `(ulong)((long)e >> n)` double cast, because `(ulong)e >> n` is a logical shift
    // and would change the value.
    [Fact]
    public void EnumRightShiftInBitwiseOr_KeepsDoubleCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.LongEnumShiftRightOrUnsigned));

        Assert.Contains("(ulong)((long)e >> n)", body);
        Assert.DoesNotContain("(ulong)e >> n", body);
        AssertCompiles("public static ulong M(CfgLongPriority e, int n, ulong x)", body, "public enum CfgLongPriority : long { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3076 precedence guard: an enum array-element left shift reconciled inside a
    // mixed-sign ARITHMETIC parent (`+`, which binds tighter than `<<`) must keep
    // parentheses — `((uint)values[i] << n) + x`, never `(uint)values[i] << n + x`
    // (which parses as `(uint)values[i] << (n + x)` and fails to bind).
    [Fact]
    public void EnumArrayShiftInArithmeticAdd_KeepsShiftParentheses()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumArrayShiftAddUnsigned));

        Assert.Contains("((uint)values[i] << n) + x", body);
        Assert.DoesNotContain("<< n + x", body);
        AssertCompiles("public static uint M(CfgPriority[] values, int i, int n, uint x)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // A bitwise CHAIN over an enum shift: the inner `|` inherits the shift's stale
    // enum ResultType while rendering as an integer, so the far sibling `y` must not
    // be coerced to the enum (`... | (E)y`, CS0019). The rewritten-integer detection
    // recurses through the chain.
    [Fact]
    public void EnumShiftInBitwiseChain_UnsignedSiblings_DoNotCoerceFarSibling()
    {
        string body = RenderFixture(nameof(EnumCastSamples.ChainIntEnumShiftOrUnsigned));

        Assert.Contains("(uint)e << 24 | x | y", body);
        Assert.DoesNotContain("(int)e << 24", body);
        Assert.DoesNotContain("(CfgPriority)y", body);
        AssertCompiles("public static uint M(CfgPriority e, uint x, uint y)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void EnumShiftInBitwiseChain_LongEnumUnsignedSiblings_DoNotCoerceFarSibling()
    {
        string body = RenderFixture(nameof(EnumCastSamples.ChainLongEnumShiftOrUnsigned));

        Assert.Contains("(ulong)e << 8 | x | y", body);
        Assert.DoesNotContain("(long)e << 8", body);
        Assert.DoesNotContain("(CfgLongPriority)y", body);
        AssertCompiles("public static ulong M(CfgLongPriority e, ulong x, ulong y)", body, "public enum CfgLongPriority : long { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // An enum shift RETURNED to an enum target renders as its underlying integer,
    // so it needs an outer `(E)` cast (CS0266) — the shift's stale enum ResultType
    // would otherwise read as an identity to the return type and drop the cast.
    [Fact]
    public void EnumShiftReturnedToEnum_KeepsOuterEnumCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftReturn));

        Assert.Contains("(CfgPriority)((int)e >> n)", body);
        AssertCompiles("public static CfgPriority M(CfgPriority e, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void EnumShiftReturnedToLongEnum_KeepsOuterEnumCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftReturnLong));

        Assert.Contains("(CfgLongPriority)((long)e >> n)", body);
        AssertCompiles("public static CfgLongPriority M(CfgLongPriority e, int n)", body, "public enum CfgLongPriority : long { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // An enum shift STORED to an enum array element: the same int->enum sink as a
    // return, reached through the assignment coercion funnel.
    [Fact]
    public void EnumShiftStoredToEnumArray_KeepsOuterEnumCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftStoreArray));

        Assert.Contains("(CfgPriority)((int)arr[0] >> n)", body);
        AssertCompiles("public static void M(CfgPriority[] arr, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 defect 1: a bitwise op whose sibling is itself an ENUM value. The shift
    // renders as its underlying integer, so the enum sibling must be coerced DOWN to
    // that integer — `(int)e << n | (int)other`, never `(int)e << n | other` (int |
    // E, CS0019). The outer `(CfgPriority)` cast the chain flows into is kept too.
    [Fact]
    public void EnumShiftInBitwiseOr_EnumSibling_CoercesSiblingToInteger()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftOrEnumSibling));

        Assert.Contains("(CfgPriority)(((int)e << n) | (int)other)", body);
        Assert.DoesNotContain("<< n) | other", body);
        AssertCompiles("public static CfgPriority M(CfgPriority e, int n, CfgPriority other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 defect 2: an enum shift COMPARED to an enum-constant sibling. The shift
    // renders as int, so the sibling is coerced DOWN — `(int)e << n == (int)E.High`,
    // never `int == E` (CS0019). The shift's stale enum ResultType must not coerce
    // the sibling up to the enum.
    [Fact]
    public void EnumShiftComparedToEnumConstant_CoercesSiblingToInteger()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftCompareEnumConst));

        Assert.Contains("((int)e << n) == (int)CfgPriority.High", body);
        Assert.DoesNotContain("== CfgPriority.High", body);
        AssertCompiles("public static bool M(CfgPriority e, int n)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 defect 3: an enum shift as a ternary arm flowing into an ENUM target (a
    // method argument, which stays a genuine conditional). The arm's stale enum
    // ResultType matches the target, so the `(CfgPriority)` cast is dropped and the
    // arm renders `(int)e << n` (int) against the enum arm — CS1503. The cast is
    // kept: `b ? (CfgPriority)((int)e << n) : CfgPriority.High`.
    [Fact]
    public void EnumShiftAsConditionalArmToEnum_KeepsEnumCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftTernaryToEnum));

        Assert.Contains("b ? (CfgPriority)((int)e << n) : CfgPriority.High", body);
        Assert.DoesNotContain("b ? ((int)e << n) :", body);
        AssertCompiles(
            "public static void M(bool b, CfgPriority e, int n)",
            "static void TakeCfgPriority(CfgPriority value) { }\n" + body,
            "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 mixed-sign edge: an UNSIGNED enum shift (`shr.un`) renders as uint, so
    // its enum sibling is coerced to that width and sign — `(uint)other`, never
    // `(int)other` (a mixed-sign bitwise op, CS0019).
    [Fact]
    public void EnumUnsignedShiftInBitwiseOr_EnumSibling_CoercesSiblingToUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftUnsignedOrEnumSibling));

        Assert.Contains("((uint)e >> n) | (uint)other", body);
        Assert.DoesNotContain(">> n) | other", body);
        Assert.DoesNotContain("(int)other", body);
        AssertCompiles("public static uint M(CfgPriority e, int n, CfgPriority other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 defect 2 edge: the enum sibling of a comparison is a RUNTIME value (not a
    // constant) — still coerced down to the shift's rendered integer.
    [Fact]
    public void EnumShiftComparedToEnumValue_CoercesSiblingToInteger()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftCompareEnumValue));

        Assert.Contains("((int)e << n) == (int)other", body);
        Assert.DoesNotContain("== other", body);
        AssertCompiles("public static bool M(CfgPriority e, int n, CfgPriority other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 defect 1 edge: an enum FAR sibling in a bitwise CHAIN over a shift — the
    // down-coercion recurses through the chain so the trailing enum renders as its
    // underlying integer (`(int)e << 4 | y | (int)other`).
    [Fact]
    public void EnumShiftChainWithEnumFarSibling_CoercesSiblingToInteger()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftChainEnumSibling));

        Assert.Contains("(int)e << 4 | y", body);
        Assert.Contains("| (int)other", body);
        Assert.DoesNotContain("y | other", body);
        AssertCompiles("public static int M(CfgPriority e, int y, CfgPriority other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review, GPT + Gemini): an UNSIGNED ordering
    // (`clt.un`) of an enum shift against an enum sibling. The compare cannot round-
    // trip through the enum backing: down-coercing to the shift's SIGNED rendered
    // integer renders a signed `<`, and up-coercing to a narrow enum backing
    // truncates the widened shift value. Both sides reconcile to the unsigned stack-
    // width integer — `(uint)((int)e << n) < (uint)other` — matching `clt.un`.
    [Fact]
    public void EnumShiftUnsignedOrdering_ComparesAsUnsignedEnum()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftUnsignedCompare));

        Assert.Contains("(uint)((int)e << n)", body);
        Assert.Contains("< (uint)other", body);
        Assert.DoesNotContain("(CfgFlags)", body);
        AssertCompiles("public static bool M(CfgFlags e, int n, CfgFlags other)", body, "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    // #3087 follow-up (adversarial review, GPT + Gemini): the same unsigned ordering
    // with a BYTE-backed enum. Up-coercing to the byte enum (`(CfgTiny)((int)e << n)`)
    // would re-narrow the 32-bit shift to 8 bits and then compare signed — a
    // soundness bug (`0x100 < y` becomes `0 < y`). The unsigned-width spelling
    // `(uint)((int)e << n) < (uint)other` must be emitted instead.
    [Fact]
    public void EnumShiftUnsignedOrdering_ByteBacked_ComparesAsUnsignedWidth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftUnsignedCompareByte));

        Assert.Contains("(uint)((int)e << n)", body);
        Assert.Contains("< (uint)other", body);
        Assert.DoesNotContain("(CfgTiny)", body);
        AssertCompiles("public static bool M(CfgTiny e, int n, CfgTiny other)", body, "public enum CfgTiny : byte { A = 1, B = 2 }");
    }

    // #3087 follow-up (adversarial review R3, GPT): the unsigned ordering with a
    // PLAIN INTEGER sibling. The shift still needs the unsigned reinterpret so the
    // compare is `clt.un` — `(uint)((int)e << n) < (uint)other` — not the sign-
    // widened `((int)e << n) < (uint)other` (`int < uint` promotes to a signed
    // `long`) the stale-enum-ResultType fallthrough produced.
    [Fact]
    public void EnumShiftUnsignedOrdering_PlainIntSibling_ReinterpretsBothUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftUnsignedCompareIntSibling));

        Assert.Contains("(uint)((int)e << n)", body);
        Assert.Contains("< (uint)other", body);
        AssertCompiles("public static bool M(CfgPriority e, int n, int other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review R3, GPT): the 8-byte unsigned ordering
    // with a plain `long` sibling. The fallthrough rendered `((long)e << n) <
    // (ulong)other` (`long < ulong`, CS0034 — did not compile); both sides must
    // reconcile to `ulong`.
    [Fact]
    public void EnumShiftUnsignedOrdering_PlainLongSibling_ReinterpretsBothUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftUnsignedCompareLongSibling));

        Assert.Contains("(ulong)((long)e << n)", body);
        Assert.Contains("< (ulong)other", body);
        AssertCompiles("public static bool M(CfgLongPriority e, int n, long other)", body, "public enum CfgLongPriority : long { Low = 0, High = 2 }");
    }

    // #3087 follow-up (adversarial review R3, Gemini): a bitwise chain mixing an
    // enum shift with a NARROW (ushort) integer, compared unsigned. IL promotes the
    // short to Int32, so the chain is wide; the printer must reconcile both sides to
    // `uint` — `(uint)((int)e << n | mask) < (uint)other` — not drop the casts
    // (`((int)e << n | mask) < other`, CS0019 / silent signed compare).
    [Fact]
    public void EnumShiftUnsignedOrdering_NarrowBitwiseChain_ReinterpretsBothUnsigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftNarrowChainUnsignedCompare));

        Assert.Contains("(uint)((int)e << n | mask)", body);
        Assert.Contains("< (uint)other", body);
        Assert.DoesNotContain("(CfgTiny)", body);
        AssertCompiles("public static bool M(CfgTiny e, int n, ushort mask, CfgTiny other)", body, "public enum CfgTiny : byte { A = 1, B = 2 }");
    }

    // #3087 follow-up (adversarial review, GPT): a bitwise sibling MASKED as its
    // primitive by a typed ldelem — `values[i]` renders enum-typed though its
    // ResultType is the storage width, so it must still be coerced DOWN —
    // `(int)e << n | (int)values[i]`, never `| values[i]` (int | E, CS0019).
    [Fact]
    public void EnumShiftInBitwiseOr_MaskedArraySibling_CoercesSiblingToInteger()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftOrArraySibling));

        Assert.Contains("(int)values[i]", body);
        Assert.DoesNotContain("| values[i]", body);
        AssertCompiles("public static CfgPriority M(CfgPriority e, int n, CfgPriority[] values, int i)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review, GPT): an enum-CONSTANT sibling whose
    // unsigned-backed value overflows the shift's signed rendered integer
    // (CfgFlags.Top = 0x80000000u > int.MaxValue). The down-coercion needs
    // `unchecked` — a plain `(int)CfgFlags.Top` is a CS0221 constant overflow.
    [Fact]
    public void EnumShiftComparedToOverflowingConstant_WrapsUnchecked()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftCompareOverflowConst));

        Assert.Contains("== unchecked((int)CfgFlags.Top)", body);
        AssertCompiles("public static bool M(CfgFlags e, int n)", body, "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    // #3087 follow-up (adversarial review R4, GPT + Gemini): EQUALITY against a
    // plain `uint` sibling reconciles to the shift's SIGNED stack width —
    // `((int)e << n) == (int)x`. The stale-ResultType fallthrough dropped the cast
    // (`== x`); `int == uint` widens to a signed 64-bit `ceq` in C#, so with
    // `e = (CfgPriority)(-1)`, `x = 0xFFFFFFFF` the IL 32-bit `ceq` is TRUE but
    // `-1L == 4294967295L` is FALSE — a silent behavior change.
    [Fact]
    public void EnumShiftEquality_PlainUintSibling_ReconcilesToSignedWidth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftEqualsUintSibling));

        Assert.Contains("((int)e << n) == (int)x", body);
        Assert.DoesNotContain("(uint)", body);
        AssertCompiles("public static bool M(CfgPriority e, int n, uint x)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review R4, GPT + Gemini): SIGNED ordering with a
    // plain `int` sibling reconciles to the shift's signed stack width; the sibling
    // is already `int`, so it stays bare — `((int)e << n) < other`, no promotion.
    [Fact]
    public void EnumShiftSignedOrdering_PlainIntSibling_StaysSigned()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftSignedLessIntSibling));

        Assert.Contains("((int)e << n) < other", body);
        Assert.DoesNotContain("(uint)", body);
        AssertCompiles("public static bool M(CfgPriority e, int n, int other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review R4, GPT): a SIGNED ordering after an
    // UNSIGNED shift (`shr.un` renders `uint`). The target follows the COMPARISON
    // (signed), not the shift's rendered sign, so the shift is reinterpreted back to
    // `int` — `(int)((uint)e >> n) < (int)other`. Coercing to the shift's `uint`
    // instead would emit an unsigned compare, silently flipping the ordering.
    [Fact]
    public void EnumShiftUnsignedShr_SignedOrdering_ReinterpretsToSignedWidth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftUnsignedShrSignedCompare));

        Assert.Contains("(int)((uint)e >> n) < (int)other", body);
        AssertCompiles("public static bool M(CfgPriority e, int n, CfgPriority other)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review R5, GPT): the reconcile cast the printer
    // INSERTS is not in the IL (`shl; clt.un`), so under an enclosing `checked`
    // region the signed->unsigned reinterpret must be `unchecked`-wrapped or it
    // throws OverflowException on a negative shift value (`(uint)(-1)`), where the
    // IL only reinterprets bits. Both reconcile paths (this shift side and the enum/
    // plain-integer sides) route through CheckedSafeCast.
    [Fact]
    public void EnumShiftComparison_InCheckedContext_WrapsInsertedCastUnchecked()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftCheckedUnsignedCompare));

        Assert.Contains("checked(", body);
        Assert.Contains("unchecked((uint)((int)e << n))", body);
        AssertCompiles("public static int M(CfgPriority e, int n, uint other, int y)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    // #3087 follow-up (adversarial review R5, Gemini): a bitwise chain mixing an int
    // enum-shift with an UNSIGNED-backed enum sibling, compared unsigned. The enum
    // sibling is coerced DOWN to the shift's signed stack type (`(int)x`), so the
    // chain is `int` and the outer unsigned comparison must keep the `(uint)`
    // reinterpret. Reporting the chain as `uint` would drop it — a silent signed
    // 64-bit promotion (or CS0034 at 8-byte width).
    [Fact]
    public void EnumShiftChain_UintEnumSibling_UnsignedCompare_KeepsOuterReinterpret()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumShiftChainUintEnumSiblingCompare));

        Assert.Contains("(uint)(((int)e << n) | (int)x) > other", body);
        Assert.DoesNotContain("(CfgFlags)", body);
        AssertCompiles(
            "public static bool M(CfgPriority e, int n, CfgFlags x, uint other)",
            body,
            "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 } public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    // A sub-int (byte) backing promotes to int in a C# shift; the reinterpret
    // targets the 4-byte width the shift runs on. Synthetic to keep the enum load
    // a bare operand (a compiled `(byte)` narrowing could add a conv node).
    [Fact]
    public void EnumLeftShift_ByteBackedEnum_CastsToShiftWidth()
    {
        var enumType = TypeRef.Definition("test", "", "CfgTiny");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var intType = TypeRef.CoreLib("System", "Int32");
        var shift = new Binary(
            BinaryKind.ShiftLeft,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            new LoadArgument(1, "n", intType));
        var block = new Block(0);
        block.Add(new Return(shift));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("flags", enumType), new Parameter("n", intType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("test", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = byteType },
        };

        string body = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("(int)flags << n", body);
        AssertCompiles("public static int M(CfgTiny flags, int n)", body, "public enum CfgTiny : byte { A = 1, B = 2 }");
    }

    // Close negative: a plain (non-enum) integer shift keeps its bare operands —
    // the enum branch must not perturb ordinary shifts.
    [Fact]
    public void IntegerShift_NonEnum_KeepsBareOperands()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var shift = new Binary(
            BinaryKind.ShiftRight,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "x", intType),
            new LoadArgument(1, "n", intType));
        var block = new Block(0);
        block.Add(new Return(shift));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("x", intType), new Parameter("n", intType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("test", "", "Holder"), signature, [], container);

        string body = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("x >> n", body);
        Assert.DoesNotContain("(int)x", body);
    }

    [Fact]
    public void EnumConstantConditionalArms_IntoCrossAssemblyEnum_CastsEachArm()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumConditional));

        Assert.Contains("(StringComparison)4", body);
        Assert.Contains("(StringComparison)5", body);
        Assert.DoesNotContain("? 4 : 5", body);
        AssertCompiles("public static bool M(string name, bool ci)", body);
    }

    [Fact]
    public void BitwiseCompound_IntoCrossAssemblyFlagsEnum_CastsRightOperand()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumFlagsCompound));

        Assert.Contains("|= (AttributeTargets)4", body);
        Assert.Contains("|= (AttributeTargets)8", body);
        Assert.DoesNotContain("|= 4", body);
        Assert.DoesNotContain("|= 8", body);
        AssertCompiles("public static AttributeTargets M(bool a, bool b)", body);
    }

    [Fact]
    public void EnumConditional_MixedConstantAndNonConstantArms_CastsBoth()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumConditionalMixedArm));

        Assert.Contains("(StringComparison)4", body);
        Assert.Contains("(StringComparison)raw", body);
        Assert.DoesNotContain(": raw", body);
        AssertCompiles("public static bool M(string name, bool ci, int raw)", body);
    }

    [Fact]
    public void EnumCompound_NegativeConstant_ForcesUncheckedCast()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumFlagsCompoundNegative));

        Assert.Contains("unchecked((AttributeTargets)(-5))", body);
        AssertCompiles("public static AttributeTargets M(AttributeTargets seed)", body);
    }

    [Fact]
    public void EnumCoalesce_IntoCrossAssemblyEnum_CastsFallback()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumCoalesce));

        Assert.Contains("?? (StringComparison)4", body);
        Assert.DoesNotContain("?? 4", body);
        AssertCompiles("public static StringComparison M(StringComparison? value)", body);
    }

    // #2302: the same join-arm contract for a plain numeric-primitive out-of-range
    // constant — a bare `-1` at a `uint` coalesce is CS0019, so it must reinterpret.
    [Fact]
    public void CrossSignCoalesce_OutOfRangeConstant_UncheckedReinterprets()
    {
        string body = RenderFixture(nameof(EnumCastSamples.CrossSignCoalesceConstant));

        Assert.Contains("unchecked((uint)(-1))", body);
        Assert.DoesNotContain("?? -1", body);
        AssertCompiles("public static uint M(uint? value)", body);
    }

    [Fact]
    public void EnumSwitchExpression_ReturningCrossAssemblyEnum_CastsArms()
    {
        var enumType = TypeRef.CoreLib("System", "StringComparison");
        var intType = TypeRef.CoreLib("System", "Int32");
        string body = RenderSyntheticSwitchExpression(
            enumType,
            new Constant(4, intType),
            new Constant(5, intType));

        Assert.Contains("=> (StringComparison)4", body);
        Assert.Contains("=> (StringComparison)5", body);
        Assert.DoesNotContain("=> 4", body);
        Assert.DoesNotContain("=> 5", body);
        AssertCompiles("public static StringComparison M(int value)", body);
    }

    [Fact]
    public void SameAssemblyEnumCoalesce_KeepsNamedMember()
    {
        string body = RenderFixture(nameof(EnumCastSamples.SameAssemblyEnumCoalesce));

        Assert.Contains("CfgPriority.High", body);
        Assert.DoesNotContain("(CfgPriority)2", body);
        AssertCompiles("public static CfgPriority M(CfgPriority? value)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void SameAssemblyEnumSwitchExpression_KeepsNamedMembers()
    {
        var enumType = TypeRef.Definition("test", "", "CfgPriority");
        string body = RenderSyntheticSwitchExpression(
            enumType,
            new Constant(2, enumType),
            new Constant(3, enumType),
            new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [enumType] = new Dictionary<long, string> { [2] = "High", [3] = "Critical" },
            });

        Assert.Contains("CfgPriority.High", body);
        Assert.Contains("CfgPriority.Critical", body);
        Assert.DoesNotContain("(CfgPriority)2", body);
        Assert.DoesNotContain("(CfgPriority)3", body);
        AssertCompiles("public static CfgPriority M(int value)", body, "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void RetypedUnsignedEnumConstant_ForcesUncheckedCast()
    {
        // A same-assembly unsigned-enum constant retyped by TypedConstantsPass with
        // no named member, in comparison / bitwise / coalesce positions.
        const string declaration = "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }";

        string comparison = RenderFixture(nameof(EnumCastSamples.UnsignedEnumConstantComparison));
        Assert.Contains("unchecked((CfgFlags)(-1))", comparison);
        AssertCompiles("public static bool M(CfgFlags f)", comparison, declaration);

        string bitwise = RenderFixture(nameof(EnumCastSamples.UnsignedEnumConstantBitwise));
        Assert.Contains("unchecked((CfgFlags)(-1))", bitwise);
        AssertCompiles("public static CfgFlags M(CfgFlags f)", bitwise, declaration);

        string coalesce = RenderFixture(nameof(EnumCastSamples.UnsignedEnumConstantCoalesce));
        Assert.Contains("unchecked((CfgFlags)(-1))", coalesce);
        AssertCompiles("public static CfgFlags M(CfgFlags? f)", coalesce, declaration);
    }

    [Fact]
    public void EnumConditional_SameAssemblyUnsignedEnum_NamesHighBitMember()
    {
        // #2076: `c ? CfgFlags.Top : e` where CfgFlags : uint. Top (0x80000000u)
        // is emitted as `ldc.i4` int.MinValue, so the conditional slot's importer
        // type is unknown; the fold anchors the enum. The name-or-cast rule
        // (EnumConstantText) resolves the payload to the member — `CfgFlags.Top`,
        // never a bare `-2147483648` (CS0029) or an unchecked cast of the raw
        // literal. The unnamed-value fallback to `unchecked` is pinned by
        // CoerceChokePointTests.UnnamedHighBitConstantArm_KeepsUncheckedCast.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.UnsignedEnumConditionalArm));

        Assert.Contains("CfgFlags.Top", body);
        Assert.DoesNotContain(": -2147483648", body);
        Assert.DoesNotContain("(CfgFlags)(-2147483648)", body);
        AssertCompiles(
            "public static bool M(bool c, CfgFlags e)",
            body,
            "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    [Fact]
    public void KnownEnumPositiveConstantOutOfRange_ForcesUncheckedCast()
    {
        // A same-assembly enum with a known narrow underlying type: an out-of-range
        // constant cast is CS0221 unless wrapped, while an in-range one stays bare.
        string outOfRange = RenderKnownEnumReturnConstant(300, TypeRef.CoreLib("System", "Byte"));
        Assert.Contains("return unchecked((Tiny)300);", outOfRange);
        AssertCompiles("public static Tiny M()", outOfRange, "public enum Tiny : byte { }");

        string inRange = RenderKnownEnumReturnConstant(4, TypeRef.CoreLib("System", "Byte"));
        Assert.Contains("return (Tiny)4;", inRange);
        Assert.DoesNotContain("unchecked", inRange);
        AssertCompiles("public static Tiny M()", inRange, "public enum Tiny : byte { }");
    }

    [Fact]
    public void EnumWithUnresolvedBackingWidth_AssumesIntBacking()
    {
        // An enum classified TypeShape.Enum but whose underlying width is not in the
        // map (e.g. no value__ field) assumes C#'s default `int` backing: an
        // int-range negative constant stays a bare cast (matching ExactMember), and
        // only a genuinely out-of-int value would be wrapped.
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new Constant(-1, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            // EnumUnderlyingTypes intentionally left empty.
        };

        string body = CSharpPrinter.Print(function).Output!.Trim();
        Assert.Contains("(Tiny)(-1)", body);
        Assert.DoesNotContain("unchecked", body);
    }

    [Fact]
    public void UnknownEnumPositiveConstantThatMayOverflow_ForcesUncheckedCast()
    {
        string sbyteBody = RenderUnknownEnumReturnConstant(128);
        string byteBody = RenderUnknownEnumReturnConstant(300);

        Assert.Contains("return unchecked((Tiny)128);", sbyteBody);
        Assert.DoesNotContain("return (Tiny)128;", sbyteBody);
        AssertCompiles("public static Tiny M()", sbyteBody, "public enum Tiny : sbyte { }");

        Assert.Contains("return unchecked((Tiny)300);", byteBody);
        Assert.DoesNotContain("return (Tiny)300;", byteBody);
        AssertCompiles("public static Tiny M()", byteBody, "public enum Tiny : byte { }");
    }

    [Fact]
    public void UnknownEnumSwitchLabelPositiveConstantThatMayOverflow_ForcesUncheckedCast()
    {
        string body = RenderUnknownEnumSwitchLabel(128);

        Assert.Contains("case unchecked((Tiny)128):", body);
        Assert.DoesNotContain("case (Tiny)128:", body);
        AssertCompiles("public static void M(Tiny value)", body, "public enum Tiny : sbyte { }");
    }

    [Fact]
    public void UnknownEnumSwitchExpressionLabelPositiveConstantThatMayOverflow_ForcesUncheckedCast()
    {
        string body = RenderUnknownEnumSwitchExpressionLabel(128);

        Assert.Contains("unchecked((Tiny)128) => 1", body);
        Assert.DoesNotContain("(Tiny)128 => 1", body);
        AssertCompiles("public static int M(Tiny value)", body, "public enum Tiny : sbyte { }");
    }

    [Fact]
    public void IntegerNullableCoalesce_IntoUnknownEnum_CastsWholeCoalesce()
    {
        string body = RenderIntegerNullableCoalesceIntoUnknownEnum();

        Assert.Contains("return (Tiny)(value ?? 4);", body);
        Assert.DoesNotContain("value ?? (Tiny)4", body);
        AssertCompiles("public static Tiny M(int? value)", body, "public enum Tiny { }");
    }

    [Fact]
    public void EnumSwitchLabel_LongConstant_CastsInsteadOfBareLiteral()
    {
        // #2076 (review): a long case label on a long-backed enum switch must cast
        // (`case (LEnum)...:`), not render a bare `case 1311768467463790320:`
        // (CS0266). Member names still win when the value is named.
        const long value = 1311768467463790320L;
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Switch(
            new LoadArgument(0, "value", enumType),
            [
                new SwitchSection(ImmutableArray.Create(new Constant(value, longType)), isDefault: false, SingleReturnContainer()),
                new SwitchSection(ImmutableArray<Constant>.Empty, isDefault: true, SingleReturnContainer()),
            ]));
        body.Add(block);
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("value", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = longType },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();
        Assert.Contains("case (LEnum)1311768467463790320:", output);
        Assert.DoesNotContain("case 1311768467463790320:", output);
    }

    [Fact]
    public void CrossAssemblyEnumArray_CastsElementStore()
    {
        // A cross-assembly enum array element store must cast to the enum, not emit
        // a bare `int` (CS0266) off the `stelem.i4` storage type.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.CrossAssemblyEnumArray));
        Assert.Contains("(StringComparison)4", body);
        Assert.DoesNotContain("= 4;", body);
        AssertCompiles("public static System.StringComparison[] M()", body);
    }

    [Fact]
    public void UnsignedLongEnumMaxConstant_ConvertWrapped_NamesMember()
    {
        // ulong.MaxValue lowers as `ldc.i4.m1; conv.i8`; TypedConstantsPass folds
        // the widening into an enum-typed constant (sign-extended payload -1
        // matches the member map's keying), so the value renders by name — the
        // ideal spelling for leak case #6, replacing
        // `unchecked((CfgULong)((long)(-1)))`. The unnamed-value unchecked
        // fallback stays pinned by the Unknown-enum and CoerceChokePoint tests.
        const string declaration = "public enum CfgULong : ulong { None = 0, All = 18446744073709551615UL }";

        string boxed = RenderRaisedFixture(nameof(EnumCastSamples.ULongEnumBoxedMax));
        Assert.Contains("CfgULong.All", boxed);
        Assert.DoesNotContain("(long)(-1)", boxed);
        AssertCompiles("public static System.Enum M()", boxed, declaration);

        string array = RenderRaisedFixture(nameof(EnumCastSamples.ULongEnumArrayMax));
        Assert.Contains("CfgULong.All", array);
        AssertCompiles("public static CfgULong[] M()", array, declaration);
    }

    [Fact]
    public void LongBackedEnumConstants_InArrayAndBox_CastOrName()
    {
        // Array elements: an unnamed long payload renders as the enum cast, never
        // a bare `long` (CS0266). AssertCompiles is the real validity gate.
        string array = RenderRaisedFixture(nameof(EnumCastSamples.LongEnumArray));
        Assert.Contains("(CfgLongPriority)5000000000", array);
        Assert.DoesNotContain("= 5000000000;", array);
        AssertCompiles(
            "public static CfgLongPriority[] M()",
            array,
            "public enum CfgLongPriority : long { Low = 0, High = 2 }");

        // Box target: the enum value must keep its type (bare long is CS0029 for
        // System.Enum). The small value arrives as `Convert(long, ...)`;
        // TypedConstantsPass folds it, so the named member renders.
        string boxed = RenderRaisedFixture(nameof(EnumCastSamples.LongEnumBoxed));
        Assert.Contains("CfgLongPriority.High", boxed);
        Assert.DoesNotContain("return (long)", boxed);
        AssertCompiles(
            "public static System.Enum M()",
            boxed,
            "public enum CfgLongPriority : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void EnumArm_AtIntegerSwitchExpressionTarget_CastsToUnderlying()
    {
        // Slice-4 second-family review (GPT-5.5): the enum→integer mirror rule
        // must cover switch-expression arms, not only conditional arms — an
        // enum arm at an int-typed switch result is CS0266 bare. One join-arm
        // rule (TryCoerceJoinArm) now serves all three arm renderers.
        var enumType = TypeRef.Definition("test", "", "CfgPriority");
        var intType = TypeRef.CoreLib("System", "Int32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "value", intType),
            [
                new SwitchExpressionArm(ImmutableArray.Create(0), isDefault: false, new LoadArgument(1, "e", enumType)),
                new SwitchExpressionArm(ImmutableArray<int>.Empty, isDefault: true, new Constant(7, intType)),
            ]);
        var block = new Block(0);
        block.Add(new Return(switchExpression));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("value", intType), new Parameter("e", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("test", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = intType },
        };
        string body = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("(int)e", body);
        Assert.DoesNotContain("=> e,", body);
        AssertCompiles(
            "public static int M(int value, CfgPriority e)",
            body,
            "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    [Fact]
    public void ByteEnumSwitchDispatch_CastsEnumArmAtIntSinks()
    {
        // Slice-4 cross-check review (Opus 4.8): a byte-backed enum flowing
        // into an int-typed switch dispatch — the raised switch-expression arm
        // (or the statement form's int store) must cast, never render `e`
        // bare (CS0029/CS0266 while graded Full). The widening enum→int cast
        // is value-preserving; the sink rule now accepts same-family widening
        // rather than demanding the exact underlying type.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.SwitchEnumOrInt));

        Assert.Contains("(int)e", body);
        Assert.DoesNotContain("=> e,", body);
        Assert.DoesNotContain("= e;", body);
        AssertCompiles(
            "public static int M(int k, CfgTiny e, int x)",
            body,
            "public enum CfgTiny : byte { A = 1, B = 2 }");
    }

    [Fact]
    public void ByteEnumIntJoin_KeepsIntSemantics_NoNarrowing()
    {
        // Slice-4 adversarial review (GPT-5.5, blocking): the byte-enum/int
        // join must NOT be typed as the enum — `(CfgTiny)x` would narrow 300
        // to 44 and flip the boxed type. The join stays integer-typed and the
        // int path renders uncast.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.ByteEnumOrIntBox));

        Assert.DoesNotContain("(CfgTiny)x", body);
        Assert.DoesNotContain("(CfgTiny)(x)", body);
        AssertCompiles(
            "public static object M(bool c, CfgTiny e, int x)",
            body,
            "public enum CfgTiny : byte { A = 1, B = 2 }");
    }

    [Fact]
    public void IntEnumJoinThroughSlot_RendersLegalIntSinks()
    {
        // The sound half (exact underlying match): the enum-typed join is a
        // pure reinterpretation, and the slot renders legally at int sinks —
        // the Gemini finding's CS0266 shape, made valid by width discipline.
        string body = RenderRaisedFixture(nameof(EnumCastSamples.IntEnumJoinThroughSlot));

        AssertCompiles(
            "public static int M(bool c, CfgPriority e)",
            body,
            "public enum CfgPriority { Low, Medium = 1, High = 2, Critical = 3 }");
    }

    static string RenderFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(EnumCastSamples).FullName!, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    // As RenderFixture, but for a body that is only Partial at import (e.g. a slot
    // whose int/enum join the importer cannot type) and is raised to valid C# by
    // the pipeline — so it skips the import-time Full precondition.
    static string RenderRaisedFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(EnumCastSamples).FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static string RenderSyntheticSwitchExpression(
        TypeRef returnType,
        IrExpression firstArm,
        IrExpression defaultArm,
        IReadOnlyDictionary<TypeRef, TypeShape>? typeShapes = null,
        IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>>? enumMembers = null)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "value", intType),
            [
                new SwitchExpressionArm(ImmutableArray.Create(0), isDefault: false, firstArm),
                new SwitchExpressionArm(ImmutableArray<int>.Empty, isDefault: true, defaultArm),
            ]);
        var block = new Block(0);
        block.Add(new Return(switchExpression));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [new Parameter("value", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = typeShapes ?? new Dictionary<TypeRef, TypeShape>(),
            EnumMembers = enumMembers ?? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>(),
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderUnknownEnumReturnConstant(int value)
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderKnownEnumReturnConstant(int value, TypeRef underlying)
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, intType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [enumType] = underlying },
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderUnknownEnumSwitchLabel(int value)
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Switch(
            new LoadArgument(0, "value", enumType),
            [
                new SwitchSection(ImmutableArray.Create(new Constant(value, intType)), isDefault: false, SingleReturnContainer()),
                new SwitchSection(ImmutableArray<Constant>.Empty, isDefault: true, SingleReturnContainer()),
            ]));
        body.Add(block);
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("value", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderUnknownEnumSwitchExpressionLabel(int value)
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "value", enumType),
            [
                new SwitchExpressionArm(ImmutableArray.Create(value), isDefault: false, new Constant(1, intType)),
                new SwitchExpressionArm(ImmutableArray<int>.Empty, isDefault: true, new Constant(0, intType)),
            ]);
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(switchExpression));
        body.Add(block);
        var signature = new MethodSignature(
            intType,
            [new Parameter("value", enumType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderIntegerNullableCoalesceIntoUnknownEnum()
    {
        var enumType = TypeRef.Definition("other", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            ImmutableArray.Create(intType));
        var coalesce = new Coalesce(new LoadArgument(0, "value", nullableInt), new Constant(4, intType));
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(coalesce));
        body.Add(block);
        var signature = new MethodSignature(
            enumType,
            [new Parameter("value", nullableInt)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], body);

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static BlockContainer SingleReturnContainer()
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(null));
        container.Add(block);
        return container;
    }

    static void AssertCompiles(string header, string body, string extraDeclarations = "")
    {
        var errors = Recompile(header, body, extraDeclarations)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body, string extraDeclarations)
    {
        string source = $$"""
            using System;
            {{extraDeclarations}}
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;
}
