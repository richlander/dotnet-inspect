using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using SignatureVerificationResult = DotnetInspector.Services.SignatureVerificationResult;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class LibraryCommand
{
    public static async Task<int> ExecuteAsync(LibraryOptions options)
    {
        var assemblyPath = options.AssemblyName;
        var pipeline = LibrarySections.CreatePipeline();
        var scannerRegistry = LibrarySections.CreateScannerRegistry();

        var schemaMap = InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema();
        bool hasInputSource = !string.IsNullOrEmpty(assemblyPath)
            || !string.IsNullOrEmpty(options.PackagePath)
            || !string.IsNullOrEmpty(options.PlatformAssembly);

        // Static discovery mode: -D --schema lists schema without resolving/loading the library.
        if (options.Discover != null)
        {
            if (!options.Schema && hasInputSource)
            {
                // Need to run pipeline to determine effective sections — handled after data collection below.
            }
            else
            {
                return DiscoverOutput.Execute(options.Discover, schemaMap,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
                    verbosity: (int)options.Verbosity,
                    sectionCostAnnotations: pipeline.GetCostAnnotations(),
                    sectionCategories: pipeline.GetCategoryMap());
            }
        }

        // -D defaults to effective discovery for target-based commands.
        bool effectiveDiscovery = options.Discover != null && !options.Schema && hasInputSource;
        var userVerbosity = options.Verbosity; // preserve for display formatting
        options = options with { UserVerbosityOverride = userVerbosity };
        if (effectiveDiscovery)
            options = options with { Verbosity = Verbosity.Detailed };

        var normalized = NormalizeILOffsetSelection(options);
        if (normalized.Error is not null)
        {
            Console.Error.WriteLine(normalized.Error);
            return 1;
        }
        options = normalized.Options;

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
        {
            if (selectResult.Sections.Overlaps(ILCoordinateSections)
                && string.IsNullOrWhiteSpace(options.ILOffsetParameter))
            {
                if (!HasExactILCoordinateSelection(options.Select))
                {
                    selectResult.Sections.ExceptWith(ILCoordinateSections);
                }
                else if (options.Discover == null)
                {
                    Console.Error.WriteLine("Error: IL coordinate sections require --il-offset <token>+<offset>.");
                    return 1;
                }
            }

            options = options with { IncludeSections = selectResult.Sections };
        }

        if (!string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Overlaps(ILCoordinateSections))
        {
            Console.Error.WriteLine("Error: --il-offset requires an IL coordinate section. Omit -S or include -S \"Source Location\", -S \"Member Context\", -S \"Instruction Context\", -S \"Exception Context\", -S \"Callsite Context\", or -S \"Return Address Context\".");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            && !string.IsNullOrWhiteSpace(options.ILOffsetsPath))
        {
            Console.Error.WriteLine("Error: --il-offset cannot be combined with --il-offsets.");
            return 1;
        }

        if (options.Count && !CountOutput.ValidateSingleSection(options.IncludeSections))
            return 1;

        if (options.Count && (options.Print || options.PrintAll))
        {
            Console.Error.WriteLine("Error: --count cannot be combined with --print or --print-all.");
            return 1;
        }

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount > 1)
        {
            Console.Error.WriteLine("Error: specify only one of --value, --urls, or --paths.");
            return 1;
        }

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            if (!ShapeProjectionOutput.ValidateSingleSection(options.IncludeSections, optionName))
                return 1;
            if (options.Count || options.Print || options.PrintAll)
            {
                Console.Error.WriteLine($"Error: {optionName} cannot be combined with --count, --print, or --print-all.");
                return 1;
            }
            if (options.Rows is not null)
            {
                Console.Error.WriteLine($"Error: --rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N to select a projected row.");
                return 1;
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print && !options.PrintAll)
        {
            Console.Error.WriteLine("Error: --json-array requires --value, --urls, --paths, --print, or --print-all.");
            return 1;
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            Console.Error.WriteLine("Error: --json-array cannot be combined with --json or --jsonl.");
            return 1;
        }

        if ((options.Print || options.PrintAll) && !ValidateLibraryPrintSelection(options.IncludeSections))
            return 1;

        if ((options.Print || options.PrintAll) && options.Rows is not null)
        {
            Console.Error.WriteLine("Error: --rows cannot be combined with --print or --print-all; use --row N to choose a printed row.");
            return 1;
        }

        if (options.Print && options.PrintAll)
        {
            Console.Error.WriteLine("Error: --print cannot be combined with --print-all.");
            return 1;
        }

        if (options.PrintAll && options.PrintRow is not null)
        {
            Console.Error.WriteLine("Error: --print-all cannot be combined with --row.");
            return 1;
        }

        if (options.ProjectionRow is not null && !options.Print && !options.PrintAll && shapeCount == 0)
        {
            Console.Error.WriteLine("Error: --row requires --print, --value, --urls, or --paths.");
            return 1;
        }

        // -S targeting specific sections: promote verbosity to ensure data collection
        var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
        if (requiredVerbosity > options.Verbosity)
            options = options with { Verbosity = requiredVerbosity };

        // Pre-render validation: check --fields/--columns names against the section schema
        if ((options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 }) && options.IncludeSections is { Count: > 0 })
        {
            if (!ProjectionDiagnostics.ValidateProjection(schemaMap, options.IncludeSections, options.Fields, options.Columns))
                return 1;
        }

        if (!OutputFormatResolver.ValidateSingleSectionForTabular(options.OneLineExplicitlySet, options.IncludeSections))
            return 1;

        // Warn if tabular output is combined with detailed verbosity without section selector
        if (!effectiveDiscovery && !options.Count)
            OutputFormatResolver.WarnIfOneLineDetailMismatch(options.OneLine, options.Verbosity, options.IncludeSections);

        // Compute which scanners are needed for the requested sections
        var scanners = pipeline.GetRequiredScanners(
            options.Verbosity, options.IncludeSections);

        // Check for valid input source
        if (string.IsNullOrEmpty(assemblyPath) &&
            string.IsNullOrEmpty(options.PackagePath) &&
            string.IsNullOrEmpty(options.PlatformAssembly))
        {
            Console.Error.WriteLine("Error: Library path, package name, or --platform required.");
            Console.Error.WriteLine("Run 'dotnet-inspect library --help' for usage.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            // Parse package name and version for symbol package download
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageExtractor.ParsePackageReference(options.PackagePath);
            }

            if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                var (resolvedPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    options.PlatformAssembly,
                    context.HttpClient,
                    logger.Log,
                    options.PlatformFramework,
                    useRuntimeAssemblies: true,
                    platformVersion: options.PlatformVersion);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                logger.Log($"Using platform runtime library: {framework} {version}");

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                    return await WriteILCoordinateBatchAsync(resolvedPath!, null, null, isPlatformAssembly: true, options, context.HttpClient, logger);

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && options.Discover is { Length: 0 } && !HasILOffsetCoordinate(options))
                {
                    var cached = TryGetCachedEffective(resolvedPath!);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(resolvedPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                var inspection = await LibraryMetadataService.InspectAsync(resolvedPath!, options, logger, null, null, context.HttpClient, isPlatformAssembly: true, scanners: scanners, scannerRegistry: scannerRegistry);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {resolvedPath}");
                    return 1;
                }

                inspection.Source = SourceKind.Platform;
                inspection.PlatformVersion = version;
                inspection.LastModified = File.GetLastWriteTimeUtc(resolvedPath!);

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, resolvedPath!, null, null, isPlatformAssembly: true, options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                if (effectiveDiscovery)
                    return WriteEffectiveSections(resolvedPath!, inspection, options, pipeline, userVerbosity, cache: !HasILOffsetCoordinate(options));
                if (TryWriteLibrarySingletonCount(inspection, options))
                    return 0;
                if (options.Print || options.PrintAll)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                WarnEmptySections(inspection, options, pipeline);
                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                ExtractResourcesIfRequested(resolvedPath!, options, logger);
                return IntegrityExitCode(inspection);
            }
            else if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(
                    assemblyPath, options.PackagePath, options.Tfm,
                    options.SourceOptions, options.IncludePrerelease, logger, context.HttpClient);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion) = extractResult.Value;
                tempDir = extractTempDir;
                packageName = resolvedPackageName;
                packageVersion = resolvedPackageVersion;

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                    return await WriteILCoordinateBatchAsync(assemblyPaths[0], packageName, packageVersion, isPlatformAssembly: false, options, context.HttpClient, logger);

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && options.Discover is { Length: 0 } && assemblyPaths.Count > 0 && !HasILOffsetCoordinate(options))
                {
                    var cached = TryGetCachedEffective(assemblyPaths[0]);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPaths[0]);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                // Verify package signature if nupkg is available
                SignatureVerificationResult? signatureResult = null;
                if (nupkgPath != null)
                {
                    logger.Log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
                    signatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
                }

                // Inspect all assemblies
                var inspections = await CollectPackageInspectionsAsync(
                    assemblyPaths, options, logger, packageName, packageVersion,
                    extractPath, context.HttpClient, signatureResult, scanners, scannerRegistry);

                if (inspections.Count == 0)
                {
                    Console.Error.WriteLine("Error: No libraries could be read from the package.");
                    return 1;
                }

                foreach (var insp in inspections)
                    insp.Source = SourceKind.NuGet;

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspections[0], assemblyPaths[0], packageName, packageVersion, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                if (effectiveDiscovery)
                    return WriteEffectiveSections(assemblyPaths[0], inspections[0], options, pipeline, userVerbosity, cache: !HasILOffsetCoordinate(options));
                if (TryWriteLibrarySingletonCount(inspections[0], options))
                    return 0;
                if (options.Print || options.PrintAll)
                    return await WriteLibraryPrintProjectionAsync(inspections[0], options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspections[0], options);
                WarnEmptySections(inspections[0], options, pipeline);
                if (inspections.Count == 1)
                    OutputFormatter.WriteLibraryResult(inspections[0], options, pipeline);
                else
                    OutputFormatter.WriteLibraryResults(inspections, options, pipeline);

                if (assemblyPaths.Count > 0)
                    ExtractResourcesIfRequested(assemblyPaths[0], options, logger);

                return IntegrityExitCode([.. inspections]);
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {assemblyPath}");
                    return 1;
                }

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                    return await WriteILCoordinateBatchAsync(assemblyPath!, null, null, isPlatformAssembly: false, options, context.HttpClient, logger);

                // Check effective sections cache before running full inspection
                if (effectiveDiscovery && options.Discover is { Length: 0 } && !HasILOffsetCoordinate(options))
                {
                    var cached = TryGetCachedEffective(assemblyPath!);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, userVerbosity, rootLabel);
                    }
                }

                var inspection = await LibraryMetadataService.InspectAsync(assemblyPath!, options, logger, null, null, context.HttpClient, scanners: scanners, scannerRegistry: scannerRegistry);
                if (inspection == null)
                {
                    Console.Error.WriteLine($"Error: Could not read library: {assemblyPath}");
                    return 1;
                }

                inspection.Source = SourceKind.File;

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, assemblyPath!, null, null, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                if (effectiveDiscovery)
                    return WriteEffectiveSections(assemblyPath!, inspection, options, pipeline, userVerbosity, cache: !HasILOffsetCoordinate(options));
                if (TryWriteLibrarySingletonCount(inspection, options))
                    return 0;
                if (options.Print || options.PrintAll)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                WarnEmptySections(inspection, options, pipeline);
                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                ExtractResourcesIfRequested(assemblyPath!, options, logger);
                return IntegrityExitCode(inspection);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            // Cleanup temp directory if we extracted from a package
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static int IntegrityExitCode(params LibraryInspection[] inspections)
    {
        return inspections.Any(insp => insp.SourceIntegrityMismatches is { Count: > 0 }) ? 1 : 0;
    }

    private static async Task<int> WriteILCoordinateBatchAsync(
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!File.Exists(options.ILOffsetsPath))
        {
            Console.Error.WriteLine($"Error: IL offsets file not found: {options.ILOffsetsPath}");
            return 1;
        }

        if (!TryReadILCoordinates(options.ILOffsetsPath!, out var coordinates, out var readErrors, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        HashSet<string> sections = options.IncludeSections is { Count: > 0 }
            ? [.. options.IncludeSections]
            : [.. BatchCoordinateSections];

        var rows = readErrors
            .Select(errorRow => new ILCoordinateBatchRow(null, errorRow.Label, null, null, "error", errorRow.Error))
            .ToList();
        using var service = SourceLinkService.Open(assemblyPath, logger.Log);
        foreach (var coordinate in coordinates)
        {
            var queryOptions = options with
            {
                ILOffsetParameter = coordinate.Coordinate,
                IncludeSections = sections,
                Select = [.. sections],
                Discover = null,
                Print = false,
                PrintAll = false,
                Count = false,
                Value = false,
                Urls = false,
                Paths = false
            };
            var resolved = await ILOffsetSourceQuery.ResolveBatchAsync(
                service,
                packageName,
                packageVersion,
                isPlatformAssembly,
                queryOptions,
                httpClient,
                logger);
            rows.Add(resolved.Result is { } result
                ? BuildILCoordinateBatchRow(coordinate, result)
                : new ILCoordinateBatchRow(coordinate.Coordinate, coordinate.Label, null, null, "error", resolved.Error ?? "could not resolve"));
        }

        WriteILCoordinateBatchRows(rows, options);
        return rows.Any(row => row.Meaning == "error") ? 1 : 0;
    }

    private static readonly string[] BatchCoordinateSections =
    [
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.ExceptionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static bool TryReadILCoordinates(string path, out List<ILCoordinateInput> coordinates, out List<ILCoordinateReadError> readErrors, out string? error)
    {
        coordinates = [];
        readErrors = [];
        error = null;
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var coordinateIndex = Array.FindIndex(tokens, token => ILOffsetSourceQuery.TryParse(token, out _, out _));
            if (coordinateIndex < 0)
            {
                readErrors.Add(new ILCoordinateReadError($"{path}:{lineNumber}", "expected a MethodDef token + IL offset coordinate"));
                continue;
            }

            var labelTokens = tokens
                .Where((_, index) => index != coordinateIndex)
                .ToArray();
            coordinates.Add(new ILCoordinateInput(
                tokens[coordinateIndex],
                labelTokens.Length == 0 ? null : string.Join(' ', labelTokens)));
        }

        if (coordinates.Count == 0 && readErrors.Count == 0)
        {
            error = $"Error: {path} did not contain any IL coordinates.";
            return false;
        }

        return true;
    }

    private static ILCoordinateBatchRow BuildILCoordinateBatchRow(ILCoordinateInput input, ILOffsetResult result)
    {
        var (meaning, evidence) = ExplainILCoordinate(result);
        return new ILCoordinateBatchRow(
            input.Coordinate,
            input.Label,
            result.Method,
            FormatBatchOffset(result),
            meaning,
            evidence);
    }

    private static (string Meaning, string Evidence) ExplainILCoordinate(ILOffsetResult result)
    {
        if (result.ReturnAddressContext is { } returnAddress)
            return ("return address", $"call at {returnAddress.CallOffset} to {returnAddress.Callee}");
        if (result.AllocationContext is { Count: > 0 } allocations)
        {
            var allocation = allocations[0];
            return ("allocation", $"{allocation.AllocationKind} {allocation.AllocatedType}".Trim());
        }
        if (result.SafetyContext is { Count: > 0 } safetyFacts)
        {
            var safety = safetyFacts[0];
            return ("safety", $"{safety.SafetyKind} {safety.Operation}".Trim());
        }
        if (result.CostContext is { Count: > 0 } costFacts)
        {
            var cost = costFacts[0];
            return ("cost", $"{cost.CostKind} {cost.Operation}".Trim());
        }
        if (result.CallsiteContext is { } callsite)
            return ("callsite", $"{callsite.Opcode} {callsite.Callee}");
        if (result.ExceptionContext is { Count: > 0 } exceptions)
            return ("exception", string.Join(", ", exceptions.Select(e => $"{e.Context} {e.Clause}".Trim())));
        if (result.InstructionContext is { } instruction)
            return ("instruction", $"{instruction.Opcode} {instruction.Operand}".Trim());
        return ("member", result.MemberContext?.Signature ?? result.Method ?? "");
    }

    private static string? FormatBatchOffset(ILOffsetResult result)
        => result.InstructionContext?.ILOffset is { } offset
            ? FormatHexOffset(offset)
            : result.MemberContext?.ILOffset is { } memberOffset
                ? FormatHexOffset(memberOffset)
                : result.ILOffset;

    private static string FormatHexOffset(string value)
        => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset)
            ? $"IL_{offset:X4}"
            : value;

    private static void WriteILCoordinateBatchRows(List<ILCoordinateBatchRow> rows, LibraryOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new ILCoordinateBatchResult(rows), ILCoordinateBatchJsonContext.Default.ILCoordinateBatchResult));
            return;
        }

        if (!options.OneLine && !options.Tsv && !options.Jsonl && !options.NoHeader)
        {
            Console.WriteLine("## IL Coordinates");
            Console.WriteLine();
        }

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Coordinate", "Label", "Member", "IL Offset", "Meaning", "Evidence"],
                ["coordinate", "label", "member", "il_offset", "meaning", "evidence"],
                rows.Select(row => new[]
                {
                    row.Coordinate ?? "",
                    row.Label ?? "",
                    row.Member ?? "",
                    row.ILOffset ?? "",
                    row.Meaning,
                    row.Evidence
                }).ToArray());
            markoutWriter.Flush();
        }, options.Rows);
    }

    private static (LibraryOptions Options, string? Error) NormalizeILOffsetSelection(LibraryOptions options)
    {
        var select = options.Select?.ToList() ?? [];
        string? ilOffset = options.ILOffsetParameter;
        bool hasExplicitSelect = select.Count > 0;

        for (var i = 0; i < select.Count; i++)
        {
            var value = select[i].Trim();
            if (value.StartsWith("IL Offset:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Source Location:", StringComparison.OrdinalIgnoreCase))
                return (options, "Error: IL offset parameters belong in --il-offset, not in -S. Use --il-offset 0x06000001+0x5 -S \"Source Location\".");
        }

        if (!string.IsNullOrWhiteSpace(ilOffset)
            && options.Discover == null
            && !hasExplicitSelect)
        {
            select.Add(SectionNames.ILOffset);
            select.Add(SectionNames.MemberContext);
            select.Add(SectionNames.InstructionContext);
            select.Add(SectionNames.ExceptionContext);
            select.Add(SectionNames.CallsiteContext);
            select.Add(SectionNames.ReturnAddressContext);
        }

        return (options with
        {
            ILOffsetParameter = ilOffset,
            Select = select.Count == 0 ? null : [.. select]
        }, null);
    }

    private static readonly string[] ILCoordinateSections =
    [
        SectionNames.ILOffset,
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.ExceptionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static readonly string[] ILCoordinateSingletonSections =
    [
        SectionNames.ILOffset,
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static bool HasILOffsetCoordinate(LibraryOptions options)
        => !string.IsNullOrWhiteSpace(options.ILOffsetParameter);

    private static bool HasExactILCoordinateSelection(string[]? select)
    {
        if (select is not { Length: > 0 })
            return false;

        foreach (var value in select)
        {
            if (value.StartsWith('@'))
                continue;
            if (ILCoordinateSections.Contains(value, StringComparer.OrdinalIgnoreCase)
                || value.Equals("IL Offset", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<int> PopulateILOffsetIfRequestedAsync(
        LibraryInspection inspection,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            || (options.Discover == null && options.IncludeSections?.Overlaps(ILCoordinateSections) != true))
            return 0;

        var resolved = await ILOffsetSourceQuery.ResolveAsync(
            assemblyPath, packageName, packageVersion, isPlatformAssembly, options, httpClient, logger);
        if (resolved.ExitCode != 0)
            return resolved.ExitCode;

        inspection.ILOffset = resolved.Result;
        return 0;
    }

    private static bool ValidateLibraryPrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 } && sections.Contains(SectionNames.ILOffset))
            return true;

        Console.Error.WriteLine("Error: --print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static bool TryWriteLibrarySingletonCount(LibraryInspection inspection, LibraryOptions options)
    {
        if (!options.Count
            || options.IncludeSections is not { Count: 1 } sections
            || !sections.Overlaps(ILCoordinateSingletonSections))
        {
            return false;
        }

        var section = sections.Single();
        var hasRow = section switch
        {
            SectionNames.ILOffset => inspection.ILOffset != null,
            SectionNames.MemberContext => inspection.ILOffset?.MemberContext != null,
            SectionNames.InstructionContext => inspection.ILOffset?.InstructionContext != null,
            SectionNames.CallsiteContext => inspection.ILOffset?.CallsiteContext != null,
            SectionNames.ReturnAddressContext => inspection.ILOffset?.ReturnAddressContext != null,
            SectionNames.AllocationContext => inspection.ILOffset?.AllocationContext is { Count: > 0 },
            SectionNames.SafetyContext => inspection.ILOffset?.SafetyContext is { Count: > 0 },
            SectionNames.CostContext => inspection.ILOffset?.CostContext is { Count: > 0 },
            _ => false
        };
        Console.WriteLine(hasRow ? 1 : 0);
        return true;
    }

    private static int WriteLibraryShapeProjection(LibraryInspection inspection, LibraryOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            "Source Files" => ProjectLibrarySourceFiles(inspection, section, kind, options),
            "Library Info" => ProjectLibraryInfo(inspection, section, kind, options),
            SectionNames.ILOffset => ProjectLibraryILOffset(inspection, section, kind, options),
            SectionNames.MemberContext => ProjectLibraryMemberContext(inspection, section, kind, options),
            SectionNames.InstructionContext => ProjectLibraryInstructionContext(inspection, section, kind, options),
            SectionNames.ExceptionContext => ProjectLibraryExceptionContext(inspection, section, kind, options),
            SectionNames.CallsiteContext => ProjectLibraryCallsiteContext(inspection, section, kind, options),
            SectionNames.ReturnAddressContext => ProjectLibraryReturnAddressContext(inspection, section, kind, options),
            _ => []
        };

        if (rows.Count == 0 && section is SectionNames.MemberContext or SectionNames.InstructionContext or SectionNames.ExceptionContext
            or SectionNames.CallsiteContext or SectionNames.ReturnAddressContext)
        {
            if (kind != ShapeProjectionKind.Value)
                Console.Error.WriteLine($"Error: section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        if (rows.Count == 0 && section is not ("Source Files" or "Library Info") && section != SectionNames.ILOffset)
        {
            Console.Error.WriteLine($"Error: section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }
        if (rows.Count == 0 && section == "Library Info" && kind == ShapeProjectionKind.Value)
            return 1;

        return ShapeProjectionOutput.Write(
            rows,
            new ShapeProjectionOptions(kind, options.ProjectionRow, options.JsonOutput, options.Jsonl, options.JsonArray));
    }

    private static async Task<int> WriteLibraryPrintProjectionAsync(LibraryInspection inspection, LibraryOptions options)
    {
        var section = options.IncludeSections!.Single();
        var projection = section switch
        {
            SectionNames.ILOffset => await ProjectLibraryILOffsetPrintableAsync(inspection, section),
            _ => new PrintProjectionResult([])
        };
        if (projection.Error is not null)
        {
            Console.Error.WriteLine(projection.Error);
            return 1;
        }

        if (projection.Documents.Count == 0 && section != SectionNames.ILOffset)
        {
            Console.Error.WriteLine($"Error: section '{section}' is not printable.");
            return 1;
        }

        return PrintProjectionOutput.Write(
            projection.Documents,
            new PrintProjectionOptions(
                options.PrintAll,
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                Bare: false,
                OutputPath: null));
    }

    private sealed record PrintProjectionResult(IReadOnlyList<PrintableDocument> Documents, string? Error = null);

    private static async Task<PrintProjectionResult> ProjectLibraryILOffsetPrintableAsync(
        LibraryInspection inspection,
        string section)
    {
        if (inspection.ILOffset is not { } result)
            return new PrintProjectionResult([]);

        var (content, error) = await ReadILOffsetSourceLineAsync(result);
        if (error is not null)
            return new PrintProjectionResult([], error);
        if (content is null)
            return new PrintProjectionResult([]);

        return new PrintProjectionResult(
        [
            new PrintableDocument(1, section, result.Method ?? result.File ?? section, result.File, result.Url, content)
        ]);
    }

    internal static Task<(string? Content, string? Error)> ReadILOffsetSourceLineForTestsAsync(ILOffsetResult result)
        => ReadILOffsetSourceLineAsync(result);

    private static async Task<(string? Content, string? Error)> ReadILOffsetSourceLineAsync(ILOffsetResult result)
    {
        if (result.Line is not { } line || line < 1)
        {
            return (null, "Error: Source Location row has no source line to print.");
        }

        if (string.IsNullOrWhiteSpace(result.Url))
        {
            return (null, "Error: Source Location row has no printable source body. Use --urls or --paths to inspect available payloads.");
        }

        var rawUrl = StripUrlFragment(GitHubUrlResolver.ConvertBlobToRawUrl(result.Url));
        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var source = await fetcher.FetchSourceAsync(rawUrl);
        if (source is null)
        {
            return (null, $"Error: Could not fetch SourceLink source for {rawUrl}.");
        }

        return ReadLine(source.ReplaceLineEndings("\n").Split('\n'), line);
    }

    private static (string? Content, string? Error) ReadLine(IEnumerable<string> lines, int line)
    {
        var value = lines.Skip(line - 1).FirstOrDefault();
        if (value is null)
        {
            return (null, $"Error: Source line {line} is out of range.");
        }

        return (value, null);
    }

    private static string StripUrlFragment(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Fragment))
            return url;

        var builder = new UriBuilder(uri) { Fragment = "" };
        return builder.Uri.ToString();
    }

    private static List<ShapeProjectionRow> ProjectLibrarySourceFiles(LibraryInspection inspection, string section, ShapeProjectionKind kind, LibraryOptions options)
    {
        var rows = new LibraryInspectionView(inspection).SourceFilesSection ?? [];
        return rows
            .Select((row, index) =>
            {
                string? value = kind switch
                {
                    ShapeProjectionKind.Urls => row.Url,
                    ShapeProjectionKind.Value => SelectLibrarySourceValue(row, options),
                    _ => null
                };
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new ShapeProjectionRow(index + 1, section, value, Label: row.Type, Url: row.Url);
            })
            .Where(row => row is not null)
            .Cast<ShapeProjectionRow>()
            .ToList();
    }

    private static string? SelectLibrarySourceValue(SourceFileRow row, LibraryOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "type" => row.Type,
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectLibraryILOffset(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (inspection.ILOffset is not { } result)
            return [];

        var value = kind switch
        {
            ShapeProjectionKind.Urls => result.Url,
            ShapeProjectionKind.Paths => result.File,
            ShapeProjectionKind.Value => SelectLibraryILOffsetValue(result, options),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value)
            ? []
            : [new ShapeProjectionRow(1, section, value, Label: result.Method, Url: result.Url, Path: result.File)];
    }

    private static string? SelectLibraryILOffsetValue(ILOffsetResult result, LibraryOptions options)
    {
        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        return field?.ToLowerInvariant() switch
        {
            "method" => result.Method,
            "token" => result.Token,
            "il offset" or "iloffset" => result.ILOffset,
            "matched offset" or "matchedoffset" => result.MatchedOffset,
            "file" or "path" => result.File,
            "line" => result.Line?.ToString(CultureInfo.InvariantCulture),
            "url" or "source" => result.Url,
            _ => result.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectLibraryMemberContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.MemberContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Member Context requires --fields <name>.");
            return [];
        }

        var value = SelectMemberContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in Member Context.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectMemberContextValue(ILOffsetMemberContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "assembly" => context.Assembly,
            "type" => context.Type,
            "type kind" or "typekind" => context.TypeKind,
            "member" => context.Member,
            "signature" => context.Signature,
            "member kind" or "memberkind" => context.MemberKind,
            "visibility" => context.Visibility,
            "static" => context.Static,
            "async" => context.Async,
            "metadata token" or "metadatatoken" or "token" => context.MetadataToken,
            "il offset" or "iloffset" => context.ILOffset,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryInstructionContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.InstructionContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Instruction Context requires --fields <name>.");
            return [];
        }

        var value = SelectInstructionContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in Instruction Context.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectInstructionContextValue(ILOffsetInstructionContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "il offset" or "iloffset" => context.ILOffset,
            "boundary" => context.Boundary,
            "opcode" => context.Opcode,
            "operand kind" or "operandkind" => context.OperandKind,
            "operand" => context.Operand,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            "branch targets" or "branchtargets" => context.BranchTargets,
            "next offset" or "nextoffset" => context.NextOffset,
            "length" => context.Length?.ToString(CultureInfo.InvariantCulture),
            "block" => context.Block?.ToString(CultureInfo.InvariantCulture),
            "terminates block" or "terminatesblock" => context.TerminatesBlock,
            "falls through" or "fallsthrough" => context.FallsThrough,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryExceptionContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.ExceptionContext is not { Count: > 0 } rows)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Exception Context requires --fields <name>.");
            return [];
        }

        List<ShapeProjectionRow> projected = [];
        for (var i = 0; i < rows.Count; i++)
        {
            var value = SelectExceptionContextValue(rows[i], field);
            if (!string.IsNullOrWhiteSpace(value))
                projected.Add(new ShapeProjectionRow(i + 1, section, value, Label: field));
        }

        if (projected.Count == 0)
            Console.Error.WriteLine($"Error: field '{field}' has no value in Exception Context.");

        return projected;
    }

    private static string? SelectExceptionContextValue(ILOffsetExceptionContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "region" => context.Region.ToString(CultureInfo.InvariantCulture),
            "context" => context.Context,
            "clause" => context.Clause,
            "try range" or "tryrange" => context.TryRange,
            "handler range" or "handlerrange" => context.HandlerRange,
            "filter range" or "filterrange" => context.FilterRange,
            "caught type" or "caughttype" => context.CaughtType,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryCallsiteContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.CallsiteContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Callsite Context requires --fields <name>.");
            return [];
        }

        var value = SelectCallsiteContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in Callsite Context.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectCallsiteContextValue(ILOffsetCallsiteContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "call offset" or "calloffset" or "offset" => context.CallOffset,
            "opcode" => context.Opcode,
            "call kind" or "callkind" => context.CallKind,
            "callee" => context.Callee,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            "return address" or "returnaddress" => context.ReturnAddress,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryReturnAddressContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.ReturnAddressContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Return Address Context requires --fields <name>.");
            return [];
        }

        var value = SelectReturnAddressContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in Return Address Context.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectReturnAddressContextValue(ILOffsetReturnAddressContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "il offset" or "iloffset" or "offset" => context.ILOffset,
            "call offset" or "calloffset" => context.CallOffset,
            "opcode" => context.Opcode,
            "call kind" or "callkind" => context.CallKind,
            "callee" => context.Callee,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryInfo(LibraryInspection inspection, string section, ShapeProjectionKind kind, LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value)
            return [];
        var info = new LibraryInspectionView(inspection).AssemblyInfoSection;
        if (info is null)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            Console.Error.WriteLine("Error: --value for Library Info requires --fields <name>.");
            return [];
        }

        var values = GetLibraryInfoValues(info);
        if (!values.TryGetValue(field, out var value))
        {
            Console.Error.WriteLine($"Error: field '{field}' was not found in Library Info.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Error: field '{field}' has no value in Library Info.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static Dictionary<string, string?> GetLibraryInfoValues(LibraryInfoSection info)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(LibraryInfoSection).GetProperties())
        {
            var name = ToPascalCaseWords(property.Name);
            values[name] = FormatLibraryInfoValue(property.GetValue(info));
        }

        return values;
    }

    private static string? FormatLibraryInfoValue(object? value)
        => value switch
        {
            null => null,
            bool b => b ? "Yes" : "No",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static string ToPascalCaseWords(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length + 8);
        builder.Append(value[0]);
        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            var previous = value[i - 1];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
                builder.Append(' ');
            builder.Append(current);
        }

        return builder.ToString();
    }

    private static int WriteEffectiveSections(string assemblyPath, LibraryInspection inspection,
        LibraryOptions options, SectionPipeline<LibraryInspection> pipeline, Verbosity userVerbosity = Verbosity.Minimal,
        bool cache = true)
    {
        // Compute all structurally applicable sections for discovery/caching,
        // including opt-in sections whose renderability depends on the section's
        // own work (for example SourceLink audit sections).
        var allEffective = pipeline.GetDiscoverableSections(inspection);
        var schemaMap = InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema();

        // Field-level filtering on ALL effective sections (unfiltered) for caching
        var filteredSchema = FilterSchemaToEffectiveFields(inspection, allEffective, schemaMap, pipeline, allEffective.ToArray());
        if (cache)
            CacheEffective(assemblyPath, allEffective, filteredSchema);

        // Apply user filters
        var effective = FilterEffective(allEffective, options);

        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath);
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, filteredSchema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
            verbosity: (int)userVerbosity, rootLabel: rootLabel, fullSchema: schemaMap,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap());
    }

    // ── Effective sections cache ──

    private const string EffectiveCategory = "effective-v10";

    static LibraryCommand()
    {
        CoreCache.RegisterVersionedCategory("effective-v", EffectiveCategory);
    }

    private static (List<string> Sections, DocumentSchema Schema)? TryGetCachedEffective(string assemblyPath)
    {
        string key = GetEffectiveCacheKey(assemblyPath);
        var cached = CoreCache.TryGet(EffectiveCategory, key, extension: "tsv");
        if (cached == null) return null;

        var sections = new List<string>();
        var schema = new DocumentSchema();
        foreach (var line in cached.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            var name = parts[0];
            sections.Add(name);
            if (parts.Length >= 3)
                schema.Add(name, parts[1], parts[2].Split(','));
            else
                schema.AddSection(name);
        }
        return (sections, schema);
    }

    private static void CacheEffective(string assemblyPath, List<string> sections, DocumentSchema filteredSchema)
    {
        string key = GetEffectiveCacheKey(assemblyPath);
        var sb = new System.Text.StringBuilder();
        foreach (var name in sections)
        {
            var section = filteredSchema.GetSection(name);
            if (section != null && section.Items.Length > 0)
                sb.AppendLine($"{name}\t{section.ItemKind}\t{string.Join(',', section.Items.Select(i => i.Name))}");
            else
                sb.AppendLine(name);
        }
        CoreCache.Set(EffectiveCategory, key, sb.ToString(), extension: "tsv");
    }

    private static string GetEffectiveCacheKey(string assemblyPath)
    {
        // Include file size for invalidation when local files change
        var size = new FileInfo(assemblyPath).Length;
        return $"{assemblyPath}#{size}";
    }

    private static List<string> FilterEffective(List<string> sections, LibraryOptions options)
    {
        if (options.IncludeSections is { Count: > 0 })
            sections = sections.Where(s => options.IncludeSections.Contains(s)).ToList();
        if (!HasILOffsetCoordinate(options))
            sections = sections.Where(s => !ILCoordinateSections.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        return sections;
    }

    private static int RenderEffective(List<string> effective, DocumentSchema schema, LibraryOptions options,
        Verbosity userVerbosity = Verbosity.Minimal, string? rootLabel = null)
    {
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
            verbosity: (int)userVerbosity, rootLabel: rootLabel,
            sectionCostAnnotations: LibrarySections.CreatePipeline().GetCostAnnotations(),
            sectionCategories: LibrarySections.CreatePipeline().GetCategoryMap());
    }

    /// <summary>
    /// Renders the targeted sections and filters the schema to only fields that produced output.
    /// </summary>
    private static DocumentSchema FilterSchemaToEffectiveFields(LibraryInspection inspection,
        List<string> effectiveSections, DocumentSchema schema, SectionPipeline<LibraryInspection> pipeline,
        string[] discover)
    {
        // Resolve which sections are being discovered
        var targetSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in discover)
        {
            var resolved = schema.ResolveSection(d);
            if (resolved != null && effectiveSections.Contains(resolved))
                targetSections.Add(resolved);
        }
        if (targetSections.Count == 0) return schema;

        var view = new LibraryInspectionView(inspection);
        var writerOpts = new MarkoutWriterOptions { IncludeSections = targetSections };
        var rendered = MarkoutSerializer.Serialize(view, InspectionContext.Default, writerOpts);

        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section == null) { filtered.AddSection(name); continue; }

            // Only filter fields for discovered sections; keep others intact
            if (!targetSections.Contains(name))
            {
                filtered.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
                continue;
            }

            if (string.Equals(name, "Library Info", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SectionNames.MemberContext, StringComparison.OrdinalIgnoreCase))
            {
                var renderedLabels = GetRenderedFieldLabels(rendered, name);
                var effectiveItems = section.Items
                    .Where(item => renderedLabels.Contains(item.Name))
                    .Select(item => item.Name)
                    .ToArray();
                filtered.Add(name, section.ItemKind,
                    effectiveItems.Length > 0 ? effectiveItems : section.Items.Select(i => i.Name).ToArray());
            }
            else if (section.Items.Length > 0)
                filtered.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
            else
                filtered.AddSection(name);
        }
        return filtered;
    }

    internal static HashSet<string> GetRenderedFieldLabelsForTests(string rendered, string sectionName)
        => GetRenderedFieldLabels(rendered, sectionName);

    private static HashSet<string> GetRenderedFieldLabels(string rendered, string sectionName)
    {
        HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ExtractSection(rendered, sectionName))
        {
            if (!line.StartsWith('|'))
                continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 3
                || cells[1].Equals("Field", StringComparison.OrdinalIgnoreCase)
                || IsMarkdownSeparatorCell(cells[1]))
                continue;

            labels.Add(cells[1]);
        }

        return labels;
    }

    private static IEnumerable<string> ExtractSection(string rendered, string sectionName)
    {
        var inSection = false;
        var heading = "## " + sectionName;
        foreach (var line in rendered.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line.Equals(heading, StringComparison.Ordinal)
                    || line.StartsWith(heading + " (", StringComparison.Ordinal);
                continue;
            }

            if (inSection)
                yield return line;
        }
    }

    private static bool IsMarkdownSeparatorCell(string cell)
    {
        var trimmed = cell.Trim();
        if (trimmed.Length == 0)
            return false;

        return trimmed.All(ch => ch is '-' or ':');
    }

    private static void WarnEmptySections(LibraryInspection inspection, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var (empty, requested) = pipeline.GetEmptySections(inspection, options.Verbosity, options.IncludeSections);
        if (empty.Count > 0 && empty.Count == requested)
        {
            var label = empty.Count == 1 ? "section has" : "sections have";
            Console.Error.WriteLine($"Note: {empty.Count} matched {label} no data: {string.Join(", ", empty)}.");
        }
    }

    private static void ExtractResourcesIfRequested(string assemblyPath, LibraryOptions options, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(options.ExtractResources))
            return;

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            var extracted = ResourceScanner.ExtractAll(stream, options.ExtractResources);
            if (extracted.Count == 0)
            {
                Console.Error.WriteLine("No embedded resources found.");
            }
            else
            {
                Console.Error.WriteLine($"Extracted {extracted.Count} resource(s) to {options.ExtractResources}");
                foreach (var path in extracted)
                {
                    Console.Error.WriteLine($"  {Path.GetFileName(path)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error extracting resources: {ex.Message}");
        }
    }

    private static async Task<List<LibraryInspection>> CollectPackageInspectionsAsync(
        List<string> assemblyPaths, LibraryOptions options, VerboseLogger logger,
        string? packageName, string? packageVersion, string extractPath,
        HttpClient httpClient, SignatureVerificationResult? signatureResult,
        HashSet<string>? scanners = null, ScannerRegistry? scannerRegistry = null)
    {
        List<LibraryInspection> inspections = [];

        foreach (var targetPath in assemblyPaths)
        {
            var version = packageVersion ?? (packageName != null ? PackageExtractor.ExtractVersionFromPath(targetPath, packageName) : null);

            var inspection = await LibraryMetadataService.InspectAsync(targetPath, options, logger, packageName, version, httpClient, scanners: scanners, scannerRegistry: scannerRegistry);
            if (inspection == null)
            {
                logger.Log($"Warning: Could not read library: {Path.GetFileName(targetPath)}");
                continue;
            }

            // Populate TFM from path for multi-TFM display
            var relativePath = Path.GetRelativePath(extractPath, targetPath).Replace('\\', '/');
            inspection.Tfm = TfmResolver.ExtractTfmFromPath(relativePath);

            if (signatureResult != null)
            {
                inspection.Publisher = signatureResult.Publisher;
                inspection.PublisherVerified = signatureResult.AuthorVerified;
                inspection.RepositoryVerified = signatureResult.RepositoryVerified;
                inspection.SignatureStatus = signatureResult.StatusMessage;
            }

            inspections.Add(inspection);
        }

        return inspections;
    }

    private static async Task<(List<string> assemblyPaths, string extractPath, string? tempDir, string? nupkgPath, string? packageName, string? packageVersion)?> ExtractFromPackageAsync(
        string? assemblyName,
        string packageSource,
        string? tfm,
        NuGetSourceOptions? sourceOptions,
        bool includePrerelease,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            httpClient, packageSource, logger.Log, sourceOptions: sourceOptions, includePrerelease: includePrerelease);
        if (!outcome.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
            return null;
        }
        var resolution = outcome.Result!;

        string extractPath = resolution.ExtractPath;
        string? tempDir = resolution.TempDir;
        string? nupkgPath = resolution.NupkgPath;
        string? resolvedPackageName = resolution.PackageName;
        string? resolvedPackageVersion = resolution.Version;

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        if (allDlls.Length == 0)
        {
            var payload = await TryResolveToolPayloadPackageAsync(
                resolution, packageSource, sourceOptions, logger, httpClient).ConfigureAwait(false);

            if (payload.Error != null)
            {
                Console.Error.WriteLine(payload.Error);
                DeleteTempDir(tempDir);
                return null;
            }

            if (payload.Result != null)
            {
                DeleteTempDir(tempDir);
                resolution = payload.Result;
                extractPath = resolution.ExtractPath;
                tempDir = resolution.TempDir;
                nupkgPath = resolution.NupkgPath;
                resolvedPackageName = resolution.PackageName;
                resolvedPackageVersion = resolution.Version;
                allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
            }
        }

        // --tfm all: return all assemblies from every TFM
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = TfmSelector.GetPackageDlls(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                DeleteTempDir(tempDir);
                return null;
            }
            return (candidates, extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        // --tfm <specific>: find assembly by TFM
        if (!string.IsNullOrEmpty(tfm))
        {
            var tfmAssembly = TfmSelector.FindAssemblyByTfm(extractPath, tfm, resolution.PackageName);
            if (tfmAssembly == null)
            {
                Console.Error.WriteLine($"Error: No library found for TFM '{tfm}'.");
                Console.Error.WriteLine("Available TFMs:");
                var tfms = allDlls
                    .Select(d => TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, d).Replace('\\', '/')))
                    .Where(t => t != null)
                    .Distinct()
                    .OrderByDescending(t => TfmResolver.GetTfmPriority(t!));
                foreach (var t in tfms)
                {
                    Console.Error.WriteLine($"  {t}");
                }
                DeleteTempDir(tempDir);
                return null;
            }
            logger.Log($"Using TFM: {tfm}");
            return ([tfmAssembly], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        // No --tfm and no assembly name: select the highest-priority TFM (default)
        if (string.IsNullOrEmpty(assemblyName))
        {
            var candidates = TfmSelector.GetPackageDlls(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                DeleteTempDir(tempDir);
                return null;
            }

            var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(candidates, extractPath, resolution.PackageName);
            if (selectedPath == null)
            {
                // No TFM structure found, fall back to first DLL
                return ([candidates[0]], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
            }

            logger.Log($"Using TFM: {selectedTfm}");
            return ([selectedPath], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        var (matchedAssembly, matchedTfm) = TfmSelector.FindAssemblyInPackage(extractPath, assemblyName, tfm);
        if (matchedAssembly == null)
        {
            Console.Error.WriteLine($"Error: Library '{assemblyName}' not found in package.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --path \"lib/\"' to list available libraries.");
            DeleteTempDir(tempDir);
            return null;
        }

        if (matchedTfm != null)
            logger.Log($"Using TFM: {matchedTfm}");

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchedAssembly)}");
        return ([matchedAssembly], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
    }

    private sealed record ToolPayloadResolution(PackageExtractionResult? Result, string? Error);

    private static async Task<ToolPayloadResolution> TryResolveToolPayloadPackageAsync(
        PackageExtractionResult package,
        string originalPackageSource,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var payloadId = GetToolPayloadPackageId(package.ExtractPath, package.PackageName);
        if (payloadId == null)
            return new(null, null);

        var version = package.Version ?? GetNuspecVersion(package.ExtractPath);
        if (version == null)
            return new(null, $"Error: Tool package '{package.PackageName}' has no DLLs and its version could not be determined.");

        var localPayload = TryFindLocalSiblingPackage(originalPackageSource, payloadId, version);
        var payloadOutcome = localPayload != null
            ? await PackageExtractor.ExtractPackageAsync(httpClient, localPayload, logger.Log).ConfigureAwait(false)
            : await PackageExtractor.ExtractPackageAsync(
                httpClient, payloadId, logger.Log, sourceOptions: sourceOptions, version: version).ConfigureAwait(false);

        if (!payloadOutcome.IsSuccess)
            return new(null, $"Error: Tool package '{package.PackageName}' has no inspectable DLLs and payload package '{payloadId}@{version}' could not be resolved: {payloadOutcome.ErrorMessage}");

        var payload = payloadOutcome.Result!;
        var dlls = Directory.GetFiles(payload.ExtractPath, "*.dll", SearchOption.AllDirectories)
            .Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dlls.Count == 0)
        {
            DeleteTempDir(payload.TempDir);
            return new(null, $"Error: Tool payload package '{payload.PackageName}@{payload.Version}' does not contain inspectable .NET DLLs.");
        }

        logger.Log($"Tool package has no DLLs; inspecting payload package: {payload.PackageName} {payload.Version}");
        return new(payload, null);
    }

    private static string? GetToolPayloadPackageId(string extractPath, string? packageName)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        if (Directory.Exists(toolsDir))
        {
            var settings = ToolsAnalyzer.ReadToolSettings(toolsDir);
            var anyPayload = settings?.RuntimeIdentifierPackages?
                .FirstOrDefault(r => r.RuntimeIdentifier.Equals("any", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(anyPayload?.PackageId))
                return anyPayload.PackageId;
        }

        return TryGetSiblingAnyPackageId(packageName);
    }

    private static string? TryGetSiblingAnyPackageId(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        string[] knownRidSuffixes =
        [
            ".win-x64",
            ".win-arm64",
            ".linux-x64",
            ".linux-arm64",
            ".osx-arm64"
        ];

        var suffix = knownRidSuffixes.FirstOrDefault(s =>
            packageName.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        return suffix == null ? null : packageName[..^suffix.Length] + ".any";
    }

    private static string? TryFindLocalSiblingPackage(string originalPackageSource, string payloadId, string version)
    {
        if (!originalPackageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(originalPackageSource));
        if (directory == null)
            return null;

        var exact = Path.Combine(directory, $"{payloadId}.{version}.nupkg");
        if (File.Exists(exact))
            return exact;

        return Directory.GetFiles(directory, $"{payloadId}.*.nupkg")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? GetNuspecVersion(string extractPath)
    {
        var nuspec = Directory.GetFiles(extractPath, "*.nuspec", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        return nuspec == null ? null : NuspecParser.Parse(nuspec).Version;
    }

    private static void DeleteTempDir(string? tempDir)
    {
        if (tempDir == null)
            return;

        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

}

internal sealed record ILCoordinateInput(string Coordinate, string? Label);

internal sealed record ILCoordinateReadError(string Label, string Error);

internal sealed record ILCoordinateBatchResult(List<ILCoordinateBatchRow> Rows);

[MarkoutSerializable]
internal sealed record ILCoordinateBatchRow(
    [property: MarkoutSkipNull] string? Coordinate,
    [property: MarkoutSkipNull] string? Label,
    [property: MarkoutSkipNull] string? Member,
    [property: MarkoutPropertyName("IL Offset")]
    [property: MarkoutSkipNull] string? ILOffset,
    string Meaning,
    string Evidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ILCoordinateBatchResult))]
internal partial class ILCoordinateBatchJsonContext : JsonSerializerContext
{
}
