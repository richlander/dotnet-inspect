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
}
