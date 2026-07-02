using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class ReturnToSenderPrototypeTests
{
    [Fact]
    public void CompileBackFirstPropertyGetter_RoundTripsMinimalClassProperty()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal("Class1", result.Plan.TargetMethod.Type);
            Assert.Equal("get_Method1", result.Plan.TargetMethod.Method);
            Assert.Contains("public unsafe class Class1", result.Source);
            Assert.Contains("public string Method1", result.Source);
            Assert.Contains("return \"Hello World\";", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_ExposesTypedModuleAndTypeShellPlan()
    {
        var assemblyPath = CompileFixture("""
            namespace Fixtures;

            public class Class1
            {
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);
            var type = Assert.Single(result.Plan.Types);
            var member = Assert.Single(type.Members);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal("Fixtures", type.Namespace);
            Assert.Equal("Class1", type.Name);
            Assert.Equal(ReturnToSender.TypeShellKind.Class, type.Kind);
            Assert.Equal("Method1", member.Name);
            Assert.Equal(ReturnToSender.TypeMemberShellKind.PropertyGet, member.Kind);
            Assert.Equal("string", member.Type);
            Assert.Contains("System", result.Plan.Module.Usings);
            Assert.Empty(result.Plan.Module.AssemblyAttributes);
            Assert.Empty(result.Plan.Module.ModuleAttributes);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackFirstPropertyGetter_UsesDependencyReferencesAndNamespaces()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var dependencyPath = CompileFixture("""
            namespace External;

            public class Greeting
            {
                public static Greeting Create() => new Greeting();
            }
            """, directory, "ExternalLib");
        var assemblyPath = CompileFixture("""
            using External;

            public class Class1
            {
                public Greeting Method1 => Greeting.Create();
            }
            """, directory, "Fixture", [MetadataReference.CreateFromFile(dependencyPath)]);
        try
        {
            var result = ReturnToSender.CompileBackFirstPropertyGetter(assemblyPath);

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("External", result.Plan.Module.Usings);
            Assert.Contains("public External.Greeting Method1", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackPropertyGetters_EvaluatesSupportedGetterLadderWithCap()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public string Method1 => "Hello World";
                public int Count => 42;
                public string this[int index] => index.ToString();
            }

            public readonly struct SkippedStruct
            {
                public int Value => 1;
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2);

            Assert.Collection(
                results,
                first =>
                {
                    Assert.Equal("get_Method1", first.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, first.Status);
                },
                second =>
                {
                    Assert.Equal("get_Count", second.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, second.Status);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackPropertyGetters_PreservesSuccessesAfterFailingTarget()
    {
        var assemblyPath = CompileFixture("""
            public class Helper
            {
            }

            public class Class1
            {
                public Helper SameAssemblyType => new Helper();
                public string Method1 => "Hello World";
            }
            """);
        try
        {
            var results = ReturnToSender.CompileBackPropertyGetters(assemblyPath, maxTargets: 2);

            Assert.Collection(
                results,
                first =>
                {
                    Assert.Equal("get_SameAssemblyType", first.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.RecompileFail, first.Status);
                },
                second =>
                {
                    Assert.Equal("get_Method1", second.Plan.TargetMethod.Method);
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, second.Status);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    static string CompileFixture(
        string source,
        string? directory = null,
        string assemblyName = "fixture",
        IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        directory ??= Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{assemblyName}.dll");
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Concat(additionalReferences ?? []);
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable));

        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    static void DeleteFixture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        File.Delete(assemblyPath);
        if (directory is not null && Path.GetFileName(directory).StartsWith("return-to-sender-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
