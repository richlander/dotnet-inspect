using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The lock-sugar pass: the csc Monitor lockTaken lowering raises to a
/// lock (obj) { ... } statement, the synthetic V_object/V_taken locals
/// disappear, and the lock object is the original expression.
/// </summary>
public class LockSugarTests
{
    static string RaiseLock(string methodName)
    {
        using var source = MetadataSource.Open(typeof(LockFixtureSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LockFixtureSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n").TrimEnd();
    }

    [Fact]
    public void VoidLock_RaisesToLockStatement()
    {
        var (function, output) = (IrImportFor(nameof(LockFixtureSamples.IncrementUnderLock)), RaiseLock(nameof(LockFixtureSamples.IncrementUnderLock)));

        Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());            // the try/finally is consumed
        Assert.DoesNotContain("Monitor", output);                          // no Monitor.Enter/Exit left
        Assert.Matches(@"lock \(_root\)", output);
        Assert.DoesNotContain("bool V_", output);                          // the lockTaken local is gone
    }

    [Fact]
    public void LockOnParameter_UsesParameterExpression()
    {
        Assert.Contains("lock (gate)", RaiseLock(nameof(LockFixtureSamples.LockOnParameter)));
    }

    [Fact]
    public void LockBody_IsStillRaised()
    {
        // The body inside the lock continues through later passes.
        string output = RaiseLock(nameof(LockFixtureSamples.ReadUnderLock));
        Assert.Contains("lock (_root)", output);
        Assert.DoesNotContain("Monitor", output);
    }

    [Fact]
    public void StaticFieldLock_PreservesCopiedReceiverLocal()
    {
        var function = IrImportFor(nameof(LockFixtureSamples.IncrementStaticUnderLock));

        var lockNode = Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        var lockObject = Assert.IsType<LoadLocal>(lockNode.LockObject);
        Assert.Contains(function.Descendants.OfType<StoreLocal>(), store => store.Index == lockObject.Index);

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Matches(@"lock \(V_\d+\)", output);
    }

    static IrFunction IrImportFor(string methodName)
    {
        using var source = MetadataSource.Open(typeof(LockFixtureSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LockFixtureSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        return function;
    }
}
