using System.Diagnostics;

using ILInspector.Analysis.Planning;

namespace ILInspector.AnalysisHarness;

// The corpus measurement for docs/design/unsafe-member-findings.md#role-scan.
static class UnsafeRoleScanAgreement
{
    public static int Run(string corpusList)
    {
        if (!File.Exists(corpusList))
        {
            Console.Error.WriteLine($"Corpus list not found: {corpusList}");
            return 2;
        }

        string[] paths =
        [
            .. File.ReadAllLines(corpusList)
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0 && !line.StartsWith('#')),
        ];
        int images = 0, failed = 0, bodies = 0, decided = 0, disagreements = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (string path in paths)
        {
            UnsafeMemberRoleScanAgreement agreement;
            try
            {
                agreement = UnsafeMemberRoleScanAgreementAnalysis.Measure(path, File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is InvalidDataException or BadImageFormatException or IOException)
            {
                failed++;
                Console.WriteLine($"{Path.GetFileName(path)}\tfailed\t{ex.Message}");
                continue;
            }

            images++;
            bodies += agreement.Bodies;
            decided += agreement.Decided;
            disagreements += agreement.Disagreements.Length;
            Console.WriteLine(
                $"{Path.GetFileName(path)}\tbodies={agreement.Bodies}\tdecided={agreement.Decided}\tdisagreements={agreement.Disagreements.Length}");
            foreach (string disagreement in agreement.Disagreements)
                Console.WriteLine($"  {disagreement}");
        }

        Console.WriteLine(
            $"total\timages={images}\tfailed={failed}\tbodies={bodies}\tdecided={decided}\tdisagreements={disagreements}\telapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
        return disagreements == 0 && failed == 0 ? 0 : 1;
    }
}
