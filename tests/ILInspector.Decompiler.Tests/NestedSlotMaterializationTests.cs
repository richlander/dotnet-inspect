using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class NestedSlotMaterializationTests
{
    [Theory]
    [InlineData(false, "Microsoft.CodeAnalysis.SyntaxDiffer", "RecordChange", 1, "CreateQueue")]
    [InlineData(true, "Microsoft.CodeAnalysis.CSharp.OverloadResolution", "BetterConversionTargetCore", 1, null)]
    public void RealOuterSlotMaterializesIndependentlyOfNestedStorage(
        bool csharp, string typeName, string methodName, int overloadIndex, string? residualBoundLocalFunction)
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
        // A nested body binds its own residual webs inside its own pipeline
        // tail (residual storage binding) before the nested node exists, so by
        // the outer materialization position no nested slot-256 decision
        // remains: the nested web is already an S_256 local in the nested body.
        // Where that local is residual-bound rather than materialized, the
        // raised node carries the binding's provenance for hosts and the census.
        Assert.Empty(nested);
        Assert.NotEmpty(materializedNested);
        Assert.All(materializedNested, scope =>
        {
            var (name, index, bound) = scope switch
            {
                Lambda lambda => (
                    (string?)null,
                    lambda.SynthesizedLocalNames.IndexOf("S_256"),
                    lambda.ResidualSlotBindings),
                LocalFunctionStatement localFunction => (
                    localFunction.Name,
                    localFunction.SynthesizedLocalNames.IndexOf("S_256"),
                    localFunction.ResidualSlotBindings),
                _ => throw new InvalidOperationException(),
            };
            int localIndex = index + (scope is Lambda l ? l.Locals.Length - l.SynthesizedLocalNames.Length : ((LocalFunctionStatement)scope).Locals.Length - ((LocalFunctionStatement)scope).SynthesizedLocalNames.Length);
            if (name == residualBoundLocalFunction)
            {
                var binding = Assert.Contains(localIndex, bound);
                Assert.Equal(256, binding.Slot);
            }
            else
            {
                Assert.DoesNotContain(localIndex, bound);
            }
        });
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
