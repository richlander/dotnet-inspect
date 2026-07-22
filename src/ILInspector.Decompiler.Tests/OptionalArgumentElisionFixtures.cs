namespace ILInspector.Decompiler.Tests;

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
}
