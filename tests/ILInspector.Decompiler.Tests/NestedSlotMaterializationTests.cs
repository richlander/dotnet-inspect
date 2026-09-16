using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class NestedSlotMaterializationTests
{
    [Theory]
    [InlineData(false, "Microsoft.CodeAnalysis.SyntaxDiffer", "RecordChange", 1)]
    [InlineData(true, "Microsoft.CodeAnalysis.CSharp.OverloadResolution", "BetterConversionTargetCore", 1)]
    public void RealOuterSlotMaterializesIndependentlyOfNestedStorage(
        bool csharp, string typeName, string methodName, int overloadIndex)
    {
        string path = csharp
            ? typeof(CSharpSyntaxTree).Assembly.Location
            : typeof(SyntaxTree).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
        Assert.NotNull(function);
        var context = PassContext.ForImport(reference => IrImporter.Import(source, reference));
        foreach (var pass in IrPasses.Default)
        {
            if (pass is SlotMaterializationPass)
                break;
            pass.Run(function, context);
        }

        var decisions = SlotMaterializationPass.Analyze(function);
        var outer = Assert.Single(decisions, decision => decision.Slot == 256
            && ReferenceEquals(decision.Scope, function));
        Assert.True(outer.WillMaterialize);
        var nested = decisions.Where(decision => decision.Slot == outer.Slot
            && !ReferenceEquals(decision.Scope, function)).ToArray();
        var materializedNested = function.Descendants.Where(scope =>
        {
            int index = scope switch
            {
                Lambda lambda => lambda.SynthesizedLocalNames.IndexOf("S_256"),
                LocalFunctionStatement localFunction => localFunction.SynthesizedLocalNames.IndexOf("S_256"),
                _ => -1,
            };
            return index >= 0 && CoercionSinks.ScopeNodes(scope).OfType<StoreLocal>()
                .Any(store => store.Index == index);
        }).ToArray();
        if (csharp)
            Assert.NotEmpty(materializedNested);
        else
            Assert.NotEmpty(nested);
        Assert.All(nested, decision => Assert.Equal(SlotMaterializationVeto.NestedScope, decision.Vetoes));
        var retainedNodes = nested.Select(decision => decision.Scope)
            .Concat(materializedNested)
            .SelectMany(CoercionSinks.ScopeNodes)
            .Where(node => node is StoreStackSlot or LoadStackSlot or StoreLocal or LoadLocal).ToArray();

        new SlotMaterializationPass().Run(function, context);
        new CoercionInsertionPass().Run(function, context);

        Assert.DoesNotContain(CoercionSinks.ScopeNodes(function.Body),
            node => node is StoreStackSlot store && store.Slot == outer.Slot
                || node is LoadStackSlot load && load.Slot == outer.Slot);
        Assert.All(retainedNodes, node => Assert.Contains(node, function.Descendants));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }
}
