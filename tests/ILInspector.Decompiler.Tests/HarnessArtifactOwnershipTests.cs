using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class HarnessArtifactOwnershipTests
{
    [Fact]
    public void ProductWholeMemberSplice_PreservesConstructorArtifact()
    {
        const string member = """
                private Fixture()
                {
                    Consume(,);
                }
            """;

        var emitted = new StringBuilder();
        FidelityCheck.EmitPrerenderedMember(member, emitted, "        ");
        string source = emitted.ToString();

        Assert.Contains("private Fixture()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Fixture()", source, StringComparison.Ordinal);
        Assert.Contains("Consume(,);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitEvent_RemainsOutsideProductWholeMemberEvidence()
    {
        string assemblyPath = FidelityCheckGeneratedFilterTests.CompileFixture("""
            using System;

            public interface IEventContract
            {
                event EventHandler? Changed;
            }

            public sealed class ExplicitEventFixture : IEventContract
            {
                event EventHandler? IEventContract.Changed
                {
                    add { }
                    remove { }
                }
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinition type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                    == "ExplicitEventFixture"));
            EventDefinition @event = reader.GetEventDefinition(Assert.Single(type.GetEvents()));

            using var source = MetadataSource.Open(assemblyPath);
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                @event.GetAccessors().Adder,
                targeted: true,
                isPrimaryConstructor: false));
            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                @event.GetAccessors().Adder,
                targeted: false,
                isPrimaryConstructor: false));
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }
}
