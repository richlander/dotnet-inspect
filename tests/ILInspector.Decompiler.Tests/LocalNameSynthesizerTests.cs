using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LocalNameSynthesizerTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Double = TypeRef.CoreLib("System", "Double");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Builder = TypeRef.Definition("Asm", "System.Text", "StringBuilder");

    static IReadOnlySet<string> Taken(params string[] names) => new HashSet<string>(names);

    [Fact]
    public void NamesByType()
    {
        Assert.Equal("text", LocalNameSynthesizer.Synthesize(String, false, Taken()));
        Assert.Equal("flag", LocalNameSynthesizer.Synthesize(Boolean, false, Taken()));
        Assert.Equal("num", LocalNameSynthesizer.Synthesize(Int32, false, Taken()));
        Assert.Equal("value", LocalNameSynthesizer.Synthesize(Double, false, Taken()));
        Assert.Equal("items", LocalNameSynthesizer.Synthesize(TypeRef.SzArray(Int32), false, Taken()));
        // A non-core named type camel-cases its leaf simple name.
        Assert.Equal("stringBuilder", LocalNameSynthesizer.Synthesize(Builder, false, Taken()));
    }

    [Fact]
    public void LoopCounterPrefersIjk()
    {
        Assert.Equal("i", LocalNameSynthesizer.Synthesize(Int32, isLoopCounter: true, Taken()));
        Assert.Equal("j", LocalNameSynthesizer.Synthesize(Int32, isLoopCounter: true, Taken("i")));
        Assert.Equal("k", LocalNameSynthesizer.Synthesize(Int32, isLoopCounter: true, Taken("i", "j")));
    }

    [Fact]
    public void CounterRoleIgnoredForNonIntegerType()
    {
        // The counter role only applies to integers; a string "counter" still
        // names by its type, never i/j/k.
        Assert.Equal("text", LocalNameSynthesizer.Synthesize(String, isLoopCounter: true, Taken()));
    }

    [Fact]
    public void ResolvesCollisionsWithNumericSuffix()
    {
        Assert.Equal("text2", LocalNameSynthesizer.Synthesize(String, false, Taken("text")));
        Assert.Equal("text3", LocalNameSynthesizer.Synthesize(String, false, Taken("text", "text2")));
        // Counters exhaust i..n, then suffix the primary.
        Assert.Equal("i2", LocalNameSynthesizer.Synthesize(Int32, isLoopCounter: true, Taken("i", "j", "k", "l", "m", "n")));
    }

    [Fact]
    public void NoEvidenceFallsBackToNull()
    {
        // Unknown type -> the caller keeps V_index rather than fabricating a name.
        Assert.Null(LocalNameSynthesizer.Synthesize(null, false, Taken()));
        // System.Object camel-cases to the keyword `object`, which is unusable, so
        // it also falls back rather than emitting an escaped or wrong name.
        Assert.Null(LocalNameSynthesizer.Synthesize(Object, false, Taken()));
    }

    [Fact]
    public void NamesThroughRefAndPointerWrappers()
    {
        Assert.Equal("text", LocalNameSynthesizer.Synthesize(TypeRef.ByRef(String), false, Taken()));
        Assert.Equal("num", LocalNameSynthesizer.Synthesize(TypeRef.Pointer(Int32), false, Taken()));
    }
}
