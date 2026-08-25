using System.Text.Json;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using CSharpText;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Tests;

public sealed class SemanticTypeOutputContainmentTests
{
    [Fact]
    public void TypePresentation_CarriesContainedTextWithoutChangingIdentity()
    {
        var type = new ApiType
        {
            Namespace = "Dotnet\u200BInspect.Metadata",
            Name = "Arity`1",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "T\u2060Value" },
            ],
        };
        var surface = new ApiSurface
        {
            Name = "Fixture",
            Types = [type],
        };

        var shape = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: []);
        var (table, _) = ApiOutputFormatter.BuildSurfaceTableView(
            surface,
            new ApiOptions());
        var (document, _) = ApiOutputFormatter.BuildFullApiView(
            surface,
            new ApiOptions { Verbosity = Verbosity.Minimal });

        Assert.Equal(
            @"Dotnet\u200BInspect.Metadata.Arity<T\u2060Value>",
            shape.FullNameText.ToString());
        Assert.Equal(
            shape.FullNameText,
            Assert.Single(table.Rows!).TypeText);
        Assert.Equal(
            shape.FullNameText,
            Assert.Single(document.Classes!).TypeText);

        Assert.Equal("Dotnet\u200BInspect.Metadata", type.Namespace);
        Assert.Equal("T\u2060Value", Assert.Single(type.TypeParameters).Name);
    }

    [Fact]
    public void TypeJson_ContainsDecodedValuesAndPreservesRawModel()
    {
        var member = new ApiMember
        {
            Kind = "method",
            Name = "Run\u202E",
            ReturnType =
                CSharpIdentifier.ContainRenderedText("Result\u202E"),
            Signature = CSharpIdentifier.ContainRenderedText(
                "Result\u202E Run\u202E()"),
            Documentation = new DocComment
            {
                Summary = "before\u202Eafter",
            },
        };
        var type = new ApiType
        {
            Namespace = "名前空間",
            Name = "Arity`1",
            MetadataName = @"Type\u202E",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter
                {
                    Name = "型\u202EValue",
                    Variance = "out",
                },
            ],
            Members = [member],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiArtifactJson.Type);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement root = document.RootElement;
        Assert.Equal("名前空間", root.GetProperty("namespace").GetString());
        Assert.Equal(
            @"Type\\u202E",
            root.GetProperty("metadata_name").GetString());
        Assert.Equal(
            @"型\u202EValue",
            root.GetProperty("type_parameters")[0]
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            @"out 型\u202EValue",
            root.GetProperty("type_parameters")[0]
                .GetProperty("display_name")
                .GetString());
        JsonElement memberJson = root.GetProperty("members")[0];
        Assert.Equal(@"Run\u202E", memberJson.GetProperty("name").GetString());
        Assert.Equal(
            @"Result\u202E Run\u202E()",
            memberJson.GetProperty("signature").GetString());
        Assert.Equal(
            @"before\u202Eafter",
            memberJson.GetProperty("documentation")
                .GetProperty("summary")
                .GetString());
        Assert.All(
            EnumerateJsonStrings(root)
                .Where(value => value != @"Type\\u202E"),
            value => Assert.DoesNotContain(@"\\u202E", value));

        Assert.Equal(@"Type\u202E", type.MetadataName);
        Assert.Equal("型\u202EValue", Assert.Single(type.TypeParameters).Name);
        Assert.Equal("Run\u202E", member.Name);
        Assert.Equal(
            @"Result\u202E Run\u202E()",
            member.Signature);
    }

    [Fact]
    public void CSharpField_PreservesEscapesAndContainsResidualScalars()
    {
        const string rendered =
            "void Meth\\\\u0041(string path = \"C:\\\\temp\")";
        Assert.Equal(
            rendered,
            ApiViewText.CSharpField(rendered).ToString());

        string surrogate = ApiViewText.CSharpField(
            "string M(string value = \"A\uD800B\")").ToString();
        Assert.Equal(
            "string M(string value = \"A\\uD800B\")",
            surrogate);
        Assert.DoesNotContain('\uD800', surrogate);
    }

    [Fact]
    public void DocumentationEncoding_PreservesLiteralEscapeIdentity()
    {
        var comment = new DocComment
        {
            Summary = @"Escape \u0041 and C:\temp",
        };

        Assert.Equal(
            @"Escape \\u0041 and C:\\temp",
            comment.Summary);
        Assert.Equal(
            comment.Summary,
            ApiViewText.EncodedField(comment.Summary!).ToString());
    }

    [Fact]
    public void DocumentationEncoding_RoundTripsThroughPersistenceJson()
    {
        var type = new ApiType
        {
            Documentation = new DocComment
            {
                Summary = "before\u202Eafter literal \\u0041",
            },
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiTypeJsonContext.Default.ApiType);
        ApiType restored = JsonSerializer.Deserialize(
            json,
            ApiTypeJsonContext.Default.ApiType)!;

        Assert.Equal(
            type.Documentation.Summary,
            restored.Documentation.Summary);
    }

    [Fact]
    public void SurfaceDescription_ImportsDocumentationEncodingOnce()
    {
        var type = new ApiType
        {
            Namespace = "Docs",
            Name = "Widget",
            Kind = "class",
            Documentation = new DocComment
            {
                Summary = "before\u202Eafter literal \\u0041",
            },
        };

        var (view, _) = ApiOutputFormatter.BuildSurfaceTableView(
            new ApiSurface { Types = [type] },
            new ApiOptions { ShowDocs = true });
        ApiSurfaceTableRow row = Assert.Single(
            view.RowsWithDescription!);
        Assert.Equal(
            @"before\u202Eafter literal \\u0041",
            row.DescriptionText!.Value.ToString());
    }

    [Fact]
    public void MemberTableAndShape_CarryContainedArtifactText()
    {
        var type = new ApiType
        {
            Namespace = "Dotnet\u200BInspect",
            Name = "Widget",
            Kind = "class",
            BaseType =
                CSharpIdentifier.ContainRenderedText("Base\u202EType"),
            Interfaces =
            [
                CSharpIdentifier.ContainRenderedText(
                    "I\u200BContract"),
            ],
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run\u202E",
                    ReturnType =
                        CSharpIdentifier.ContainRenderedText(
                            "Result\u202E"),
                    Signature =
                        CSharpIdentifier.ContainRenderedText(
                            "Result\u202E Run\u202E()"),
                },
            ],
        };

        var (table, _) = ApiOutputFormatter.BuildTypeTableView(
            type,
            new ApiOptions());
        var row = Assert.Single(table.Rows!);
        Assert.Equal(@"Run\u202E", row.NameText.ToString());
        Assert.Equal(
            @"Result\u202E",
            row.ReturnTypeText.ToString());

        var methods = new MethodsView();
        var view = new TypeView();
        ApiOutputFormatter.PopulateMemberSections(
            view,
            methods,
            new OperatorsView(),
            new ExplicitInterfaceImplementationsView(),
            new ExtensionMethodsView(),
            new EventsView(),
            type,
            new ApiOptions());
        var detailed = Assert.Single(methods.Rows!);
        Assert.Equal(@"Run\u202E", detailed.NameText.ToString());
        Assert.Contains(
            @"Run\u202E",
            detailed.SignatureText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            @"\\u202E",
            detailed.SignatureText.ToString(),
            StringComparison.Ordinal);

        ApiOutputFormatter.PopulateMemberSignature(
            view,
            type,
            new ApiOptions());
        var signature = Assert.Single(view.SignatureRows!);
        Assert.DoesNotContain(
            @"\\u202E",
            signature.SignatureText.ToString(),
            StringComparison.Ordinal);

        var index = Assert.Single(
            ApiOutputFormatter.BuildMemberIndexRows(
                type,
                type.Members));
        Assert.Equal(
            @"<code>Run\u202E</code>",
            index.SelectorText.ToString());

        var shape = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: []);
        Assert.Equal(
            @"Dotnet\u200BInspect.Widget",
            shape.FullNameText.ToString());
        Assert.All(
            Flatten(shape.Members),
            text =>
            {
                Assert.DoesNotContain('\u200B', text);
                Assert.DoesNotContain('\u202E', text);
                Assert.DoesNotContain(
                    @"\\u202E",
                    text,
                    StringComparison.Ordinal);
            });

        static IEnumerable<string> Flatten(IEnumerable<TreeNode> nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node.Text;
                foreach (string child in Flatten(node.Children ?? []))
                    yield return child;
            }
        }
    }

    private static IEnumerable<string> EnumerateJsonStrings(
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    foreach (string value in EnumerateJsonStrings(item))
                        yield return value;
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                    foreach (string value in EnumerateJsonStrings(property.Value))
                        yield return value;
                break;
        }
    }

    [Fact]
    public void BenignInternationalTypeName_IsByteNeutral()
    {
        var type = new ApiType
        {
            Namespace = "日本語",
            Name = "Über",
            Kind = "class",
        };

        Assert.Equal(
            "日本語.Über",
            ApiOutputFormatter.FormatGenericFullName(type).ToString());

        var surface = new ApiSurface
        {
            Name = "Fixture",
            Types = [type],
        };

        AssertEquivalentJson(
            JsonSerializer.Serialize(
                surface,
                ApiJsonContext.Default.ApiSurface),
            JsonSerializer.Serialize(
                surface,
                ApiArtifactJson.Surface));
        AssertEquivalentJson(
            JsonSerializer.Serialize(
                type,
                ApiTypeJsonContext.Default.ApiType),
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.Type));
        AssertEquivalentJson(
            JsonSerializer.Serialize(
                type,
                ApiTypeCompactJsonContext.Default.ApiType),
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.CompactType));

        static void AssertEquivalentJson(
            string expected,
            string actual)
        {
            using JsonDocument expectedDocument =
                JsonDocument.Parse(expected);
            using JsonDocument actualDocument =
                JsonDocument.Parse(actual);
            Assert.True(
                JsonElement.DeepEquals(
                    expectedDocument.RootElement,
                    actualDocument.RootElement));
        }
    }
}
