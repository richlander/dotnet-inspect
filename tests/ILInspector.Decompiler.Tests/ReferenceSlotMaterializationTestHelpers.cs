using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

internal static class ReferenceSlotMaterializationTestHelpers
{
    internal static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");

    internal static void AssertMaterializes(
        MetadataSource source, string type, string method, TypeRef targetType, string keyword)
    {
        var function = RaiseToMaterialization(source, type, method);
        var slots = SlotMaterializationPass.Analyze(function)
            .Where(decision => decision.WillMaterialize && targetType.Equals(decision.Type))
            .Select(decision => decision.Slot).ToHashSet();
        Assert.NotEmpty(slots);

        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.DoesNotContain(CoercionSinks.ScopeNodes(function.Body),
            node => node is StoreStackSlot store && slots.Contains(store.Slot)
                || node is LoadStackSlot load && slots.Contains(load.Slot));
        Assert.Contains($"{keyword} S_", CSharpPrinter.Print(function).Output);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    internal static IrFunction RaiseToMaterialization(MetadataSource source, string type, string method)
    {
        var function = IrImporter.Import(source, type, method);
        Assert.NotNull(function);
        var context = PassContext.ForImport(reference => IrImporter.Import(source, reference),
            source.AreProvablyDisjoint);
        foreach (var pass in IrPasses.Default)
        {
            if (pass is SlotMaterializationPass)
                break;
            pass.Run(function, context);
        }
        return function;
    }

    internal static void AssertRetained(IrFunction function)
    {
        var nodes = function.Descendants.Where(node => node is StoreStackSlot or LoadStackSlot).ToArray();
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Empty(function.Locals);
        Assert.All(nodes, node => Assert.Contains(node, function.Descendants));
        function.CheckInvariant();
    }

    internal static IrFunction Function(TypeRef returnType, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("M", Owner,
            new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0), [], body);
    }
}
