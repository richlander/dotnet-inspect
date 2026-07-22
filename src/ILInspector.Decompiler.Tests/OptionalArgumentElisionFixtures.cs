namespace ILInspector.Decompiler.Tests;

using System.Runtime.CompilerServices;

// Fixtures for OptionalArgumentElisionPass. Each caller below is what the tests
// decompile; the callees declare the C# optional parameters whose baked defaults
// the pass tries to elide from the call site.
public class OptionalArgumentElisionFixtures
{
    // Single overload, reference-type null default: the canonical elide case.
    static int Speak(int count, string? note = null) => count + (note?.Length ?? 0);

    // Single overload, enum default (mirrors the Humanizer ToWords witness).
    static int Announce(int count, System.DayOfWeek day = System.DayOfWeek.Sunday) => count + (int)day;

    // Overload pair whose shorter member ties the callee's leading signature, so
    // eliding verbose would rebind Log(message, false) to the distinct Log(message).
    static int Log(string message) => message.Length;
    static int Log(string message, bool verbose = false) => verbose ? 1 : message.Length;

    // Three defaulted parameters, to exercise the trailing-run boundary.
    static int Triple(int a = 1, int b = 2, int c = 3) => a + b + c;

    // Reference-subtype hazard: Feed(Dog) would win a shortened Feed((Animal)d),
    // so the pass-time identity check must keep the explicit trailing argument.
    class Animal { }
    sealed class Dog : Animal { }
    static int Feed(Animal a, int portion = 1) => portion;
    static int Feed(Dog d) => 0;

    // Instance method (HasThis) with a reference-type null default: the common
    // real call shape, exercising the receiver-offset path in the pass.
    string Greet(string name, string? title = null) => title ?? name;

    // Receiver-subtype steal hazard: the optional method is on the base, and the
    // derived receiver declares a shorter same-named overload that captures the
    // shortened call. r.Emit("x", false) binds Reporter.Emit(string, bool) but
    // r.Emit("x") would rebind to LoudReporter.Emit(string) — a different method.
    // The importer's sibling scan only sees the callee's declaring type (Reporter),
    // so the pass-time receiver-static-type guard must keep the explicit argument.
    class Reporter { public virtual string Emit(string message, bool loud = false) => loud ? message : ""; }
    sealed class LoudReporter : Reporter { public string Emit(string message) => message + "!"; }

    // Generic-sibling steal hazard: Pick(5, 0) binds the non-generic
    // Pick(int, int = 0), but eliding to Pick(5) would rebind to the generic
    // Pick<int>(int) — the "fewer declared parameters" tie-break beats the
    // optional-using candidate. Any same-named generic sibling declines elision.
    static int Pick(int value, int seed = 0) => value + seed;
    static int Pick<T>(T value) => value?.GetHashCode() ?? 0;

    // OverloadResolutionPriority steal hazard: Rank(5, 0) binds Rank(int, int=0),
    // but the attribute deprioritizes it, so eliding to Rank(5) would rebind to
    // the differently-typed Rank(long) that AritySafe's leading-signature check
    // ignores. Any candidate carrying the attribute declines the whole callee.
    [OverloadResolutionPriority(-1)]
    static string Rank(int value, int seed = 0) => $"int:{value + seed}";
    static string Rank(long value) => $"long:{value}";

    // --- Callers under test ---

    public int CallSpeakElidesNull() => Speak(3);
    public int CallAnnounceElidesEnum() => Announce(3);
    public string CallGreetElidesNull() => Greet("Ada");
    public int CallLogKeepsSiblingTie() => Log("x", false);
    public int CallTripleTrailingRun() => Triple(1, 9, 3);
    public int CallFeedKeepsSubtype()
    {
        Dog d = new Dog();
        return Feed(d, 1);
    }
    public string[] CallSplitCrossAssembly(string s) => s.Split(';');

    public string CallReporterKeepsDerivedSteal()
    {
        LoudReporter r = new LoudReporter();
        return r.Emit("x", false);
    }

    // A lone extension on a cross-assembly receiver (string): the witness shape
    // (ToWords is itself a static extension). No competing same-named extension
    // exists, so the trailing null default drops in receiver syntax.
    public string CallExtensionElidesTrailingNull(string s) => s.WithTag(2);

    // Cross-class extension steal: eliding the trailing null would rebind
    // s.Pin(2, null) to StealerExtensions.Pin(string, int). The assembly-wide
    // extension scan finds the tie and keeps the explicit argument.
    public string CallExtensionKeepsCrossClassSteal(string s) => s.Pin(2, null);

    // Generic sibling present on the declaring type: the explicit trailing 0 stays.
    public int CallPickKeepsGenericSibling() => Pick(5, 0);

    // Priority-deprioritized callee: the explicit trailing 0 stays because eliding
    // would rebind to the differently-typed Rank(long).
    public string CallRankKeepsPriorityOverload() => Rank(5, 0);
}

// --- Extension-method fixtures (assembly-wide overload scan + receiver guard) ---
// Extension methods must live in top-level, non-generic static classes, so these
// sit beside the fixture class rather than nested inside it.

internal static class TagExtensions
{
    public static string WithTag(this string value, int weight, string? tag = null)
        => tag is null ? $"{value}:{weight}" : $"{value}:{weight}:{tag}";
}

// The intended callee: a trailing optional after one real parameter.
internal static class IntendedExtensions
{
    public static string Pin(this string value, int weight, string? tag = null)
        => tag is null ? $"{value}#{weight}" : $"{value}#{weight}#{tag}";
}

// The thief: a shorter same-named extension on the same receiver that ties the
// intended callee's leading signature at the shortened arity, in a different
// class the old declaring-type-only scan never saw.
internal static class StealerExtensions
{
    public static string Pin(this string value, int weight) => $"{value}!{weight}";
}
