using System.Text.Json;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
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
        var type = new ApiType
        {
            Namespace = "名前空間",
            Name = "Arity`1",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "型\u2060Value" },
            ],
        };

        string json = JsonSerializer.Serialize(
            type,
            ApiArtifactJson.TypeContext.ApiType);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement root = document.RootElement;
        Assert.Equal("名前空間", root.GetProperty("namespace").GetString());
        Assert.Equal(
            @"型\u2060Value",
            root.GetProperty("type_parameters")[0]
                .GetProperty("name")
                .GetString());
        Assert.Equal("型\u2060Value", Assert.Single(type.TypeParameters).Name);
    }

    [Fact]
    public void MemberTableAndShape_CarryContainedArtifactText()
    {
        var type = new ApiType
        {
            Namespace = "Dotnet\u200BInspect",
            Name = "Widget",
            Kind = "class",
            BaseType = "Base\u2060Type",
            Interfaces = ["I\u200BContract"],
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run\u2060",
                    ReturnType = "Result\u200B",
                    Signature = "Result\u200B Run\u2060()",
                },
            ],
        };

        var (table, _) = ApiOutputFormatter.BuildTypeTableView(
            type,
            new ApiOptions());
        var row = Assert.Single(table.Rows!);
        Assert.Equal(@"Run\u2060", row.NameText.ToString());
        Assert.Equal(@"Result\u200B", row.ReturnTypeText.ToString());

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
                Assert.DoesNotContain('\u2060', text);
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
                ApiArtifactJson.SurfaceContext.ApiSurface));
        AssertEquivalentJson(
            JsonSerializer.Serialize(
                type,
                ApiTypeJsonContext.Default.ApiType),
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.TypeContext.ApiType));
        AssertEquivalentJson(
            JsonSerializer.Serialize(
                type,
                ApiTypeCompactJsonContext.Default.ApiType),
            JsonSerializer.Serialize(
                type,
                ApiArtifactJson.CompactTypeContext.ApiType));

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
