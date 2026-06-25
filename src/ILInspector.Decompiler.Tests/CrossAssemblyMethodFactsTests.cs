using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

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
    public void PlatformForwardedByRefMemberRef_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, locator: TrustedPlatformLocator());

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseUri), "TryCreate");
        Assert.Equal(ParameterRefKindFacts.Known, call.Callee.ParameterRefKindsFacts);
        Assert.Collection(
            call.Callee.ParameterRefKinds,
            kind => Assert.Equal(ArgumentRefKind.Value, kind),
            kind => Assert.Equal(ArgumentRefKind.Value, kind),
            kind => Assert.Equal(ArgumentRefKind.Out, kind));
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
    public void CrossAssemblyDelegateConstructor_RecoversDelegateTypeFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var newObject = SingleNewObject(source, nameof(CrossAssemblyFixtureMethods.UseExternalDelegate));

        Assert.Equal(MetadataFactState.Yes, newObject.Constructor.DeclaringTypeIsDelegate);
    }

    [Fact]
    public void CrossAssemblyOperatorMemberRef_RecoversOperatorFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator), "op_Addition");

        Assert.Equal(MetadataFactState.Yes, call.Callee.IsOperator);
    }

    [Fact]
    public void CrossAssemblyOperatorNameLookalike_RecoversNotOperatorFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var addition = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition), "op_Addition");
        var conversion = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit), "op_Implicit");

        Assert.Equal(MetadataFactState.No, addition.Callee.IsOperator);
        Assert.Equal(MetadataFactState.No, conversion.Callee.IsOperator);
    }

    [Fact]
    public void CrossAssemblyOperatorNameLookalike_RendersMethodCall()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        string addition = Print(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition));
        string conversion = Print(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit));

        Assert.Contains(".op_Addition(left, right)", addition);
        Assert.DoesNotContain("left + right", addition);
        Assert.Contains(".op_Implicit(value)", conversion);
        Assert.DoesNotContain("return (int)value;", conversion);
    }

    [Fact]
    public void CrossAssemblyOperatorMemberRef_RendersOperator()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        string output = Print(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator));

        Assert.Contains("left + right", output);
        Assert.DoesNotContain("op_Addition", output);
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

        var externalDelegate = SingleNewObject(source, nameof(CrossAssemblyFixtureMethods.UseExternalDelegate));
        Assert.Equal(MetadataFactState.Unknown, externalDelegate.Constructor.DeclaringTypeIsDelegate);

        var operatorLike = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition), "op_Addition");
        Assert.Equal(MetadataFactState.Unknown, operatorLike.Callee.IsOperator);
    }

    static string Print(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, "ExternalFacts.Consumer", methodName);
        Assert.NotNull(function);
        function.CheckInvariant();
        return CSharpPrinter.Print(function!).Output ?? "";
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

    static NewObject SingleNewObject(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, "ExternalFacts.Consumer", methodName);
        Assert.NotNull(function);
        function.CheckInvariant();
        return Assert.Single(function!.Descendants.OfType<NewObject>());
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

                    public delegate int ExternalDelegate(int value);

                    public static class DelegateLibrary
                    {
                        public static int DelegateTarget(int value) => value + 1;
                    }

                    public static class OperatorLikeLibrary
                    {
                        public static int op_Addition(int left, int right) => left - right;
                        public static int op_Implicit(int value) => value + 1;
                    }

                    public readonly struct ExternalNumber
                    {
                        public ExternalNumber(int value) => Value = value;
                        public int Value { get; }
                        public static ExternalNumber operator +(ExternalNumber left, ExternalNumber right)
                            => new(left.Value + right.Value);
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

                        public static ExternalDelegate UseExternalDelegate()
                            => DelegateLibrary.DelegateTarget;

                        public static int UseOperatorLikeAddition(int left, int right)
                            => OperatorLikeLibrary.op_Addition(left, right);

                        public static int UseOperatorLikeImplicit(int value)
                            => OperatorLikeLibrary.op_Implicit(value);

                        public static ExternalNumber UseRealOperator(ExternalNumber left, ExternalNumber right)
                            => left + right;

                        public static bool UseUri(string value)
                            => System.Uri.TryCreate(value, System.UriKind.Absolute, out var uri) && uri is not null;
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

    static AssemblyLocator TrustedPlatformLocator()
    {
        var assemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return (name, trust) =>
            trust == AssemblyTrust.Platform && assemblies.TryGetValue(name, out var path)
                ? path
                : null;
    }

    static class CrossAssemblyFixtureMethods
    {
        public const string UseOut = nameof(UseOut);
        public const string UseRef = nameof(UseRef);
        public const string UseIn = nameof(UseIn);
        public const string UseGenerated = nameof(UseGenerated);
        public const string UseExternalDelegate = nameof(UseExternalDelegate);
        public const string UseOperatorLikeAddition = nameof(UseOperatorLikeAddition);
        public const string UseOperatorLikeImplicit = nameof(UseOperatorLikeImplicit);
        public const string UseRealOperator = nameof(UseRealOperator);
        public const string UseUri = nameof(UseUri);
    }
}
