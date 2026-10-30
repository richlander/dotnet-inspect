using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;

namespace ILInspector.Analysis.Tests;

// Gates for docs/design/unsafe-member-findings.md#role-scan: a body the role
// scan decides is one the full rule completes with only the member's own
// contract.
public class UnsafeMemberRoleScanTests
{
    public static TheoryData<string> Images()
    {
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return new TheoryData<string>(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(),
            FixtureCatalog.MetadataMemorySafety.AssemblyPath(),
            FixtureCatalog.AnalysisCallFunctionPointerScope.AssemblyPath(),
            typeof(ClassicAsyncUnsafeFixtures).Assembly.Location,
            typeof(object).Assembly.Location,
            Path.Combine(runtime, "System.Text.Json.dll"),
            Path.Combine(runtime, "System.Linq.Expressions.dll"));
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void RoleScanDecisionsAgreeWithTheFullRule(string path)
    {
        UnsafeMemberRoleScanAgreement agreement =
            UnsafeMemberRoleScanAgreementAnalysis.Measure(path, File.ReadAllBytes(path));

        Assert.True(agreement.Decided > 0, $"{Path.GetFileName(path)}: the scan decided no body.");
        Assert.True(
            agreement.Disagreements.IsEmpty,
            $"{Path.GetFileName(path)}: {string.Join("; ", agreement.Disagreements.Take(10))}");
    }
}
