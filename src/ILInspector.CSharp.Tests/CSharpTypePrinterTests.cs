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

        var result = _printer.Print(requests);

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
            Name = "Converter<T>",
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
            "public TResult Convert<TResult>(T value) where TResult : class;",
            result.Units[0].Source,
            StringComparison.Ordinal);
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

    static ApiType CreateEmptyType(string? @namespace, string name)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class"
        };
}
