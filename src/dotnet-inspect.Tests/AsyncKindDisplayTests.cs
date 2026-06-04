using DotnetInspector.Models;
using DotnetInspector.Metadata;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Validates the "Async Kind" row in the Library Info section. The kind reflects the public
/// async surface (matching the "Async Methods" section): "Runtime" (impl-flag async only),
/// "State machine" (compiler state-machine async only), "Mixed" (both), and "None" when the
/// assembly exposes no public async methods.
/// </summary>
public class AsyncKindDisplayTests
{
    private static string SerializeFull(LibraryInspection inspection)
    {
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
        Assert.Contains("| Async Kind | Runtime |", SerializeFull(MakeInspection(runtime: true, stateMachine: false)));
    }

    [Fact]
    public void AsyncKind_StateMachineOnly_ShowsStateMachine()
    {
        Assert.Contains("| Async Kind | State machine |", SerializeFull(MakeInspection(runtime: false, stateMachine: true)));
    }

    [Fact]
    public void AsyncKind_Both_ShowsMixed()
    {
        Assert.Contains("| Async Kind | Mixed |", SerializeFull(MakeInspection(runtime: true, stateMachine: true)));
    }

    [Fact]
    public void AsyncKind_Neither_ShowsNone()
    {
        Assert.Contains("| Async Kind | None |", SerializeFull(MakeInspection(runtime: false, stateMachine: false)));
    }
}
