using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Text.Json;

using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Harvests authored-source correspondence corpora: for a set of input
/// assemblies, enumerate real-method targets, resolve each target's
/// authoritative authored source through SourceLink, and snapshot the
/// checksum-verified member body to a vendored JSONL corpus. Because the
/// authored body is captured at generation time, benchmark runs that consume the
/// corpus are fully offline.
///
/// Two selection policies share this harvester. Both order libraries by ascending
/// candidate count so a few large libraries do not drown out the small ones, and
/// both round-robin across declaring types so no single type dominates a
/// library's contribution. They differ only in what each type contributes next:
/// <list type="bullet">
/// <item>The <em>CIVIL</em> corpus (Curated Index of Varied IL, identity-only)
/// keeps candidates in enumeration order.</item>
/// <item>The <em>EVIL</em> corpus (Edge-case Verification of IL Legibility)
/// orders each type's candidates by descending IL difficulty score, so each
/// library contributes its most diabolical methods first, and attaches the
/// difficulty profile to every emitted row.</item>
/// </list>
/// Source content is fetched last, only to snapshot and verify; a failed or
/// unavailable fetch simply advances to the next candidate, so every emitted row
/// has real, checksum-verified source.
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
        string AuthoredBody,
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        Guid? ModuleVersionId = null,
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? PrinterBody = null,
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        int? PrinterBodyVersion = null,
        // Omitted for the CIVIL (identity) corpus so its rows stay
        // schema-identical to the vendored corpus; populated only for EVIL.
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        IlDifficulty? Difficulty = null);

    /// <summary>
    /// One harvest attempt for one target: either the captured corpus row, or the typed
    /// reason the target produced no row.
    ///
    /// <para>Both harvest modes and the source-oracle candidate ledger call the same
    /// attempt. The harvest modes previously saw only "row or null" and counted every
    /// non-row as one undifferentiated skip, so a sweep could not say whether a target
    /// was missing because its source did not arrive or because its body was not
    /// extractable — the distinction the candidate ledger has to publish, and the
    /// distinction between a file that is genuinely rejected and one that was never
    /// measured.</para>
    /// </summary>
    internal sealed record HarvestAttempt(
        CorpusRecord? Record,
        SourceOracleCandidateLedger.CandidateReason? Reason)
    {
        public static HarvestAttempt Rejected(SourceOracleCandidateLedger.CandidateReason reason)
            => new(null, reason);
    }

    /// <summary>
    /// The assembly-identity coordinates every captured row carries. Taken apart from
    /// the harvest's own library state so the candidate ledger, which opens its own
    /// SourceLink service for the PDB census, calls the same attempt.
    /// </summary>
    internal sealed record HarvestIdentity(
        string AssemblyName,
        string AssemblyVersion,
        Guid ModuleVersionId,
        string Tfm);

    sealed class LibraryState
    {
        public required string AssemblyPath { get; init; }
        public required string AssemblyName { get; init; }
        public required string AssemblyVersion { get; init; }
        public required Guid ModuleVersionId { get; init; }
        public required string Tfm { get; init; }
        public required SourceLinkService Source { get; init; }
        public required Queue<RealMethodTargetEnumerator.RealMethodTarget> Candidates { get; init; }

        public HarvestIdentity Identity
            => new(AssemblyName, AssemblyVersion, ModuleVersionId, Tfm);
    }

    public static int Run(
        IReadOnlyList<string> assemblies,
        string outputPath,
        int target,
        bool evil = false,
        IReadOnlyList<string>? repositoryPaths = null)
        => RunAsync(assemblies, outputPath, target, evil, repositoryPaths).GetAwaiter().GetResult();

    static async Task<int> RunAsync(
        IReadOnlyList<string> assemblies,
        string outputPath,
        int target,
        bool evil,
        IReadOnlyList<string>? repositoryPaths)
    {
        if (assemblies.Count == 0)
        {
            Console.Error.WriteLine("Harvest requires at least one input assembly.");
            return 1;
        }

        HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        using var httpClient = HttpClientFactory.CreateClient();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);

        var libraries = new List<LibraryState>();
        try
        {
            foreach (string assemblyPath in assemblies)
            {
                var library = await TryOpenLibrary(assemblyPath, httpClient, evil);
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
            var skipReasons = new Dictionary<SourceOracleCandidateLedger.CandidateReason, int>();
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

                    var attempt = await TryHarvestAsync(library, candidate, fetcher, evil, repositoryPaths);
                    if (attempt.Record is not { } record)
                    {
                        skipped++;
                        var reason = attempt.Reason
                            ?? throw new InvalidOperationException(
                                "A harvest attempt without a record must carry a reason.");
                        skipReasons.TryGetValue(reason, out int reasonCount);
                        skipReasons[reason] = reasonCount + 1;
                        continue;
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(record, jsonOptions));
                    resolved++;
                    perLibraryKept.TryGetValue(library.AssemblyName, out int kept);
                    perLibraryKept[library.AssemblyName] = kept + 1;
                }
            }

            await writer.FlushAsync();

            Console.WriteLine($"{(evil ? "EVIL" : "AUTHORED-SOURCE")} HARVEST -> {outputPath}");
            Console.WriteLine($"  target        : {target}");
            Console.WriteLine($"  resolved      : {resolved}");
            Console.WriteLine($"  attempts      : {attempts}");
            Console.WriteLine($"  skipped       : {skipped}");
            foreach (var entry in skipReasons
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => SourceOracleCandidateLedger.Code(pair.Key), StringComparer.Ordinal))
            {
                Console.WriteLine(
                    $"    {SourceOracleCandidateLedger.Code(entry.Key),-28} "
                    + $"{entry.Value} ({SourceOracleCandidateLedger.FamilyOf(entry.Key)})");
            }
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

    static async Task<LibraryState?> TryOpenLibrary(string assemblyPath, HttpClient httpClient, bool evil)
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
        bool ownershipTransferred = false;
        try
        {
            source = SourceLinkService.Open(assemblyPath);
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);

            var state = new LibraryState
            {
                AssemblyPath = assemblyPath,
                AssemblyName = name,
                AssemblyVersion = version,
                ModuleVersionId = ReadModuleVersionId(assemblyPath),
                Tfm = InferTfm(assemblyPath),
                Source = source,
                Candidates = new Queue<RealMethodTargetEnumerator.RealMethodTarget>(
                    DiversifyByDeclaringType(targets, evil)),
            };
            ownershipTransferred = true;
            return state;
        }
        catch (Exception ex) when (ex is HttpRequestException
            or IOException
            or TaskCanceledException
            or InvalidOperationException
            or BadImageFormatException)
        {
            Console.Error.WriteLine(
                $"Warning: harvest skipped '{assemblyPath}' opening SourceLink ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
        finally
        {
            // Own the SourceLinkService until it is handed to a returned LibraryState.
            // Any failure path (caught transient error, an unlisted exception, or the
            // Queue/DiversifyByDeclaringType materialization throwing) disposes it here.
            if (!ownershipTransferred)
                source?.Dispose();
        }
    }

    static async Task<HarvestAttempt> TryHarvestAsync(
        LibraryState library,
        RealMethodTargetEnumerator.RealMethodTarget candidate,
        SourceFetcher fetcher,
        bool evil,
        IReadOnlyList<string>? repositoryPaths)
        => await TryHarvestAsync(
            library.Source,
            library.Identity,
            candidate,
            fetcher,
            evil,
            repositoryPaths);

    /// <summary>
    /// Attempts one target: acquire its authoritative authored source through the PDB,
    /// reduce it to the member body, and return the corpus row — or the typed reason no
    /// row exists.
    ///
    /// <para>The reasons are disjoint and stable, and split into the families the
    /// candidate ledger reports on: an <em>acquisition</em> reason means the target was
    /// never measured (no mapping, no immutable source identity, source unavailable or
    /// unfetchable), while a <em>structural</em> reason means the target was measured and
    /// is not eligible for whole-file printer correspondence.</para>
    /// </summary>
    internal static async Task<HarvestAttempt> TryHarvestAsync(
        SourceLinkService source,
        HarvestIdentity identity,
        RealMethodTargetEnumerator.RealMethodTarget candidate,
        SourceFetcher fetcher,
        bool evil,
        IReadOnlyList<string>? repositoryPaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(fetcher);

        var subject = new FindingSubject(
            $"{candidate.Type}::{candidate.Method}#{candidate.Overload}",
            $"{candidate.Type}.{candidate.Method}");

        PdbMemberSourceInspection authored;
        try
        {
            authored = await PdbSourceAcquisition.AcquireMemberAsync(
                source,
                candidate.MetadataToken,
                candidate.Method,
                subject,
                fetcher,
                repositoryPaths);
        }
        catch (Exception ex) when (ex is IOException
            or InvalidOperationException
            or HttpRequestException
            or TaskCanceledException)
        {
            return HarvestAttempt.Rejected(
                SourceOracleCandidateLedger.CandidateReason.SourceAcquisitionFailed);
        }

        if (ClassifyUnavailableInspection(authored) is { } reason)
            return HarvestAttempt.Rejected(reason);

        string memberSource = authored.Text!;

        // Reduce the PDB line-span slice to the clean, disambiguated member body
        // the benchmark will compare the decompiler output against.
        if (!AuthoredRebuildFidelity.TryExtractTargetBodies(
                memberSource,
                candidate.Method,
                candidate.ParameterCount,
                out string body,
                out string? printerBody)
            || body.Length == 0)
        {
            return HarvestAttempt.Rejected(
                SourceOracleCandidateLedger.CandidateReason.BodyExtractionFailed);
        }

        var record = new CorpusRecord(
            Assembly: identity.AssemblyName,
            AssemblyVersion: identity.AssemblyVersion,
            Tfm: identity.Tfm,
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
            AuthoredBody: body,
            ModuleVersionId: identity.ModuleVersionId,
            PrinterBody: printerBody,
            PrinterBodyVersion: printerBody is null
                ? null
                : AuthoredSourceOracleManifest.PrinterComparisonVersion,
            Difficulty: evil ? candidate.Difficulty : null);
        return new HarvestAttempt(record, null);
    }

    internal static SourceOracleCandidateLedger.CandidateReason?
        ClassifyUnavailableInspection(PdbMemberSourceInspection inspection)
    {
        if (inspection.Mapping is not null
            && inspection.Document is not null
            && inspection.ChecksumVerification is SourceChecksumVerification.Exact
                or SourceChecksumVerification.LineEndingNormalized
            && inspection.Text is not { Length: > 0 })
        {
            return SourceOracleCandidateLedger.CandidateReason.BodyExtractionFailed;
        }

        if (inspection.Lines.Value is FindingInspection<string>.Failed)
            return SourceOracleCandidateLedger.CandidateReason.SourceAcquisitionFailed;

        if (inspection.Mapping is null)
            return SourceOracleCandidateLedger.CandidateReason.NoPdbSourceMapping;

        if (inspection.Text is not { Length: > 0 })
        {
            // A mapped document without immutable identity cannot become a whole-file
            // candidate; keep it distinct from an unavailable identified source.
            return SourceOracleCandidateLedger.HasImmutableIdentity(inspection.Document)
                ? SourceOracleCandidateLedger.CandidateReason.SourceUnavailable
                : SourceOracleCandidateLedger.CandidateReason.NoImmutableSourceIdentity;
        }

        return null;
    }

    internal static Guid ReadModuleVersionId(string assemblyPath)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    // Round-robin across declaring types so no single type dominates a library's
    // contribution to the corpus. For the EVIL corpus, each type's candidates
    // are ordered by descending IL difficulty first, so a type contributes its
    // most diabolical methods before its easy ones while the round-robin still
    // spreads the budget across types.
    static IEnumerable<RealMethodTargetEnumerator.RealMethodTarget> DiversifyByDeclaringType(
        IReadOnlyList<RealMethodTargetEnumerator.RealMethodTarget> targets,
        bool rankByDifficulty)
    {
        IEnumerable<RealMethodTargetEnumerator.RealMethodTarget> ordered = rankByDifficulty
            ? targets
                .OrderByDescending(target => target.Difficulty.Score)
                .ThenByDescending(target => target.Difficulty.IlSize)
            : targets;

        var byType = new Dictionary<string, Queue<RealMethodTargetEnumerator.RealMethodTarget>>(
            StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var target in ordered)
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
    internal static string InferTfm(string assemblyPath)
    {
        string? directory = Path.GetDirectoryName(assemblyPath);
        return directory is null ? "" : Path.GetFileName(directory);
    }
}
