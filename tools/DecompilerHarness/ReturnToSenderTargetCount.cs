using DotnetInspector.ResearchQueries;
using DotnetInspector.ResearchSections;

using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

static class ReturnToSenderTargetCount
{
    internal static ReturnToSenderTargetCountOutcome Count(
        IReadOnlyList<string> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var sessions =
            new AssemblyInspectionSession[assemblies.Count];
        try
        {
            var subjects =
                new ReturnToSenderTargetSubject[
                    assemblies.Count];
            for (int index = 0; index < assemblies.Count; index++)
            {
                string assemblyPath = assemblies[index];
                sessions[index] =
                    AssemblyInspectionSession.Open(assemblyPath);
                subjects[index] =
                    new(assemblyPath, sessions[index]);
            }

            return ReturnToSenderTargetInspection.Count(
                subjects,
                ReturnToSenderTargetQuery.CreateCountRequest());
        }
        finally
        {
            for (int index = sessions.Length - 1;
                 index >= 0;
                 index--)
            {
                sessions[index]?.Dispose();
            }
        }
    }

    internal static int Run(
        IReadOnlyList<string> assemblies)
    {
        ReturnToSenderTargetCountOutcome outcome =
            Count(assemblies);
        if (outcome is not
            ReturnToSenderTargetCountOutcome.Counted counted)
        {
            var rejected =
                (ReturnToSenderTargetCountOutcome.Rejected)outcome;
            Console.Error.WriteLine(
                "RTS target Count request was rejected: "
                + rejected.Resolution);
            return 1;
        }

        Console.WriteLine(
            $"RTS target Count: {counted.Count}");
        Console.WriteLine(
            $"  assemblies: {counted.Receipt.Assemblies.Count}");
        Console.WriteLine(
            $"  scanned method bodies: "
            + $"{counted.Receipt.ScannedBodyCount}");
        Console.WriteLine(
            $"  declaration candidates: "
            + $"{counted.Receipt.DeclarationCandidateCount}");
        Console.WriteLine(
            $"  materialized target rows: "
            + $"{counted.Receipt.MaterializedRowCount}");
        return 0;
    }
}
