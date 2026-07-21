using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.RoundTripCompilation;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public sealed class RoundTripComparisonTests
{
    static string AssemblyPath => typeof(RoundTripComparisonTests).Assembly.Location;

    [Fact]
    public void Compare_ReportsExactCSharpAndIlForSameArtifact()
    {
        var request = CreateRequest();

        var result = RoundTripComparison.Compare(request, File.ReadAllBytes(AssemblyPath));

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Exact, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Exact, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Exact, member.IlStatus);
        Assert.NotNull(member.Evidence);
        string json = JsonSerializer.Serialize(result);
        Assert.Contains("\"cSharpStatus\":0", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"token\":{request.Targets[0].Method.Token}", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_ReportsChangedEvidenceForRecompiledBody()
    {
        var request = CreateRequest();
        var donor = Compile("""
            namespace ILInspector.Decompiler.Tests;
            public sealed class RoundTripComparisonFixture
            {
                public int Transform(int value) => value + 2;
            }
            """);

        var result = RoundTripComparison.Compare(request, donor);

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Exact, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Changed, member.CSharpStatus);
        Assert.NotEqual(IlBodyDiffOutcome.Exact, member.IlStatus);
        Assert.NotNull(member.Evidence);
    }

    [Fact]
    public void Compare_PreservesAbsentCorrespondenceAsUnavailable()
    {
        var request = CreateRequest();
        var donor = Compile("public sealed class Other { public int Transform(int value) => value; }");

        var result = RoundTripComparison.Compare(request, donor);

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Absent, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Unavailable, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, member.IlStatus);
        Assert.Null(member.Evidence);
    }

    [Fact]
    public void Compare_FailsWhenInputBytesNoLongerMatchRequest()
    {
        var valid = CreateRequest();
        var request = RoundTripRequest.Create(
            new RoundTripArtifactIdentity(AssemblyPath, new string('0', 64), "test"),
            valid.Module,
            valid.Targets,
            valid.Scope,
            valid.BodyPolicy,
            valid.Replacements);

        var result = RoundTripComparison.Compare(request, File.ReadAllBytes(AssemblyPath));

        Assert.Equal(RoundTripComparisonStatus.Failed, result.Status);
        Assert.Contains("content hash", result.Failure);
        Assert.Empty(result.Members);
    }

    static RoundTripRequest CreateRequest()
    {
        using var image = new MetadataImage(AssemblyPath);
        var methodHandle = FindMethod(
            image.Reader,
            nameof(RoundTripComparisonFixture),
            nameof(RoundTripComparisonFixture.Transform));
        var method = image.Reader.GetMethodDefinition(methodHandle);
        var typeHandle = method.GetDeclaringType();
        var anchor = ApiMemberIdentity.CreateMethodAnchor(
            image.Reader,
            typeHandle,
            method);
        var module = image.Reader.GetModuleDefinition();
        var mvid = image.Reader.GetGuid(module.Mvid);
        return RoundTripRequest.Create(
            RoundTripArtifactIdentity.FromFile(AssemblyPath, "test"),
            new RoundTripModuleIdentity(image.Reader.GetString(module.Name), mvid),
            [new RoundTripTarget(MetadataMethodAddress.Create(image.Reader, methodHandle), anchor)],
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected);
    }

    static byte[] Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "round-trip-comparison-donor",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return output.ToArray();
    }

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string typeName, string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return methodHandle;
            }
        }
        throw new InvalidOperationException($"Method '{typeName}::{methodName}' was not found.");
    }

    sealed class MetadataImage : IDisposable
    {
        readonly Stream _stream;
        readonly PEReader _pe;

        public MetadataImage(string path)
        {
            _stream = File.OpenRead(path);
            _pe = new PEReader(_stream);
            Reader = _pe.GetMetadataReader();
        }

        public MetadataReader Reader { get; }

        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }
}

public sealed class RoundTripComparisonFixture
{
    public int Transform(int value) => value + 1;
}
