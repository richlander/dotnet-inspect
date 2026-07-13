using SectionRegistrySpike.Verification;

namespace SectionRegistrySpike;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool verify = args.Contains("--verify");
        bool benchmark = args.Contains("--benchmark");
        if (!verify && !benchmark)
        {
            Console.Error.WriteLine("Usage: section-registry-spike [--verify] [--benchmark]");
            return 2;
        }

        if (benchmark)
            Console.Out.Write(Performance.Run());

        if (!verify)
            return 0;

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
