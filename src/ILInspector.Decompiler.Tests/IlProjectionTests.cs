using System.Text.RegularExpressions;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IlProjectionTests
{
    static string Project(string method, IlProjectionDepth depth)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var result = IlProjection.Project(source, typeof(CfgSampleClass).FullName!, method, depth);
        Assert.NotNull(result.Output);
        // Normalize CRLF so line-anchored assertions are platform-agnostic (the
        // projection renders with Environment.NewLine).
        return result.Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void Raw_RendersOffsetsAndOpcodes()
    {
        var output = Project(nameof(CfgSampleClass.Add), IlProjectionDepth.Raw);
        Assert.Contains("IL_0000:", output);
        Assert.Contains("add", output);
        Assert.Contains("ret", output);
    }

    [Fact]
    public void Raw_ResolvesCallOperandToName()
    {
        // LengthOf is `s.Length` — the method token must render resolved (the
        // importer's ResolveMethod), not as a raw `0x06......` token.
        var output = Project(nameof(CfgSampleClass.LengthOf), IlProjectionDepth.Raw);
        Assert.Contains("get_Length", output);
        Assert.DoesNotContain("0x", output);
    }

    [Fact]
    public void Typed_AnnotatesPerInstructionStackTypes()
    {
        // LengthOf is `s.Length`: ldarg.0 leaves [string], get_Length leaves [int].
        // The types come from the importer's stack simulation via the trace hook.
        var output = Project(nameof(CfgSampleClass.LengthOf), IlProjectionDepth.Typed);
        Assert.Contains("// [string]", output);
        Assert.Contains("// [int]", output);
    }

    [Fact]
    public void Structured_LabelsBranchBlocks()
    {
        // A conditional splits into multiple basic blocks, each leader labeled.
        var output = Project(nameof(CfgSampleClass.Pick), IlProjectionDepth.Structured);
        int labels = Regex.Matches(output, @"^IL_[0-9A-F]{4}:$", RegexOptions.Multiline).Count;
        Assert.True(labels >= 2, $"expected >= 2 block labels, got {labels}");
    }

    [Fact]
    public void Structured_MarksExceptionRegions()
    {
        var output = Project(nameof(CfgSampleClass.CatchLogs), IlProjectionDepth.Structured);
        Assert.Contains("// .try", output);
        Assert.Contains("// catch", output);
    }
}
