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
/// The EH structuring slice: flat regions raise to TryCatch/TryFinally with
/// consumed regions, entry stores fold into clause variables, tail leaves
/// trim to fallthrough, and the supported typed filter shape raises to
/// catch-when — while out-of-slice EH shapes keep the flat form with regions
/// intact.
/// </summary>
public class EhStructuringTests
{
    static (IrFunction Function, string Output) RaiseFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return (function, result.Output!.ReplaceLineEndings("\n").TrimEnd());
    }

    [Fact]
    public void TryFinally_RaisesToStructuredForm()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TryFinallyAdd));

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("finally", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("endfinally", output);
    }

    [Fact]
    public void TryFinally_MultipleReturns_InlineThroughFinally()
    {
        // The two in-try returns leave to distinct return blocks; the EH pass
        // inlines them so the try/finally raises with the returns in the body
        // and no leftover goto/leave.
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TryFinallyTwoReturns));

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("finally", output);
        Assert.Equal(2, function.Descendants.OfType<Return>().Count());
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("endfinally", output);
    }

    [Fact]
    public void Catch_EntryStore_FoldsIntoClauseVariable()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.CatchLogs));

        // Debug stores the catch variable at handler entry, folding the store into
        // the clause header; Release leaves the once-used exception on the stack and
        // EhStructuring synthesizes the variable back. Either way the region is
        // consumed and the try/catch binds a variable used by the handler body.
        Assert.Empty(function.Regions);
        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.NotNull(clause.VariableIndex);
        Assert.Matches(@"catch \(FormatException V_\d+\)", output);
        Assert.DoesNotContain("__exception", output);
        // The clause owns the declaration — nothing declares the local up front.
        Assert.DoesNotMatch(@"FormatException V_\d+;", output);
        // ldc.i4.m1 must print -1, not the ushort-wrapped 65535.
        Assert.Contains("-1;", output);
        Assert.DoesNotContain("65535", output);
    }

    [Fact]
    public void Catch_DiscardedException_PrintsBareType()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.CatchDiscards));

        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.Null(clause.VariableIndex);
        Assert.Matches(@"(?m)^catch \(FormatException\)$", output);
    }

    [Fact]
    public void CatchAll_PrintsBareCatch()
    {
        var (_, output) = RaiseFixture(nameof(CfgSampleClass.CatchEverything));

        Assert.Matches(@"(?m)^catch$", output);
    }

    [Fact]
    public void Rethrow_PrintsBareThrow()
    {
        var (_, output) = RaiseFixture(nameof(CfgSampleClass.LogAndRethrow));

        Assert.Contains("throw;", output);
        Assert.DoesNotContain("__exception", output);
    }

    [Fact]
    public void MultiCatch_PreservesClauseOrder()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TwoCatches));

        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.Equal(2, tryCatch.Clauses.Count);
        Assert.True(output.IndexOf("FormatException", StringComparison.Ordinal)
            < output.IndexOf("OverflowException", StringComparison.Ordinal));
    }

    [Fact]
    public void TryCatchFinally_TailLeaves_TrimThroughNestedConstructs()
    {
        // The arms of the inner try/catch leave straight past the outer
        // finally; in tail position that is plain fallthrough, so no goto
        // and no label survive.
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.ParseWithCleanup));

        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void Filter_RaisesToCatchWhen()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.FilteredLength));

        Assert.Empty(function.Regions);
        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        var clause = Assert.Single(tryCatch.Clauses);
        Assert.NotNull(clause.Filter);
        Assert.Empty(function.Descendants.OfType<EndFilter>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("catch (Exception e) when (e.Message.Length > 0)", output);
        Assert.DoesNotContain("endfilter", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void Overloads_EnumeratesEverySameNameMethod_IndexAlignsWithImport()
    {
        // Overloads is the harness's --dump disambiguator: it must list every
        // same-name method in metadata order, and each reported index must select
        // that same method through Import(overloadIndex).
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        var overloads = IrImporter.Overloads(source, typeof(CtorChainSamples).FullName!, ".ctor");

        Assert.True(overloads.Count > 1, "fixture should have multiple .ctor overloads");
        // Indices are dense and sequential from 0.
        Assert.Equal(Enumerable.Range(0, overloads.Count), overloads.Select(o => o.Index));
        // Instance constructors: HasThis, and each has a body that Import resolves
        // at the matching index.
        foreach (var overload in overloads)
        {
            Assert.True(overload.HasThis);
            Assert.True(overload.HasBody);
            Assert.NotNull(IrImporter.Import(source, typeof(CtorChainSamples).FullName!, ".ctor", overload.Index));
            Assert.StartsWith("(", overload.Describe());
        }

        // A distinct parameter arity proves the overloads are not aliased.
        Assert.Contains(overloads, o => o.ParameterTypes.Length != overloads[0].ParameterTypes.Length);
    }

    [Fact]
    public void Overloads_UnknownTypeOrMethod_IsEmpty()
    {
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        Assert.Empty(IrImporter.Overloads(source, "No.Such.Type", "Nope"));
        Assert.Empty(IrImporter.Overloads(source, typeof(CtorChainSamples).FullName!, "NoSuchMethod"));
    }
}
