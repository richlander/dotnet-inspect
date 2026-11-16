using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

// Gates for docs/design/unsafe-member-findings.md#execution, Member scope.
public sealed partial class UnsafeMemberFindingsTests
{
    // Each declared member's own census is the Library census's finding for
    // it: lambda, local-function, state-machine, and iterator bodies fold in
    // through the authenticated expansion of the member alone.
    [Theory]
    [InlineData("new")]
    [InlineData("legacy")]
    [InlineData("iterators")]
    [InlineData("analysis")]
    public void MemberScopeAgreesWithLibraryScopeWithinItsBreadth(string fixture)
    {
        string path = fixture switch
        {
            "new" => UnsafeFixturePath(legacy: false),
            "legacy" => UnsafeFixturePath(legacy: true),
            "iterators" => IteratorFixturePath,
            _ => AnalysisFixturePath,
        };
        UnsafeMemberCensus library = LibraryCensus(path);
        Assert.NotEmpty(library.Members);
        Assert.Contains(
            library.Members,
            finding => finding.Evidence.Any(evidence =>
                evidence.Body.MetadataToken != finding.Member.MetadataToken));

        foreach (UnsafeMemberFinding expected in library.Members)
        {
            UnsafeMemberCensus member = Assert.IsType<UnsafeMemberCensusResult.Available>(
                    MemberScopeCensus(path, [MetadataTokens.MethodDefinitionHandle(expected.Member.MetadataToken)]))
                .Census;
            Assert.Equal(expected, Assert.Single(member.Members));
            Assert.Equal(expected.HasPartialEvidence, !member.IsComplete);
        }
    }

    // Member-scope breadth is the member's definitions and the generated
    // bodies authenticated to them: no type range, no table scan, and no
    // unauthenticated candidates.
    [Fact]
    public void MemberScopeWorkStaysWithinTheMemberAndItsExpansion()
    {
        UnsafeMemberCensus library = LibraryCensus(AnalysisFixturePath);
        UnsafeMemberFinding finding = Single(
            library,
            nameof(ClassicAsyncUnsafeFixtures),
            nameof(ClassicAsyncUnsafeFixtures.AsyncStackAllocation));
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(finding.Member.MetadataToken);
        var available = Assert.IsType<UnsafeMemberCensusResult.Available>(
            MemberScopeCensus(AnalysisFixturePath, [method]));
        MethodDefinitionSourceCoverage coverage = available.SourceReceipt.Coverage;
        Assert.NotEmpty(coverage.GeneratedExpansion.Origins);
        Assert.Empty(coverage.GeneratedExpansion.UnauthenticatedCandidates);

        using var stream = File.OpenRead(AnalysisFixturePath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        HashSet<MethodDefinitionHandle> allowed =
        [
            method,
            .. coverage.GeneratedExpansion.Origins.Select(static origin => origin.Method),
        ];
        Assert.True(reader.MethodDefinitions.Count > allowed.Count * 10);
        Assert.Equal(allowed.Count, coverage.MethodsSelected.Count);
        Assert.All(allowed, selected => Assert.True(coverage.MethodsSelected.Contains(selected)));
        Assert.True(coverage.BodiesAcquired.Count <= allowed.Count);
        Assert.Equal(0, available.SourceReceipt.ModuleLookups);
    }

    static UnsafeMemberCensusResult MemberScopeCensus(
        string path,
        IEnumerable<MethodDefinitionHandle> methods)
    {
        var operation = AssemblyAnalysisOperation<UnsafeMemberCensus>.Create(
            path,
            UnsafeMemberCensusAnalysis
                .ForMethods(methods)
                .AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(File.ReadAllBytes(path), writable: false));
        AssemblyAnalysisExecution<UnsafeMemberCensus> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<UnsafeMemberCensus>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;
        return UnsafeMemberCensusAnalysis.Project(execution);
    }
}
