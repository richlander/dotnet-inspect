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

    [Fact]
    public void UnsafeEvidence_ClassifiesMembersDeclaredOnUnsafeApi()
    {
        var index = LibraryBodyIndex.Open(typeof(Unsafe).Assembly.Location);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.DeclaringType is { Namespace: "System.Runtime.CompilerServices", Name: "Unsafe" }
            && evidence.Member.Name == "Add"
            && evidence is { Reason: "Unsafe API member", Kind: "api" });
    }
}

public class CallTreeTests
{
    static readonly LibraryBodyIndex Index = LibraryBodyIndex.Open(typeof(CallTreeFixtures).Assembly.Location);

    static int Token(string methodName)
        => Index.Methods
            .First(method => method.DeclaringType.Name == nameof(CallTreeFixtures) && method.Name == methodName)
            .MetadataToken;

    static CallTreeNode? Find(CallTreeNode node, string memberName)
    {
        if (node.Member.Name == memberName)
            return node;
        foreach (var child in node.Children)
            if (Find(child, memberName) is { } match)
                return match;
        return null;
    }

    [Fact]
    public void BuildCallTree_ExpandsInAssemblyChain()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 5, maxNodes: 100);

        Assert.Equal(nameof(CallTreeFixtures.Root), tree.Member.Name);
        var one = Assert.Single(tree.Children, child => child.Member.Name == nameof(CallTreeFixtures.LevelOne));
        var two = Assert.Single(one.Children);
        Assert.Equal(nameof(CallTreeFixtures.LevelTwo), two.Member.Name);
        var three = Assert.Single(two.Children);
        Assert.Equal(nameof(CallTreeFixtures.LevelThree), three.Member.Name);
    }

    [Fact]
    public void BuildCallTree_StopsAtDepthLimit()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 2, maxNodes: 100);

        var two = Find(tree, nameof(CallTreeFixtures.LevelTwo));
        Assert.NotNull(two);
        Assert.Equal(CallTreeStatus.DepthLimited, two!.Status);
        Assert.Empty(two.Children);
        Assert.Null(Find(tree, nameof(CallTreeFixtures.LevelThree)));
    }

    [Fact]
    public void BuildCallTree_RecordsCrossAssemblyCalleesAsExternalLeaves()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.LevelThree)), maxDepth: 5, maxNodes: 100);

        var external = Assert.Single(tree.Children);
        Assert.Equal("WriteLine", external.Member.Name);
        Assert.Equal(CallTreeStatus.External, external.Status);
        Assert.Empty(external.Children);
    }

    [Fact]
    public void BuildCallTree_MarksCyclesAsAlreadyShown()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Ping)), maxDepth: 5, maxNodes: 100);

        var pong = Assert.Single(tree.Children);
        Assert.Equal(nameof(CallTreeFixtures.Pong), pong.Member.Name);
        var pingAgain = Assert.Single(pong.Children);
        Assert.Equal(nameof(CallTreeFixtures.Ping), pingAgain.Member.Name);
        Assert.Equal(CallTreeStatus.AlreadyShown, pingAgain.Status);
        Assert.Empty(pingAgain.Children);
    }

    [Fact]
    public void BuildCallTree_TruncatesAtNodeCap()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 5, maxNodes: 2);

        Assert.Equal(CallTreeStatus.Truncated, tree.Status);
        Assert.Single(tree.Children);
    }

    [Fact]
    public void BuildCallTree_LeafMethodHasNoChildren()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Leaf)), maxDepth: 5, maxNodes: 100);

        Assert.Equal(CallTreeStatus.Leaf, tree.Status);
        Assert.Empty(tree.Children);
    }
}

public static class CallSiteFixtures
{
    public static void CallsConsoleWriteLine() => Console.WriteLine("hello");

    public static string? CallsVirtualToString(object value) => value.ToString();

    public static void CallsListAdd(List<int> values) => values.Add(42);
}

public static class CallTreeFixtures
{
    public static void Root()
    {
        LevelOne();
        External();
    }

    public static void LevelOne() => LevelTwo();

    public static void LevelTwo() => LevelThree();

    public static void LevelThree() => Console.WriteLine("leaf");

    public static void External() => Console.WriteLine("external");

    public static void Ping() => Pong();

    public static void Pong() => Ping();

    public static void Leaf() { }
}

public static partial class UnsafeEvidenceFixtures
{
    public static unsafe int UnsafePointerRead(int* value) => *value;

    public static uint CallsUnsafeAs(ref int value) => Unsafe.As<int, uint>(ref value);

    [DllImport("kernel32.dll")]
    public static extern int PInvokeOnly();
}
