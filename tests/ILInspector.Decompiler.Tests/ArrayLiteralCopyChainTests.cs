using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

// #9249: the importer mints a fresh stack slot for every `dup`, so a
// multi-element `newarr; dup; ldc; e; stelem; dup; ...` fill reaches
// ArrayLiteralFromStoresPass as a direct copy chain (S_257 = S_256;
// S_257[1] = e1;). The pass treats the chain as one place, retires its copy
// statements, and commits only when the raise succeeds. A conditional element
// csc spilled into its own slot right before the element store joins the run.
[Trait("Area", "Pass")]
public class ArrayLiteralCopyChainTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef ObjectArray = TypeRef.SzArray(Object);
    static readonly TypeRef ObjectArrayArray = TypeRef.SzArray(ObjectArray);
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.CoreLib("System", "MyType");

    static MethodRef Sink(int argCount) => new(Owner, "Sink", Void, [.. Enumerable.Repeat(Object, argCount)], HasThis: false);
    static Call Effect(string name) => new(new MethodRef(Owner, name, Object, [], HasThis: false), isVirtual: false, []);
    static Constant Index(int value) => new(value, Int32);
    static NewArray Allocate(int length, TypeRef? element = null) => new(element ?? Object, Index(length));
    static LoadStackSlot Slot(int slot, TypeRef? type = null) => new(slot, type ?? ObjectArray);
    static StoreElement Fill(int slot, int index, IrExpression value, TypeRef? element = null) => new(element ?? Object, Slot(slot), Index(index), value);
    static StoreStackSlot Copy(int target, int source) => new(target, Slot(source));
    static ExpressionStatement SinkOf(params IrExpression[] arguments) => new(new Call(Sink(arguments.Length), isVirtual: false, arguments));

    static IrFunction Build(params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        if (statements[^1] is not Return)
            block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Owner, signature, [], container);
    }

    static void RunPass(IrFunction function)
    {
        new ArrayLiteralFromStoresPass().Run(function, PassContext.None);
        function.CheckInvariant();
    }

    static IReadOnlyList<IrNode> Statements(IrFunction function) => function.Body.Blocks[0].Children.ToList();
    static ArrayLiteral Literal(IrFunction function) => function.Descendants.OfType<ArrayLiteral>().Single();
    static IEnumerable<string> Callees(ArrayLiteral literal) => literal.Elements.Select(element => ((Call)element).Callee.Name);
    static int SlotMentions(IrFunction function, int slot)
        => function.Descendants.Count(n => n is LoadStackSlot load && load.Slot == slot || n is StoreStackSlot store && store.Slot == slot);

    // S_256 = new object[2]; S_256[0] = A(); S_257 = S_256; S_257[1] = B(); Sink(S_257);
    // folds to S_256 = new object[] { A(), B() }; Sink(S_256);
    [Fact]
    public void ChainOfTwo_FoldsAndRetiresTheCopy()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            Fill(257, 1, Effect("B")),
            SinkOf(Slot(257)));

        RunPass(function);

        Assert.Equal(["A", "B"], Callees(Literal(function)));
        Assert.Equal(0, SlotMentions(function, 257));
        var statements = Statements(function);
        Assert.IsType<StoreStackSlot>(statements[0]);
        var sink = (Call)((ExpressionStatement)statements[1]).Expression;
        Assert.Equal(256, ((LoadStackSlot)sink.Arguments[0]).Slot);
    }

    // A four-element fill: every element after the first stores through the
    // next link of the chain S_256 -> S_257 -> S_258 -> S_259, and the last
    // link is the escaping read.
    [Fact]
    public void ChainOfFour_FoldsAndRetiresEveryCopy()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(4)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            Fill(257, 1, Effect("B")),
            Copy(258, 257),
            Fill(258, 2, Effect("C")),
            Copy(259, 258),
            Fill(259, 3, Effect("D")),
            new Return(Slot(259)));

        RunPass(function);

        Assert.Equal(["A", "B", "C", "D"], Callees(Literal(function)));
        foreach (int slot in new[] { 257, 258, 259 })
            Assert.Equal(0, SlotMentions(function, slot));
        Assert.Equal(2, Statements(function).Count);
        Assert.Equal(256, ((LoadStackSlot)((Return)Statements(function)[1]).Value!).Slot);
    }

    // The escaping read is the root slot itself; the copy sits ahead of an
    // intervening effect and is still retired.
    [Fact]
    public void CopyAheadOfTheRun_FoldsAtTheRunPosition()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Copy(257, 256),
            new ExpressionStatement(Effect("Between")),
            Fill(257, 0, Effect("A")),
            Fill(257, 1, Effect("B")),
            SinkOf(Slot(256)));

        RunPass(function);

        Assert.Equal(["A", "B"], Callees(Literal(function)));
        Assert.Equal(0, SlotMentions(function, 257));
        var statements = Statements(function);
        Assert.Equal("Between", ((Call)((ExpressionStatement)statements[0]).Expression).Callee.Name);
        Assert.IsType<ArrayLiteral>(((StoreStackSlot)statements[1]).Value);
    }

    // GetArgArray's shape: the second element is a conditional csc evaluated
    // into its own slot between the dup and the stelem. It moves into its
    // element position; the result is `return new object[] { A(), c ? B() : C() };`.
    [Fact]
    public void SpilledConditionalElement_JoinsTheRun()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            new StoreStackSlot(3, new Conditional(Effect("Test"), Effect("B"), Effect("C"))),
            Fill(257, 1, new LoadStackSlot(3, Object)),
            new Return(Slot(257)));

        RunPass(function);

        var literal = Literal(function);
        Assert.Equal("A", ((Call)literal.Elements[0]).Callee.Name);
        Assert.IsType<Conditional>(literal.Elements[1]);
        Assert.Equal(0, SlotMentions(function, 3));
        Assert.Equal(0, SlotMentions(function, 257));
        Assert.Equal(2, Statements(function).Count);
    }

    // object[][]: each inner array folds first, then the outer chain folds;
    // the inner literal stored right before element 1 joins the outer run.
    [Fact]
    public void NestedArrays_FoldInnerThenOuter()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2, ObjectArray)),
            new StoreStackSlot(258, Allocate(1)),
            Fill(258, 0, Effect("A")),
            Fill(256, 0, Slot(258), ObjectArray),
            Copy(257, 256),
            new StoreStackSlot(259, Allocate(1)),
            Fill(259, 0, Effect("B")),
            Fill(257, 1, Slot(259), ObjectArray),
            new Return(Slot(257, ObjectArrayArray)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<NewArray>());
        var outer = function.Descendants.OfType<ArrayLiteral>().Single(l => l.Parent is StoreStackSlot { Slot: 256 });
        Assert.IsType<ArrayLiteral>(outer.Elements[1]);
        Assert.Equal(0, SlotMentions(function, 257));
        Assert.Equal(0, SlotMentions(function, 259));
    }

    // A chain member read between the allocation and the run observes the
    // unfilled array: decline, and leave every statement in place.
    [Fact]
    public void AliasReadBeforeTheRun_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Copy(257, 256),
            SinkOf(Slot(257)),
            Fill(256, 0, Effect("A")),
            Fill(256, 1, Effect("B")),
            SinkOf(Slot(256)));
        int before = Statements(function).Count;

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Equal(before, Statements(function).Count);
    }

    // An element value reading a chain member (`a[1] = a`) would read the
    // slot before the literal commits: decline.
    [Fact]
    public void ElementReadsAnAlias_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            Fill(257, 1, Slot(257)),
            new Return(Slot(257)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Equal(1, function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == 257));
    }

    // Two escaping reads through different members: the array is observed
    // twice, so there is no single expression position for the literal.
    [Fact]
    public void SecondEscapeThroughAnotherAlias_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            Fill(257, 1, Effect("B")),
            SinkOf(Slot(256)),
            SinkOf(Slot(257)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
    }

    // A member with a second store elsewhere is a join, not an alias.
    [Fact]
    public void MultiStoreAlias_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            Fill(257, 1, Effect("B")),
            SinkOf(Slot(257)),
            new StoreStackSlot(257, new Constant(null, ObjectArray)),
            SinkOf(Slot(257)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
    }

    // A typed local never joins a chain: it can be re-bound or addressed.
    [Fact]
    public void LocalCopy_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            new StoreLocal(0, ObjectArray, Slot(256)),
            new StoreElement(Object, new LoadLocal(0, ObjectArray), Index(1), Effect("B")),
            SinkOf(new LoadLocal(0, ObjectArray)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
    }

    // A run interrupted by an unrelated statement does not raise, and the
    // copy statements stay: nothing is retired unless the literal commits.
    [Fact]
    public void BlockedRun_LeavesTheChainUntouched()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            new ExpressionStatement(Effect("Between")),
            Fill(257, 1, Effect("B")),
            SinkOf(Slot(257)));
        int before = Statements(function).Count;

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Equal(before, Statements(function).Count);
        Assert.Equal(3, SlotMentions(function, 257));
    }

    // A spilled element read twice is a shared carrier, not the element's own
    // evaluation: it stays, and the run does not raise.
    [Fact]
    public void SharedElementSpill_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            new StoreStackSlot(3, Effect("B")),
            Fill(257, 1, new LoadStackSlot(3, Object)),
            SinkOf(new LoadStackSlot(3, Object), Slot(257)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Equal(1, function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == 3));
    }

    // A spilled element that reads the array being built would observe the
    // slot before the literal commits: decline.
    [Fact]
    public void ElementSpillReadsThePlace_DoesNotFold()
    {
        var function = Build(
            new StoreStackSlot(256, Allocate(2)),
            Fill(256, 0, Effect("A")),
            Copy(257, 256),
            new StoreStackSlot(3, new Call(Sink(1), isVirtual: false, [Slot(256)])),
            Fill(257, 1, new LoadStackSlot(3, Object)),
            new Return(Slot(257)));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
    }

    // Newtonsoft.Json 13.0.4 witnesses through the full pipeline: the #9249
    // conditional-element shape and the two-, three-, and four-argument params
    // fills (chains of one, two, and three copies).
    [Theory]
    [InlineData("Newtonsoft.Json.Utilities.DynamicProxyMetaObject`1", "GetArgArray#2", "return new Expression[] { Expression.NewArrayInit(typeof(object), GetArgs(args)), ")]
    [InlineData("Newtonsoft.Json.Utilities.StringUtils", "FormatWith#4", "return format.FormatWith(provider, new object[] { arg0, arg1 });")]
    [InlineData("Newtonsoft.Json.Utilities.StringUtils", "FormatWith#5", "return format.FormatWith(provider, new object[] { arg0, arg1, arg2 });")]
    [InlineData("Newtonsoft.Json.Utilities.StringUtils", "FormatWith#6", "return format.FormatWith(provider, new object[] { arg0, arg1, arg2, arg3 });")]
    public void RealDupChainsRaise(string typeName, string method, string expected)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "ReferenceConditional", "Newtonsoft.Json.dll");
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = FindByArity(source, typeName, method);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains(expected, output);
        Assert.DoesNotContain("S_257", output);
    }

    // "Name#n" selects the overload with n parameters; a bare name selects the first.
    static IrFunction FindByArity(MetadataSource source, string typeName, string method)
    {
        string[] parts = method.Split('#');
        if (parts.Length == 1)
            return IrImporter.Import(source, typeName, method) ?? throw new InvalidOperationException($"{typeName}::{method} not found");
        int arity = int.Parse(parts[1]);
        for (int overload = 0; ; overload++)
        {
            var function = IrImporter.Import(source, typeName, parts[0], overload)
                ?? throw new InvalidOperationException($"{typeName}::{parts[0]} with {arity} parameters not found");
            if (function.Signature.Parameters.Length == arity)
                return function;
        }
    }
}
