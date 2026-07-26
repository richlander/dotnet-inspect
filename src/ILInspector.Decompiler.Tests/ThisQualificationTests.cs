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

    // A default-interface-member call reached through `this` must render the
    // erased `((I)this)` cast (#3128): the DIM is not a member of the implementing
    // class, so bare `Value()` / `this.Value()` is CS1061. The default (knob-off)
    // render was already invalid before this fix; it is now the faithful spelling
    // that recompiles to `ldarg.0; callvirt IDimFace::Value()`.
    [Fact]
    public void DefaultInterfaceMemberCall_RendersInterfaceCast_ByDefault()
    {
        var text = RenderMember(typeof(DimConsumer), nameof(DimConsumer.DimCall));
        Assert.Contains("((IDimFace)this).Value()", text);
        Assert.DoesNotContain("this.Value()", text);
    }

    // The `((I)this)` cast is a validity fix, not a `this.`-qualification opt-in:
    // it is present with the qualify-method knob off and unchanged with it on (the
    // knob's `this.` arm never runs for an interface-declared callee).
    [Fact]
    public void DefaultInterfaceMemberCall_RendersInterfaceCast_UnderMethodQualification()
    {
        var text = RenderMember(typeof(DimConsumer), nameof(DimConsumer.DimCall),
            new PrinterOptions { QualifyMethodAccess = true });
        Assert.Contains("((IDimFace)this).Value()", text);
        Assert.DoesNotContain("this.Value()", text);
    }

    // A DIM method group over `this` must likewise cast: `((IDimFace)this).Value`
    // recompiles to `ldarg.0; dup; ldvirtftn IDimFace::Value; newobj Func`, where
    // bare `Value` (CS1061) or `this.Value` would not bind.
    [Fact]
    public void DefaultInterfaceMemberGroup_RendersInterfaceCast_ByDefault()
    {
        var text = RenderMember(typeof(DimConsumer), nameof(DimConsumer.DimGroup));
        Assert.Contains("((IDimFace)this).Value", text);
        Assert.DoesNotContain("(Value)", text);
    }

    // An explicit interface implementation invoked through `this` reaches the call
    // site with the interface as declaring type; the member does not bind on the
    // implementing class, so it must render `((I)this).FaceMethod()` — the faithful
    // spelling of the fixture's own source (#3128).
    [Fact]
    public void ExplicitInterfaceCall_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(ThisQualificationExplicitFace),
            nameof(ThisQualificationExplicitFace.CallExplicitInterface));
        Assert.Contains("((IThisQualificationFace)this).FaceMethod()", text);
        Assert.DoesNotContain("this.FaceMethod()", text);
    }

    // The value-type sibling of the #3128 arms (#3201). A struct reaches an
    // interface member through `this` by BOXING — `((IDimSFace)this).Value()`
    // lowers to `ldarg.0; ldobj Struct; box Struct; callvirt IDimSFace::Value()`,
    // so the IR receiver is a Box over the this value, not a bare LoadArgument.
    // The printer must still re-insert the `((I)this)` cast; bare `(this).Value()`
    // is CS1061. Faithful: the cast re-emits exactly the `ldobj; box` the struct
    // boxing produced.
    [Fact]
    public void StructDefaultInterfaceMemberCall_RendersInterfaceCast_ByDefault()
    {
        var text = RenderMember(typeof(StructDimConsumer), nameof(StructDimConsumer.DimCall));
        Assert.Contains("((IDimSFace)this).Value()", text);
        Assert.DoesNotContain("(this).Value()", text);
    }

    // A struct DIM method group over `this` boxes the same way:
    // `((IDimSFace)this).Value` recompiles to `ldarg.0; ldobj S; box S; dup;
    // ldvirtftn IDimSFace::Value; newobj Func`. Bare `(this).Value` (CS1061) or the
    // knob's `this.Value` would not bind.
    [Fact]
    public void StructDefaultInterfaceMemberGroup_RendersInterfaceCast_ByDefault()
    {
        var text = RenderMember(typeof(StructDimConsumer), nameof(StructDimConsumer.DimGroup));
        Assert.Contains("((IDimSFace)this).Value", text);
        Assert.DoesNotContain("(this).Value", text);
    }

    // An explicit interface implementation on a struct invoked through `this`
    // reaches the call site with the interface as declaring type and a boxed
    // receiver; it must render `((IStructFace)this).FaceMethod()` — bare
    // `(this).FaceMethod()` is CS1061 (#3201).
    [Fact]
    public void StructExplicitInterfaceCall_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(StructExplicitFace),
            nameof(StructExplicitFace.CallExplicitInterface));
        Assert.Contains("((IStructFace)this).FaceMethod()", text);
        Assert.DoesNotContain("(this).FaceMethod()", text);
    }

    // A boxed `this` reaching a NON-interface VIRTUAL member (here
    // `object.ToString()` via `((object)this).ToString()`) renders the cast form
    // `((object)this).ToString()`: the explicit `box` is the source upcast, and a
    // virtual `callvirt object::ToString` re-emits from `ldobj; box`. Bare
    // `(this).ToString()` would instead lower to `constrained. callvirt` (no box) —
    // not opcode-faithful — and must never be over-cast to the enclosing struct
    // (#3213, extending #3201 beyond interface callees).
    [Fact]
    public void StructBoxedThis_ObjectVirtualCallee_RendersObjectCast()
    {
        var text = RenderMember(typeof(StructBoxedObjectReceiver),
            nameof(StructBoxedObjectReceiver.CallObjectToString));
        Assert.Contains("((object)this).ToString()", text);
        Assert.DoesNotContain("StructBoxedObjectReceiver)this", text);
        Assert.DoesNotContain("(this).ToString()", text);
    }

    // A boxed `this` reaching a NON-interface NON-VIRTUAL base member renders
    // `base.M()`. A struct overriding `GetHashCode` and calling `base.GetHashCode()`
    // lowers to `ldarg.0; ldobj S; box S; call System.ValueType::GetHashCode; ret`
    // — a non-virtual `call` (not `callvirt`) to a base method. Bare
    // `(this).GetHashCode()` re-dispatches virtually to the struct's own override:
    // infinite self-recursion. `base.GetHashCode()` re-emits exactly the
    // `ldobj; box; call ValueType::GetHashCode` (#3213).
    [Fact]
    public void StructBoxedThis_NonVirtualBaseCallee_RendersBaseCall()
    {
        var text = RenderMember(typeof(StructBaseHashCall),
            nameof(StructBaseHashCall.GetHashCode));
        Assert.Contains("base.GetHashCode()", text);
        Assert.DoesNotContain("(this).GetHashCode()", text);
        Assert.DoesNotContain(")this).GetHashCode()", text);
    }

    // A boxed `this` reaching a virtual member through an explicit base cast renders
    // the cast form, not `base.`. Casting a struct to `System.ValueType` boxes;
    // because `GetHashCode`'s virtual slot is introduced on `System.Object`, csc
    // binds the `callvirt` to `object::GetHashCode`, so the faithful cast is
    // `((object)this).GetHashCode()` (re-emits `ldobj; box; callvirt
    // object::GetHashCode`). Distinguished from `base.M()` by the call being
    // virtual, not by the receiver shape (#3213).
    [Fact]
    public void StructBoxedThis_ExplicitBaseCastVirtualCallee_RendersObjectCast()
    {
        var text = RenderMember(typeof(StructValueTypeCastReceiver),
            nameof(StructValueTypeCastReceiver.CallViaValueTypeCast));
        Assert.Contains("((object)this).GetHashCode()", text);
        Assert.DoesNotContain("base.", text);
        Assert.DoesNotContain("(this).GetHashCode()", text);
    }

    // Negative guard (#3213 review, GPT): a boxed-`this` method group whose callee
    // is a STATIC extension method must NOT be cast to the extension host. A struct
    // forming `((object)this).ExtValue` (or `this.ExtValue`) as a delegate boxes and
    // emits `ldobj; box; ldftn Host::ExtValue(object); newobj`, matching the
    // boxed-this shape — but the callee is static (`method.HasThis` false) and its
    // declaring type is the static host, which is not a cast target: `((Host)this)`
    // is CS0716. It falls through to the ordinary receiver path, spelling the boxed
    // receiver, so the render must show neither the host cast nor `base.`.
    [Fact]
    public void StructBoxedThis_StaticExtensionMethodGroup_IsNotCastToHost()
    {
        var text = RenderMember(typeof(StructExtensionGroupReceiver),
            nameof(StructExtensionGroupReceiver.ExtGroup));
        Assert.Contains("(this).ExtValue", text);
        Assert.DoesNotContain("BoxedThisExtensionHost)this", text);
        Assert.DoesNotContain("base.", text);
    }

    // Negative guard (#3213 review, Gemini): a STATIC method whose first parameter is
    // spelled `@this` emits the metadata name `"this"` at index 0, so a boxed
    // `((object)@this).M()` matches the boxed-this shape — but `base`/`this` are
    // illegal in a static method (CS0026). The boxed-this arm is gated on the
    // enclosing method having an implicit `this`, so it must NOT synthesize
    // `base.GetType()` (non-virtual) or `((object)this).ToString()` (virtual) here;
    // the boxed `@this` parameter falls through to the ordinary receiver path.
    [Fact]
    public void StaticMethodWithThisNamedParameter_BoxedReceiver_DoesNotSynthesizeBaseOrCast()
    {
        var nonVirtual = RenderMember(typeof(StructStaticThisParameter),
            nameof(StructStaticThisParameter.BoxNonVirtual));
        Assert.DoesNotContain("base.", nonVirtual);
        var virtualText = RenderMember(typeof(StructStaticThisParameter),
            nameof(StructStaticThisParameter.BoxVirtual));
        Assert.DoesNotContain("((object)this)", virtualText);
        Assert.DoesNotContain("base.", virtualText);
    }

    // Negative guard (#3213): an implicit `this.ToString()` on a struct that does
    // NOT box does not go through the boxed-this arm. Because the struct does not
    // override `ToString`, csc emits `ldarg.0; constrained. S; callvirt
    // object::ToString()` — a bare `LoadArgument{0,"this"}` receiver with a
    // `constrained.` prefix and NO `box`, so `IsBoxedThisReceiver` declines. The
    // render must not gain a cast or `base.` — the constrained call already binds.
    [Fact]
    public void StructImplicitThis_ConstrainedCall_IsNotBoxedOrBased()
    {
        var text = RenderMember(typeof(StructImplicitToString),
            nameof(StructImplicitToString.CallImplicitToString));
        Assert.DoesNotContain("base.", text);
        Assert.DoesNotContain(")this).ToString()", text);
    }

    // Regression (#3213 review, Gemini + GPT): a boxed `this` reaching a SEALED
    // default interface member must render the `((I)this)` cast, NOT `base.`. A
    // sealed DIM is non-virtual, so `((I)this).SealedPing()` lowers to
    // `ldobj; box; call ISealedDim::SealedPing` — a non-virtual `call` that shares
    // the base-call shape (`!IsVirtual && IsCrossType`). Before the ValueType gate
    // the base arm over-fired here and emitted invalid `base.SealedPing()` (CS0117
    // — a struct's base `System.ValueType` has no such member). The declaring type
    // is an interface, not `System.ValueType`, so the cast arm re-emits the exact
    // non-virtual `call ISealedDim::SealedPing`.
    [Fact]
    public void StructBoxedThis_SealedInterfaceCallee_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(StructSealedDimReceiver),
            nameof(StructSealedDimReceiver.CallSealedDim));
        Assert.Contains("((ISealedDim)this).SealedPing()", text);
        Assert.DoesNotContain("base.", text);
        Assert.DoesNotContain("(this).SealedPing()", text);
    }

    // Regression (#3213 review, Gemini): the method-group form of the sealed-DIM
    // case. `((ISealedDim)this).SealedPing` (as a delegate) emits `ldobj; box;
    // ldftn ISealedDim::SealedPing; newobj` — a non-virtual `ldftn` matching the
    // base-group shape. It must render the `((I)this)` cast, never invalid
    // `base.SealedPing` (CS0117).
    [Fact]
    public void StructBoxedThis_SealedInterfaceMethodGroup_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(StructSealedDimReceiver),
            nameof(StructSealedDimReceiver.SealedDimGroup));
        Assert.Contains("((ISealedDim)this).SealedPing", text);
        Assert.DoesNotContain("base.", text);
    }

    // A boxed `this` reaching a NON-VIRTUAL `System.Object` member
    // (`base.GetType()`) renders `base.GetType()`. `System.Object` is a struct's
    // (transitive) base class, so `base.GetType()` lowers to `ldobj; box; call
    // object::GetType`, matching the source; the printer whitelists both a struct's
    // base classes (ValueType and Object) for the base arm. `GetType` is public and
    // non-virtual, so `((object)this).GetType()` would ALSO round-trip, but the
    // printer prefers `base.` (the source spelling, and the only valid spelling for
    // a protected base member — see the MemberwiseClone regression) (#3213 review).
    [Fact]
    public void StructBoxedThis_ObjectNonVirtualCallee_RendersBaseCall()
    {
        var text = RenderMember(typeof(StructBaseGetType),
            nameof(StructBaseGetType.CallBaseGetType));
        Assert.Contains("base.GetType()", text);
        Assert.DoesNotContain(")this).GetType()", text);
        Assert.DoesNotContain("(this).GetType()", text);
    }

    // Regression (#3213 review, Gemini + GPT): a boxed `this` reaching the PROTECTED
    // `System.Object.MemberwiseClone` renders `base.MemberwiseClone()` — the ONLY
    // valid spelling. `base.MemberwiseClone()` lowers to `ldobj; box; call
    // object::MemberwiseClone`; the cast `((object)this).MemberwiseClone()` is
    // CS1540 (a protected member cannot be accessed through a base-typed qualifier),
    // so the object-cast fallback that serves public members like `GetType` must NOT
    // apply here. The base arm whitelisting both base classes (ValueType and Object)
    // keeps this valid. The method-group form must likewise stay `base.`.
    [Fact]
    public void StructBoxedThis_ProtectedObjectCallee_RendersBaseCall()
    {
        var call = RenderMember(typeof(StructMemberwiseCloneReceiver),
            nameof(StructMemberwiseCloneReceiver.CloneCall));
        Assert.Contains("base.MemberwiseClone()", call);
        Assert.DoesNotContain(")this).MemberwiseClone", call);

        var group = RenderMember(typeof(StructMemberwiseCloneReceiver),
            nameof(StructMemberwiseCloneReceiver.CloneGroup));
        Assert.Contains("base.MemberwiseClone", group);
        Assert.DoesNotContain(")this).MemberwiseClone", group);
    }

    // #3214: a boxed NON-`this` ref parameter reaching a DEFAULT interface member
    // renders the `((I)s)` cast. `((IBoxedNonThisDim)s).Dim()` lowers to `ldarg.1;
    // ldobj S; box S; callvirt IBoxedNonThisDim::Dim` — the same box shape as the
    // boxed-`this` interface case (#3201/#3213) but the boxed operand is a ref
    // parameter, not arg0/`this`. `Dim` is a DIM not on the struct, so bare
    // `(s).Dim()` is CS1061; the erased upcast must be re-inserted (#3214).
    [Fact]
    public void BoxedNonThisRefParam_DefaultInterfaceCallee_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.DimOnRefParam));
        Assert.Contains("((IBoxedNonThisDim)s).Dim()", text);
        Assert.DoesNotContain("(s).Dim()", text);
    }

    // #3214: a boxed by-value parameter reaching a normally-implemented interface
    // member renders `((I)s).Face()`. `Face` IS on the struct, so bare `(s).Face()`
    // compiles but silently rebinds to the struct's own `call S::Face` (fidelity
    // loss); the explicit `((IBoxedNonThisFace)s)` re-emits `box; callvirt I::Face`.
    [Fact]
    public void BoxedNonThisValueParam_InterfaceCallee_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.FaceOnValueParam));
        Assert.Contains("((IBoxedNonThisFace)s).Face()", text);
        Assert.DoesNotContain("(s).Face()", text);
    }

    // #3214: a boxed local reaching an interface member renders the cast in both the
    // call and method-group forms. The local carries no metadata name (spelled
    // `V_n`), so assert the `((I)` cast prefix and the member tail, not the name.
    [Fact]
    public void BoxedNonThisLocal_InterfaceCallee_RendersInterfaceCast()
    {
        var call = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.FaceOnLocal));
        Assert.Contains("((IBoxedNonThisFace)", call);
        Assert.Contains(").Face()", call);

        var group = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.FaceGroupOnLocal));
        Assert.Contains("((IBoxedNonThisFace)", group);
        Assert.Contains(").Face)", group);
    }

    // #3214: a boxed FIELD reaching a default interface member renders
    // `((I)_field).Dim()`. The boxed operand is `LoadField`, not arg0/`this`.
    [Fact]
    public void BoxedNonThisField_DefaultInterfaceCallee_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.DimOnField));
        Assert.Contains("((IBoxedNonThisDim)_field).Dim()", text);
    }

    // Negative (#3214): a boxed non-`this` value reaching a base-CLASS member
    // (`object::ToString`) must NOT be cast — the arm is gated on a confirmed
    // interface callee. A boxed value reaching a base-class member is a separate
    // fidelity concern and stays on the ordinary receiver path (`(s).ToString()`).
    [Fact]
    public void BoxedNonThisRefParam_BaseClassCallee_StaysUncast()
    {
        var text = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.BaseOnRefParam));
        Assert.Contains("(s).ToString()", text);
        Assert.DoesNotContain("(object)", text);
    }

    // #3214 (review): a boxed POINTER deref reaching a default interface member.
    // `*p` is loaded by `ldobj; box; callvirt`, so the cast operand is the deref.
    // It MUST be parenthesized — `((I)(*p))` — because `((I)*p)` reparses as the
    // multiplication `(I) * p` (CS0119) and dropping the deref (`((I)p)`) is a
    // pointer-to-interface cast (CS0030).
    [Fact]
    public void BoxedNonThisPointer_DefaultInterfaceCallee_RendersParenthesizedDeref()
    {
        var text = RenderMember(typeof(BoxedNonThisHost),
            nameof(BoxedNonThisHost.DimOnPointer));
        Assert.Contains("((IBoxedNonThisDim)(*p)).Dim()", text);
        Assert.DoesNotContain("(IBoxedNonThisDim)*p", text);
        Assert.DoesNotContain("(IBoxedNonThisDim)p)", text);
    }

    // #3214 (review): the callee interface is the rendering method's OWN enclosing
    // interface. The enclosing-instantiation carve-out that lets a real `this`
    // receiver skip the cast must NOT fire for a boxed struct place — the boxed
    // value still needs the upcast, so bare `(s).OwnDim()` (CS1061) is wrong.
    [Fact]
    public void BoxedNonThisParam_EnclosingInterfaceCallee_RendersInterfaceCast()
    {
        var text = RenderMember(typeof(IBoxedNonThisSelf),
            nameof(IBoxedNonThisSelf.CallOnParam));
        Assert.Contains("((IBoxedNonThisSelf)s).OwnDim()", text);
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
// implementing class). The qualify-method knob records no taste decision: a
// cross-type callee is never a `this.` opt-in. The printer re-inserts the erased
// cast (#3128), so the emit is the faithful ((IThisQualificationFace)this).FaceMethod().
public interface IThisQualificationFace
{
    int FaceMethod();
}

public sealed class ThisQualificationExplicitFace : IThisQualificationFace
{
    int IThisQualificationFace.FaceMethod() => 7;

    public int CallExplicitInterface() => ((IThisQualificationFace)this).FaceMethod();
}

// A constructed generic self-call. From within I<T>, ((I<object>)this).M() emits
// `callvirt I<object>::M`. I<object> shares I<T>'s DEFINITION but is a DIFFERENT
// instantiation, so bare/`this.` M() would bind to I<T>::M, not I<object>::M — the
// qualifier is NOT byte-preserving. Definition-only equality would wrongly treat
// this as same-type; the exact-instantiation guard records nothing. (`out T` +
// `class` makes the covariant cast to I<object> legal.)
public interface IThisQualificationGeneric<out T> where T : class
{
    int M() => 1;
    int CallViaObjectInstantiation() => ((IThisQualificationGeneric<object>)this).M();
}

// Two overloads whose signatures differ only by a function pointer's RETURN type
// (delegate*<int, int> vs delegate*<int, void>). The dedup discriminator must key
// on the function pointer's return type, calling convention, and parameter
// ref-kinds — not parameters alone — or the two collapse into one row and hide a
// taste application.
public unsafe class ThisQualificationFnPtr
{
    public void Select(delegate*<int, int> p) { }
    public void Select(delegate*<int, void> p) { }
    public void CallBoth(delegate*<int, int> a, delegate*<int, void> b)
    {
        this.Select(a);
        this.Select(b);
    }
}

// A derived type that HIDES a base field with a `new` field of the same name, so
// the class holds two distinct `X` fields. ReadOwnField reads the derived field
// (declaring type == the enclosing type) and is a genuine this. opt-in. But
// ReadBaseField reads base.X — the load targets the BASE field
// (ldarg.0; ldfld Base::X). A pre-existing emit gap mis-spells that as this.X,
// yet this.X binds to the DERIVED field, not base.X, so it is NOT byte-preserving
// and must record nothing. ReadInheritedField reads a merely-inherited (unhidden)
// base field; its this. is safe but the exact-instantiation guard under-records it
// (a false-negative is safe).
public class ThisQualificationFieldBase
{
    public int Hidden;
    public int Inherited;
}

public sealed class ThisQualificationFieldDerived : ThisQualificationFieldBase
{
    public new int Hidden;

    public int ReadOwnField() => Hidden;
    public int ReadBaseField() => base.Hidden;
    public int ReadInheritedField() => Inherited;
}

// Two overloads that differ only by ARITY: M() and M<T>() share an empty parameter
// list, so a parameter-types-only dedup key collapses them into one row and hides
// a taste application. Qualifying both must record TWO decisions. CallTwoInstantiations
// qualifies the SAME generic method at two different instantiations (G<int>, G<string>);
// those are one source member and must collapse into ONE row (arity, not the specific
// type arguments, is the key).
public sealed class ThisQualificationArity
{
    public void M() { }
    public void M<T>() { }
    public void G<T>() { }

    public void CallBothArities()
    {
        this.M();
        this.M<int>();
    }

    public void CallTwoInstantiations()
    {
        this.G<int>();
        this.G<string>();
    }
}

// A generic method whose PARAMETER mentions its type parameter: Echo<T>(T). The
// callee's ParameterTypes are substituted per MethodSpec (T -> int, T -> string),
// so a key built from them would split this.Echo<int>(1) and this.Echo<string>("s")
// into two rows for ONE source member. Keying on the DEFINITION signature (T left
// as !!0) collapses them into a single row.
public sealed class ThisQualificationGenericParam
{
    public T Echo<T>(T value) => value;

    public void CallTwoInstantiations()
    {
        this.Echo<int>(1);
        this.Echo<string>("s");
    }
}

// A method GROUP over a generic instance method (this.Make<int> as a Func<int>).
// MethodGroupText renders only the bare name, dropping the <int> (a pre-existing
// emit gap; the group path never appends type arguments the way call and &-of
// paths do). The emitted this.Make does not round-trip — delegate return-type
// inference cannot recover T (CS0411) — so it must record nothing.
public sealed class ThisQualificationGenericGroup
{
    public T Make<T>() => default!;

    public System.Func<int> Build() => this.Make<int>;
}

// A default interface member (DIM) reached from an implementing class. The DIM is
// NOT a member of DimConsumer, so the source must write the ((IDimFace)this) cast;
// a class→interface upcast emits no IL, so csc lowers ((IDimFace)this).Value() to
// `ldarg.0; callvirt IDimFace::Value()` and the method group ((IDimFace)this).Value
// to `ldarg.0; dup; ldvirtftn IDimFace::Value; newobj Func`. Both leave the IR
// target a bare LoadArgument{0,"this"} with the callee declared on the interface,
// so the printer must re-insert the erased cast — bare Value()/this.Value() is
// CS1061 (#3128).
public interface IDimFace
{
    int Value() => 7;
}

public sealed class DimConsumer : IDimFace
{
    public int DimCall() => ((IDimFace)this).Value();

    public System.Func<int> DimGroup() => ((IDimFace)this).Value;
}

// The value-type siblings of DimConsumer/ThisQualificationExplicitFace (#3201).
// A struct reaches an interface member through `this` by BOXING: ((IDimSFace)this)
// / ((IStructFace)this) lowers to `ldarg.0; ldobj Struct; box Struct; callvirt
// IFace::M()` (a boxing conversion that DOES emit `box`, unlike the no-IL class
// upcast of #3128). The IR receiver is therefore a Box over the this value, not a
// bare LoadArgument{0,"this"}, so the printer must recognise the boxed-this shape
// and re-insert the ((I)this) cast — bare (this).M() is CS1061.
public interface IDimSFace
{
    int Value() => 7;
}

public struct StructDimConsumer : IDimSFace
{
    public int DimCall() => ((IDimSFace)this).Value();

    public System.Func<int> DimGroup() => ((IDimSFace)this).Value;
}

public interface IStructFace
{
    int FaceMethod();
}

public struct StructExplicitFace : IStructFace
{
    int IStructFace.FaceMethod() => 7;

    public int CallExplicitInterface() => ((IStructFace)this).FaceMethod();
}

// Boxed-this fixtures reaching NON-interface members (#3213). All three box
// `this` (`ldarg.0; ldobj S; box S; ...`) — the value-type sibling of a reference
// upcast — but the callee is not an interface member, so the fix must split on
// call kind rather than interface-ness: a virtual `callvirt` re-emits the cast
// (`((T)this).M()`); a non-virtual `call` to a base method is `base.M()`.
//   * CallObjectToString: virtual `callvirt object::ToString` -> `((object)this)`.
//   * GetHashCode: non-virtual `call ValueType::GetHashCode` -> `base.` (a bare
//     `(this).GetHashCode()` would recurse into this override forever).
//   * CallViaValueTypeCast: an explicit `(ValueType)this` cast boxes, but csc
//     binds the virtual call to the slot-defining `object::GetHashCode`, so it
//     renders `((object)this)` (still a cast, not `base.`, because it is virtual).
public struct StructBoxedObjectReceiver
{
    public string CallObjectToString() => ((object)this).ToString()!;
}

public struct StructBaseHashCall
{
    public override int GetHashCode() => base.GetHashCode();
}

public struct StructValueTypeCastReceiver
{
    public int CallViaValueTypeCast() => ((System.ValueType)this).GetHashCode();
}

// Negative fixture (#3213): an implicit `this.ToString()` on a struct that does
// NOT override ToString does NOT box — csc emits `ldarg.0; constrained. S;
// callvirt object::ToString()`, leaving a bare `LoadArgument{0,"this"}` receiver
// (with a `constrained.` prefix), not a Box. `IsBoxedThisReceiver` declines, so
// the boxed-this arm must not fire and the render gains no cast or `base.`.
public struct StructImplicitToString
{
    public string CallImplicitToString() => this.ToString()!;
}

// Boxed-this method-group whose callee is a STATIC extension method (#3213 review,
// GPT). `((object)this).ExtValue` boxes and emits `ldobj; box; ldftn
// BoxedThisExtensionHost::ExtValue(object); newobj Func` — the boxed-this shape,
// but the callee is static, so `method.HasThis` gates the boxed-this arm off and
// the render must spell the boxed receiver, never `((BoxedThisExtensionHost)this)`
// (CS0716 — a static type is not a cast target).
public static class BoxedThisExtensionHost
{
    public static int ExtValue(this object x) => 7;
}

public struct StructExtensionGroupReceiver
{
    public System.Func<int> ExtGroup() => ((object)this).ExtValue;
}

// A STATIC method whose first parameter is spelled `@this` (#3213 review, Gemini).
// The compiler emits the metadata name `"this"` at index 0, so a boxed
// `((object)@this).M()` matches `Box{LoadIndirect{LoadArgument{0,"this"}}}` — yet
// `base`/`this` are illegal in a static context (CS0026). The boxed-this arm is
// gated on the ENCLOSING method having an implicit `this` (`_function.Signature
// .HasThis`), so these must not synthesize `base.`/`((object)this)`; the boxed
// `@this` parameter falls through to the ordinary receiver path.
public struct StructStaticThisParameter
{
    public static System.Type BoxNonVirtual(ref StructStaticThisParameter @this) => ((object)@this).GetType();

    public static string BoxVirtual(ref StructStaticThisParameter @this) => ((object)@this).ToString()!;
}

// Sealed default interface member reached from a boxed struct `this` (#3213
// review, Gemini + GPT). A sealed DIM is NON-VIRTUAL, so `((ISealedDim)this)
// .SealedPing()` lowers to `ldobj; box; call ISealedDim::SealedPing` and its
// method group to `ldobj; box; ldftn ISealedDim::SealedPing; newobj` — both share
// the non-virtual base-call/group shape. The declaring type is an interface, not
// the struct's immediate base `System.ValueType`, so the printer must re-emit the
// `((I)this)` cast, never `base.SealedPing` (CS0117 — a struct's base has no such
// member).
public interface ISealedDim
{
    sealed void SealedPing() { }
}

public struct StructSealedDimReceiver : ISealedDim
{
    public void CallSealedDim() => ((ISealedDim)this).SealedPing();

    public System.Action SealedDimGroup() => ((ISealedDim)this).SealedPing;
}

// A boxed struct `this` reaching a NON-VIRTUAL `System.Object` member via `base.`
// (#3213 review). `base.GetType()` lowers to `ldobj; box; call object::GetType` —
// `System.Object` is one of a struct's two base classes (with `System.ValueType`),
// both whitelisted for the `base.` arm, so this renders `base.GetType()`. `GetType`
// is public and non-virtual, so `((object)this).GetType()` would also round-trip,
// but the printer prefers the source `base.` spelling.
public struct StructBaseGetType
{
    public System.Type CallBaseGetType() => base.GetType();
}

// A boxed struct `this` reaching the PROTECTED `System.Object.MemberwiseClone` via
// `base.` (#3213 review, Gemini + GPT). `base.MemberwiseClone()` lowers to `ldobj;
// box; call object::MemberwiseClone`; the group is `ldobj; box; ldftn
// object::MemberwiseClone; newobj`. `MemberwiseClone` is protected, so the
// `((object)this).MemberwiseClone()` cast that serves a public member like
// `GetType` is CS1540 here — only `base.` compiles, which is why the base arm must
// whitelist `System.Object` (not just `System.ValueType`).
public struct StructMemberwiseCloneReceiver
{
    public object CloneCall() => base.MemberwiseClone();

    public System.Func<object> CloneGroup() => base.MemberwiseClone;
}

// #3214 fixtures: a boxed NON-`this` value (parameter, local, field, or ref place)
// reaching an interface member. csc boxes the value type for the implicit upcast to
// the interface, so bare `(x).M()` is CS1061 for a default interface member (`Dim`)
// or a silent rebind to the struct's own method for a normally-implemented one
// (`Face`); the printer must re-insert the erased `((I)x)` cast (#3214).
public interface IBoxedNonThisFace
{
    int Face();
}

public interface IBoxedNonThisDim
{
    int Dim() => 11;
}

public struct BoxedNonThisStruct : IBoxedNonThisFace, IBoxedNonThisDim
{
    public int Face() => 3;
}

public class BoxedNonThisHost
{
    private BoxedNonThisStruct _field = new BoxedNonThisStruct();

    // boxed ref parameter reaching a default interface member (the CS1061 case)
    public int DimOnRefParam(ref BoxedNonThisStruct s) => ((IBoxedNonThisDim)s).Dim();

    // boxed by-value parameter reaching a normally-implemented interface member
    public int FaceOnValueParam(BoxedNonThisStruct s) => ((IBoxedNonThisFace)s).Face();

    // boxed local reaching an interface member (call form)
    public int FaceOnLocal()
    {
        var s = default(BoxedNonThisStruct);
        return ((IBoxedNonThisFace)s).Face();
    }

    // boxed local reaching an interface member (method-group form)
    public System.Func<int> FaceGroupOnLocal()
    {
        var s = default(BoxedNonThisStruct);
        return ((IBoxedNonThisFace)s).Face;
    }

    // boxed field reaching a default interface member
    public int DimOnField() => ((IBoxedNonThisDim)_field).Dim();

    // boxed POINTER deref reaching a default interface member; the deref must be
    // parenthesized in the cast operand -> `((IBoxedNonThisDim)(*p)).Dim()`.
    public static unsafe int DimOnPointer(BoxedNonThisStruct* p) => ((IBoxedNonThisDim)(*p)).Dim();

    // NEGATIVE: a boxed non-`this` value reaching a base-class (object) member stays
    // uncast — the interface-only gate must not fire (separate fidelity concern).
    public string? BaseOnRefParam(ref BoxedNonThisStruct s) => ((object)s).ToString();
}

// #3214 (review): the callee interface is the rendering method's OWN enclosing
// interface. A default interface member of `IBoxedNonThisSelf` boxes a struct
// parameter and reaches another `IBoxedNonThisSelf` member; the boxed place still
// needs the `((IBoxedNonThisSelf)s)` upcast (the enclosing-instantiation carve-out
// is sound only for a real `this` receiver, never a boxed struct place).
public interface IBoxedNonThisSelf
{
    int OwnDim() => 7;

    int CallOnParam(BoxedNonThisSelfStruct s) => ((IBoxedNonThisSelf)s).OwnDim();
}

public struct BoxedNonThisSelfStruct : IBoxedNonThisSelf
{
}
