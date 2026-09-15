using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class OptionalArgumentElisionPassTests
{
    // Cross-assembly enum spelling (DayOfWeek, StringSplitOptions) and the
    // cross-assembly String.Split callee need the running-runtime resolver, the
    // same pattern ExtensionMethodCallTests uses.
    static readonly ILInspector.Metadata.IAssemblyReferenceResolver RuntimeResolver =
        TestAssemblyReferenceResolvers.RuntimeAssemblies();

    static string PrintRaised(string methodName)
    {
        using var context = new MetadataContext(RuntimeResolver);
        using var source = MetadataSource.Open(typeof(OptionalArgumentElisionFixtures).Assembly.Location, null, RuntimeResolver, context);
        var function = IrImporter.Import(source, typeof(OptionalArgumentElisionFixtures).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void TrailingNullDefault_IsElided()
    {
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallSpeakElidesNull));

        Assert.Contains("Speak(3)", output);
        Assert.DoesNotContain("null", output);
    }

    [Fact]
    public void TrailingEnumDefault_IsElided()
    {
        // The Humanizer ToWords witness reduced to a fixture: the trailing enum
        // constant equal to the declared default drops.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallAnnounceElidesEnum));

        Assert.Contains("Announce(3)", output);
        Assert.DoesNotContain("DayOfWeek", output);
    }

    [Fact]
    public void InstanceMethodTrailingNullDefault_IsElided()
    {
        // An instance (HasThis) callee: the receiver stays and only the baked
        // trailing null default drops, exercising the receiver-offset path.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallGreetElidesNull));

        Assert.Contains("Greet(\"Ada\")", output);
        Assert.DoesNotContain("null", output);
    }

    [Fact]
    public void SiblingOverloadTiesLeadingSignature_KeepsExplicitArgument()
    {
        // Log(message) has the callee's leading signature at the shorter arity, so
        // eliding verbose would rebind to a different method. The trailing false
        // must stay explicit.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallLogKeepsSiblingTie));

        Assert.Contains(", false)", output);
    }

    [Fact]
    public void NonDefaultArgumentStopsTrailingRun()
    {
        // Triple(1, 9, 3): c==3 matches its default and drops, but b==9 breaks the
        // run, so a==1 must stay even though it also equals its default (Triple(9)
        // would bind 9 to a).
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallTripleTrailingRun));

        Assert.Contains("Triple(1, 9)", output);
        Assert.DoesNotContain("Triple(1, 9, 3)", output);
        Assert.DoesNotContain("Triple(9)", output);
    }

    [Fact]
    public void RetainedSubtypeArgument_KeepsExplicitArgument()
    {
        // Feed(Animal, int=1) is called with a Dog. Eliding to Feed(d) would rebind
        // to the better-matching Feed(Dog) overload, so the identity check keeps
        // the explicit trailing 1.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallFeedKeepsSubtype));

        Assert.Contains(", 1)", output);
    }

    [Fact]
    public void CrossAssemblyCallee_IsNotElided()
    {
        // String.Split(char, StringSplitOptions.None) is cross-assembly; v1 only
        // stamps same-assembly MethodDef callees, so the baked default stays.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallSplitCrossAssembly));

        Assert.Contains("StringSplitOptions", output);
    }

    [Fact]
    public void DerivedReceiverStealsShortenedCall_KeepsExplicitArgument()
    {
        // The optional method is on the base (Reporter.Emit(string, bool = false)),
        // but a shorter LoudReporter.Emit(string) would capture Emit("x") through
        // the derived receiver. The receiver-static-type guard keeps false explicit.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallReporterKeepsDerivedSteal));

        Assert.Contains(", false)", output);
    }

    [Fact]
    public void LoneExtensionTrailingNullDefault_IsElided()
    {
        // A static extension callee (like the ToWords witness) with no competing
        // same-named extension in the assembly: the trailing null default drops
        // while the receiver and the real argument stay.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallExtensionElidesTrailingNull));

        Assert.Contains("WithTag", output);
        Assert.DoesNotContain("null", output);
    }

    [Fact]
    public void CrossClassExtensionStealsShortenedCall_KeepsExplicitArgument()
    {
        // IntendedExtensions.Pin(string, int, string = null) is the callee, but a
        // shorter StealerExtensions.Pin(string, int) in a different class ties it
        // at arity 2. The assembly-wide extension scan (not just the declaring
        // class) must see the tie and keep the explicit trailing null.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallExtensionKeepsCrossClassSteal));

        Assert.Contains("null", output);
    }

    [Fact]
    public void GenericSiblingOverload_KeepsExplicitArgument()
    {
        // Pick(int, int = 0) has a generic sibling Pick<T>(T). Eliding to Pick(5)
        // would rebind to Pick<int>(int), so any same-named generic sibling
        // declines elision and the explicit trailing 0 stays.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallPickKeepsGenericSibling));

        Assert.Contains(", 0)", output);
    }

    [Fact]
    public void OverloadResolutionPrioritySibling_KeepsExplicitArgument()
    {
        // Rank(int, int = 0) carries [OverloadResolutionPriority(-1)]; eliding to
        // Rank(5) would rebind to Rank(long) because priority reorders candidates
        // ahead of betterness. Any candidate carrying the attribute declines, so
        // the explicit trailing 0 stays.
        string output = PrintRaised(nameof(OptionalArgumentElisionFixtures.CallRankKeepsPriorityOverload));

        Assert.Contains(", 0)", output);
    }
}
