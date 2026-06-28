using System.Collections.Immutable;
using System.Reflection;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class MemberNameCollisionRenderingTests
{
    [Fact]
    public void PropertyReceiverNamedLikeItsType_QualifiesThis()
    {
        string body = Render(typeof(ListNamePropertySpecimen), "get_Count");

        Assert.Contains("return this.List.Count;", body);
        AssertBodyCompiles("""
            using System.Collections.Generic;
            public class __Gate
            {
                private List<int> List { get; } = new();
                public int M()
                {
            """, body);
    }

    [Fact]
    public void FieldReceiverNamedLikeItsType_QualifiesThis()
    {
        string body = Render(typeof(ListNameFieldSpecimen), "get_Count");

        Assert.Contains("return this.List.Count;", body);
        AssertBodyCompiles("""
            using System.Collections.Generic;
            public class __Gate
            {
                private readonly List<int> List = new();
                public int M()
                {
            """, body);
    }

    [Fact]
    public void MemberInfoReceiverNamedLikeItsType_QualifiesThis()
    {
        string body = Render(typeof(MemberInfoNameSpecimen), "get_Name");

        Assert.Contains("return this.MemberInfo.Name;", body);
        AssertBodyCompiles("""
            using System;
            using System.Reflection;
            public class __Gate
            {
                public MethodInfo MemberInfo { get; set; } =
                    typeof(string).GetMethod(nameof(string.ToString), Type.EmptyTypes)!;
                public string M()
                {
            """, body);
    }

    [Fact]
    public void TypeReceiverNamedLikeItsType_QualifiesThis()
    {
        string body = Render(typeof(TypeNameSpecimen), nameof(TypeNameSpecimen.IsAssignableTo));

        Assert.Contains("return this.Type.IsAssignableTo(type);", body);
        AssertBodyCompiles("""
            using System;
            public class __Gate
            {
                public Type Type { get; } = typeof(string);
                public bool M(Type type)
                {
            """, body);
    }

    static string Render(Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static void AssertBodyCompiles(string prefix, string body)
    {
        string source = $$"""
            {{prefix}}
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                references.Add(MetadataReference.CreateFromFile(path));
        }
        return references.ToImmutable();
    }
}

public class ListNamePropertySpecimen
{
    private List<int> List { get; } = new();
    public int Count => List.Count;
}

public class ListNameFieldSpecimen
{
    readonly List<int> List = new();
    public int Count => List.Count;
}

public class MemberInfoNameSpecimen
{
    public MethodInfo MemberInfo { get; set; } =
        typeof(string).GetMethod(nameof(string.ToString), Type.EmptyTypes)!;

    public string Name => MemberInfo.Name;
}

public class TypeNameSpecimen
{
    public Type Type { get; } = typeof(string);

    public bool IsAssignableTo(Type type) => Type.IsAssignableTo(type);
}
