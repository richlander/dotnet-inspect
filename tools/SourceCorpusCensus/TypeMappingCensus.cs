using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceCorpusCensus;

sealed record SweepManifest(SweepPackage[] Packages);

sealed record SweepPackage(
    string? ResolvedPackage,
    string? ResolvedVersion,
    string? AssemblyPath);

sealed record PackageIdentity(string Name, string Version);

sealed record TypeMapping(
    string Assembly,
    string Type,
    int DocumentCount,
    bool Correlated,
    bool CompilerGeneratedName,
    string[] Documents);

static class TypeMappingCensus
{
    const int SchemaVersion = 1;

    public static async Task<int> RunAsync(
        string assemblyListCandidate,
        string manifestCandidate,
        string cacheCandidate)
    {
        string assemblyListPath = Path.GetFullPath(assemblyListCandidate);
        string manifestPath = Path.GetFullPath(manifestCandidate);
        string cachePath = Path.GetFullPath(cacheCandidate);
        Directory.CreateDirectory(cachePath);
        NuGetCache.Initialize("source-corpus-census", cachePath);

        SweepManifest manifest = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false),
            CensusJsonContext.Default.SweepManifest)
            ?? throw new InvalidDataException(
                "Sweep manifest decoded as null.");
        string[] assemblyPaths =
        [
            .. File.ReadLines(assemblyListPath)
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(Path.GetFullPath),
        ];
        IReadOnlyDictionary<string, PackageIdentity> packageByAssemblyPath =
            MapPackageIdentities(manifest, manifestPath, assemblyPaths);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "dotnet-inspect-source-corpus-census/1.0");
        var downloader = new SymbolPackageDownloader(client);
        var assemblies = new List<AssemblyCensus>();
        var mappings = new List<TypeMapping>();

        for (int index = 0; index < assemblyPaths.Length; index++)
        {
            string assemblyPath = assemblyPaths[index];
            Console.Error.WriteLine(
                $"[{index + 1:n0}/{assemblyPaths.Length:n0}] "
                    + Path.GetFileName(assemblyPath));
            (AssemblyCensus assembly, IReadOnlyList<TypeMapping> observed) =
                await MeasureAssemblyAsync(
                    assemblyPath,
                    packageByAssemblyPath,
                    downloader).ConfigureAwait(false);
            assemblies.Add(assembly);
            mappings.AddRange(observed);
        }

        TypeMapping[] correlated =
        [
            .. mappings.Where(static mapping => mapping.Correlated),
        ];
        TypeMapping[] inferred =
        [
            .. mappings.Where(static mapping => !mapping.Correlated),
        ];
        TypeMapping[] sourceNamed =
        [
            .. correlated.Where(static mapping =>
                !mapping.CompilerGeneratedName),
        ];
        TypeMapping[] generated =
        [
            .. correlated.Where(static mapping =>
                mapping.CompilerGeneratedName),
        ];
        var report = new TypeMappingCorpusReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            assemblies.Count,
            assemblies.Count(static assembly =>
                assembly.Outcome == "measured"),
            assemblies.Count(static assembly => assembly.HasPdb),
            assemblies.Count(static assembly => assembly.HasSourceLink),
            assemblies.Count(static assembly =>
                assembly.Outcome == "failed"),
            assemblies.Sum(static assembly => assembly.TotalTypes),
            assemblies.Sum(static assembly => assembly.RejectedTypeNames),
            mappings.Count,
            correlated.Length,
            inferred.Length,
            correlated.Count(static mapping =>
                mapping.DocumentCount > 1),
            Statistics.Metric(correlated.Select(static mapping =>
                mapping.DocumentCount)),
            Statistics.Metric(inferred.Select(static mapping =>
                mapping.DocumentCount)),
            sourceNamed.Length,
            sourceNamed.Count(static mapping =>
                mapping.DocumentCount > 1),
            Statistics.Metric(sourceNamed.Select(static mapping =>
                mapping.DocumentCount)),
            generated.Length,
            generated.Count(static mapping =>
                mapping.DocumentCount > 1),
            Statistics.Metric(generated.Select(static mapping =>
                mapping.DocumentCount)),
            [
                .. sourceNamed
                    .OrderByDescending(static mapping =>
                        mapping.DocumentCount)
                    .ThenBy(
                        static mapping => mapping.Assembly,
                        StringComparer.Ordinal)
                    .ThenBy(
                        static mapping => mapping.Type,
                        StringComparer.Ordinal)
                    .Take(100)
                    .Select(static mapping => new TypeMappingExample(
                        mapping.Assembly,
                        mapping.Type,
                        mapping.DocumentCount,
                        mapping.Documents)),
            ],
            [
                .. assemblies.OrderBy(
                    static assembly => assembly.Assembly,
                    StringComparer.Ordinal),
            ]);
        CensusOutput.Write(report);
        return report.AssembliesFailed == 0 ? 0 : 1;
    }

    internal static IReadOnlyDictionary<string, PackageIdentity>
        MapPackageIdentities(
            SweepManifest manifest,
            string manifestPath,
            IReadOnlyList<string> assemblyPaths)
    {
        string manifestDirectory = Path.GetDirectoryName(
            Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException(
                "Sweep manifest has no directory.");
        SweepPackage[] packages =
        [
            .. manifest.Packages.Where(static package =>
                package.AssemblyPath is not null
                && package.ResolvedPackage is not null
                && package.ResolvedVersion is not null),
        ];
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var result = new Dictionary<string, PackageIdentity>(pathComparer);
        foreach (string assemblyCandidate in assemblyPaths)
        {
            string assemblyPath = Path.GetFullPath(assemblyCandidate);
            SweepPackage[] matches =
            [
                .. packages.Where(package => ManifestPathMatches(
                    assemblyPath,
                    manifestDirectory,
                    package.AssemblyPath!)),
            ];
            if (matches.Length > 1)
            {
                throw new InvalidDataException(
                    $"Assembly '{assemblyPath}' matches multiple sweep "
                        + "manifest packages.");
            }
            if (matches is not [var match])
                continue;

            result.Add(
                assemblyPath,
                new(
                    match.ResolvedPackage!,
                    match.ResolvedVersion!));
        }

        return result;
    }

    static async Task<(
        AssemblyCensus Assembly,
        IReadOnlyList<TypeMapping> Mappings)> MeasureAssemblyAsync(
        string assemblyPath,
        IReadOnlyDictionary<string, PackageIdentity> packageByAssemblyPath,
        SymbolPackageDownloader downloader)
    {
        string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        int totalTypes = 0;
        int rejectedTypeNames = 0;
        int mappedTypes = 0;
        int correlatedTypes = 0;
        int inferredTypes = 0;
        string outcome;
        string? detail = null;
        bool hasPdb = false;
        bool hasSourceLink = false;
        var mappings = new List<TypeMapping>();

        try
        {
            using var source = SourceLinkService.Open(assemblyPath);
            if (source.Context.NeedsPdb
                && source.Context.PdbId is { } pdb)
            {
                PackageIdentity? package =
                    packageByAssemblyPath.GetValueOrDefault(assemblyPath)
                    ?? InferNuGetIdentity(assemblyPath);
                try
                {
                    PdbDownloadResult download =
                        await downloader.DownloadPdbAsync(
                            pdb.Guid,
                            pdb.Age,
                            pdb.PdbFileName,
                            pdb.IsPortable,
                            assemblyPath,
                            package?.Name,
                            package?.Version,
                            portablePdbStamp: pdb.Stamp)
                            .ConfigureAwait(false);
                    if (download.PdbFilePath is { } pdbPath)
                    {
                        source.LoadPdb(
                            pdbPath,
                            "Symbol Package",
                            download.SymbolServer);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException
                        or HttpRequestException
                        or InvalidDataException
                        or InvalidOperationException
                        or TaskCanceledException
                        or UnauthorizedAccessException)
                {
                    detail =
                        $"PDB acquisition failed: "
                        + exception.GetType().Name;
                }
            }

            hasPdb = source.HasPdb;
            hasSourceLink = source.HasSourceLink;
            if (!hasPdb)
            {
                outcome = source.Context.WindowsPdbDetected
                    ? "windows-pdb"
                    : source.Context.NeedsPdb
                        ? "portable-pdb-unavailable"
                        : "pdb-absent";
            }
            else
            {
                using var stream = File.OpenRead(assemblyPath);
                using var pe = new PEReader(stream);
                MetadataReader reader = pe.GetMetadataReader();
                assemblyName = reader.IsAssembly
                    ? reader.GetString(
                        reader.GetAssemblyDefinition().Name)
                    : assemblyName;
                foreach (TypeDefinitionHandle handle
                    in reader.TypeDefinitions)
                {
                    totalTypes++;
                    MetadataTypeDefinitionNameReadResult read =
                        MetadataTypeDefinitionName.Read(reader, handle);
                    if (read
                        is not MetadataTypeDefinitionNameReadResult.Read
                            named)
                    {
                        rejectedTypeNames++;
                        continue;
                    }

                    SourceLinkResolver.TypeSourceInfo? mapping =
                        source.ResolveTypeSource(named.Name);
                    if (mapping is null)
                        continue;

                    mappedTypes++;
                    bool correlated =
                        mapping.Documents.Length > 0
                        && mapping.Documents.All(static document =>
                            document.ResolutionMethod
                                == SourceLinkResolver
                                    .SourceResolutionMethod.SourceLink);
                    if (correlated)
                        correlatedTypes++;
                    else
                        inferredTypes++;

                    mappings.Add(
                        new(
                            assemblyName,
                            named.Name.ToEscapedFullName(),
                            mapping.Documents.Length,
                            correlated,
                            named.Name.Segments.Any(static segment =>
                                segment.Contains(
                                    '<',
                                    StringComparison.Ordinal)),
                            [
                                .. mapping.Documents.Select(
                                    static document =>
                                        Path.GetFileName(
                                            Uri.TryCreate(
                                                    document.SourceUrl,
                                                    UriKind.Absolute,
                                                    out Uri? uri)
                                                ? uri.LocalPath
                                                : document.FilePath)),
                            ]));
                }

                outcome = "measured";
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or BadImageFormatException
                or InvalidDataException
                or InvalidOperationException
                or UnauthorizedAccessException)
        {
            outcome = "failed";
            detail = exception.GetType().Name;
        }

        return (
            new(
                assemblyName,
                outcome,
                detail,
                hasPdb,
                hasSourceLink,
                totalTypes,
                rejectedTypeNames,
                mappedTypes,
                correlatedTypes,
                inferredTypes),
            mappings);
    }

    static bool ManifestPathMatches(
        string assemblyPath,
        string manifestDirectory,
        string manifestAssemblyPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalized = manifestAssemblyPath.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            return assemblyPath.Equals(
                Path.GetFullPath(normalized),
                comparison);
        }

        string directPath = Path.GetFullPath(
            Path.Combine(manifestDirectory, normalized));
        if (assemblyPath.Equals(directPath, comparison))
            return true;

        string suffix =
            Path.DirectorySeparatorChar
            + normalized.TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        return assemblyPath.EndsWith(suffix, comparison);
    }

    static PackageIdentity? InferNuGetIdentity(string assemblyPath)
    {
        string[] segments = assemblyPath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        int packages = Array.FindLastIndex(
            segments,
            static segment => segment.Equals(
                "packages",
                StringComparison.Ordinal));
        return packages >= 0 && packages + 2 < segments.Length
            ? new PackageIdentity(
                segments[packages + 1],
                segments[packages + 2])
            : null;
    }
}
