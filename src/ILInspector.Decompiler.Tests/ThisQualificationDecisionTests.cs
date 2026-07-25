using System.Linq;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The recorded evidence for the opt-in <c>this.</c>-qualification knobs
/// (<see cref="PrinterOptions.QualifyFieldAccess"/> and friends). These are
/// class-3 spelling choices: byte-preserving (the bare and qualified spellings
/// emit identical IL), so when a knob adds <c>this.</c> the printer records a
/// <see cref="DecompilerDecisionCategories.Taste"/> decision the Applied Taste
/// surface can report. The default (knob-off) render records nothing, and a
/// mandatory shadow-disambiguation <c>this.</c> — one that would appear with the
/// knob off too — is never attributed to the knob.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class ThisQualificationDecisionTests
{
    static string AssemblyPath => typeof(ThisQualificationDecisionTests).Assembly.Location;

    static DecompilerResult Decompile(string typeName, string method, PrinterOptions? options = null)
    {
        using var source = MetadataSource.Open(AssemblyPath);
        var function = IrImporter.Import(source, typeName, method);
        Assert.NotNull(function);
        return CSharpPrinter.PrintRaised(
            function!,
            importMethodBody: methodRef => IrImporter.Import(source, methodRef),
            options,
            source.AreProvablyDisjoint);
    }

    static DecompilerResult Decompile(string method, PrinterOptions? options = null)
        => Decompile(typeof(ThisQualificationSpecimen).FullName!, method, options);

    [Fact]
    public void FieldQualification_WhenKnobSet_RecordsBytePreservingDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.ReadField),
            new PrinterOptions { QualifyFieldAccess = true });

        var decision = Assert.Single(result.Decisions, d => d.RuleId == "qualify-field-access");
        Assert.Equal(DecompilerDecisionCategories.Taste, decision.Category);
        Assert.Equal("_value", decision.OldValue);
        Assert.Equal("this._value", decision.NewValue);
    }

    [Fact]
    public void FieldQualification_Default_RecordsNoDecision()
    {
        var result = Decompile(nameof(ThisQualificationSpecimen.ReadField));

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    [Fact]
    public void FieldQualification_RepeatedAccess_RecordsSingleDedupedDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.SumFieldTwice),
            new PrinterOptions { QualifyFieldAccess = true });

        Assert.Single(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    [Fact]
    public void PropertyQualification_WhenKnobSet_RecordsDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.ReadProperty),
            new PrinterOptions { QualifyPropertyAccess = true });

        var decision = Assert.Single(result.Decisions, d => d.RuleId == "qualify-property-access");
        Assert.Equal(DecompilerDecisionCategories.Taste, decision.Category);
    }

    [Fact]
    public void MethodCallQualification_WhenKnobSet_RecordsDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.CallMethod),
            new PrinterOptions { QualifyMethodAccess = true });

        var decision = Assert.Single(result.Decisions, d => d.RuleId == "qualify-method-access");
        Assert.Equal(DecompilerDecisionCategories.Taste, decision.Category);
    }

    [Fact]
    public void MethodGroupQualification_WhenKnobSet_RecordsDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.MethodGroup),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.Single(result.Decisions, d => d.RuleId == "qualify-method-access");
    }

    [Fact]
    public void EventQualification_WhenKnobSet_RecordsDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.Subscribe),
            new PrinterOptions { QualifyEventAccess = true });

        var decision = Assert.Single(result.Decisions, d => d.RuleId == "qualify-event-access");
        Assert.Equal(DecompilerDecisionCategories.Taste, decision.Category);
    }

    [Fact]
    public void EventQualification_Default_RecordsNoDecision()
    {
        var result = Decompile(nameof(ThisQualificationSpecimen.Subscribe));

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-event-access");
    }

    // The design subtlety: a local shadows the field, so this._value is mandatory
    // disambiguation the printer emits regardless of the knob. It must never be
    // attributed to the qualify-field knob as an opt-in taste choice — not by
    // default, and not even when the knob is also enabled.
    [Fact]
    public void MandatoryDisambiguation_Default_RecordsNoDecision()
    {
        var result = Decompile(nameof(ThisQualificationSpecimen.ReadShadowedField));

        Assert.Contains("this._value", result.Output);
        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    [Fact]
    public void MandatoryDisambiguation_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.ReadShadowedField),
            new PrinterOptions { QualifyFieldAccess = true });

        Assert.Contains("this._value", result.Output);
        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    // Two same-named overloads qualified in one body are distinct members, so they
    // must record two distinct decisions — the callee parameter types keep their
    // dedup keys apart rather than collapsing them into a single "Overloaded" row.
    [Fact]
    public void OverloadedMethodCalls_WhenKnobSet_RecordDistinctDecisionsPerOverload()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.CallOverloads),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.Equal(2, result.Decisions.Count(d => d.RuleId == "qualify-method-access"));
    }

    // A local delegate shadows the instance method name, so this.ReadField() is
    // mandatory disambiguation (bare ReadField binds to the delegate). The method
    // sites lacked the shadow guard the field/property sites have; it must not be
    // attributed to the qualify-method knob as an opt-in taste choice.
    [Fact]
    public void MethodShadowedByLocal_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.MethodShadowedByLocal),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-method-access");
    }

    // A captured-this lambda is lifted to a compiler-generated instance method and
    // referenced as a this.-qualified method group over an unspeakable <...>b__N
    // name. That target is never user-authored, so the knob records no taste
    // decision — in particular, no decision whose subject is an unspeakable name.
    [Fact]
    public void SyntheticMethodGroup_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.CapturedThisOnlyLambda),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(
            result.Decisions,
            d => d.RuleId == "qualify-method-access" && d.Subject.IndexOf('<') >= 0);
    }

    // Two overloads whose signatures differ only by generic type argument
    // (List<int> vs List<string>) are still distinct members. The dedup
    // discriminator must be structurally complete: a {Namespace}.{Name} key
    // renders both parameter types as "System.Collections.Generic.List" and
    // collapses the two into one row, hiding a real taste application.
    [Fact]
    public void GenericOverloadedMethodCalls_WhenKnobSet_RecordDistinctDecisionsPerOverload()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.CallGenericOverloads),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.Equal(2, result.Decisions.Count(d => d.RuleId == "qualify-method-access"));
    }

    // A local function capturing `this` is lifted to a compiler-generated instance
    // method with an unspeakable RAW name (<...>g__Local|N_M). It is not a member
    // and never carries a user `this.`; the knob records nothing. This guards the
    // raw-name check: SourceMethodName strips the <...> to a bare `Local`, so a
    // post-sanitization unspeakable check would wrongly record it.
    [Fact]
    public void CapturingLocalFunction_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            nameof(ThisQualificationSpecimen.CallsCapturingLocalFunction),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-method-access");
    }

    // An explicit interface implementation invoked through this reaches the call
    // site with the interface as its declaring type (cross-type from the
    // implementing class). Bare `FaceMethod()`/`this.FaceMethod()` does not bind —
    // the member requires a cast — so it is never a `this.` opt-in. The cross-type
    // guard records no taste decision.
    [Fact]
    public void ExplicitInterfaceCall_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationExplicitFace).FullName!,
            nameof(ThisQualificationExplicitFace.CallExplicitInterface),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-method-access");
    }

    // A constructed generic self-call ((I<object>)this).M() from within I<T> shares
    // only the DEFINITION with the enclosing type. Bare/this. M() binds to I<T>::M,
    // not I<object>::M, so the qualifier is not byte-preserving. Definition-only
    // equality would wrongly record it; the exact-instantiation guard records
    // nothing.
    [Fact]
    public void ConstructedGenericSelfCall_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            typeof(IThisQualificationGeneric<>).FullName!,
            nameof(IThisQualificationGeneric<object>.CallViaObjectInstantiation),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-method-access");
    }

    // Two overloads differing only by a function pointer's return type
    // (delegate*<int, int> vs delegate*<int, void>) are distinct members. The
    // discriminator must key on the function pointer's return type / calling
    // convention / parameter ref-kinds, not parameters alone, or the two collapse
    // into one row and hide a taste application.
    [Fact]
    public void FunctionPointerReturnTypeOverloads_WhenKnobSet_RecordDistinctDecisions()
    {
        var result = Decompile(
            typeof(ThisQualificationFnPtr).FullName!,
            nameof(ThisQualificationFnPtr.CallBoth),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.Equal(2, result.Decisions.Count(d => d.RuleId == "qualify-method-access"));
    }

    // Inside a static extension method whose first parameter is spelled `@this`
    // (IL name "this"), @this.ReadField() reaches the this-receiver call site, but
    // the enclosing method has no implicit receiver (HasThis is false), so the
    // qualify-method knob records no taste decision.
    [Fact]
    public void StaticExtensionThisParam_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationSpecimenExtensions).FullName!,
            nameof(ThisQualificationSpecimenExtensions.CallThroughThisParam),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-method-access");
    }
    // A derived type's OWN field (declared on the enclosing type at its own
    // instantiation) qualified with this. is a genuine byte-preserving opt-in and
    // records one decision.
    [Fact]
    public void OwnField_WithKnobEnabled_RecordsDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationFieldDerived).FullName!,
            nameof(ThisQualificationFieldDerived.ReadOwnField),
            new PrinterOptions { QualifyFieldAccess = true });

        Assert.Single(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    // A HIDDEN base field read via base.X targets the BASE field, but a pre-existing
    // emit gap mis-spells it this.X. this.X binds to the DERIVED field, so it is not
    // byte-preserving. The exact-instantiation guard records nothing.
    [Fact]
    public void HiddenBaseField_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationFieldDerived).FullName!,
            nameof(ThisQualificationFieldDerived.ReadBaseField),
            new PrinterOptions { QualifyFieldAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    // A merely-inherited (unhidden) base field is safe to record, but the
    // exact-instantiation guard uniformly under-records cross-type members. A
    // false-negative is safe; the important guarantee is no false positive.
    [Fact]
    public void InheritedBaseField_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationFieldDerived).FullName!,
            nameof(ThisQualificationFieldDerived.ReadInheritedField),
            new PrinterOptions { QualifyFieldAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-field-access");
    }

    // M() and M<T>() differ only by arity and share an empty parameter list. The
    // dedup discriminator must fold generic arity in, or the two collapse into one
    // row and hide a taste application. Qualifying both must record two decisions.
    [Fact]
    public void ArityOverloadedMethodCalls_WhenKnobSet_RecordDistinctDecisions()
    {
        var result = Decompile(
            typeof(ThisQualificationArity).FullName!,
            nameof(ThisQualificationArity.CallBothArities),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.Equal(2, result.Decisions.Count(d => d.RuleId == "qualify-method-access"));
    }

    // Two instantiations of the SAME generic method (G<int>, G<string>) are one
    // source member. The discriminator keys on arity, not the specific type
    // arguments, so both collapse into a single row.
    [Fact]
    public void GenericMethodInstantiations_WhenKnobSet_RecordSingleDedupedDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationArity).FullName!,
            nameof(ThisQualificationArity.CallTwoInstantiations),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.Single(result.Decisions, d => d.RuleId == "qualify-method-access");
    }

    // A method GROUP over a generic instance method (this.Make<int>) drops the type
    // argument in the emitted spelling (a pre-existing MethodGroupText gap). The
    // emitted this.Make fails delegate return-type inference (CS0411), so it is not
    // byte-preserving; recording is suppressed for generic method groups.
    [Fact]
    public void GenericMethodGroup_WithKnobEnabled_RecordsNoDecision()
    {
        var result = Decompile(
            typeof(ThisQualificationGenericGroup).FullName!,
            nameof(ThisQualificationGenericGroup.Build),
            new PrinterOptions { QualifyMethodAccess = true });

        Assert.DoesNotContain(result.Decisions, d => d.RuleId == "qualify-method-access");
    }
}

