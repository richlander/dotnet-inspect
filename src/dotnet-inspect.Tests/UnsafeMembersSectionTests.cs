using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class UnsafeMembersSectionTests
{
    [Fact]
    public async Task LibraryUnsafeMembers_IncludesSignaturesCallsAndOpcodes()
    {
        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = typeof(SampleUnsafeClass).Assembly.Location,
            IncludeSections = [ "Unsafe Members" ],
            Markdown = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Members", result.Output);
        Assert.Contains("`DotnetInspector.Tests.SampleUnsafeClass.UnsafePointerMethod(int*)`", result.Output);
        Assert.Contains("| Unsafe signature |", result.Output);
        Assert.Contains("| Unsafe operation | `ldind.i4` | opcode |", result.Output);
        Assert.Contains("`DotnetInspector.Tests.SampleUnsafeClass.CallsUnsafeAs(ref int)`", result.Output);
        Assert.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", result.Output);
        Assert.DoesNotContain("SamplePInvokeClass.GetCurrentProcessId", result.Output);
    }

    [Fact]
    public async Task MemberUnsafeOperations_ShowsSelectedMemberEvidence()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.UnsafePointerMethod)],
            IncludeSections = [SectionNames.UnsafeOperations],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Unsafe Operations", result.Output);
        Assert.Contains("| Unsafe signature | `int*` | signature |", result.Output);
        Assert.Contains("| Unsafe operation | `ldind.i4` | opcode |", result.Output);
    }

    [Fact]
    public async Task MemberUnsafeOperations_DiscoverListsColumns()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(SampleUnsafeClass).FullName,
            AssemblyPath = typeof(SampleUnsafeClass).Assembly.Location,
            MemberFilter = [nameof(SampleUnsafeClass.CallsUnsafeAs)],
            IncludeSections = [SectionNames.UnsafeOperations],
            Discover = ["Unsafe Operations"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            OneLine = true,
            Tsv = true,
            OneLineExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Reason\tcolumn", result.Output);
        Assert.Contains("Detail\tcolumn", result.Output);
        Assert.Contains("IL\tcolumn", result.Output);
        Assert.Contains("Token\tcolumn", result.Output);
    }
}
