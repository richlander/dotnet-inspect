using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpTypePrinterTests
{
    readonly CSharpTypePrinter _printer = new();

    [Fact]
    public void SkeletonPrintsApiProposalStyleSource()
    {
        var type = new ApiType
        {
            Namespace = "System.Text",
            Name = "StringBuilder",
            Kind = "class",
            IsSealed = true,
            Members =
            [
                new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = "this text must not be used",
                    SignatureModel = new ApiSignature()
                },
                new ApiMember
                {
                    Name = "Append",
                    Kind = "method",
                    Signature = "this text must not be used",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Text.StringBuilder",
                        MemberName = "Append",
                        Parameters = [new ApiParameter { Type = "string?", Name = "value" }]
                    }
                },
                new ApiMember
                {
                    Name = "ToString",
                    Kind = "method",
                    IsOverride = true,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "string",
                        MemberName = "ToString"
                    }
                }
            ]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Equal("System.Text", unit.Namespace);
        Assert.Equal(
            """
            namespace System.Text;

            public sealed class StringBuilder
            {
                public StringBuilder();
                public StringBuilder Append(string? value);
                public override string ToString();
            }
            """,
            unit.Source);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void BatchGroupsTypesIntoNamespaceSourceUnits()
    {
        var requests = new[]
        {
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "First")),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Third")),
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Second"))
        };

        var result = _printer.PrintBatch(requests);

        Assert.Collection(
            result.Units,
            unit =>
            {
                Assert.Equal("Samples", unit.Namespace);
                Assert.Equal(
                    """
                    namespace Samples;

                    public class First
                    {
                    }

                    public class Second
                    {
                    }
                    """,
                    unit.Source);
            },
            unit =>
            {
                Assert.Equal("Other", unit.Namespace);
                Assert.Contains("public class Third", unit.Source, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void GlobalNamespaceOmitsNamespaceDeclaration()
    {
        var type = CreateEmptyType(null, "GlobalType");

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Null(unit.Namespace);
        Assert.Equal(
            """
            public class GlobalType
            {
            }
            """,
            unit.Source);
    }

    [Fact]
    public void SkeletonPrefersStructuredGenericSignature()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Converter`1",
            MetadataName = "Converter`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }],
            Members =
            [
                new ApiMember
                {
                    Name = "Convert",
                    Kind = "method",
                    Signature = "broken compatibility signature",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "TResult",
                        MemberName = "Convert<TResult>",
                        TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                        Parameters = [new ApiParameter { Type = "T", Name = "value" }]
                    }
                }
            ]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public class Converter<T>",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public TResult Convert<TResult>(T value) where TResult : class;",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Converter<T><T>", result.Units[0].Source, StringComparison.Ordinal);
        Assert.DoesNotContain("broken compatibility signature", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SkeletonMatchesMetadataDeclarationWriter()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Create",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Samples.Widget",
                MemberName = "Create"
            }
        });
        var declarationOptions = new CSharpDeclarationOptions
        {
            TypeNameMode = CSharpTypeNameMode.ContextualShort,
            ContainingNamespace = "Samples",
            NamespaceMode = CSharpNamespaceMode.Omit,
            TerminateMemberDeclaration = true
        };
        var expectedDeclaration = CSharpDeclarationWriter.RenderTypeUnit(
            type,
            type.Members,
            declarationOptions);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Equal($"namespace Samples;\n\n{expectedDeclaration.Source}", result.Units[0].Source);
        Assert.Equal(expectedDeclaration.Diagnostics, result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }

    [Fact]
    public void NonSkeletonPolicyFailsInsteadOfDroppingBodies()
    {
        var request = new CSharpTypePrintRequest(
            CreateEmptyType("Samples", "Widget"),
            CSharpTypeBodyPolicy.Full);

        var exception = Assert.Throws<NotSupportedException>(() => _printer.Print(request));

        Assert.Contains("requires a body provider", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("enum")]
    [InlineData("delegate")]
    public void UnsupportedTypeKindFailsInsteadOfEmittingInvalidSkeleton(string kind)
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Shape",
            Kind = kind
        };

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains($"type kind '{kind}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedTypeFailsWithoutItsDeclaringType()
    {
        var type = CreateEmptyType("Samples", "Outer.Inner");
        type.MetadataName = "Outer+Inner";

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("requires its declaring type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTypeRequestsFailInsteadOfEmittingDuplicateDeclarations()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var requests = new[]
        {
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintRequest(type)
        };

        var exception = Assert.Throws<ArgumentException>(() => _printer.PrintBatch(requests));

        Assert.Contains("duplicate C# type 'Samples.Widget'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalMetadataIdentityRejectsDuplicateGenericDeclarations()
    {
        var first = CreateEmptyType("Samples", "Converter`1");
        first.MetadataName = "Converter`1";
        first.TypeParameters = [new TypeParameter { Name = "T" }];
        var second = CreateEmptyType("Samples", "Other`1");
        second.MetadataName = "Converter`1";
        second.TypeParameters = [new TypeParameter { Name = "U" }];

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(first),
                new CSharpTypePrintRequest(second)
            ]));

        Assert.Contains("duplicate C# type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpSpelledGenericTypeNameFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Converter<T>");
        type.TypeParameters = [new TypeParameter { Name = "T" }];

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("must use a metadata name", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Converter", 1)]
    [InlineData("Converter`2", 1)]
    [InlineData("Converter`x", 1)]
    public void InconsistentGenericMetadataArityFailsExplicitly(string name, int parameterCount)
    {
        var type = CreateEmptyType("Samples", name);
        type.TypeParameters = Enumerable.Range(0, parameterCount)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains(
            name.Contains('`', StringComparison.Ordinal)
                ? "inconsistent metadata arity"
                : "requires metadata arity",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedTypeNameFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Name = null!;

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("non-empty type name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedMemberCollectionFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members = null!;

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("null member collection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullCallsResolveToExplicitArgumentFailures()
    {
        Assert.Throws<ArgumentNullException>(() => _printer.Print(null!));
        Assert.Throws<ArgumentNullException>(() => _printer.PrintBatch(null!));
    }

    static ApiType CreateEmptyType(string? @namespace, string name)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class"
        };
}
