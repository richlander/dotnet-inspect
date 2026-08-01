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
    /// The gate for the no-seam boundary. Three shipped output paths print with no
    /// import seam — <c>CSharpBodyDiff</c> and two <c>ResearchViews</c> lenses — and
    /// there NOTHING can be raised, so every synthesized local-function call is
    /// declined and must say so. Leaving them unstamped reproduces #3631 verbatim in
    /// those paths: a decoded name declared nowhere, reported <c>Full</c>.
    /// </summary>
    [Fact]
    public void WithoutTheCrossMethodSeam_EveryCallIsStampedAndFidelityDegrades()
    {
        var type = typeof(UnraisedLocalFunctionSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(UnraisedLocalFunctionSamples.CallsRaisedIf));
        Assert.NotNull(function);

        // CallsRaisedIf is the close negative: WITH the seam it raises to `F(...)` at
        // Full (see RaisedLocalFunction_KeepsItsSourceSpellingAndFullFidelity), so any
        // honesty here is owed to the missing seam and nothing else.
        IrPasses.Run(function!);

        var calls = function!.Descendants.OfType<Call>()
            .Where(call => GeneratedCodeIdentity.IsSynthesizedLocalFunctionName(call.Callee.Name))
            .ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.True(call.Callee.LocalFunctionRaiseDeclined));
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);

        var result = CSharpPrinter.PrintRaised(function!);
        Assert.True(result.Succeeded);
        Assert.DoesNotContain("return F(", result.Output!);
        Assert.Contains("_g__F_", result.Output!);
    }

    /// <summary>
    /// The gate for the sharpest failure mode: two local functions in disjoint scopes
    /// may share one source name, and when the pass raises one and declines the other,
    /// spelling the declined call by its source name binds it to the WRONG function —
    /// output that compiles and silently means something else, which is worse than the
    /// undeclared name #3631 reported. The stamp is therefore keyed on whether THIS
    /// call survived the pass, never on whether some declaration of the decoded name
    /// was emitted.
    /// </summary>
    [Fact]
    public void DeclinedLocalFunction_IsNotSpelledAsARaisedSiblingOfTheSameName()
    {
        var type = typeof(DuplicateLocalFunctionNameSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(DuplicateLocalFunctionNameSamples.PickOne));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!.ReplaceLineEndings("\n");

        // Exactly one Pick is raised, so exactly one declaration exists...
        Assert.Equal(1, CountOccurrences(output, "static int Pick("));
        // ...and the declined sibling must carry its own identity, not borrow that one.
        Assert.Contains("__PickOne_g__Pick_0_1", output);
        Assert.Equal(DecompilationFidelity.Partial, function!.Fidelity);
    }

    /// <summary>
    /// The gate for the node the sweep would miss if it only walked calls. A local
    /// function converted to a delegate lowers to <c>ldftn</c>, which imports as
    /// <c>DelegateCreation</c> and carries no <c>Call</c>, so raising never considers
    /// it and no declaration is emitted — yet decoding its name spells
    /// <c>...Samples.F</c>, a member that does not exist (CS0117), at <c>Full</c>.
    /// </summary>
    [Fact]
    public void LocalFunctionUsedAsAMethodGroup_IsSpelledHonestlyAndDegradesFidelity()
    {
        var type = typeof(LocalFunctionMethodGroupSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(LocalFunctionMethodGroupSamples.UsesMethodGroup));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.DoesNotContain($"{nameof(LocalFunctionMethodGroupSamples)}.F", output);
        Assert.Contains("_g__F_", output);
        // Only the delegate type's own generic arguments may carry angle brackets;
        // the method-group name must not leak raw metadata decoration.
        Assert.DoesNotContain("<UsesMethodGroup>", output);
        Assert.DoesNotContain('|', output);
        Assert.Equal(DecompilationFidelity.Partial, function!.Fidelity);
        Assert.Contains(
            FidelityRemarks.CollectCauses(function),
            cause => cause.Discriminator == DecompilerFidelityDiscriminators.LocalFunctionMethodName);
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
