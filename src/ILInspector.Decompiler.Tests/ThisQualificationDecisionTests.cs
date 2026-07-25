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
}
