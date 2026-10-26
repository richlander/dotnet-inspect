using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CSharpText.MemberSlicing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public sealed class MemberSourceAuthorizationTests
{
    // PR-fast: inspect one small real repository Library, without acquisition.
    [Theory]
    [InlineData(Verbosity.Quiet)]
    [InlineData(Verbosity.Minimal)]
    [InlineData(Verbosity.Normal)]
    [InlineData(Verbosity.Detailed)]
    public void AutomaticDetailDoesNotRequestStandalonePdb(Verbosity verbosity)
    {
        ApiType type = LoadRealMember();
        var options = new MemberOptions
        {
            OverloadIndex = 1,
            Verbosity = verbosity,
        };

        if (verbosity == Verbosity.Detailed)
        {
            Assert.Contains(
                SectionNames.DecompiledSource,
                ApiCommand.GetRequestedMemberSections(type, options));
            Assert.Contains(
                SectionNames.PdbSource,
                ApiCommand.GetRequestedMemberSections(type, options));
        }

        Assert.False(MemberCommand.NeedsMemberSourceResolution(type, options));
        Assert.False(MemberCommand.AuthorizesMemberSourceResolution(type, options));
        Assert.False(MemberCommand.AuthorizesMemberSourceContent(type, options));
        Assert.False(MemberCommand.NeedsMemberSourceResolution(
            type, options with { UserVerbosityOverride = Verbosity.Minimal }));
    }

    [Theory]
    [InlineData(SectionNames.DecompiledSource)]
    [InlineData(SectionNames.AnnotatedSource)]
    [InlineData(SectionNames.AnnotatedSourceDocument)]
    [InlineData(SectionNames.FindingCensus)]
    [InlineData(SectionNames.BodyShapes)]
    [InlineData(SectionNames.BodyShapeSummary)]
    [InlineData(SectionNames.Facts)]
    [InlineData(SectionNames.Source)]
    [InlineData(SectionNames.PdbSource)]
    [InlineData(SectionNames.SourceDiff)]
    public void ExplicitDetailKeepsPdbRequestAtEveryView(string section)
    {
        ApiType type = LoadRealMember();
        foreach (Verbosity verbosity in Enum.GetValues<Verbosity>())
        {
            var options = new MemberOptions
            {
                OverloadIndex = 1,
                Verbosity = verbosity,
                IncludeSections = new(StringComparer.OrdinalIgnoreCase) { section },
            };
            Assert.True(MemberCommand.AuthorizesMemberSourceResolution(type, options));
            Assert.False(MemberCommand.AuthorizesMemberSourceResolution(
                type, options with { OverloadIndex = null }));
        }
    }

    [Fact]
    public void UnrelatedExplicitSelectionDoesNotRequestStandalonePdb()
    {
        ApiType type = LoadRealMember();
        var options = new MemberOptions
        {
            OverloadIndex = 1,
            Verbosity = Verbosity.Detailed,
            IncludeSections = new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.Signature,
            },
        };
        Assert.False(MemberCommand.NeedsMemberSourceResolution(type, options));
    }

    private static ApiType LoadRealMember()
    {
        using var stream = File.OpenRead(typeof(MemberTextSlicer).Assembly.Location);
        using var pe = new PEReader(stream);
        var api = ApiSurfaceExtractor.Extract(pe, includeAll: true);
        ApiType type = Assert.Single(api.Types,
            candidate => candidate.FullName == typeof(MemberTextSlicer).FullName);
        ApiMember member = Assert.Single(type.Members,
            candidate => candidate.Name == nameof(MemberTextSlicer.ExtractMemberText));
        int token = Assert.IsType<int>(member.MetadataToken);
        var handle = (MethodDefinitionHandle)MetadataTokens.Handle(token);
        Assert.NotEqual(0, pe.GetMetadataReader().GetMethodDefinition(handle).RelativeVirtualAddress);
        member.HasMethodBody = true;
        type.Members = [member];
        return type;
    }
}
