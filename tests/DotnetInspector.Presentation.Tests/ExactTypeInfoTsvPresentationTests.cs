using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;

using DotnetInspector.Libraries;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation.Tests;

public class ExactTypeInfoTsvPresentationTests
{
    [Fact]
    public void Write_PreservesNestedNonAsciiTypeAsUtf8()
    {
        TypeSignature signature =
            Signature(typeof(Δοκιμή.內部));
        var document = new ExactTypeInfoTsvDocument(
            signature,
            "class",
            ["sealed"],
            "/app/Example.dll",
            "Library");
        var output = new ArrayBufferWriter<byte>();

        ExactTypeInfoTsvPresentation.Write(
            output,
            document,
            showHeader: true);

        Assert.Equal(
            """
            field	value
            Type	DotnetInspector.Presentation.Tests.Δοκιμή.內部
            Kind	class
            Modifiers	sealed
            Interfaces	2
            Library	/app/Example.dll
            Source	Library

            """,
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Fact]
    public void Write_EscapesDeclarationKeywordAndHonorsNoHeader()
    {
        TypeSignature signature =
            Signature(typeof(@class));
        var document = new ExactTypeInfoTsvDocument(
            signature,
            "class",
            [],
            "Example.dll",
            "Library");
        var output = new ArrayBufferWriter<byte>();

        ExactTypeInfoTsvPresentation.Write(
            output,
            document,
            showHeader: false);

        Assert.Equal(
            """
            Type	DotnetInspector.Presentation.Tests.@class
            Kind	class
            Library	Example.dll
            Source	Library

            """,
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Fact]
    public void Write_SpellsGenericParametersBySegment()
    {
        TypeSignature signature =
            Signature(typeof(GenericOuter<>.Inner<,>));
        var document = new ExactTypeInfoTsvDocument(
            signature,
            "interface",
            [],
            "Example.dll",
            "Library");
        var output = new ArrayBufferWriter<byte>();

        ExactTypeInfoTsvPresentation.Write(
            output,
            document,
            showHeader: false);

        Assert.Equal(
            """
            Type	DotnetInspector.Presentation.Tests.GenericOuter<T>.Inner<U, V>
            Kind	interface
            Type Parameters	T, U, V : class?, new()
            Library	Example.dll
            Source	Library

            """,
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Write_RejectsParameterAssignedOutsideTypeSegments(
        int segmentIndex)
    {
        TypeSignature signature =
            Signature(typeof(GenericOuter<>));
        TypeSignatureGenericParameter source =
            Assert.Single(signature.TypeParameters);
        var parameter = new TypeSignatureGenericParameter(
            segmentIndex,
            source.MetadataIndex,
            source.Name,
            source.Variance,
            source.PrimaryConstraint,
            source.TypeConstraints,
            source.ConstructorConstraint,
            source.AllowsRefStructConstraint);
        var invalid = new TypeSignature(
            signature.Subject,
            signature.SubjectName,
            signature.BaseType,
            ImmutableArray.Create(parameter),
            signature.InterfaceCount,
            signature.AreEnclosingTypesExternallyReachable,
            signature.NestedTypeCount);
        var document = new ExactTypeInfoTsvDocument(
            invalid,
            "interface",
            [],
            "Example.dll",
            "Library");

        Assert.Throws<InvalidOperationException>(
            () => ExactTypeInfoTsvPresentation.Write(
                new ArrayBufferWriter<byte>(),
                document,
                showHeader: false));
    }

    private static TypeSignature Signature(Type type)
    {
        MetadataTypeDefinitionName name =
            Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName
                        .ParseSerialized(
                            type.FullName
                                ?? throw new InvalidOperationException(
                                    "The fixture requires a full Type name.")))
                .Name;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                type.Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "Type Signature presentation test"));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 0,
            maxMembers: 0,
            maxInspectionFailures: 0,
            maxTypeForwarders: 0,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 0);
        InspectionEnvelope<TypeSignatureInspectionOutcome>
            envelope = TypeSignatureInspectionOperation.Execute(
                assembly,
                new(
                    name,
                    bounds,
                    new MetadataExactTypeInput(name)));
        return Assert.IsType<
                TypeSignatureInspectionOutcome.Available>(
                    envelope.Content)
            .Signature;
    }
}

public class Δοκιμή
{
    public sealed class 內部
        : IDisposable, IAsyncDisposable
    {
        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}

public sealed class @class;

public interface GenericOuter<out T>
{
    public interface Inner<U, V>
        where V : class?, new();
}
