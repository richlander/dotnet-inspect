using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The opt-in <c>this.</c>-qualification knobs
/// (<see cref="PrinterOptions.QualifyFieldAccess"/> /
/// <see cref="PrinterOptions.QualifyPropertyAccess"/> /
/// <see cref="PrinterOptions.QualifyMethodAccess"/> /
/// <see cref="PrinterOptions.QualifyEventAccess"/>). These are class-3 spelling
/// choices with no IL anchor: <c>this.field</c>/<c>this.Prop</c>/<c>this.M()</c>/
/// <c>this.E += h</c> emit the same <c>ldarg.0; ...</c> sequence as the bare name.
/// Off by default — an unshadowed instance member stays bare — so the default
/// render is byte-identical to before the knobs existed. A genuine
/// <c>base.M()</c> call is never rewritten (that would re-enable virtual dispatch).
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

    static string RenderMember(System.Type declaringType, string memberName, PrinterOptions? options = null)
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == declaringType.FullName);
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

    // A knob that changes rendered output must also be recorded in the product
    // evidence (DecompilerResult.EffectiveOptions), matching ReadableLocalNames /
    // WrapSplittableExpressions — otherwise a host cannot tell an on render from an
    // off one without reverse-engineering the text.
    static DecompilerResult PrintSynthetic(PrinterOptions options)
    {
        var holder = TypeRef.Definition("synthetic", "", "Holder");
        var int32 = TypeRef.CoreLib("System", "Int32");
        var block = new Block(0);
        block.Add(new Return(new LoadArgument(0, "value", int32)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(int32, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", holder, signature, [], container);
        return CSharpPrinter.Print(function, options);
    }

    [Fact]
    public void EffectiveOptions_RecordsFieldKnob()
    {
        Assert.True(PrintSynthetic(new PrinterOptions { QualifyFieldAccess = true }).EffectiveOptions.QualifyFieldAccess);
        Assert.False(PrintSynthetic(PrinterOptions.Default).EffectiveOptions.QualifyFieldAccess);
    }

    [Fact]
    public void EffectiveOptions_RecordsPropertyKnob()
    {
        Assert.True(PrintSynthetic(new PrinterOptions { QualifyPropertyAccess = true }).EffectiveOptions.QualifyPropertyAccess);
        Assert.False(PrintSynthetic(PrinterOptions.Default).EffectiveOptions.QualifyPropertyAccess);
    }

    [Fact]
    public void MethodCall_DefaultsToBareName()
    {
        var text = Render(nameof(ThisQualificationSpecimen.CallMethod));
        Assert.Contains("ReadField()", text);
        Assert.DoesNotContain("this.ReadField()", text);
    }

    [Fact]
    public void MethodCall_QualifiesWithThis_WhenRequested()
    {
        var text = Render(nameof(ThisQualificationSpecimen.CallMethod),
            new PrinterOptions { QualifyMethodAccess = true });
        Assert.Contains("this.ReadField()", text);
    }

    [Fact]
    public void MethodGroup_DefaultsToBareName()
    {
        var text = Render(nameof(ThisQualificationSpecimen.MethodGroup));
        Assert.Contains("ReadField", text);
        Assert.DoesNotContain("this.ReadField", text);
    }

    [Fact]
    public void MethodGroup_QualifiesWithThis_WhenRequested()
    {
        var text = Render(nameof(ThisQualificationSpecimen.MethodGroup),
            new PrinterOptions { QualifyMethodAccess = true });
        Assert.Contains("this.ReadField", text);
    }

    [Fact]
    public void EventSubscription_DefaultsToBareName()
    {
        var text = Render(nameof(ThisQualificationSpecimen.Subscribe));
        Assert.Contains("Changed +=", text);
        Assert.DoesNotContain("this.Changed", text);
    }

    [Fact]
    public void EventSubscription_QualifiesWithThis_WhenRequested()
    {
        var text = Render(nameof(ThisQualificationSpecimen.Subscribe),
            new PrinterOptions { QualifyEventAccess = true });
        Assert.Contains("this.Changed +=", text);
    }

    // The method and event knobs are independent from the field/property knobs
    // and from each other: enabling one must not qualify a member the other
    // governs. (Events and properties in particular share the printer's
    // PropertyTarget helper, so this pins their decoupling.)
    [Fact]
    public void PropertyKnob_DoesNotQualifyEvents()
    {
        var text = Render(nameof(ThisQualificationSpecimen.Subscribe),
            new PrinterOptions { QualifyPropertyAccess = true });
        Assert.DoesNotContain("this.Changed", text);
    }

    [Fact]
    public void EventKnob_DoesNotQualifyMethods()
    {
        var text = Render(nameof(ThisQualificationSpecimen.CallMethod),
            new PrinterOptions { QualifyEventAccess = true });
        Assert.DoesNotContain("this.ReadField", text);
    }

    // A genuine non-virtual base call (base.M()) deliberately skips virtual
    // dispatch; the qualify-method knob must leave it as base.M() and never
    // rewrite it to this.M() (which would re-enable dispatch -- here, unbounded
    // recursion).
    [Fact]
    public void BaseCall_StaysBase_WhenMethodQualificationRequested()
    {
        var text = RenderMember(typeof(ThisQualificationDerived),
            nameof(ThisQualificationDerived.Value),
            new PrinterOptions { QualifyMethodAccess = true });
        Assert.Contains("base.Value()", text);
        Assert.DoesNotContain("this.Value()", text);
    }

    // A method group over base.<virtual method> compiles to a NON-virtual
    // `ldftn Base::M`; rendering it bare or `this.M` rebinds to the derived
    // override with virtual dispatch (ldvirtftn), changing behavior. It must stay
    // `base.M` both by default and under the qualify-method knob.
    [Fact]
    public void BaseMethodGroup_RendersBase_ByDefault()
    {
        var text = RenderMember(typeof(ThisQualificationDerived),
            nameof(ThisQualificationDerived.BaseValueGroup));
        Assert.Contains("base.Value", text);
    }

    [Fact]
    public void BaseMethodGroup_StaysBase_WhenMethodQualificationRequested()
    {
        var text = RenderMember(typeof(ThisQualificationDerived),
            nameof(ThisQualificationDerived.BaseValueGroup),
            new PrinterOptions { QualifyMethodAccess = true });
        Assert.Contains("base.Value", text);
        Assert.DoesNotContain("this.Value", text);
    }

    // A closed static extension method group over this shares the base group's
    // `ldarg.0; ldftn` shape but is NOT base.M: the callee is static and declared
    // on the extension host, so base.Extend is CS0117. It must never enter the
    // base arm -- bare by default, this.Extend under the qualify-method knob.
    [Fact]
    public void ExtensionMethodGroup_NeverRendersBase_ByDefault()
    {
        var text = RenderMember(typeof(ThisQualificationDerived),
            nameof(ThisQualificationDerived.ExtensionGroup));
        Assert.DoesNotContain("base.", text);
    }

    [Fact]
    public void ExtensionMethodGroup_RendersThis_WhenMethodQualificationRequested()
    {
        var text = RenderMember(typeof(ThisQualificationDerived),
            nameof(ThisQualificationDerived.ExtensionGroup),
            new PrinterOptions { QualifyMethodAccess = true });
        Assert.Contains("this.Extend", text);
        Assert.DoesNotContain("base.", text);
    }

    [Fact]
    public void EffectiveOptions_RecordsMethodKnob()
    {
        Assert.True(PrintSynthetic(new PrinterOptions { QualifyMethodAccess = true }).EffectiveOptions.QualifyMethodAccess);
        Assert.False(PrintSynthetic(PrinterOptions.Default).EffectiveOptions.QualifyMethodAccess);
    }

    [Fact]
    public void EffectiveOptions_RecordsEventKnob()
    {
        Assert.True(PrintSynthetic(new PrinterOptions { QualifyEventAccess = true }).EffectiveOptions.QualifyEventAccess);
        Assert.False(PrintSynthetic(PrinterOptions.Default).EffectiveOptions.QualifyEventAccess);
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

    // Reads the field twice: with the qualify-field knob on, both accesses emit
    // this._value, but AddDecision dedups them into a single recorded decision.
    public int SumFieldTwice() => _value + _value;

    // A parameter shadows the field, so the bare name binds to the parameter and
    // reaching the field REQUIRES this._value. That this. is mandatory
    // disambiguation, not the qualify-field knob, so it records no taste decision
    // even when the knob is enabled.
    public int ReadShadowedField(int _value) => this._value + _value;

    public int ReadProperty() => Count;

    // Instance method call on the implicit this receiver.
    public int CallMethod() => ReadField() + 1;

    // Two overloads reachable through the implicit this receiver. Qualifying both
    // must record TWO distinct decisions: their shared display name alone must not
    // dedup them into one row (the callee parameter types disambiguate the key).
    public void Overloaded(int x) { }
    public void Overloaded(string x) { }
    public void CallOverloads()
    {
        this.Overloaded(1);
        this.Overloaded("a");
    }

    // Two overloads whose signatures differ ONLY by generic type argument
    // (List<int> vs List<string>). The dedup discriminator must distinguish them
    // structurally: a name-only or {Namespace}.{Name} key renders both parameter
    // types as the same "System.Collections.Generic.List", collapsing two genuine
    // taste applications into one row and HIDING one. Qualifying both must record
    // TWO distinct decisions.
    public void GenericOverloaded(System.Collections.Generic.List<int> x) { }
    public void GenericOverloaded(System.Collections.Generic.List<string> x) { }
    public void CallGenericOverloads()
    {
        this.GenericOverloaded(new System.Collections.Generic.List<int>());
        this.GenericOverloaded(new System.Collections.Generic.List<string>());
    }

    // A local function that captures `this` (reads _value) is lifted to a
    // compiler-generated instance method whose RAW metadata name is
    // <CallsCapturingLocalFunction>g__Local|N_M — unspeakable, and never a member
    // you can write `this.` in front of. The knob must record nothing. The
    // unspeakable check must run against the raw name: CSharpNaming.SourceMethodName
    // strips the <...> to a bare `Local`, which would slip past a post-sanitization
    // check.
    public int CallsCapturingLocalFunction()
    {
        int Local() => _value + 1;
        return Local();
    }

    // A local delegate shadows an instance method name, so the bare call binds to
    // the delegate and reaching the method REQUIRES this.ReadField(). That this. is
    // mandatory disambiguation, not the qualify-method knob, so it records no taste
    // decision even when the knob is enabled.
    public int MethodShadowedByLocal()
    {
        System.Func<int> ReadField = () => 3;
        return this.ReadField() + ReadField();
    }

    // A lambda that captures only `this` is lifted to a compiler-generated instance
    // method and referenced as a method group `this.<...>b__N`. That synthetic
    // target is unspeakable (never user-authored), so the qualify-method knob
    // records no taste decision for it.
    public System.Func<int> CapturedThisOnlyLambda() => () => _value + 1;

    // Method group over the implicit this receiver.
    public System.Func<int> MethodGroup() => ReadField;

#pragma warning disable CS0067 // Changed is subscribed to via Subscribe; the fixture never raises it.
    public event System.EventHandler? Changed;
#pragma warning restore CS0067

    // Event subscription (+=) on the implicit this receiver.
    public void Subscribe(System.EventHandler handler) => Changed += handler;
}

// A base/derived pair so a genuine non-virtual base.Value() call is available:
// the qualify-method knob must never rewrite it to this.Value().
public class ThisQualificationBase
{
    public virtual int Value() => 1;
}

public sealed class ThisQualificationDerived : ThisQualificationBase
{
    public override int Value() => base.Value() + 1;

    // A method group over base.Value: csc emits a NON-virtual `ldftn Base::Value`,
    // so it must render `base.Value` (bare or this.Value would rebind to the
    // override with virtual dispatch and change behavior).
    public System.Func<int> BaseValueGroup() => base.Value;

    // A method group over an extension method also emits `ldarg.0; ldftn`, but the
    // callee is static (HasThis == false) and its declaring type is the extension
    // host, not a base type. It must NOT render base.Extend (CS0117 on the base);
    // under the qualify-method knob it stays this.Extend.
    public System.Func<int> ExtensionGroup() => this.Extend;
}

// An extension on ThisQualificationDerived so `this.Extend` forms a closed
// static-method group (ldarg.0; ldftn Extensions::Extend(Derived)).
public static class ThisQualificationExtensions
{
    public static int Extend(this ThisQualificationDerived value) => 42;
}

// The extension's first parameter is spelled `@this`, so its IL parameter name is
// "this" — the same LoadArgument{Index:0, Name:"this"} shape an instance method's
// implicit receiver produces. But this is a STATIC method with no implicit
// receiver: a this.-qualified member access inside it is a compile error, never a
// taste choice, so the qualify-method knob records no decision here.
public static class ThisQualificationSpecimenExtensions
{
    public static int CallThroughThisParam(this ThisQualificationSpecimen @this)
        => @this.ReadField();
}

// An explicit interface implementation: `int IThisQualificationFace.FaceMethod()`
// can ONLY be reached through a cast (((IThisQualificationFace)this).FaceMethod()),
// never through a bare `FaceMethod()` or `this.FaceMethod()` — the member does not
// bind unqualified. csc emits `callvirt IThisQualificationFace::FaceMethod` on the
// this receiver, whose declaring type is the interface (cross-type from the
// implementing class). The qualify-method knob must record no taste decision: a
// cross-type callee is never a `this.` opt-in. (The printer's pre-existing emit
// still mis-spells this as this.FaceMethod(); only the false RECORD is suppressed.)
public interface IThisQualificationFace
{
    int FaceMethod();
}

public sealed class ThisQualificationExplicitFace : IThisQualificationFace
{
    int IThisQualificationFace.FaceMethod() => 7;

    public int CallExplicitInterface() => ((IThisQualificationFace)this).FaceMethod();
}
