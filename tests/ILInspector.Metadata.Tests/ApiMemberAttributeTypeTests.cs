using System.ComponentModel;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

// docs/design/member-diff.md#inputs: attributes pair by their Type's identity,
// which the API surface records beside each retained attribute's text.
public sealed class ApiMemberAttributeTypeTests
{
    [Fact]
    public void Extractor_RecordsEachRetainedAttributeType_ParallelToItsText()
    {
        using var stream = File.OpenRead(typeof(ApiMemberAttributeTypeTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader);
        ApiType fixture = surface.Types.Single(type =>
            type.FullName.EndsWith("." + nameof(AttributeTypeFixture), StringComparison.Ordinal));

        foreach (string name in new[] { "Method", "Property", "Field" })
        {
            ApiMember member = fixture.Members.Single(member => member.Name == name);
            Assert.Equal(member.Attributes.Count, member.AttributeTypes.Count);
            Assert.Equal(
                ["System.ComponentModel.CategoryAttribute", "System.ComponentModel.DescriptionAttribute"],
                member.AttributeTypes);
            // [Obsolete] is printed from IsObsolete, so it is skipped from both lists.
            Assert.True(member.IsObsolete);
            Assert.DoesNotContain(member.Attributes, attribute => attribute.Contains("Obsolete", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AttributeTypes_CountTowardRetainedText()
    {
        var withoutTypes = new ApiMember { Name = "M", Attributes = ["N.A"] };
        var withTypes = new ApiMember { Name = "M", Attributes = ["N.A"], AttributeTypes = ["N.AAttribute"] };

        Assert.Equal(
            "N.AAttribute".Length,
            ApiSurfaceExtractor.CountRetainedText(withTypes)
                - ApiSurfaceExtractor.CountRetainedText(withoutTypes));
    }

    public sealed class AttributeTypeFixture
    {
        [Category("fixture")]
        [Description("method")]
        [Obsolete("old")]
        public void Method() { }

        [Category("fixture")]
        [Description("property")]
        [Obsolete("old")]
        public int Property { get; set; }

        [Category("fixture")]
        [Description("field")]
        [Obsolete("old")]
        public int Field;
    }
}
