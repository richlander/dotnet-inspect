using DotnetInspector.Models;
using ILInspector.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Validates the "Async Kind" row in the Signals section. The kind reflects the public
/// async surface (matching the "Async Methods" section): "Runtime" (impl-flag async only),
/// "State machine" (compiler state-machine async only), "Mixed" (both), and "None" when the
/// assembly exposes no public async methods.
/// </summary>
public class AsyncKindDisplayTests
{
    private static string SerializeFull(LibraryInspection inspection)
    {
        inspection.UnsafeMethods = [];
        inspection.PInvokeMethods = [];
        inspection.AsyncMethods = [];
        AuditSignalBuilder.PopulateLibraryAudit(
            typeof(AsyncKindDisplayTests).Assembly.Location,
            inspection,
            new VerboseLogger(false));

        var view = new LibraryInspectionView(inspection);
        return MarkoutSerializer.Serialize(view, InspectionContext.Default).TrimEnd();
    }

    private static LibraryInspection MakeInspection(bool runtime, bool stateMachine) => new()
    {
        FileName = "Test.dll",
        FileType = "dll",
        FileSize = 1024,
        AssemblyInfo = new AssemblyInfo { AssemblyName = "Test", AssemblyVersion = "1.0.0.0" },
        HasRuntimeAsync = runtime,
        HasStateMachineAsync = stateMachine,
    };

    [Fact]
    public void AsyncKind_RuntimeOnly_ShowsRuntime()
    {
        var output = SerializeFull(MakeInspection(runtime: true, stateMachine: false));

        Assert.Contains("| Compatibility | Async Kind | Runtime | public async method classification |", output);
        Assert.DoesNotContain("| Async Kind | Runtime |", BeforeSignals(output));
    }

    [Fact]
    public void AsyncKind_StateMachineOnly_ShowsStateMachine()
    {
        var output = SerializeFull(MakeInspection(runtime: false, stateMachine: true));

        Assert.Contains("| Compatibility | Async Kind | State machine | public async method classification |", output);
        Assert.DoesNotContain("| Async Kind | State machine |", BeforeSignals(output));
    }

    [Fact]
    public void AsyncKind_Both_ShowsMixed()
    {
        var output = SerializeFull(MakeInspection(runtime: true, stateMachine: true));

        Assert.Contains("| Compatibility | Async Kind | Mixed | public async method classification |", output);
        Assert.DoesNotContain("| Async Kind | Mixed |", BeforeSignals(output));
    }

    [Fact]
    public void AsyncKind_Neither_ShowsNone()
    {
        var output = SerializeFull(MakeInspection(runtime: false, stateMachine: false));

        Assert.Contains("| Compatibility | Async Kind | None | public async method classification |", output);
        Assert.DoesNotContain("| Async Kind | None |", BeforeSignals(output));
    }

    private static string BeforeSignals(string output)
    {
        var marker = output.IndexOf("## Signals", StringComparison.Ordinal);
        return marker >= 0 ? output[..marker] : output;
    }
}
