using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
[Trait("Speed", "Fast")]
public sealed class AsyncLoweringFixtureMatrixTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures";
    const string FixtureMethod = "AwaitValue";
    const MethodImplAttributes RuntimeAsync =
        (MethodImplAttributes)0x2000;

    [Fact]
    public void IdenticalSource_ProducesClassicAndRuntimeAsyncPhysicalShapes()
    {
        FixtureDefinition classic = FixtureCatalog.DecompilerClassicAsync;
        FixtureDefinition runtime = FixtureCatalog.DecompilerRuntimeAsync;

        Assert.All(
            FixtureCatalog.DecompilerAsyncLoweringFixtures.Fixtures,
            fixture => Assert.Contains(
                FixtureBoundary.CompilerLowering,
                fixture.Boundaries));
        IReadOnlyList<string> classicSources = classic.SourcePaths();
        Assert.Contains(
            Path.Combine(classic.ProjectDirectory(), "AsyncFixtures.cs"),
            classicSources);
        Assert.Equal(classicSources, runtime.SourcePaths());

        using FixtureEvidence classicArtifact =
            FixtureEvidence.Open(classic.AssemblyPath());
        using FixtureEvidence runtimeArtifact =
            FixtureEvidence.Open(runtime.AssemblyPath());

        MethodDefinitionHandle classicMethod =
            classicArtifact.FindMethod(FixtureType, FixtureMethod);
        MethodDefinitionHandle runtimeMethod =
            runtimeArtifact.FindMethod(FixtureType, FixtureMethod);

        Assert.False(HasRuntimeAsyncFlag(
            classicArtifact.Reader.GetMethodDefinition(classicMethod)));
        var relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                classicArtifact.Relationships.GetByKickoff(classicMethod));
        Assert.Equal(
            StateMachineClaimKind.ClassicAsync,
            relationship.Relationship.Kind);

        Assert.True(HasRuntimeAsyncFlag(
            runtimeArtifact.Reader.GetMethodDefinition(runtimeMethod)));
        Assert.IsType<StateMachineRelationshipResult.Absent>(
            runtimeArtifact.Relationships.GetByKickoff(runtimeMethod));
    }

    static bool HasRuntimeAsyncFlag(MethodDefinition method)
        => (method.ImplAttributes & RuntimeAsync) != 0;

    sealed class FixtureEvidence : IDisposable
    {
        readonly FileStream _stream;
        readonly PEReader _peReader;

        FixtureEvidence(FileStream stream, PEReader peReader)
        {
            _stream = stream;
            _peReader = peReader;
            Reader = peReader.GetMetadataReader();
            Relationships = StateMachineRelationshipIndex.Create(Reader);
        }

        public MetadataReader Reader { get; }
        public StateMachineRelationshipIndex Relationships { get; }

        public static FixtureEvidence Open(string path)
        {
            FileStream stream = File.OpenRead(path);
            var peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen);
            return new FixtureEvidence(stream, peReader);
        }

        public MethodDefinitionHandle FindMethod(
            string typeName,
            string methodName)
        {
            foreach (TypeDefinitionHandle typeHandle in Reader.TypeDefinitions)
            {
                TypeDefinition type = Reader.GetTypeDefinition(typeHandle);
                string candidateType = string.IsNullOrEmpty(
                    Reader.GetString(type.Namespace))
                    ? Reader.GetString(type.Name)
                    : $"{Reader.GetString(type.Namespace)}.{Reader.GetString(type.Name)}";
                if (!string.Equals(
                    candidateType,
                    typeName,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method =
                        Reader.GetMethodDefinition(methodHandle);
                    if (string.Equals(
                        Reader.GetString(method.Name),
                        methodName,
                        StringComparison.Ordinal))
                    {
                        return methodHandle;
                    }
                }
            }

            throw new InvalidOperationException(
                $"Expected {typeName}.{methodName} in the fixture.");
        }

        public void Dispose()
        {
            _peReader.Dispose();
            _stream.Dispose();
        }
    }
}
