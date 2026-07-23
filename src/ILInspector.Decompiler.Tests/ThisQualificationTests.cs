using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The opt-in <c>this.</c>-qualification knobs
/// (<see cref="PrinterOptions.QualifyFieldAccess"/> /
/// <see cref="PrinterOptions.QualifyPropertyAccess"/>). These are class-3 spelling
/// choices with no IL anchor: <c>this.field</c>/<c>this.Prop</c> emit the same
/// <c>ldarg.0; ldfld</c> / <c>ldarg.0; call get_Prop</c> as the bare name. Off by
/// default — an unshadowed instance member stays bare — so the default render is
/// byte-identical to before the knobs existed.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class ThisQualificationTests
{
    static string AssemblyPath => typeof(ThisQualificationTests).Assembly.Location;

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(ThisQualificationSpecimen).FullName);
    }

    static string Render(string memberName, PrinterOptions? options = null)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);
        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null, printerOptions: options);
        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.NotNull(rendered.Text);
        return rendered.Text!;
    }

    [Fact]
    public void FieldAccess_DefaultsToBareName()
    {
        var text = Render(nameof(ThisQualificationSpecimen.ReadField));
        Assert.Contains("_value", text);
        Assert.DoesNotContain("this._value", text);
    }

    [Fact]
    public void FieldAccess_QualifiesWithThis_WhenRequested()
    {
        var text = Render(nameof(ThisQualificationSpecimen.ReadField),
            new PrinterOptions { QualifyFieldAccess = true });
        Assert.Contains("this._value", text);
    }

    [Fact]
    public void PropertyAccess_DefaultsToBareName()
    {
        var text = Render(nameof(ThisQualificationSpecimen.ReadProperty));
        Assert.Contains("Count", text);
        Assert.DoesNotContain("this.Count", text);
    }

    [Fact]
    public void PropertyAccess_QualifiesWithThis_WhenRequested()
    {
        var text = Render(nameof(ThisQualificationSpecimen.ReadProperty),
            new PrinterOptions { QualifyPropertyAccess = true });
        Assert.Contains("this.Count", text);
    }

    // The two knobs are independent: the field knob must not qualify a property
    // read, and the property knob must not qualify a field read.
    [Fact]
    public void FieldKnob_DoesNotQualifyProperties()
    {
        var text = Render(nameof(ThisQualificationSpecimen.ReadProperty),
            new PrinterOptions { QualifyFieldAccess = true });
        Assert.DoesNotContain("this.Count", text);
    }

    [Fact]
    public void PropertyKnob_DoesNotQualifyFields()
    {
        var text = Render(nameof(ThisQualificationSpecimen.ReadField),
            new PrinterOptions { QualifyPropertyAccess = true });
        Assert.DoesNotContain("this._value", text);
    }
}

// A real compiled type: an unshadowed instance field and instance property, each
// read through `this` by a public method, so the field/property access flows
// through FieldTarget / PropertyTarget with a `LoadArgument{Index:0,Name:"this"}`
// receiver — the exact sites the qualification knobs gate.
public sealed class ThisQualificationSpecimen
{
    int _value;

    public ThisQualificationSpecimen(int seed) => _value = seed;

    public int Count { get; set; }

    public int ReadField() => _value;

    public int ReadProperty() => Count;
}
