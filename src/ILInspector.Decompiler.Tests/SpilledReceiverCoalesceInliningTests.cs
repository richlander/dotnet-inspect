using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A reference-type field initializer `field = arg ?? new()` spills the receiver
// `this` and the coalesce result into stack slots across the `??` branch
// (S_0 = this; S_1 = arg ?? new(); S_0.field = S_1). ExpressionInliningPass now
// collapses both temporaries — reference-type `this` is a plain, non-reassignable
// object reference, so the receiver spill folds via the live-range mode, which
// unblocks the value spill (a non-first-leaf, non-pure value deferred only past
// the now-pure receiver) via the preceding-evaluation-pure gate. A value-type
// receiver is a byref managed pointer an intervening call could mutate, so it
// stays spilled.
[Trait("Area", "Pass")]
public class SpilledReceiverCoalesceInliningTests
{
    static string PrintRaised(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(SpilledCoalesceField).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n");
    }

    // Both defects together on a real compiled explicit constructor: the spilled
    // receiver (S_0) and coalesce (S_1) collapse into one clean field store.
    [Fact]
    public void ExplicitConstructor_SpilledReceiverAndCoalesce_CollapseToFieldInit()
    {
        string output = PrintRaised(typeof(SpilledCoalesceField).FullName!, ".ctor");

        Assert.Contains("options ?? new SpilledCoalesceOptions()", output);
        Assert.DoesNotContain("S_0", output);
        Assert.DoesNotContain("S_1", output);
        Assert.DoesNotContain("Unsupported", output);
    }

    // A primary constructor emits the implicit base object..ctor after the field
    // inits; with the temps spilled the prologue was not all instance-field
    // stores, so the base call leaked as an unsupported residual. Collapsing the
    // temps restores the clean prologue that elides the base call.
    [Fact]
    public void PrimaryConstructor_SpilledCoalesceField_ElidesBaseCallResidual()
    {
        string output = PrintRaised(typeof(SpilledCoalescePrimaryField).FullName!, ".ctor");

        Assert.Contains("_options = options ?? new SpilledCoalesceOptions();", output);
        Assert.DoesNotContain("Unsupported", output);
        Assert.DoesNotContain("S_0", output);
        Assert.DoesNotContain("S_1", output);
    }

    // Direct-IR A/B on the receiver-spill gate: a reference-type `this` folds
    // into the field store's receiver; a value-type `this` (byref, possibly
    // mutated by an intervening call) stays spilled.
    [Theory]
    [InlineData("System", "Object", true)]   // class receiver ⇒ fold
    [InlineData("System", "ValueType", false)] // struct receiver ⇒ keep spilled
    public void ReceiverSpill_FoldsOnlyForReferenceTypeThis(string baseNamespace, string baseName, bool folds)
    {
        var declaringType = TypeRef.Definition("Synthetic", "Samples", "SpillReceiver");
        var baseType = TypeRef.CoreLib(baseNamespace, baseName);
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var field = new FieldRef(declaringType, "_x", intType);
        var touch = new MethodRef(declaringType, "Touch", voidType, [], HasThis: false);

        // S_0 = this; Touch(); this._x = 0
        // The intervening void call is a non-removable statement, so the receiver
        // spill's single use is never adjacent to its store — only the live-range
        // path (governed by ReceiverThisIsPure) can fold it. A reference-type
        // `this` is an immutable object reference and folds; a value-type `this`
        // is a byref whose target the call could mutate, so it stays spilled.
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "this", declaringType)));
        block.Add(new ExpressionStatement(new Call(touch, isVirtual: false, [])));
        block.Add(new StoreField(field, new LoadStackSlot(0, declaringType), new Constant(0, intType)));
        block.Add(new Return(null));
        var signature = new MethodSignature(voidType, [], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("SetX", declaringType, signature, [], container) { BaseType = baseType };

        new ExpressionInliningPass().Run(function, PassContext.None);

        var storeField = function.Descendants.OfType<StoreField>().Single();
        if (folds)
            Assert.IsType<LoadArgument>(storeField.Instance);
        else
            Assert.IsType<LoadStackSlot>(storeField.Instance);
    }
}
