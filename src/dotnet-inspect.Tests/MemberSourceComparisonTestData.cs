using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.Tests;

internal static class MemberSourceComparisonTestData
{
    internal static AssemblyMemberSourceComparisonEntry.Available Create(
        ApiType type,
        ApiMember member,
        string pdbText,
        string decompiledText,
        SourceChecksumVerification checksumVerification =
            SourceChecksumVerification.Exact)
        => Create(
            AssemblyMemberSourceRequest.From(type, member),
            pdbText,
            decompiledText,
            checksumVerification);

    internal static MemberSourceDiffPresentation CreatePresentation(
        string pdbText,
        string decompiledText)
    {
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Example",
                    ["C"]))
            .Name;
        var request = new AssemblyMemberSourceRequest(
            type,
            new MemberAnchor(
                "M()",
                "Example.C.M()",
                "fingerprint",
                "Example.C",
                "M"),
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(1)));
        AssemblyMemberSourceComparisonEntry.Available comparison =
            Create(
                request,
                pdbText,
                decompiledText,
                SourceChecksumVerification.Exact);
        return Assert.IsType<MemberSourceDiffPresentationResult.Available>(
                MemberSourceDiffPresentationAdapter.Create(comparison))
            .Presentation;
    }

    static AssemblyMemberSourceComparisonEntry.Available Create(
        AssemblyMemberSourceRequest request,
        string pdbText,
        string decompiledText,
        SourceChecksumVerification checksumVerification)
    {
        var document = new SourceDocumentObservation(
            CanonicalPath: "Fixture.cs",
            OriginalPath: "/_/Fixture.cs",
            DocumentRowId: 1,
            Storage: SourceDocumentStorage.SourceLink,
            ResolvedUrl:
                "https://raw.githubusercontent.com/example/repo/0123456789abcdef/Fixture.cs",
            ChecksumAlgorithm: "SHA256",
            Checksum: "0123456789ABCDEF");
        var inspection = new PdbMemberSourceInspection(
            new FindingInspection<string>(
                new FindingInspection<string>.Complete(
                    ImmutableArray<Finding<string>>.Empty)),
            pdbText,
            Mapping: null,
            document,
            checksumVerification)
        {
            Outcome = PdbMemberSourceOutcome.Complete
        };
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(MemberSourceComparisonTestData).Assembly.Location,
                AssemblyResolutionProvenance.Local("CLI tests"));
        return new AssemblyMemberSourceComparisonEntry.Available(
            new AssemblyContextSubject(assembly),
            request,
            new AssemblyMemberPdbSourceAttempt.Available(
                inspection,
                new AssemblyPdbSourceProvenance(
                    "https://github.com/example/repo",
                    "0123456789abcdef")),
            new AssemblyMemberDecompiledSourceAttempt.Available(
                new MemberRenderResult(
                    MemberBodyProductionStatus.Complete,
                    decompiledText,
                    [])));
    }
}
