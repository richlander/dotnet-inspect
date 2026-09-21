using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public sealed class TypeApiDeclarationSectionTests
{
    [Fact]
    public void ExactTypeCatalog_RegistersExplicitDeclarationSection()
    {
        SectionPipeline<ILInspector.Metadata.ApiType> pipeline =
            ApiMemberSectionDescriptors.CreatePipeline();

        Assert.Contains(
            SectionNames.ApiDeclarations,
            pipeline.SelectableSectionNames);
        Assert.True(
            ApiMemberSectionDescriptors.ApiDeclarations.ExplicitOnly);
        Assert.False(
            ApiMemberSectionDescriptors.ApiDeclarations.ProbeEffectiveness);
        Assert.Equal(
            InspectionTargetRequirement.Type,
            ApiSectionDemandIndex.Declarations[
                SectionNames.ApiDeclarations]);
    }
}
