using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public class LibraryBodyIndexTests
{
    [Fact]
    public void FindCalls_FindsConsoleWriteLine()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method("System.Console", "WriteLine"));

        var call = Assert.Single(calls.Where(c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));
        Assert.Equal(CallKind.Call, call.Kind);
        Assert.Equal(TypeRef.CoreLib("System", "String"), Assert.Single(call.Callee.ParameterTypes));
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void DirectCalls_RecordVirtualCallEvidenceWithoutInferringTargets()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsVirtualToString)
            && c.Callee.DeclaringType.Equals(TypeRef.CoreLib("System", "Object"))
            && c.Callee.Name == "ToString"));

        Assert.Equal(CallKind.CallVirtual, call.Kind);
    }

    [Fact]
    public void MemberReferences_InstantiateGenericDeclaringTypeArguments()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsListAdd)
            && c.Callee.Name == "Add"));

        Assert.Equal("System.Collections.Generic.List<int>", call.Callee.DeclaringType.ToQualifiedDisplayString());
        Assert.Equal(TypeRef.CoreLib("System", "Int32"), Assert.Single(call.Callee.ParameterTypes));
    }

    [Fact]
    public void TypeIdentity_CanonicalizesCoreLibraryFacadeAssemblies()
    {
        Assert.Equal(
            TypeRef.CoreLib("System", "String"),
            TypeRef.Definition("System.Runtime", "System", "String"));
    }

    [Fact]
    public void DisplayStrings_RenderDecimalKeyword()
    {
        Assert.Equal("decimal", TypeRef.CoreLib("System", "Decimal").ToDisplayString());
    }

    [Fact]
    public void FindCalls_CanMatchFullParameterShape()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method(
            TypeRef.Definition("System.Console", "System", "Console"),
            "WriteLine",
            ImmutableArray.Create(TypeRef.CoreLib("System", "String"))));

        Assert.Contains(calls, c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine));
    }

    [Fact]
    public void Open_DoesNotKeepAssemblyFileLocked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"analysis-lock-{Guid.NewGuid():N}.dll");
        File.Copy(typeof(CallSiteFixtures).Assembly.Location, path);
        try
        {
            var index = LibraryBodyIndex.Open(path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.NotEmpty(index.Methods);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsafeEvidence_FindsSignatureOperationsAndUnsafeCalls()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence.Reason == "Unsafe signature"
            && evidence.Detail.Contains("int*", StringComparison.Ordinal));
        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence is { Reason: "Unsafe operation", Detail: "ldind.i4", Kind: "opcode", ILOffset: not null });
        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)
            && evidence.Reason == "Unsafe call"
            && evidence.Detail.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", StringComparison.Ordinal)
            && evidence.OperandToken is not null);
        Assert.DoesNotContain(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.PInvokeOnly));
    }
}

public static class CallSiteFixtures
{
    public static void CallsConsoleWriteLine() => Console.WriteLine("hello");

    public static string? CallsVirtualToString(object value) => value.ToString();

    public static void CallsListAdd(List<int> values) => values.Add(42);
}

public static partial class UnsafeEvidenceFixtures
{
    public static unsafe int UnsafePointerRead(int* value) => *value;

    public static uint CallsUnsafeAs(ref int value) => Unsafe.As<int, uint>(ref value);

    [DllImport("kernel32.dll")]
    public static extern int PInvokeOnly();
}
