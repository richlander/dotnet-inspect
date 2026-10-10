using DotnetInspector.Presentation;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Presentation.Tests;

public class ApiSurfacePresentationTests
{
    [Fact]
    public void Type_PreservesExactIdentityAndProjectsDisplayFacts()
    {
        MetadataTypeDefinitionName definitionName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer", "Inner`1"]))
            .Name;
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer.Inner<TValue>",
            MetadataName = "Outer+Inner`1",
            DefinitionName = definitionName,
            Accessibility = "protected",
            Kind = "class",
            IsAbstract = true,
            TypeParameters =
            [
                new() { Name = "TValue" },
            ],
        };

        ApiTypeSurfacePresentation presentation =
            ApiSurfacePresentation.Type(type);

        Assert.Equal(
            "Sample.Outer+Inner`1",
            presentation.MetadataId);
        Assert.Equal(
            @"Sample.Outer+Inner`1",
            presentation.DefinitionId);
        Assert.Equal(type.FullName, presentation.QueryId);
        Assert.Equal("Outer.Inner<TValue>", presentation.DisplayName);
        Assert.Equal("abstract class", presentation.Kind);
        Assert.Equal(
            "protected abstract class Outer.Inner<TValue>",
            presentation.Signature);
        Assert.Equal("protected", presentation.Accessibility);
    }

    [Fact]
    public void Member_ProjectsDetachedIdentityParametersAndBodySelectors()
    {
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Widget",
            Kind = "class",
        };
        var member = new ApiMember
        {
            Name = "Build",
            Kind = "method",
            Signature = "string Build(int count = 1)",
            Accessibility = "protected",
            MetadataToken = 0x06000001,
            ReturnType = "string",
            IsStatic = true,
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "Build",
                Parameters =
                [
                    new()
                    {
                        Name = "count",
                        Type = "int",
                        CanonicalType = "System.Int32",
                        HasDefault = true,
                        DefaultValueText = "1",
                    },
                ],
            },
        };

        ApiMemberSurfacePresentation<
            ApiParameterSurfacePresentation,
            ApiMemberBodySelectorPresentation> presentation =
                ApiSurfacePresentation.Member(
                    type,
                    member,
                    static parameter => parameter,
                    static selector => selector);

        Assert.Equal("protected", presentation.Accessibility);
        Assert.True(presentation.IsStatic);
        Assert.Equal("string", presentation.ReturnType);
        ApiParameterSurfacePresentation parameter =
            Assert.Single(presentation.Parameters);
        Assert.Equal("count", parameter.Name);
        Assert.Equal("int", parameter.Type);
        Assert.True(parameter.HasDefault);
        Assert.Equal("1", parameter.DefaultValue);
        ApiMemberBodySelectorPresentation selector =
            Assert.Single(presentation.BodySelectors);
        Assert.Equal(member.MetadataToken, selector.Token);
        Assert.Equal(member.Name, selector.MemberName);
        Assert.Equal(
            presentation.GraphSelectorKey,
            selector.SelectorKey);
        Assert.NotEmpty(presentation.StableSelector);
        Assert.NotEmpty(presentation.AnchorDigest);
        Assert.NotEmpty(presentation.CanonicalSignature);
    }
}
