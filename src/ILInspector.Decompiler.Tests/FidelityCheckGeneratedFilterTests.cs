using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class FidelityCheckGeneratedFilterTests
{
    [Fact]
    public void Evaluate_SkipsGeneratedCodeTypesAndMethods()
    {
        var assemblyPath = CompileFixture("""
            using System.CodeDom.Compiler;

            public class Normal
            {
                public int Echo(int value) => value + 1;
            }

            [GeneratedCode("fixture", "1.0")]
            public class GeneratedType
            {
                public int Hidden() => 42;
            }

            public class Mixed
            {
                [GeneratedCode("fixture", "1.0")]
                public int Hidden() => 42;

                public int Visible() => 7;
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "Normal" && result.Method == "Echo");
            Assert.Contains(results, result => result.Type == "Mixed" && result.Method == "Visible");
            Assert.DoesNotContain(results, result => result.Type == "GeneratedType");
            Assert.DoesNotContain(results, result => result.Type == "Mixed" && result.Method == "Hidden");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_IncludesCompilerGeneratedAutoPropertyAccessors()
    {
        var assemblyPath = CompileFixture("""
            public class AutoPropertyFixture
            {
                public AutoPropertyFixture(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "AutoPropertyFixture" && result.Method == ".ctor");
            Assert.Contains(results, result => result.Type == "AutoPropertyFixture" && result.Method == "get_Value");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_SkipsCompilerGeneratedRecordMethodsButKeepsAccessors()
    {
        var assemblyPath = CompileFixture("""
            public record GeneratedRecord(int Value);
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "GeneratedRecord" && result.Method == "get_Value");
            Assert.DoesNotContain(results, result =>
                result.Type == "GeneratedRecord" &&
                result.Method is "ToString" or "PrintMembers" or "GetHashCode");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsConstructorAssignedAutoProperties()
    {
        var assemblyPath = CompileFixture("""
            public class AutoPropertyPairFixture
            {
                public AutoPropertyPairFixture(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Left { get; }
                public int Right { get; }
            }
            """);
        try
        {
            var ctor = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "AutoPropertyPairFixture" && result.Method == ".ctor");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, ctor.Status);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsStructAutoProperties()
    {
        var assemblyPath = CompileFixture("""
            public readonly struct StructAutoPropertyPairFixture
            {
                public StructAutoPropertyPairFixture(double left, double right)
                {
                    Left = left;
                    Right = right;
                }

                public double Left { get; }
                public double Right { get; }

                public double Sum() => this.Left + this.Right;
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            var ctor = Assert.Single(
                results,
                result => result.Type == "StructAutoPropertyPairFixture" && result.Method == ".ctor");
            var getter = Assert.Single(
                results,
                result => result.Type == "StructAutoPropertyPairFixture" && result.Method == "get_Left");
            var sum = Assert.Single(
                results,
                result => result.Type == "StructAutoPropertyPairFixture" && result.Method == "Sum");

            Assert.True(ctor.Status == FidelityCheck.CompileBackStatus.Exact, ctor.Detail);
            Assert.True(getter.Status == FidelityCheck.CompileBackStatus.Exact, getter.Detail);
            Assert.True(sum.Status == FidelityCheck.CompileBackStatus.Exact, sum.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsStructObjectToStringDispatch()
    {
        var assemblyPath = CompileFixture("""
            public readonly struct StructWithToString
            {
                public override string ToString() => "value";
            }

            public static class StructWithToStringExtensions
            {
                public static string Humanize(this StructWithToString value, string? format)
                {
                    if (!string.IsNullOrWhiteSpace(format))
                        return format;
                    return value.ToString();
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "StructWithToStringExtensions" && result.Method == "Humanize");

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsExtensionMethodForwarding()
    {
        var assemblyPath = CompileFixture("""
            namespace ExtensionForwardingFixture;

            public readonly struct TinyDate
            {
                public override string ToString() => "tiny";
            }

            public static class TinyDateExtensions
            {
                public static string Humanize(this TinyDate input, int style)
                    => input.ToString() + style.ToString();

                public static string Humanize(this TinyDate? input, int style)
                {
                    if (input.HasValue)
                    {
                        return input.Value.Humanize(style);
                    }
                    return "never";
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "ExtensionForwardingFixture.TinyDateExtensions"
                          && result.Method == "Humanize"
                          && result.Signature.Contains("Nullable", StringComparison.Ordinal));

            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void RunMethodDelta_UsesCorpusMetadataForPlatformOutParameters()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;

            public class TargetedDictionaryOutFixture
            {
                public bool Lookup(Dictionary<string, int> dictionary, string key)
                {
                    int value = default;
                    return dictionary.TryGetValue(key, out value);
                }
            }
            """);
        var originalOut = Console.Out;
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "TargetedDictionaryOutFixture" && result.Method == "Lookup");
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact, result.Detail);

            var deltaPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, "delta.json");
            File.WriteAllText(deltaPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                generatedUtc = DateTimeOffset.UtcNow,
                baselineGeneratedUtc = DateTimeOffset.UtcNow,
                currentGeneratedUtc = DateTimeOffset.UtcNow,
                baselineHasMethodDetails = true,
                currentHasMethodDetails = true,
                changedMethods = new[]
                {
                    new
                    {
                        method = "fixture!TargetedDictionaryOutFixture::Lookup#0",
                        assembly = "fixture",
                        assemblyPath = Path.GetFileName(assemblyPath),
                        type = "TargetedDictionaryOutFixture",
                        methodName = "Lookup",
                        overload = 0,
                        signature = result.Signature,
                        baseline = (object?)null,
                        current = new
                        {
                            assembly = "fixture",
                            assemblyPath = Path.GetFileName(assemblyPath),
                            type = "TargetedDictionaryOutFixture",
                            method = "Lookup",
                            overload = 0,
                            signature = result.Signature,
                            fidelity = "Full",
                            fullyRaised = true,
                            residual = (string?)null,
                            passBug = (string?)null,
                            validity = "not-sampled",
                            fidelityCheck = "not-sampled",
                        },
                        deltas = new[] { "triage" },
                    },
                },
            }));

            using var writer = new StringWriter();
            Console.SetOut(writer);
            int exitCode = FidelityCheck.RunMethodDelta([assemblyPath], deltaPath, maxExamples: 5);
            Console.SetOut(originalOut);
            var output = writer.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("exact opcode match : 1", output);
            Assert.DoesNotContain("CS1620", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_BindsNamespacesReferencedByTargetBodies()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Concurrent;
            using System.Collections.Frozen;
            using System.Collections.Generic;
            using System.Text.RegularExpressions;

            public class FrameworkNamespaceFixture
            {
                public ConcurrentDictionary<string, int> CreateConcurrent()
                    => new ConcurrentDictionary<string, int>();

                public FrozenDictionary<string, int> Freeze(Dictionary<string, int> source)
                    => source.ToFrozenDictionary();

                public Match MatchS(string input)
                    => Regex.Match(input, "s");
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(results, "CreateConcurrent");
            AssertCheckable(results, "Freeze");
            AssertCheckable(results, "MatchS");

            Environment.SetEnvironmentVariable("CB_NOGROUP", "1");
            var perMethodResults = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(perMethodResults, "CreateConcurrent");
            AssertCheckable(perMethodResults, "Freeze");
            AssertCheckable(perMethodResults, "MatchS");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CB_NOGROUP", null);
            DeleteFixture(assemblyPath);
        }

        static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string method)
        {
            var result = Assert.Single(
                results,
                result => result.Type == "FrameworkNamespaceFixture" && result.Method == method);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
                result.Detail);
        }
    }

    [Fact]
    public void Evaluate_RetainsAbstractOverloadsForForwardingCalls()
    {
        var assemblyPath = CompileFixture("""
            public abstract class AbstractForwardingFixture
            {
                public enum Gender
                {
                    Neutral,
                }

                public string Convert(long value)
                    => Convert(value, Gender.Neutral, true);

                public string ConvertToOrdinal(int value, bool words)
                    => ConvertToOrdinal(value);

                public abstract string Convert(long value, Gender gender, bool words);
                public abstract string ConvertToOrdinal(int value);
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(results, "Convert");
            AssertCheckable(results, "ConvertToOrdinal");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string method)
        {
            var result = Assert.Single(
                results,
                result => result.Type == "AbstractForwardingFixture" && result.Method == method);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
                result.Detail);
        }
    }

    [Fact]
    public void Evaluate_RetainsExceptionBaseClause()
    {
        var assemblyPath = CompileFixture("""
            using System;

            public class CustomException : Exception
            {
                public CustomException(string message)
                    : base(message)
                {
                }
            }

            public static class BaseAndInterfaceFixture
            {
                public static void ThrowCustom()
                    => throw new CustomException("bad");
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);
            AssertCheckable(results, "ThrowCustom");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string method)
        {
            var result = Assert.Single(
                results,
                result => result.Type == "BaseAndInterfaceFixture" && result.Method == method);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
                result.Detail);
        }
    }

    [Fact]
    public void Evaluate_RetainsSatisfiedInterfaceBaseClause()
    {
        var assemblyPath = CompileFixture("""
            public interface IResource
            {
                string Name { get; }
            }

            public class Resource : IResource
            {
                public virtual string Name => "resource";
            }

            public sealed class ConnectionStringResource : Resource
            {
            }

            public interface IResourceBuilder<T>
                where T : IResource
            {
            }

            public static class ResourceBuilderFactory
            {
                public static IResourceBuilder<ConnectionStringResource> Create()
                    => throw null;
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "ResourceBuilderFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsProtobufSelfMessageInterfaceClause()
    {
        var assemblyPath = CompileFixture("""
            namespace Google.Protobuf.Reflection
            {
                public class MessageDescriptor { }
            }

            namespace Google.Protobuf
            {
                public interface IMessage
                {
                    Google.Protobuf.Reflection.MessageDescriptor Descriptor { get; }
                }

                public interface IMessage<T> : IMessage
                    where T : IMessage<T>
                {
                }

                public class MessageParser<T>
                    where T : IMessage<T>
                {
                }
            }

            namespace Fixture
            {
                public sealed class Request : Google.Protobuf.IMessage<Request>
                {
                    public static Google.Protobuf.Reflection.MessageDescriptor Descriptor => throw null;
                    public Request Clone() => throw null;
                    public void WriteTo(object output) { }
                    public int CalculateSize() => 0;
                    public void MergeFrom(Request other) { }
                    public void MergeFrom(object input) { }
                    Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => Descriptor;
                }

                public static class ParserFactory
                {
                    public static Google.Protobuf.MessageParser<Request> Create()
                        => throw null;
                }
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "Fixture.ParserFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsGenericBaseAndStaticMetadataClause()
    {
        var assemblyPath = CompileFixture("""
            namespace Aspire.Hosting.Dcp.Model
            {
                public interface IKubernetesStaticMetadata
                {
                    string ObjectKind { get; }
                }

                public class CustomResource { }
                public class CustomResource<TSpec, TStatus> : CustomResource { }
                public sealed class ServiceSpec { }
                public sealed class ServiceStatus { }

                public sealed class Service : CustomResource<ServiceSpec, ServiceStatus>, IKubernetesStaticMetadata
                {
                    public static string ObjectKind => "Service";
                    string IKubernetesStaticMetadata.ObjectKind => ObjectKind;
                }
            }

            namespace Aspire.Hosting.Dcp
            {
                public class RenderedModelResource<T>
                    where T : Aspire.Hosting.Dcp.Model.CustomResource, Aspire.Hosting.Dcp.Model.IKubernetesStaticMetadata
                {
                }
            }

            public static class DcpFactory
            {
                public static Aspire.Hosting.Dcp.RenderedModelResource<Aspire.Hosting.Dcp.Model.Service> Create()
                    => throw null;
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "DcpFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsNestedGenericBaseClause()
    {
        var assemblyPath = CompileFixture("""
            public class Outer
            {
                public class Base<T>
                {
                }

                public sealed class Derived : Base<int>
                {
                }
            }

            public static class NestedGenericBaseFactory
            {
                public static Outer.Derived Create()
                    => throw null;
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "NestedGenericBaseFactory", "Create");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsResourceCollectionEnumerableClause()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;
            using System.Linq;

            namespace Aspire.Hosting.ApplicationModel
            {
                public interface IResource
                {
                    string Name { get; }
                }

                public interface IResourceCollection : IEnumerable<IResource>
                {
                    bool TryGetByName(string name, out IResource resource);
                }
            }

            public static class ResourceCollectionQueries
            {
                public static Aspire.Hosting.ApplicationModel.IResource Find(
                    Aspire.Hosting.ApplicationModel.IResourceCollection resources,
                    string name)
                    => resources.SingleOrDefault(resource => resource.Name == name);
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "ResourceCollectionQueries", "Find");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RetainsResourceAnnotationCollectionSurface()
    {
        var assemblyPath = CompileFixture("""
            using System.Collections.Generic;
            using System.Linq;

            namespace Aspire.Hosting.ApplicationModel
            {
                public interface IResourceAnnotation { }

                public sealed class HttpAnnotation : IResourceAnnotation { }

                public class ResourceAnnotationCollection : List<IResourceAnnotation>
                {
                }
            }

            public static class AnnotationQueries
            {
                public static bool HasHttp(Aspire.Hosting.ApplicationModel.ResourceAnnotationCollection annotations)
                {
                    annotations.Add(new Aspire.Hosting.ApplicationModel.HttpAnnotation());
                    return annotations.OfType<Aspire.Hosting.ApplicationModel.HttpAnnotation>().Any();
                }
            }
            """);
        try
        {
            AssertCheckable(FidelityCheck.Evaluate(assemblyPath), "AnnotationQueries", "HasHttp");
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void SelectSharedFrameworkDirectory_PrefersExactThenNearestSameMajorMinor()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-frameworks-").FullName;
        try
        {
            string exact = Path.Combine(root, "11.0.1");
            string sameBandLower = Path.Combine(root, "11.0.0");
            string sameBandHigher = Path.Combine(root, "11.0.3");
            string preview = Path.Combine(root, "12.0.0-preview.6.1");
            string otherBand = Path.Combine(root, "11.1.9");
            Directory.CreateDirectory(exact);
            Directory.CreateDirectory(sameBandLower);
            Directory.CreateDirectory(sameBandHigher);
            Directory.CreateDirectory(preview);
            Directory.CreateDirectory(otherBand);

            Assert.Equal(exact, FidelityCheck.SelectSharedFrameworkDirectory(root, "11.0.1"));

            Directory.Delete(exact);
            Assert.Equal(sameBandHigher, FidelityCheck.SelectSharedFrameworkDirectory(root, "11.0.2"));
            Assert.Equal(preview, FidelityCheck.SelectSharedFrameworkDirectory(root, "12.0.0-preview.6.2"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static void AssertCheckable(IReadOnlyList<FidelityCheck.CompileBackResult> results, string type, string method)
    {
        var result = Assert.Single(
            results,
            result => result.Type == type && result.Method == method);
        Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
            result.Detail);
    }

    static string CompileFixture(string source)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fidelity-generated-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.dll");
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
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
        if (directory is not null && Path.GetFileName(directory).StartsWith("fidelity-generated-filter-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
