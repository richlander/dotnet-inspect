using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

using LegacyUnsafeAsync =
    ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures.UnsafeAsyncFixtures;
using NewUnsafeAsync =
    ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures.UnsafeAsyncFixtures;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Validity")]
public class UnsafeAsyncBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimePointerReceiverAwait_PreservesSafeBoundaryOrDeclines(
        bool usesUpdatedMemorySafetyRules)
    {
        var function = Raise(
            usesUpdatedMemorySafetyRules,
            nameof(NewUnsafeAsync.AwaitPointerReceiver));
        var result = CSharpPrinter.Print(function);

        Assert.Equal(usesUpdatedMemorySafetyRules, function.UsesUpdatedMemorySafetyRules);
        Assert.DoesNotContain(
            function.Descendants.OfType<AwaitExpression>(),
            awaitExpression => RequiresUnsafe(
                awaitExpression.Operand,
                usesUpdatedMemorySafetyRules));
        Assert.DoesNotContain("unsafe\n{\n    return await", result.Output);
        Assert.True(
            result.Fidelity == DecompilationFidelity.Partial
                || result.Output?.Contains(
                    "return await",
                    StringComparison.Ordinal) == true,
            result.Output);
    }

    [Theory]
    [InlineData(false, nameof(NewUnsafeAsync.IfUnsafeConditionAwaitBody), false)]
    [InlineData(false, nameof(NewUnsafeAsync.WhileUnsafeConditionAwaitBody), false)]
    [InlineData(false, nameof(NewUnsafeAsync.DoWhileUnsafeConditionAwaitBody), false)]
    [InlineData(false, nameof(NewUnsafeAsync.SwitchUnsafeSelectorAwaitArms), false)]
    [InlineData(false, nameof(NewUnsafeAsync.UsingUnsafeResourceAwaitBody), false)]
    [InlineData(true, nameof(NewUnsafeAsync.IfUnsafeConditionAwaitBody), true)]
    [InlineData(true, nameof(NewUnsafeAsync.WhileUnsafeConditionAwaitBody), false)]
    [InlineData(true, nameof(NewUnsafeAsync.DoWhileUnsafeConditionAwaitBody), false)]
    [InlineData(true, nameof(NewUnsafeAsync.SwitchUnsafeSelectorAwaitArms), false)]
    [InlineData(true, nameof(NewUnsafeAsync.UsingUnsafeResourceAwaitBody), false)]
    public void RuntimeUnsafeHeaderWithAwaitingBody_PreservesBoundaryOrDeclines(
        bool usesUpdatedMemorySafetyRules,
        string methodName,
        bool expectsVisibleDecline)
    {
        var function = Raise(usesUpdatedMemorySafetyRules, methodName);
        var result = CSharpPrinter.Print(function);

        Assert.Equal(usesUpdatedMemorySafetyRules, function.UsesUpdatedMemorySafetyRules);
        if (expectsVisibleDecline)
        {
            Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
            Assert.Contains(
                function.Descendants.OfType<UnsupportedNode>(),
                node => node.Opcode == "unsafe await boundary");
        }
        else
        {
            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{result.Output}{Environment.NewLine}{IrPrinter.Dump(function)}");
        }
        Assert.DoesNotContain(
            function.Descendants,
            node => node.Parent is Block
                && UnsafeAwaitBoundaryPass.RequiresUnsafeContext(
                    node,
                    usesUpdatedMemorySafetyRules)
                && UnsafeAwaitOperand.ContainsAwait(node));
        Assert.DoesNotContain("unsafe\n{\n    if", result.Output);
        Assert.DoesNotContain("unsafe\n{\n    for", result.Output);
        Assert.DoesNotContain("unsafe\n{\n    do", result.Output);
        Assert.DoesNotContain("unsafe\n{\n    switch", result.Output);
        Assert.DoesNotContain("unsafe\n{\n    using", result.Output);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false, nameof(NewUnsafeAsync.AwaitPointerReceiver))]
    [InlineData(false, nameof(NewUnsafeAsync.IfUnsafeConditionAwaitBody))]
    [InlineData(false, nameof(NewUnsafeAsync.WhileUnsafeConditionAwaitBody))]
    [InlineData(false, nameof(NewUnsafeAsync.DoWhileUnsafeConditionAwaitBody))]
    [InlineData(false, nameof(NewUnsafeAsync.SwitchUnsafeSelectorAwaitArms))]
    [InlineData(false, nameof(NewUnsafeAsync.UsingUnsafeResourceAwaitBody))]
    [InlineData(true, nameof(NewUnsafeAsync.AwaitPointerReceiver))]
    [InlineData(true, nameof(NewUnsafeAsync.IfUnsafeConditionAwaitBody))]
    [InlineData(true, nameof(NewUnsafeAsync.WhileUnsafeConditionAwaitBody))]
    [InlineData(true, nameof(NewUnsafeAsync.DoWhileUnsafeConditionAwaitBody))]
    [InlineData(true, nameof(NewUnsafeAsync.SwitchUnsafeSelectorAwaitArms))]
    [InlineData(true, nameof(NewUnsafeAsync.UsingUnsafeResourceAwaitBody))]
    public void RuntimeUnsafeAwaitBoundary_CompileBackRemainsValid(
        bool usesUpdatedMemorySafetyRules,
        string methodName)
    {
        Type fixtureType = usesUpdatedMemorySafetyRules
            ? typeof(NewUnsafeAsync)
            : typeof(LegacyUnsafeAsync);
        string assembly = fixtureType.Assembly.Location;
        var result = Assert.Single(ReturnToSender.CompileBackTargets(
            assembly,
            [new ReturnToSender.RequestedTarget(
                fixtureType.FullName!.Replace('+', '.'),
                methodName,
                Overload: 0)]));

        Assert.False(
            result.Status is FidelityCheck.CompileBackStatus.RecompileFail
                or FidelityCheck.CompileBackStatus.ContextFail,
            $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
    }

    static IrFunction Raise(
        bool usesUpdatedMemorySafetyRules,
        string methodName)
    {
        Type fixtureType = usesUpdatedMemorySafetyRules
            ? typeof(NewUnsafeAsync)
            : typeof(LegacyUnsafeAsync);
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var method = fixtureType.GetMethod(methodName);
        Assert.NotNull(method);
        var function = IrImporter.Import(source, (MethodDefinitionHandle)
            MetadataTokens.EntityHandle(method.MetadataToken));
        Assert.NotNull(function);
        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        return function;
    }

    static bool RequiresUnsafe(
        IrNode expression,
        bool usesUpdatedMemorySafetyRules)
        => UnsafeAwaitOperand.RequiresUnsafeContext(
            expression,
            usesUpdatedMemorySafetyRules);
}
