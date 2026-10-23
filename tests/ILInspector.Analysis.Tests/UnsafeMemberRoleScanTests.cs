using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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

    public static TheoryData<string> MutationImages() =>
        new(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            FixtureCatalog.MetadataMemorySafety.AssemblyPath(),
            typeof(ClassicAsyncUnsafeFixtures).Assembly.Location);

    // The scan's call-operand facts must fail wherever the full rule's callee
    // and target resolution fail. Each mutation overwrites one byte of the
    // #Strings, #Blob, or table stream; a mutated image the census cannot open
    // is skipped, and every other must keep the scan within the full rule.
    [Theory]
    [Trait("Speed", "Slow")]
    [MemberData(nameof(MutationImages))]
    public void RoleScanDecisionsAgreeWithTheFullRuleOverMutatedMetadata(string path)
    {
        byte[] original = File.ReadAllBytes(path);
        var streams = new List<(int Start, int Length)>();
        using (var peReader = new PEReader(new MemoryStream(original, writable: false)))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            int metadata = peReader.PEHeaders.MetadataStartOffset;
            foreach (HeapIndex heap in new[] { HeapIndex.String, HeapIndex.Blob })
            {
                streams.Add((
                    metadata + reader.GetHeapMetadataOffset(heap),
                    reader.GetHeapSize(heap)));
            }
            int tables = reader.GetTableMetadataOffset(TableIndex.Module);
            int tablesEnd = reader.GetTableMetadataOffset(TableIndex.GenericParamConstraint)
                + reader.GetTableRowSize(TableIndex.GenericParamConstraint)
                    * reader.GetTableRowCount(TableIndex.GenericParamConstraint);
            streams.Add((metadata + tables, tablesEnd - tables));
        }

        byte[] values = [0x00, 0x7F, 0x80, 0xC0, 0xFF];
        var random = new Random(5254);
        int measured = 0;
        var disagreements = new List<string>();
        foreach ((int start, int length) in streams)
        {
            for (int i = 0; i < 1500; i++)
            {
                byte[] image = (byte[])original.Clone();
                int offset = start + random.Next(length);
                image[offset] = values[random.Next(values.Length)];
                UnsafeMemberRoleScanAgreement agreement;
                try
                {
                    agreement = UnsafeMemberRoleScanAgreementAnalysis.Measure(path, image);
                }
                catch (Exception ex) when (ex is InvalidDataException
                    or BadImageFormatException
                    or InvalidOperationException
                    or ArgumentException)
                {
                    continue;
                }
                measured++;
                foreach (string disagreement in agreement.Disagreements)
                    disagreements.Add($"0x{offset:X}={image[offset]:X2} {disagreement}");
            }
        }

        Assert.True(measured > 0, $"{Path.GetFileName(path)}: no mutated image opened.");
        Assert.True(
            disagreements.Count == 0,
            $"{Path.GetFileName(path)}: {measured} measured; {string.Join("; ", disagreements.Take(10))}");
    }
}
