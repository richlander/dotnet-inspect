using System.Reflection;
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
/// <see cref="MethodRef.LocalFunctionRaise"/> on every call it considered and
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
        Assert.All(calls, call => Assert.Equal(LocalFunctionRaiseState.Declined, call.Callee.LocalFunctionRaise));
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

    /// <summary>
    /// The gate for the node the sweep sees rather than the one the printer sees.
    /// <c>ldftn</c> over a local function imports as <c>LoadFunctionPointer</c> and only
    /// becomes <c>AddressOfMethod</c> in <c>MethodAddressPass</c>, which runs after this
    /// pass — so stamping only <c>AddressOfMethod</c> stamps nothing, and the address
    /// printed as <c>&amp;F</c> (CS0103) at <c>Full</c>.
    /// </summary>
    [Fact]
    public void AddressOfADeclinedLocalFunction_IsSpelledHonestlyAndDegradesFidelity()
    {
        var type = typeof(LocalFunctionAddressSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(LocalFunctionAddressSamples.TakesAddress));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.DoesNotContain("&F", output);
        Assert.Contains("_g__F_", output);
        Assert.DoesNotContain('|', output);
        Assert.Equal(DecompilationFidelity.Partial, function!.Fidelity);
        Assert.Contains(
            FidelityRemarks.CollectCauses(function),
            cause => cause.Discriminator == DecompilerFidelityDiscriminators.LocalFunctionMethodName);
    }

    /// <summary>
    /// The gate that keeps the stamping sweep complete as the IR grows, and this test
    /// is that gate: it is what fails if a new <see cref="MethodRef"/>-bearing node
    /// appears and nobody classifies it. The sweep is a closed set of node types, so a
    /// node it does not know about silently falls back to decoding the raw name — which
    /// is exactly how the method-group and function-pointer holes reached review.
    ///
    /// <para>Every node carrying a <see cref="MethodRef"/> must be either swept (it can
    /// name a synthesized local function) or listed as unreachable with a reason. A new
    /// node fails here rather than shipping as a silent hole, and a stale entry fails
    /// here too, because the assertion is set equality in both directions.</para>
    /// </summary>
    [Fact]
    public void EveryMethodRefBearingNodeIsEitherSweptOrJustifiablyUnreachable()
    {
        // Swept by LocalFunctionRaisingPass — each has a MarkLocalFunctionRaiseDeclined.
        string[] swept =
        [
            nameof(Call),
            nameof(DelegateCreation),
            nameof(LoadFunctionPointer),
            nameof(AddressOfMethod),
        ];

        // Cannot name a local function, so the sweep does not need to reach them. A
        // local function is never a constructor (`.ctor`), never a property or event
        // accessor (`get_`/`set_`/`add_`/`remove_`), never a user-defined operator
        // (`op_Increment`/`op_Decrement`), and never bindable as `Deconstruct`, which C#
        // resolves only against members and extension methods. None can be `<M>g__F|0_0`.
        //
        // Five reach here through `ImmutableArray<MethodRef>` evidence collections
        // (`ConsumedMemberRefs`/`ConsumedMethods`) rather than a callee property. Those
        // hold the members the pattern consumed — `GetEnumerator`, `MoveNext`, `Current`,
        // `Dispose`, property setters, `<Clone>$` — as typed evidence routed to
        // ReturnToSender (see ConsumedMemberEvidence). They are never spelled as a callee
        // in output, and none can be a local function.
        //
        // The last two reach here only through a CARRIER record, which is why widening
        // the walk to follow carriers was needed to see them at all: `ChainedAssignment`
        // through `ImmutableArray<ChainedAssignmentTarget>`, whose `Accessor` is a
        // property setter, and `PatternSwitchExpressionArm` through `PropertySubpattern`,
        // whose `Accessor` is a property getter (`PropertyName` slices `get_` off it).
        // Both are accessors, so both fall under the accessor rule above.
        string[] unreachable =
        [
            nameof(NewObject),
            nameof(LoadProperty),
            nameof(StoreProperty),
            nameof(NullCoalescingPropertyAssignment),
            nameof(EventSubscription),
            nameof(RecursivePropertyDeclarationPattern),
            nameof(DeconstructionTarget),
            nameof(DeconstructionAssignment),
            nameof(IncrementDecrement),
            nameof(ForeachStatement),
            nameof(UsingStatement),
            nameof(ObjectInitializerExpression),
            nameof(WithExpression),
            nameof(InitializerBlock),
            nameof(ChainedAssignment),
            nameof(PatternSwitchExpressionArm),
        ];

        var actual = typeof(Call).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && typeof(IrNode).IsAssignableFrom(t))
            .Where(t => t.GetProperties(Members).Any(p => MentionsMethodRef(p.PropertyType))
                     || t.GetFields(Members).Any(f => MentionsMethodRef(f.FieldType)))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(swept.Concat(unreachable).ToHashSet(StringComparer.Ordinal), actual);

        // Non-vacuity for the "swept" half: each of those types really does expose the
        // marker, so the list cannot drift into naming a type the sweep ignores.
        Assert.All(swept, name => Assert.NotNull(
            typeof(Call).Assembly.GetTypes().Single(t => t.Name == name)
                .GetMethod("MarkLocalFunctionRaise", BindingFlags.Instance | BindingFlags.NonPublic)));
    }

    const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Whether a member's type can carry a <see cref="MethodRef"/> at all — directly, or
    /// nested inside an array, a generic such as <c>ImmutableArray&lt;MethodRef?&gt;</c>, or a
    /// carrier record reached through either (<c>ImmutableArray&lt;ChainedAssignmentTarget&gt;</c>,
    /// whose <c>Accessor</c> is a <see cref="MethodRef"/>). Matching the type exactly missed
    /// every collection-typed member, which is how five nodes escaped the gate entirely and
    /// let it pass while incomplete; stopping at generic arguments missed the carriers.
    /// </summary>
    static bool MentionsMethodRef(Type type, HashSet<Type>? visited = null)
    {
        if (type == typeof(MethodRef))
            return true;
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return MentionsMethodRef(underlying, visited);

        visited ??= [];
        if (!visited.Add(type))
            return false;

        if (type.IsArray)
            return MentionsMethodRef(type.GetElementType()!, visited);
        if (type.IsGenericType && type.GetGenericArguments().Any(a => MentionsMethodRef(a, visited)))
            return true;

        // Only follow types this assembly owns: a carrier is a small IR-side record, and
        // walking framework types would recurse the whole BCL. Child NODES are excluded
        // because every node is enumerated in its own right — following them would make
        // every node in the tree "mention" a MethodRef through its children.
        if (type.IsPrimitive
            || type == typeof(string)
            || type.Assembly != typeof(MethodRef).Assembly
            || typeof(IrNode).IsAssignableFrom(type))
        {
            return false;
        }

        return type.GetProperties(Members).Any(p => MentionsMethodRef(p.PropertyType, visited))
            || type.GetFields(Members).Any(f => MentionsMethodRef(f.FieldType, visited));
    }

    /// <summary>
    /// The close negative for the method-group gate, and the reason the stamp is a
    /// tri-state rather than a bool. <c>RaiseCalls</c> rewrites only <see cref="Call"/>
    /// nodes, so a method group over a local function survives the raise even when the
    /// declaration IS emitted. Stamping it declined would spell a sanitized name that
    /// exists nowhere and degrade a perfectly faithful method; spelling it
    /// <c>Type.F</c> (the static method-group form) is CS0117, because the declaration
    /// is a local function and not a member of the type. It must be bare <c>F</c>.
    /// </summary>
    [Fact]
    public void MethodGroupOverARaisedLocalFunction_IsSpelledUnqualifiedAndStaysFull()
    {
        var type = typeof(RaisedLocalFunctionMethodGroupSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(RaisedLocalFunctionMethodGroupSamples.CallsAndConverts));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.Contains("static int F(", output);
        Assert.Contains("(F)", output);
        Assert.DoesNotContain($"{nameof(RaisedLocalFunctionMethodGroupSamples)}.F", output);
        Assert.DoesNotContain("_g__F_", output);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
    }

    /// <summary>
    /// A generic local function must be declined, not raised. LocalFunctionStatement
    /// carries no type-parameter list, so raising `Tag&lt;T&gt;` declared `static int Tag()`
    /// and rewrote both call sites to `Tag()`, collapsing the `&lt;int&gt;` and `&lt;string&gt;`
    /// instantiations into one — uncompilable (CS0411) and reported Full.
    /// </summary>
    [Theory]
    [InlineData(nameof(GenericLocalFunctionSamples.TwoInstantiations))]
    [InlineData(nameof(GenericLocalFunctionSamples.CalledAndUsedAsMethodGroup))]
    public void GenericLocalFunction_IsDeclinedAndSpelledHonestly(string methodName)
    {
        var type = typeof(GenericLocalFunctionSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        // No declaration was emitted, so no reference may claim the source spelling.
        Assert.DoesNotContain("int Tag<", output);
        Assert.DoesNotContain("int Tag(", output);
        Assert.Contains("_g__Tag_", output);
        Assert.Equal(DecompilationFidelity.Partial, function!.Fidelity);
    }

    /// <summary>
    /// The raised-set key must distinguish same-named types that render identically.
    /// <c>ToDisplayString</c> omits namespace and assembly, so keying on it let a
    /// reference to one type's local function match a declaration raised from a
    /// different type's same-named one — output that compiles and silently binds to
    /// the wrong method.
    /// </summary>
    [Fact]
    public void Identity_DistinguishesSameNamedTypesThatRenderIdentically()
    {
        var a = TypeRef.Definition("AsmA", "NsA", "Owner");
        var b = TypeRef.Definition("AsmB", "NsB", "Owner");

        // The precondition that makes this a real hazard rather than a hypothetical.
        Assert.Equal(a.ToDisplayString(), b.ToDisplayString());

        Assert.NotEqual(
            LocalFunctionRaisingPass.Identity(MethodRefFor(a)),
            LocalFunctionRaisingPass.Identity(MethodRefFor(b)));

        // Same declaring type must still match, or grouping would split call sites.
        Assert.Equal(
            LocalFunctionRaisingPass.Identity(MethodRefFor(a)),
            LocalFunctionRaisingPass.Identity(MethodRefFor(TypeRef.Definition("AsmA", "NsA", "Owner"))));

        static MethodRef MethodRefFor(TypeRef declaringType) => new(
            declaringType,
            "<M>g__F|0_0",
            TypeRef.CoreLib("System", "Int32"),
            [],
            false);
    }

    /// <summary>
    /// The close negative for the generic decline. A non-generic local function inside a
    /// generic METHOD inherits that method's type parameters, so its call sites carry
    /// non-empty <c>TypeArguments</c> without anything being generic in the source sense.
    /// Declining on that alone regressed real framework code — <c>VectorMath.HypotSingle</c>
    /// lost its raised <c>CoreImpl</c> declaration and fell from Full to Partial.
    /// </summary>
    [Fact]
    public void LocalFunctionInGenericMethod_StillRaisesAndStaysFull()
    {
        var type = typeof(LocalFunctionInGenericMethodSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(LocalFunctionInGenericMethodSamples.Passthrough));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.Contains("Core(value)", output);
        Assert.Contains("static T Core(T v)", output);
        Assert.DoesNotContain("_g__Core_", output);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
    }

    /// <summary>
    /// A local function with its OWN type parameter cannot be raised even when every
    /// call-site type argument is a method generic parameter, which is what judging
    /// genericity from the call site got wrong: `Own&lt;U&gt;(U u)` called as `Own&lt;T&gt;(value)`
    /// inside `M&lt;T&gt;` was declared `static int Own(U u)` with `U` bound to nothing —
    /// CS0246 and CS1503, at Full. The body declares the name; the host does not have it.
    /// </summary>
    [Fact]
    public void LocalFunctionWithItsOwnTypeParameter_IsDeclinedEvenInsideAGenericMethod()
    {
        var type = typeof(OwnGenericInGenericMethodSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source,
            type.FullName!,
            nameof(OwnGenericInGenericMethodSamples.CalledWithHostTypeArgument));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.DoesNotContain("int Own(", output);
        Assert.Contains("_g__Own_", output);
        Assert.Equal(DecompilationFidelity.Partial, function!.Fidelity);
    }

    /// <summary>
    /// A raised local function is spelled with no type arguments. Inside a generic method
    /// its references inherit the host's type arguments in metadata, and
    /// <c>AddressOfMethodText</c> appended them — emitting <c>&amp;Core&lt;T&gt;</c> against the
    /// raised, non-generic declaration <c>static T Core(T v)</c>, which is CS0308, at Full.
    /// Dropping them is sound because a local function that declares its OWN type
    /// parameters is declined, so every inherited argument is already implicit.
    /// </summary>
    [Fact]
    public void AddressOfRaisedLocalFunction_CarriesNoTypeArguments()
    {
        var type = typeof(RaisedLocalFunctionReferenceSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(RaisedLocalFunctionReferenceSamples.ByAddress));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.Contains("&Core", output);
        Assert.DoesNotContain("&Core<", output);
        Assert.Contains("static T Core(T v)", output);
        Assert.DoesNotContain("_g__Core_", output);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
    }

    /// <summary>
    /// The method-group sibling of <see cref="AddressOfRaisedLocalFunction_CarriesNoTypeArguments"/>.
    /// <c>MethodGroupText</c> never appended type arguments, so this pins the property it
    /// already had rather than a fix — the two spellings must not diverge.
    /// </summary>
    [Fact]
    public void MethodGroupOfRaisedLocalFunction_CarriesNoTypeArguments()
    {
        var type = typeof(RaisedLocalFunctionReferenceSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(
            source, type.FullName!, nameof(RaisedLocalFunctionReferenceSamples.ByMethodGroup));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        string output = result.Output!;

        Assert.DoesNotContain("Core<", output);
        Assert.DoesNotContain("_g__Core_", output);
        Assert.Contains("static T Core(T v)", output);
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
