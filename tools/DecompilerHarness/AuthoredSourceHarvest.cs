using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Text.Json;

using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Harvests the authored-source correspondence corpus: for a fixed set of input
/// assemblies (the real-world corpus libraries), enumerate real-method targets,
/// resolve each target's authoritative authored source through SourceLink, and
/// snapshot the checksum-verified member body to a vendored JSONL corpus. Because
/// the authored body is captured at generation time, benchmark runs that consume
/// the corpus are fully offline.
///
/// Selection is identity-only and biased toward small libraries: candidates are
/// ordered by round-robin across declaring types within each library, then pulled
/// round-robin across libraries in ascending candidate-count order so a few large
/// libraries do not drown out the small ones. Source content is fetched last, only
/// to snapshot and verify; a failed or unavailable fetch simply advances to the
/// next candidate, so every emitted row has real, checksum-verified source.
/// </summary>
static class AuthoredSourceHarvest
{
    internal sealed record CorpusRecord(
        string Assembly,
        string AssemblyVersion,
        string Tfm,
        string Type,
        string Method,
        int Overload,
        string? Signature,
        int MetadataToken,
        int ParameterCount,
        int IlSize,
        string? SourceUrl,
        string? ChecksumAlgorithm,
        string? Checksum,
        string AuthoredBody);

    sealed class LibraryState
    {
        public required string AssemblyPath { get; init; }
        public required string AssemblyName { get; init; }
        public required string AssemblyVersion { get; init; }
        public required string Tfm { get; init; }
        public required SourceLinkService Source { get; init; }
        public required Queue<RealMethodTargetEnumerator.RealMethodTarget> Candidates { get; init; }
    }

    public static int Run(IReadOnlyList<string> assemblies, string outputPath, int target)
        => RunAsync(assemblies, outputPath, target).GetAwaiter().GetResult();

    static async Task<int> RunAsync(IReadOnlyList<string> assemblies, string outputPath, int target)
    {
        if (assemblies.Count == 0)
        {
            Console.Error.WriteLine("Harvest requires at least one input assembly.");
            return 1;
        }

        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);

        var libraries = new List<LibraryState>();
        try
        {
            foreach (string assemblyPath in assemblies)
            {
                var library = TryOpenLibrary(assemblyPath, httpClient).GetAwaiter().GetResult();
                if (library is not null)
                    libraries.Add(library);
            }

            if (libraries.Count == 0)
            {
                Console.Error.WriteLine("No input assembly produced real-method candidates.");
                return 1;
            }

            // Smallest candidate pool first so small libraries are guaranteed a
            // fair share before large libraries exhaust the target budget.
            libraries.Sort((left, right) => left.Candidates.Count.CompareTo(right.Candidates.Count));

            long attempts = 0;
            long resolved = 0;
            long skipped = 0;
            var perLibraryKept = new Dictionary<string, int>(StringComparer.Ordinal);

            await using var writer = new StreamWriter(outputPath, append: false);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            };

            bool progressed = true;
            while (resolved < target && progressed)
            {
                progressed = false;
                foreach (var library in libraries)
                {
                    if (resolved >= target)
                        break;
                    if (library.Candidates.Count == 0)
                        continue;

                    progressed = true;
                    var candidate = library.Candidates.Dequeue();
                    attempts++;

                    var record = await TryHarvestAsync(library, candidate, fetcher);
                    if (record is null)
                    {
                        skipped++;
                        continue;
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(record, jsonOptions));
                    resolved++;
                    perLibraryKept.TryGetValue(library.AssemblyName, out int kept);
                    perLibraryKept[library.AssemblyName] = kept + 1;
                }
            }

            await writer.FlushAsync();

            Console.WriteLine($"AUTHORED-SOURCE HARVEST -> {outputPath}");
            Console.WriteLine($"  target        : {target}");
            Console.WriteLine($"  resolved      : {resolved}");
            Console.WriteLine($"  attempts      : {attempts}");
            Console.WriteLine($"  skipped       : {skipped}");
            Console.WriteLine($"  libraries     : {libraries.Count}");
            Console.WriteLine("  per library   :");
            foreach (var entry in perLibraryKept.OrderByDescending(pair => pair.Value))
                Console.WriteLine($"    {entry.Key,-40} {entry.Value}");

            return resolved == 0 ? 1 : 0;
        }
        finally
        {
            foreach (var library in libraries)
                library.Source.Dispose();
        }
    }

    static async Task<LibraryState?> TryOpenLibrary(string assemblyPath, HttpClient httpClient)
    {
        IReadOnlyList<RealMethodTargetEnumerator.RealMethodTarget> targets;
        try
        {
            targets = RealMethodTargetEnumerator.Enumerate(assemblyPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidOperationException)
        {
            Console.Error.WriteLine(
                $"Warning: harvest skipped '{assemblyPath}' ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }

        if (targets.Count == 0)
            return null;

        (string name, string version) = ReadAssemblyIdentity(assemblyPath);
        SourceLinkService? source = null;
        try
        {
            source = SourceLinkService.Open(assemblyPath);
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);
        }
        catch (Exception ex) when (ex is HttpRequestException
            or IOException
            or TaskCanceledException
            or InvalidOperationException
            or BadImageFormatException)
        {
            Console.Error.WriteLine(
                $"Warning: harvest skipped '{assemblyPath}' opening SourceLink ({ex.GetType().Name}: {ex.Message}).");
            source?.Dispose();
            return null;
        }

        return new LibraryState
        {
            AssemblyPath = assemblyPath,
            AssemblyName = name,
            AssemblyVersion = version,
            Tfm = InferTfm(assemblyPath),
            Source = source,
            Candidates = new Queue<RealMethodTargetEnumerator.RealMethodTarget>(
                DiversifyByDeclaringType(targets)),
        };
    }

    static async Task<CorpusRecord?> TryHarvestAsync(
        LibraryState library,
        RealMethodTargetEnumerator.RealMethodTarget candidate,
        SourceFetcher fetcher)
    {
        var subject = new FindingSubject(
            $"{candidate.Type}::{candidate.Method}#{candidate.Overload}",
            $"{candidate.Type}.{candidate.Method}");

        AuthoredMemberSourceInspection authored;
        try
        {
            authored = await AuthoredSourceAcquisition.AcquireMemberAsync(
                library.Source,
                candidate.MetadataToken,
                candidate.Method,
                subject,
                fetcher);
        }
        catch (Exception ex) when (ex is IOException
            or InvalidOperationException
            or HttpRequestException
            or TaskCanceledException)
        {
            return null;
        }

        if (authored.Text is not { } memberSource || memberSource.Length == 0)
            return null;

        // Reduce the PDB line-span slice to the clean, disambiguated member body
        // the benchmark will compare the decompiler output against.
        if (!AuthoredRebuildFidelity.TryExtractTargetBody(
                memberSource,
                candidate.Method,
                candidate.ParameterCount,
                out string body)
            || body.Length == 0)
        {
            return null;
        }

        return new CorpusRecord(
            Assembly: library.AssemblyName,
            AssemblyVersion: library.AssemblyVersion,
            Tfm: library.Tfm,
            Type: candidate.Type,
            Method: candidate.Method,
            Overload: candidate.Overload,
            Signature: candidate.Signature,
            MetadataToken: candidate.MetadataToken,
            ParameterCount: candidate.ParameterCount,
            IlSize: candidate.IlSize,
            SourceUrl: authored.Document?.ResolvedUrl,
            ChecksumAlgorithm: authored.Document?.ChecksumAlgorithm,
            Checksum: authored.Document?.Checksum,
            AuthoredBody: body);
    }

    // Round-robin across declaring types so no single type dominates a library's
    // contribution to the corpus.
    static IEnumerable<RealMethodTargetEnumerator.RealMethodTarget> DiversifyByDeclaringType(
        IReadOnlyList<RealMethodTargetEnumerator.RealMethodTarget> targets)
    {
        var byType = new Dictionary<string, Queue<RealMethodTargetEnumerator.RealMethodTarget>>(
            StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var target in targets)
        {
            if (!byType.TryGetValue(target.Type, out var queue))
            {
                queue = new Queue<RealMethodTargetEnumerator.RealMethodTarget>();
                byType[target.Type] = queue;
                order.Add(target.Type);
            }

            queue.Enqueue(target);
        }

        bool progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (string type in order)
            {
                var queue = byType[type];
                if (queue.Count == 0)
                    continue;

                progressed = true;
                yield return queue.Dequeue();
            }
        }
    }

    internal static (string Name, string Version) ReadAssemblyIdentity(string assemblyPath)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var assembly = reader.GetAssemblyDefinition();
            return (reader.GetString(assembly.Name), assembly.Version.ToString());
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException)
        {
            return (Path.GetFileNameWithoutExtension(assemblyPath), "0.0.0.0");
        }
    }

    // The published corpus assemblies live under lib/<tfm>/Name.dll; the parent
    // directory name is the target framework moniker.
    static string InferTfm(string assemblyPath)
    {
        string? directory = Path.GetDirectoryName(assemblyPath);
        return directory is null ? "" : Path.GetFileName(directory);
    }
}
