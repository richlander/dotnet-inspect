using SectionRegistrySpike.Verification;

namespace SectionRegistrySpike;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--verify"))
        {
            Console.Error.WriteLine("Usage: section-registry-spike --verify");
            Console.Error.WriteLine("Runs the capability-registry evaluation spike for issue #2605 and prints Markdown evidence.");
            return 2;
        }

        var report = await Strategies.RunAsync();
        Console.Out.Write(report.Render());

        if (!report.Success)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"FAILED: {report.Failures.Count} invariant(s) violated:");
            foreach (var failure in report.Failures)
                Console.Error.WriteLine($"  - {failure}");
            return 1;
        }

        return 0;
    }
}
