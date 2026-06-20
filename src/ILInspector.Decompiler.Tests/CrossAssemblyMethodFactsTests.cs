using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class CrossAssemblyMethodFactsTests
{
    [Fact]
    public void CrossAssemblyByRefMemberRef_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseOut), "WriteOut", ArgumentRefKind.Out);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseRef), "Mutate", ArgumentRefKind.Ref);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseIn), "Read", ArgumentRefKind.In);
    }

    [Fact]
    public void CrossAssemblyGeneratedMemberRef_RecoversCompilerGeneratedFacts()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseGenerated), "Run");

        Assert.Equal(MetadataFactState.Yes, call.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Yes, call.Callee.CompilerGenerated);
    }

    [Fact]
    public void MissingCrossAssemblyMetadata_KeepsFactsUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, locator: (_, _) => null);

        var byRef = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOut), "WriteOut");
        Assert.Equal(ParameterRefKindFacts.Unknown, byRef.Callee.ParameterRefKindsFacts);
        Assert.Empty(byRef.Callee.ParameterRefKinds);

        var generated = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseGenerated), "Run");
        Assert.Equal(MetadataFactState.Unknown, generated.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Unknown, generated.Callee.CompilerGenerated);
    }

    static void AssertCallRefKind(MetadataSource source, string methodName, string calleeName, ArgumentRefKind expected)
    {
        var call = SingleCall(source, methodName, calleeName);
        Assert.Equal(ParameterRefKindFacts.Known, call.Callee.ParameterRefKindsFacts);
        Assert.Equal(expected, Assert.Single(call.Callee.ParameterRefKinds));
    }

    static Call SingleCall(MetadataSource source, string methodName, string calleeName)
    {
        var function = IrImporter.Import(source, "ExternalFacts.Consumer", methodName);
        Assert.NotNull(function);
        function.CheckInvariant();
        return Assert.Single(function!.Descendants.OfType<Call>(), c => c.Callee.Name == calleeName);
    }

    sealed class CrossAssemblyFixture : IDisposable
    {
        readonly string _directory;

        CrossAssemblyFixture(string directory, string consumerPath)
        {
            _directory = directory;
            ConsumerPath = consumerPath;
        }

        public string ConsumerPath { get; }

        public static CrossAssemblyFixture Create()
        {
            var directory = Directory.CreateTempSubdirectory("dotnet-inspect-method-facts-").FullName;
            try
            {
                string libraryPath = Emit(
                    directory,
                    "ExternalFacts.Library",
                    """
                    using System.Runtime.CompilerServices;

                    namespace ExternalFacts;

                    public static class ByRefLibrary
                    {
                        public static void WriteOut(out int value) => value = 42;
                        public static void Mutate(ref int value) => value++;
                        public static void Read(in int value) { _ = value; }
                    }

                    [CompilerGenerated]
                    public static class Generated__DisplayClass0_0
                    {
                        [CompilerGenerated]
                        public static int Run(int value) => value + 1;
                    }
                    """);
                string consumerPath = Emit(
                    directory,
                    "ExternalFacts.Consumer",
                    """
                    namespace ExternalFacts;

                    public static class Consumer
                    {
                        public static int UseOut()
                        {
                            ByRefLibrary.WriteOut(out var value);
                            return value;
                        }

                        public static int UseRef()
                        {
                            int value = 1;
                            ByRefLibrary.Mutate(ref value);
                            return value;
                        }

                        public static void UseIn()
                        {
                            int value = 1;
                            ByRefLibrary.Read(in value);
                        }

                        public static int UseGenerated(int value)
                            => Generated__DisplayClass0_0.Run(value);
                    }
                    """,
                    [MetadataReference.CreateFromFile(libraryPath)]);
                return new CrossAssemblyFixture(directory, consumerPath);
            }
            catch
            {
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        static string Emit(string directory, string assemblyName, string source, IEnumerable<MetadataReference>? additionalReferences = null)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var references = ImmutableArray.CreateBuilder<MetadataReference>();
            references.AddRange(RuntimeReferences());
            if (additionalReferences is not null)
                references.AddRange(additionalReferences);

            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source, parseOptions)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            string path = Path.Combine(directory, assemblyName + ".dll");
            var result = compilation.Emit(path);
            Assert.True(
                result.Success,
                "fixture compilation failed:\n" + string.Join("\n", result.Diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}")));
            return path;
        }

        static ImmutableArray<MetadataReference> RuntimeReferences()
        {
            var trustedPlatformAssemblies =
                (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var references = ImmutableArray.CreateBuilder<MetadataReference>();
            foreach (var path in trustedPlatformAssemblies)
            {
                if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                try { references.Add(MetadataReference.CreateFromFile(path)); }
                catch { }
            }
            return references.ToImmutable();
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    static class CrossAssemblyFixtureMethods
    {
        public const string UseOut = nameof(UseOut);
        public const string UseRef = nameof(UseRef);
        public const string UseIn = nameof(UseIn);
        public const string UseGenerated = nameof(UseGenerated);
    }
}
