namespace ILInspector.Decompiler.Tests;

// Regression fixtures for #2982: a constant assigned in a chain (`a = b = c = false`)
// compiles to `ldc.i4.0; dup; call set_C; dup; call set_B; call set_A` — the shared
// literal is dup'd to each sink. The importer must re-materialize the dup'd constant
// at each bool sink so it renders `A = false;`, not spill it into an int stack slot
// (`int S = 0; A = S;`), which is CS0029 (cannot implicitly convert int to bool).
// Regression + quality fixtures for #2982 / #2994: a value assigned in a chain
// (`a = b = c = v`) compiles to a dup-of-value idiom — the rvalue is evaluated
// once and dup'd to each sink (`ldc.i4.0; dup; call set_C; dup; call set_B; call
// set_A`). ChainedAssignmentPass recomposes that run into `A = B = C = v;`,
// keyed on the shared dup slot. A dup'd constant that is NOT part of a chain is
// re-materialized at its sink so a bool/char/enum literal is recovered (#2982)
// instead of spilling through an int32 slot (CS0029). Genuinely separate
// statements carry no dup slot and must not collapse.
public static class ChainedConstantAssignmentSamples
{
    public static bool A { get; set; }

    public static bool B { get; set; }

    public static bool C { get; set; }

    public static int I { get; set; }

    public static long L { get; set; }

    public static int P { get; set; }

    public static int Q { get; set; }

    public static int R { get; set; }

    public static bool F;

    public static bool G;

    public static bool H;

    static int Compute() => 42;

    // The Dapper Settings.SetDefaults shape: a bool constant chained across
    // static properties. Recomposes to `A = B = C = false;`.
    public static void ChainedBoolFalse() => A = B = C = false;

    // A widening chain: the shared int constant lands in `I` (int) and widens
    // implicitly into `L` (long). Recomposes to `L = I = -1;`.
    public static void ChainedWiden() => L = I = -1;

    // A non-constant chain: the shared call result flows to each static
    // property. Recomposes to `P = Q = R = Compute();`.
    public static void ChainedNonConstant() => P = Q = R = Compute();

    // A bool constant chained across static fields. Recomposes to
    // `F = G = H = true;`.
    public static void ChainedStaticFields() => F = G = H = true;

    // Negative: two independent statements with no dup — must stay two
    // statements, never collapse into a chain.
    public static void SeparateStatements()
    {
        A = false;
        B = false;
    }

    // Negative: a single-target dup'd constant whose value escapes into an
    // argument (`WriteLine(P = 5)`). Not a chain (one sink); the constant is
    // re-materialized to `P = 5; Console.WriteLine(5);`.
    public static void SideEffectValue() => System.Console.WriteLine(P = 5);
}
