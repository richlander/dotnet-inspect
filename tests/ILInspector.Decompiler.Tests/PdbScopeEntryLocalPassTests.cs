using ILInspector.Decompiler.Pipeline;
using LocalVariableAttributes = System.Reflection.Metadata.LocalVariableAttributes;

namespace ILInspector.Decompiler.Tests;

public sealed class PdbScopeEntryLocalPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Owner = TypeRef.Definition("Tests", "Samples", "Scopes");
    static readonly TypeRef First = TypeRef.Definition("Tests", "Samples", "First");
    static readonly TypeRef Second = TypeRef.Definition("Tests", "Samples", "Second");

    [Fact]
    public void PatternCarriers_MaterializeExactLocalsAtScopeEntries()
    {
        var function = PatternCarriers();

        new PdbScopeEntryLocalPass().Run(function, PassContext.None);
        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(4, function.Locals.Length);
        Assert.Null(function.LocalNames[0]);
        Assert.Null(function.LocalNames[1]);
        Assert.Equal("same", function.LocalNames[2]);
        Assert.Equal("same", function.LocalNames[3]);
        Assert.Null(function.LocalDeclarationBindings[0]);
        Assert.Null(function.LocalDeclarationBindings[1]);
        Assert.Equal(1, function.LocalDeclarationBindings[2]!.VariableRowId);
        Assert.Equal(2, function.LocalDeclarationBindings[3]!.VariableRowId);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("First same = V_0;", result.Output);
        Assert.Contains("Second same = V_1;", result.Output);
        Assert.DoesNotContain("First V_2", result.Output);
        Assert.DoesNotContain("Second V_3", result.Output);
    }

    [Fact]
    public void PatternCarrierMaterialization_IsIdempotent()
    {
        var function = PatternCarriers();
        var pass = new PdbScopeEntryLocalPass();
        pass.Run(function, PassContext.None);
        new PdbLocalScopePass().Run(function, PassContext.None);
        string before = CSharpPrinter.Print(function).Output!;

        pass.Run(function, PassContext.None);
        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(4, function.Locals.Length);
        Assert.Equal(before, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void CoScopedPatternCarriers_MaterializeEveryExactLocal()
    {
        var function = CoScopedPatternCarriers();

        new PdbScopeEntryLocalPass().Run(function, PassContext.None);
        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(8, function.Locals.Length);
        Assert.All(function.LocalNames.Take(4), Assert.Null);
        Assert.Equal(2, function.LocalNames.Count(name => name == "left"));
        Assert.Equal(2, function.LocalNames.Count(name => name == "right"));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split("First left = ", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, result.Output.Split("Second right = ", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("multiple-writes")]
    [InlineData("partial-assignment")]
    public void ProvenPreScopeCarrier_Materializes(string variation)
    {
        var function = PatternCarriers(variation);

        new PdbScopeEntryLocalPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Null(function.LocalDeclarationBindings[0]);
        Assert.Contains(
            function.LocalDeclarationBindings.Skip(2),
            binding => binding?.VariableRowId == 1);
    }

    [Fact]
    public void SkipLocalsInitCarrierWithoutReachingWrite_Declines()
    {
        var function = PatternCarriers("partial-assignment-skip");

        new PdbScopeEntryLocalPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal("same", function.LocalNames[0]);
        Assert.Equal(1, function.LocalDeclarationBindings[0]!.VariableRowId);
    }

    [Theory]
    [InlineData("in-scope-write")]
    [InlineData("address")]
    [InlineData("missing-entry")]
    [InlineData("ambiguous-entry")]
    [InlineData("not-target")]
    [InlineData("unassigned")]
    [InlineData("overlap")]
    [InlineData("reserved")]
    [InlineData("pinned")]
    public void UnprovedPatternCarrier_Declines(string variation)
    {
        var function = PatternCarriers(variation);

        new PdbScopeEntryLocalPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal("same", function.LocalNames[0]);
        Assert.Equal(1, function.LocalDeclarationBindings[0]!.VariableRowId);
        Assert.DoesNotContain(
            function.LocalDeclarationBindings.Skip(2),
            binding => binding?.VariableRowId == 1);
    }

    static IrFunction PatternCarriers(string? variation = null)
    {
        TypeRef firstType = variation == "pinned" ? TypeRef.Pinned(First) : First;
        var input = new Parameter("input", Object);
        Parameter[] parameters = variation == "reserved"
            ? new[] { input, new Parameter("same", Object) }
            : [input];

        var entry = new Block(0);
        Block? preFirst = null;
        if (variation is "partial-assignment" or "partial-assignment-skip")
        {
            entry.Add(At(new ConditionalBranch(
                new Constant(true, Boolean), 10), 1));
            preFirst = new Block(5);
            preFirst.Add(At(new StoreLocal(
                0,
                firstType,
                new IsInstance(firstType, new LoadArgument(0, input))), 5));
            preFirst.Add(new Branch(10));
        }
        else if (variation != "unassigned")
        {
            entry.Add(At(new StoreLocal(
                0,
                firstType,
                new IsInstance(firstType, new LoadArgument(0, input))), 1));
            if (variation == "multiple-writes")
            {
                entry.Add(At(new StoreLocal(
                    0,
                    firstType,
                    new IsInstance(firstType, new LoadArgument(0, input))), 3));
            }
        }
        if (preFirst is null)
        {
            entry.Add(Observe(0, firstType, 2, variation == "address"));
            if (variation != "not-target")
                entry.Add(new Branch(10));
        }

        var first = new Block(variation == "missing-entry" ? 11 : 10);
        if (variation == "in-scope-write")
        {
            first.Add(At(new StoreLocal(
                0,
                firstType,
                new IsInstance(firstType, new LoadArgument(0, input))), 10));
        }
        first.Add(Observe(0, firstType, 11));
        first.Add(new Branch(20));

        var testSecond = new Block(20);
        testSecond.Add(At(new StoreLocal(
            1,
            Second,
            new IsInstance(Second, new LoadArgument(0, input))), 21));
        testSecond.Add(Observe(1, Second, 22));
        testSecond.Add(new Branch(30));

        var second = new Block(30);
        second.Add(Observe(1, Second, 31));
        second.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(entry);
        if (preFirst is not null)
            body.Add(preFirst);
        body.Add(first);
        if (variation == "ambiguous-entry")
            body.Add(new Block(10));
        body.Add(testSecond);
        body.Add(second);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [.. parameters], false, 0),
            [firstType, Second],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
            SkipLocalsInit = variation == "partial-assignment-skip",
            LocalDeclarationBindings =
            [
                new PdbLocalDeclaration(
                    1,
                    1,
                    0,
                    "same",
                    new LocalSlotScope(10, 20),
                    LocalVariableAttributes.None),
                new PdbLocalDeclaration(
                    2,
                    2,
                    1,
                    "same",
                    variation == "overlap"
                        ? new LocalSlotScope(15, 40)
                        : new LocalSlotScope(30, 40),
                    LocalVariableAttributes.None),
            ],
        };
        return function;
    }

    static IrFunction CoScopedPatternCarriers()
    {
        var input = new Parameter("input", Object);

        var entry = new Block(0);
        entry.Add(At(new StoreLocal(
            0,
            First,
            new IsInstance(First, new LoadArgument(0, input))), 1));
        entry.Add(At(new StoreLocal(
            1,
            Second,
            new IsInstance(Second, new LoadArgument(0, input))), 3));
        entry.Add(new Branch(10));

        var first = new Block(10);
        first.Add(Observe(0, First, 11));
        first.Add(Observe(1, Second, 13));
        first.Add(new Branch(20));

        var between = new Block(20);
        between.Add(At(new StoreLocal(
            2,
            First,
            new IsInstance(First, new LoadArgument(0, input))), 21));
        between.Add(At(new StoreLocal(
            3,
            Second,
            new IsInstance(Second, new LoadArgument(0, input))), 23));
        between.Add(new Branch(30));

        var second = new Block(30);
        second.Add(Observe(2, First, 31));
        second.Add(Observe(3, Second, 33));
        second.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(entry);
        body.Add(first);
        body.Add(between);
        body.Add(second);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [input], false, 0),
            [First, Second, First, Second],
            body)
        {
            LocalNames = ["left", "right", "left", "right"],
            LocalDeclaredInNestedScope = [true, true, true, true],
            LocalDeclarationBindings =
            [
                new PdbLocalDeclaration(
                    1, 1, 0, "left", new LocalSlotScope(10, 20), LocalVariableAttributes.None),
                new PdbLocalDeclaration(
                    2, 2, 1, "right", new LocalSlotScope(10, 20), LocalVariableAttributes.None),
                new PdbLocalDeclaration(
                    3, 3, 2, "left", new LocalSlotScope(30, 40), LocalVariableAttributes.None),
                new PdbLocalDeclaration(
                    4, 4, 3, "right", new LocalSlotScope(30, 40), LocalVariableAttributes.None),
            ],
        };
    }

    static IrNode Observe(int index, TypeRef type, int offset, bool address = false)
    {
        IrExpression value = address
            ? new LoadLocalAddress(index, type)
            : new LoadLocal(index, type);
        value.SetSourceOffset(offset);
        return At(new ExpressionStatement(new Call(
            new MethodRef(Owner, "Observe", Void, [type], false),
            false,
            [value])), offset);
    }

    static T At<T>(T node, int offset) where T : IrNode
    {
        node.SetSourceOffset(offset);
        return node;
    }
}
