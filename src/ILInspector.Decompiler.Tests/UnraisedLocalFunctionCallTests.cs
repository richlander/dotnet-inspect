using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// A local-function call site must not be spelled with its source name unless the
/// local function was actually raised into a declaration (#3631).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LocalFunctionRaisingPass"/> declines bodies its <c>IsPrintableBody</c>
/// gate rejects — a <c>try</c>, <c>for</c>, or <c>foreach</c> body among them. When it
/// declines it correctly emits no declaration, but the host body still holds a plain
/// <see cref="Call"/> to the synthesized <c>&lt;Enclosing&gt;g__Name|N_M</c> method.
/// Decoding that to the bare source spelling produced a call to a method declared
/// nowhere in the output: invalid C# (CS0103) that read as ordinary recovered source,
/// with no diagnostic and no fidelity downgrade at any verbosity.
/// </para>
/// <para>
/// The discriminator is the pass's own stamp, not the shape of the name. Before
/// <see cref="LocalFunctionRaisingPass"/> runs, a local function that WILL be raised
/// carries an identical <c>&gt;g__</c> name, so judging by the name alone degrades
/// methods whose final output is perfectly valid — the real-world corpus sensor
/// measured exactly that. Instead the pass, the only component that knows, stamps
/// <see cref="MethodRef.LocalFunctionRaiseDeclined"/> on every call it considered and
/// left undeclared; the printer and fidelity both read that stamp. Hence the two
/// halves — the spelling must show the compiler-generated identity, and the fidelity
/// must degrade — the raised control must keep both its source spelling and
/// <see cref="DecompilationFidelity.Full"/>, and the no-seam case must be left alone.
/// </para>
/// </remarks>
[Trait("Area", "Pass")]
public class UnraisedLocalFunctionCallTests
{
    static (string Output, IrFunction Function) Raise(string methodName)
    {
        var type = typeof(UnraisedLocalFunctionSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return (result.Output!.ReplaceLineEndings("\n").Trim(), function!);
    }

    [Theory]
    [InlineData(nameof(UnraisedLocalFunctionSamples.CallsUnraisedTry))]
    [InlineData(nameof(UnraisedLocalFunctionSamples.CallsUnraisedForeach))]
    public void DeclinedLocalFunction_DoesNotCallAnUndeclaredSourceName(string methodName)
    {
        var (output, _) = Raise(methodName);

        // The pass declined, so no declaration is emitted...
        Assert.DoesNotContain("static int F(", output);
        // ...and therefore the call must not claim the source spelling either.
        Assert.DoesNotContain("F(", output);
        Assert.Contains($"__{methodName}_g__F_", output);
        // The sanitized fallback never leaks raw metadata decoration.
        Assert.DoesNotContain('<', output);
        Assert.DoesNotContain('|', output);
    }

    [Theory]
    [InlineData(nameof(UnraisedLocalFunctionSamples.CallsUnraisedTry))]
    [InlineData(nameof(UnraisedLocalFunctionSamples.CallsUnraisedForeach))]
    public void DeclinedLocalFunction_DegradesFidelityWithTheLocalFunctionDiscriminator(string methodName)
    {
        var (_, function) = Raise(methodName);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        // Assert the stable semantic facet, not the diagnostic id: the discriminator is
        // what policy consumers key on, and it is what distinguishes an unraised local
        // function from any other unspellable method name.
        Assert.Contains(
            FidelityRemarks.CollectCauses(function),
            cause => cause.Discriminator == DecompilerFidelityDiscriminators.LocalFunctionMethodName);
    }

    [Fact]
    public void RaisedLocalFunction_KeepsItsSourceSpellingAndFullFidelity()
    {
        var (output, function) = Raise(nameof(UnraisedLocalFunctionSamples.CallsRaisedIf));

        // The close negative for both halves above: this body *is* raised, so the
        // declaration is emitted, the call keeps `F`, and nothing degrades.
        Assert.Contains("static int F(", output);
        Assert.Contains("return F(", output);
        Assert.DoesNotContain("_g__F_", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    /// <summary>
    /// The gate for the boundary that keeps the stamp meaningful: without the
    /// cross-method seam <see cref="LocalFunctionRaisingPass"/> is a documented no-op,
    /// so it never considers a call and must stamp nothing. Were it to stamp here, it
    /// would report "declined" for calls it never looked at — which is what the
    /// real-world corpus sensor (it runs the pass with <see cref="PassContext.None"/>)
    /// detected as a fidelity regression across methods whose product output is valid.
    /// </summary>
    [Fact]
    public void WithoutTheCrossMethodSeam_NoCallIsStampedAndFidelityIsUnchanged()
    {
        var type = typeof(UnraisedLocalFunctionSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(UnraisedLocalFunctionSamples.CallsUnraisedTry));
        Assert.NotNull(function);

        // The default corpus-sensor configuration: passes run, no import seam.
        IrPasses.Run(function!);

        var calls = function!.Descendants.OfType<Call>()
            .Where(call => GeneratedCodeIdentity.IsSynthesizedLocalFunctionName(call.Callee.Name))
            .ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.False(call.Callee.LocalFunctionRaiseDeclined));
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}
