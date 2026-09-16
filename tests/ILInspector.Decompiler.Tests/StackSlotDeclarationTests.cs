using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StackSlotDeclarationTests
{
    static readonly TypeRef Holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

    [Fact]
    public void RefStackSlot_AssignedAndConsumedInOneBlock_DeclaresAtStore()
    {
        var stringRef = TypeRef.ByRef(String);
        var use = new MethodRef(Holder, "Use", Void, [stringRef], HasThis: false);
        var entry = new Block(0);
        entry.Add(new Branch(1));
        var consume = new Block(1);
        consume.Add(new StoreStackSlot(0, new LoadLocalAddress(0, String)));
        consume.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(0, stringRef)])));
        consume.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(consume);
        var function = new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [String],
            body);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.DoesNotContain("Unsafe.NullRef", output);
        Assert.Contains("ref string S_0 = ref V_0;", output);
    }
}
