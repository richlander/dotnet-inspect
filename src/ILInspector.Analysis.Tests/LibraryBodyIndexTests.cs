using System.Collections.Immutable;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public class LibraryBodyIndexTests
{
    [Fact]
    public void FindCalls_FindsConsoleWriteLine()
    {
        using var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method("System.Console", "WriteLine"));

        var call = Assert.Single(calls.Where(c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));
        Assert.Equal(CallKind.Call, call.Kind);
        Assert.Equal(TypeRef.CoreLib("System", "String"), Assert.Single(call.Callee.ParameterTypes));
    }

    [Fact]
    public void DirectCalls_RecordVirtualCallEvidenceWithoutInferringTargets()
    {
        using var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsVirtualToString)
            && c.Callee.DeclaringType.Equals(TypeRef.CoreLib("System", "Object"))
            && c.Callee.Name == "ToString"));

        Assert.Equal(CallKind.CallVirtual, call.Kind);
    }

    [Fact]
    public void MemberReferences_InstantiateGenericDeclaringTypeArguments()
    {
        using var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

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
    public void FindCalls_CanMatchFullParameterShape()
    {
        using var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method(
            TypeRef.Definition("System.Console", "System", "Console"),
            "WriteLine",
            ImmutableArray.Create(TypeRef.CoreLib("System", "String"))));

        Assert.Contains(calls, c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine));
    }
}

public static class CallSiteFixtures
{
    public static void CallsConsoleWriteLine() => Console.WriteLine("hello");

    public static string? CallsVirtualToString(object value) => value.ToString();

    public static void CallsListAdd(List<int> values) => values.Add(42);
}
