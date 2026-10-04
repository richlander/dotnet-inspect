#:project ../src/ILInspector.Research/ILInspector.Research.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

const string Usage =
    "Usage: dotnet run eng/census-library-type-leverage.cs -- "
    + "<package-sweep-manifest.json> <report.json> <report.md>"
    + "\n   or: dotnet run eng/census-library-type-leverage.cs -- "
    + "--self-test";

if (args is ["--self-test"])
{
    SelfTest();
    Console.WriteLine("Library Type-leverage census self-test passed.");
    return 0;
}

if (args.Length != 3)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string manifestPath = Path.GetFullPath(args[0]);
string jsonPath = Path.GetFullPath(args[1]);
string markdownPath = Path.GetFullPath(args[2]);
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine(
        $"Package-sweep manifest not found: {manifestPath}");
    return 2;
}

PackageSweepManifest? manifest;
try
{
    manifest = JsonSerializer.Deserialize<PackageSweepManifest>(
        File.ReadAllText(manifestPath),
        CensusJsonContext.Default.PackageSweepManifest);
}
catch (Exception exception)
    when (exception is JsonException or IOException)
{
    Console.Error.WriteLine(
        $"Could not read package-sweep manifest '{manifestPath}': "
        + exception.Message);
    return 2;
}

if (manifest is null
    || manifest.SchemaVersion != 1
    || manifest.Unreconciled is not null)
{
    Console.Error.WriteLine(
        "The package-sweep manifest is not a reconciled schema-version-one "
        + "manifest.");
    return 2;
}

PackageSweepEntry[] selected =
[
    .. manifest.Packages
        .Where(static package =>
            StringComparer.Ordinal.Equals(
                package.Status,
                "selected"))
        .OrderBy(static package => package.Rank),
];
if (selected.Length != manifest.SelectedPackageCount
    || selected.Length == 0)
{
    Console.Error.WriteLine(
        "The package-sweep manifest's selected package count does not "
        + "match its selected entries.");
    return 2;
}

string manifestDirectory =
    Path.GetDirectoryName(manifestPath)
    ?? throw new InvalidOperationException(
        "The package-sweep manifest has no directory.");
var assemblies = new List<LeverageAssemblyCensus>(selected.Length);
foreach (PackageSweepEntry package in selected)
{
    if (package is not
        {
            ResolvedPackage: not null,
            ResolvedVersion: not null,
            AssemblyPath: not null,
            Sha256: not null,
        })
    {
        Console.Error.WriteLine(
            $"Selected rank {package.Rank} lacks complete provenance.");
        return 2;
    }

    string assemblyPath = Path.GetFullPath(
        package.AssemblyPath,
        manifestDirectory);
    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine(
            $"Selected assembly not found: {assemblyPath}");
        return 2;
    }

    string actualSha256 =
        Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(assemblyPath)));
    if (!StringComparer.OrdinalIgnoreCase.Equals(
            actualSha256,
            package.Sha256))
    {
        Console.Error.WriteLine(
            $"Selected assembly SHA-256 does not match the manifest: "
            + assemblyPath);
        return 2;
    }

    try
    {
        assemblies.Add(
            Measure(
                package,
                assemblyPath,
                actualSha256));
    }
    catch (Exception exception)
        when (exception is BadImageFormatException
            or InvalidOperationException
            or IOException
            or ArgumentException)
    {
        Console.Error.WriteLine(
            $"Could not measure rank {package.Rank} "
            + $"{package.ResolvedPackage}@{package.ResolvedVersion}: "
            + exception.Message);
        return 1;
    }
}

LeverageCorpusSummary summary = Summarize(assemblies);
var report = new LeverageCorpusReport(
    SchemaVersion: 1,
    MethodologyVersion:
        LibraryStructuralSalience.CurrentMethodologyVersion,
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    SourceManifest: Path.GetRelativePath(
        Directory.GetCurrentDirectory(),
        manifestPath),
    SourceGeneratedAtUtc: manifest.GeneratedAtUtc,
    SourceStartRank: manifest.StartRank,
    SourceRequestedPackageCount:
        manifest.RequestedPackageCount,
    Summary: summary,
    Assemblies: assemblies);

Directory.CreateDirectory(
    Path.GetDirectoryName(jsonPath)
    ?? Directory.GetCurrentDirectory());
Directory.CreateDirectory(
    Path.GetDirectoryName(markdownPath)
    ?? Directory.GetCurrentDirectory());
File.WriteAllText(
    jsonPath,
    JsonSerializer.Serialize(
        report,
        CensusJsonContext.Default.LeverageCorpusReport)
        + Environment.NewLine);
File.WriteAllText(
    markdownPath,
    Markdown(report));
Console.Write(Markdown(report));
return 0;

static LeverageAssemblyCensus Measure(
    PackageSweepEntry package,
    string path,
    string sha256)
{
    long signatureStarted = Stopwatch.GetTimestamp();
    using AssemblyInspectionSession session =
        AssemblyInspectionSession.Open(path);
    MetadataLibrarySignatureUseResult whole =
        AvailableSignature(
            session.LibrarySignatureUses(
                new(MetadataOperationPolicy.Unbounded)));
    LibraryStructuralNamespaceLeverageIndex index =
        LibraryStructuralReport.CreateNamespaceLeverageIndex(whole);
    MetadataLibrarySignatureUseResult[] namespaceInventories =
        index.Rows.Length == 0
            ? []
            :
            [
                .. AvailableBatch(
                    session.LibrarySignatureUseBatch(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            [.. index.Rows.Select(
                                static row => row.Namespace)])))
                    .Results,
            ];
    LibraryStructuralTypeLeverageShard[] signatureShards =
    [
        .. namespaceInventories.Select(
            LibraryStructuralReport.CreateTypeLeverageShard),
    ];
    double signatureMilliseconds =
        Stopwatch.GetElapsedTime(signatureStarted)
            .TotalMilliseconds;

    long bodyStarted = Stopwatch.GetTimestamp();
    AnalysisLibraryBodyUseResult bodyUse =
        AvailableBody(
            AnalysisLibraryBodyUseService.ExecutePath(
                path,
                new()));
    double bodyAcquireMilliseconds =
        Stopwatch.GetElapsedTime(bodyStarted)
            .TotalMilliseconds;

    long bodyProjectionStarted = Stopwatch.GetTimestamp();
    LibraryStructuralBodyTypeLeverageShard[] bodyShards =
    [
        .. namespaceInventories.Select(inventory =>
            LibraryStructuralReport.CreateBodyTypeLeverageShard(
                inventory,
                bodyUse)),
    ];
    double bodyProjectionMilliseconds =
        Stopwatch.GetElapsedTime(bodyProjectionStarted)
            .TotalMilliseconds;

    var signatureByNamespace =
        signatureShards.ToDictionary(
            static shard => shard.Namespace,
            StringComparer.Ordinal);
    var bodyByNamespace =
        bodyShards.ToDictionary(
            static shard => shard.Namespace,
            StringComparer.Ordinal);
    var rankRows = new List<LeverageRankComparison>();
    var poleRows = new List<LeveragePoleComparison>();
    int signatureEligibleRows = 0;
    int bodyEligibleRows = 0;
    int signatureGracePoles = 0;
    int bodyGracePoles = 0;

    foreach (string @namespace in signatureByNamespace.Keys
        .Order(StringComparer.Ordinal))
    {
        LibraryStructuralTypeLeverageShard signature =
            signatureByNamespace[@namespace];
        LibraryStructuralBodyTypeLeverageShard body =
            bodyByNamespace[@namespace];
        signatureEligibleRows += signature.Rows.Count(
            static row => row.DesignationEligible);
        bodyEligibleRows += body.Rows.Count(
            static row => row.DesignationEligible);
        int signatureIncomingMaximum = Maximum(
            signature.Rows,
            static row => row.DesignationEligible,
            static row => row.SignatureIncomingDegree);
        int signatureOutgoingMaximum = Maximum(
            signature.Rows,
            static row => row.DesignationEligible,
            static row => row.SignatureOutgoingDegree);
        int bodyIncomingMaximum = Maximum(
            body.Rows,
            static row => row.DesignationEligible,
            static row => row.BodyIncomingDegree);
        int bodyOutgoingMaximum = Maximum(
            body.Rows,
            static row => row.DesignationEligible,
            static row => row.BodyOutgoingDegree);
        var signatureRows = signature.Rows.ToDictionary(
            static row => row.Type);
        var bodyRows = body.Rows.ToDictionary(
            static row => row.Type);

        foreach (MetadataTypeDefinitionAddress type
            in signatureRows.Keys
                .Union(bodyRows.Keys)
                .OrderBy(static type =>
                    type.Definition.Value))
        {
            signatureRows.TryGetValue(
                type,
                out LibraryStructuralTypeLeverageRow? signatureRow);
            bodyRows.TryGetValue(
                type,
                out LibraryStructuralBodyTypeLeverageRow? bodyRow);
            if (signatureRow?.Pole is null
                && bodyRow?.Pole is null)
            {
                continue;
            }

            bool signatureGrace = IsGrace(
                signatureRow?.Pole,
                signatureRow?.SignatureIncomingDegree ?? 0,
                signatureRow?.SignatureOutgoingDegree ?? 0,
                signatureIncomingMaximum,
                signatureOutgoingMaximum);
            bool bodyGrace = IsGrace(
                bodyRow?.Pole,
                bodyRow?.BodyIncomingDegree ?? 0,
                bodyRow?.BodyOutgoingDegree ?? 0,
                bodyIncomingMaximum,
                bodyOutgoingMaximum);
            if (signatureGrace)
                signatureGracePoles++;
            if (bodyGrace)
                bodyGracePoles++;

            MetadataTypeDefinitionName name =
                bodyRow?.Name
                ?? signatureRow!.Name;
            poleRows.Add(
                new(
                    Namespace: @namespace,
                    TypeDefinitionToken:
                        type.Definition.Value,
                    Type: Display(name),
                    Relation: Relation(
                        signatureRow?.Pole,
                        bodyRow?.Pole),
                    SignatureIncomingDegree:
                        signatureRow?.SignatureIncomingDegree,
                    SignatureOutgoingDegree:
                        signatureRow?.SignatureOutgoingDegree,
                    SignaturePole:
                        signatureRow?.Pole?.ToString(),
                    SignatureGrace: signatureGrace,
                    BodyIncomingDegree:
                        bodyRow?.BodyIncomingDegree,
                    BodyOutgoingDegree:
                        bodyRow?.BodyOutgoingDegree,
                    BodyPole: bodyRow?.Pole?.ToString(),
                    BodyGrace: bodyGrace));
        }

        AddRankComparisons(
            @namespace,
            signature.SeaLevel.Types,
            body.SeaLevel.Types,
            signature.MountainPeak.Types,
            body.MountainPeak.Types,
            signatureRows,
            bodyRows,
            rankRows);
    }

    PoleCounts counts = Counts(poleRows);
    LibraryStructuralEvidenceDisposition bodyDisposition =
        BodyLeverageDisposition(
            bodyUse.Disposition,
            bodyShards.Select(
                static shard => shard.RoleDisposition));
    return new(
        Rank: package.Rank,
        Package: package.ResolvedPackage!,
        Version: package.ResolvedVersion!,
        TargetFramework: package.Tfm,
        Assembly: Path.GetFileName(path),
        AssemblyPath: package.AssemblyPath!,
        Sha256: sha256,
        ModuleVersionId:
            bodyUse.Receipt.ModuleVersionId,
        NamespaceCount: signatureShards.Length,
        TypeCount: bodyUse.Types.Length,
        SignatureDisposition:
            whole.Disposition.ToString(),
        BodyDisposition: bodyDisposition.ToString(),
        BodyUseDisposition: bodyUse.Disposition.ToString(),
        BodyCoverage: new(
            bodyUse.Coverage.BodiesConsidered,
            bodyUse.Coverage.BodiesExamined,
            bodyUse.Coverage.BodiesPhysicalOnly,
            bodyUse.Coverage.BodiesUnavailable,
            bodyUse.Coverage.BodiesLimited,
            bodyUse.Coverage.OperandsConsidered,
            bodyUse.Coverage.OperandsExamined,
            bodyUse.Coverage.OperandsUnavailable,
            bodyUse.Coverage.OperandsLimited),
        SignatureEligibleRows: signatureEligibleRows,
        BodyEligibleRows: bodyEligibleRows,
        SignaturePoles: counts.SignaturePoles,
        BodyPoles: counts.BodyPoles,
        SamePoles: counts.SamePoles,
        FlippedPoles: counts.FlippedPoles,
        SignatureOnlyPoles: counts.SignatureOnlyPoles,
        BodyOnlyPoles: counts.BodyOnlyPoles,
        SignatureGracePoles: signatureGracePoles,
        BodyGracePoles: bodyGracePoles,
        IncomingRankDelta: RankDelta(
            rankRows
                .Where(static row =>
                    row.IncomingAbsoluteDelta is not null)
                .Select(static row =>
                    row.IncomingAbsoluteDelta!.Value)),
        OutgoingRankDelta: RankDelta(
            rankRows
                .Where(static row =>
                    row.OutgoingAbsoluteDelta is not null)
                .Select(static row =>
                    row.OutgoingAbsoluteDelta!.Value)),
        DiagnosticTiming: new(
            signatureMilliseconds,
            bodyAcquireMilliseconds,
            bodyProjectionMilliseconds),
        PoleComparisons: poleRows,
        RankComparisons: rankRows);
}

static MetadataLibrarySignatureUseResult AvailableSignature(
    MetadataLibrarySignatureUseOutcome outcome) =>
    outcome switch
    {
        MetadataLibrarySignatureUseOutcome.Available available =>
            available.Result,
        MetadataLibrarySignatureUseOutcome.Rejected rejected =>
            throw new InvalidOperationException(
                $"Signature use was rejected: "
                + $"{rejected.Kind}: {rejected.Detail}"),
        _ => throw new InvalidOperationException(
            "Unknown signature-use outcome."),
    };

static MetadataLibrarySignatureUseBatchResult AvailableBatch(
    MetadataLibrarySignatureUseBatchOutcome outcome) =>
    outcome switch
    {
        MetadataLibrarySignatureUseBatchOutcome.Available available =>
            available.Result,
        MetadataLibrarySignatureUseBatchOutcome.Rejected rejected =>
            throw new InvalidOperationException(
                $"Signature-use batch was rejected: "
                + $"{rejected.Kind}: {rejected.Detail}"),
        _ => throw new InvalidOperationException(
            "Unknown signature-use batch outcome."),
    };

static AnalysisLibraryBodyUseResult AvailableBody(
    AnalysisLibraryBodyUseOutcome outcome) =>
    outcome switch
    {
        AnalysisLibraryBodyUseOutcome.Available available =>
            available.Result,
        AnalysisLibraryBodyUseOutcome.Rejected rejected =>
            throw new InvalidOperationException(
                $"Body use was rejected: "
                + $"{rejected.Kind}: {rejected.Detail}"),
        _ => throw new InvalidOperationException(
            "Unknown body-use outcome."),
    };

static int Maximum<TRow>(
    IEnumerable<TRow> rows,
    Func<TRow, bool> eligible,
    Func<TRow, int> degree) =>
    rows
        .Where(eligible)
        .Select(degree)
        .DefaultIfEmpty()
        .Max();

static bool IsGrace(
    LibraryStructuralTypePole? pole,
    int incoming,
    int outgoing,
    int incomingMaximum,
    int outgoingMaximum) =>
    pole switch
    {
        LibraryStructuralTypePole.SeaLevel =>
            incoming < incomingMaximum,
        LibraryStructuralTypePole.MountainPeak =>
            outgoing < outgoingMaximum,
        null => false,
        _ => throw new InvalidOperationException(
            "Unknown structural Type pole."),
    };

static string Relation(
    LibraryStructuralTypePole? signature,
    LibraryStructuralTypePole? body)
{
    if (signature is not null && signature == body)
        return "same";
    if (signature is not null && body is not null)
        return "flipped";
    if (signature is not null)
        return "signature-only";
    return "body-only";
}

static void AddRankComparisons(
    string @namespace,
    IReadOnlyList<MetadataTypeDefinitionAddress> signatureIncoming,
    IReadOnlyList<MetadataTypeDefinitionAddress> bodyIncoming,
    IReadOnlyList<MetadataTypeDefinitionAddress> signatureOutgoing,
    IReadOnlyList<MetadataTypeDefinitionAddress> bodyOutgoing,
    IReadOnlyDictionary<
        MetadataTypeDefinitionAddress,
        LibraryStructuralTypeLeverageRow> signatureRows,
    IReadOnlyDictionary<
        MetadataTypeDefinitionAddress,
        LibraryStructuralBodyTypeLeverageRow> bodyRows,
    List<LeverageRankComparison> destination)
{
    Dictionary<MetadataTypeDefinitionAddress, int>
        signatureIncomingPositions = Positions(signatureIncoming);
    Dictionary<MetadataTypeDefinitionAddress, int>
        bodyIncomingPositions = Positions(bodyIncoming);
    Dictionary<MetadataTypeDefinitionAddress, int>
        signatureOutgoingPositions = Positions(signatureOutgoing);
    Dictionary<MetadataTypeDefinitionAddress, int>
        bodyOutgoingPositions = Positions(bodyOutgoing);
    foreach (MetadataTypeDefinitionAddress type
        in signatureIncomingPositions.Keys
            .Union(bodyIncomingPositions.Keys)
            .Union(signatureOutgoingPositions.Keys)
            .Union(bodyOutgoingPositions.Keys)
            .OrderBy(static type => type.Definition.Value))
    {
        int? signatureIncomingPosition =
            Position(signatureIncomingPositions, type);
        int? bodyIncomingPosition =
            Position(bodyIncomingPositions, type);
        int? signatureOutgoingPosition =
            Position(signatureOutgoingPositions, type);
        int? bodyOutgoingPosition =
            Position(bodyOutgoingPositions, type);
        MetadataTypeDefinitionName name =
            bodyRows.TryGetValue(
                type,
                out LibraryStructuralBodyTypeLeverageRow? bodyRow)
                ? bodyRow.Name
                : signatureRows[type].Name;
        destination.Add(
            new(
                Namespace: @namespace,
                TypeDefinitionToken: type.Definition.Value,
                Type: Display(name),
                SignatureIncomingPosition:
                    signatureIncomingPosition,
                BodyIncomingPosition: bodyIncomingPosition,
                IncomingAbsoluteDelta: AbsoluteDelta(
                    signatureIncomingPosition,
                    bodyIncomingPosition),
                SignatureOutgoingPosition:
                    signatureOutgoingPosition,
                BodyOutgoingPosition: bodyOutgoingPosition,
                OutgoingAbsoluteDelta: AbsoluteDelta(
                    signatureOutgoingPosition,
                    bodyOutgoingPosition)));
    }
}

static Dictionary<MetadataTypeDefinitionAddress, int> Positions(
    IReadOnlyList<MetadataTypeDefinitionAddress> order) =>
    order
        .Select((type, index) => (type, Position: index + 1))
        .ToDictionary(
            static item => item.type,
            static item => item.Position);

static int? Position(
    IReadOnlyDictionary<MetadataTypeDefinitionAddress, int> positions,
    MetadataTypeDefinitionAddress type) =>
    positions.TryGetValue(type, out int position)
        ? position
        : null;

static int? AbsoluteDelta(int? first, int? second) =>
    first is int firstValue && second is int secondValue
        ? Math.Abs(firstValue - secondValue)
        : null;

static LeverageRankDelta RankDelta(
    IEnumerable<int> values)
{
    int[] ordered = [.. values.Order()];
    if (ordered.Length == 0)
        return new(0, 0, 0, 0);

    return new(
        ordered.Length,
        Median(ordered),
        Percentile(ordered, 0.95),
        ordered[^1]);
}

static bool HasRequiredSelectedProvenance(
    PackageSweepEntry package) =>
    package.ResolvedPackage is not null
    && package.ResolvedVersion is not null
    && package.AssemblyPath is not null
    && package.Sha256 is not null;

static LibraryStructuralEvidenceDisposition BodyLeverageDisposition(
    AnalysisLibraryBodyUseDisposition bodyUseDisposition,
    IEnumerable<LibraryStructuralEvidenceDisposition>
        shardDispositions) =>
    bodyUseDisposition
            == AnalysisLibraryBodyUseDisposition.Complete
        && shardDispositions.All(
            static disposition =>
                disposition
                    == LibraryStructuralEvidenceDisposition.Complete)
            ? LibraryStructuralEvidenceDisposition.Complete
            : LibraryStructuralEvidenceDisposition.Qualified;

static double Median(IReadOnlyList<int> values)
{
    int middle = values.Count / 2;
    return values.Count % 2 == 0
        ? (values[middle - 1] + values[middle]) / 2d
        : values[middle];
}

static int Percentile(
    IReadOnlyList<int> values,
    double percentile)
{
    int index = (int)Math.Ceiling(
        values.Count * percentile) - 1;
    return values[Math.Max(index, 0)];
}

static PoleCounts Counts(
    IEnumerable<LeveragePoleComparison> rows)
{
    int signature = 0;
    int body = 0;
    int same = 0;
    int flipped = 0;
    int signatureOnly = 0;
    int bodyOnly = 0;
    foreach (LeveragePoleComparison row in rows)
    {
        if (row.SignaturePole is not null)
            signature++;
        if (row.BodyPole is not null)
            body++;
        switch (row.Relation)
        {
            case "same":
                same++;
                break;
            case "flipped":
                flipped++;
                break;
            case "signature-only":
                signatureOnly++;
                break;
            case "body-only":
                bodyOnly++;
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown pole comparison relation.");
        }
    }
    return new(
        signature,
        body,
        same,
        flipped,
        signatureOnly,
        bodyOnly);
}

static LeverageCorpusSummary Summarize(
    IReadOnlyList<LeverageAssemblyCensus> assemblies)
{
    PoleCounts counts = new(
        assemblies.Sum(static item =>
            item.SignaturePoles),
        assemblies.Sum(static item => item.BodyPoles),
        assemblies.Sum(static item => item.SamePoles),
        assemblies.Sum(static item => item.FlippedPoles),
        assemblies.Sum(static item =>
            item.SignatureOnlyPoles),
        assemblies.Sum(static item => item.BodyOnlyPoles));
    int signatureEligible = assemblies.Sum(
        static item => item.SignatureEligibleRows);
    int bodyEligible = assemblies.Sum(
        static item => item.BodyEligibleRows);
    return new(
        AssemblyCount: assemblies.Count,
        NamespaceCount: assemblies.Sum(
            static item => item.NamespaceCount),
        TypeCount: assemblies.Sum(
            static item => item.TypeCount),
        BodyCompleteCount: assemblies.Count(
            static item =>
                StringComparer.Ordinal.Equals(
                    item.BodyDisposition,
                    "Complete")),
        BodyQualifiedCount: assemblies.Count(
            static item =>
                StringComparer.Ordinal.Equals(
                    item.BodyDisposition,
                    "Qualified")),
        BodyUseCompleteCount: assemblies.Count(
            static item =>
                StringComparer.Ordinal.Equals(
                    item.BodyUseDisposition,
                    "Complete")),
        BodyUseQualifiedCount: assemblies.Count(
            static item =>
                StringComparer.Ordinal.Equals(
                    item.BodyUseDisposition,
                    "Qualified")),
        BodyUsePartialCount: assemblies.Count(
            static item =>
                StringComparer.Ordinal.Equals(
                    item.BodyUseDisposition,
                    "Partial")),
        BodyPhysicalOnlyCount: assemblies.Sum(
            static item =>
                item.BodyCoverage.BodiesPhysicalOnly),
        SignatureEligibleRows: signatureEligible,
        BodyEligibleRows: bodyEligible,
        SignaturePoles: counts.SignaturePoles,
        BodyPoles: counts.BodyPoles,
        SamePoles: counts.SamePoles,
        FlippedPoles: counts.FlippedPoles,
        SignatureOnlyPoles: counts.SignatureOnlyPoles,
        BodyOnlyPoles: counts.BodyOnlyPoles,
        SignatureGracePoles: assemblies.Sum(
            static item =>
                item.SignatureGracePoles),
        BodyGracePoles: assemblies.Sum(
            static item => item.BodyGracePoles),
        SignaturePoleDensity:
            Ratio(counts.SignaturePoles, signatureEligible),
        BodyPoleDensity:
            Ratio(counts.BodyPoles, bodyEligible),
        SameShareOfSignaturePoles:
            Ratio(counts.SamePoles, counts.SignaturePoles),
        SameShareOfBodyPoles:
            Ratio(counts.SamePoles, counts.BodyPoles),
        AnyPoleInBothShareOfSignaturePoles:
            Ratio(
                counts.SamePoles + counts.FlippedPoles,
                counts.SignaturePoles),
        AnyPoleInBothShareOfBodyPoles:
            Ratio(
                counts.SamePoles + counts.FlippedPoles,
                counts.BodyPoles));
}

static double Ratio(int numerator, int denominator) =>
    denominator == 0 ? 0 : (double)numerator / denominator;

static string Display(
    MetadataTypeDefinitionName name)
{
    string type = string.Join('+', name.Segments);
    return string.IsNullOrEmpty(name.Namespace)
        ? type
        : $"{name.Namespace}.{type}";
}

static string Markdown(LeverageCorpusReport report)
{
    LeverageCorpusSummary summary = report.Summary;
    var text = new StringBuilder();
    text.AppendLine("# Library Type-leverage corpus census");
    text.AppendLine();
    text.AppendLine(
        $"Methodology: `{report.MethodologyVersion}`  ");
    text.AppendLine(
        $"Source: `{report.SourceManifest}`  ");
    text.AppendLine(
        $"Packages: {summary.AssemblyCount}; namespaces: "
        + $"{summary.NamespaceCount}; Types: {summary.TypeCount}");
    text.AppendLine();
    text.AppendLine("## Aggregate comparison");
    text.AppendLine();
    text.AppendLine("| Measure | Value |");
    text.AppendLine("| --- | ---: |");
    text.AppendLine(
        $"| Signature poles | {summary.SignaturePoles} |");
    text.AppendLine(
        $"| Body poles | {summary.BodyPoles} |");
    text.AppendLine(
        $"| Same Type and pole | {summary.SamePoles} |");
    text.AppendLine(
        $"| Same Type, flipped pole | {summary.FlippedPoles} |");
    text.AppendLine(
        $"| Signature-only poles | {summary.SignatureOnlyPoles} |");
    text.AppendLine(
        $"| Body-only poles | {summary.BodyOnlyPoles} |");
    text.AppendLine(
        $"| Signature grace poles | {summary.SignatureGracePoles} |");
    text.AppendLine(
        $"| Body grace poles | {summary.BodyGracePoles} |");
    text.AppendLine(
        $"| Same-pole share of signature | "
        + $"{Percent(summary.SameShareOfSignaturePoles)} |");
    text.AppendLine(
        $"| Same-pole share of body | "
        + $"{Percent(summary.SameShareOfBodyPoles)} |");
    text.AppendLine(
        $"| Any-pole overlap share of signature | "
        + $"{Percent(summary.AnyPoleInBothShareOfSignaturePoles)} |");
    text.AppendLine(
        $"| Any-pole overlap share of body | "
        + $"{Percent(summary.AnyPoleInBothShareOfBodyPoles)} |");
    text.AppendLine(
        $"| Signature pole density | "
        + $"{Percent(summary.SignaturePoleDensity)} |");
    text.AppendLine(
        $"| Body pole density | "
        + $"{Percent(summary.BodyPoleDensity)} |");
    text.AppendLine(
        $"| Body leverage complete / qualified | "
        + $"{summary.BodyCompleteCount} / "
        + $"{summary.BodyQualifiedCount} |");
    text.AppendLine(
        $"| Body source complete / qualified / partial | "
        + $"{summary.BodyUseCompleteCount} / "
        + $"{summary.BodyUseQualifiedCount} / "
        + $"{summary.BodyUsePartialCount} |");
    text.AppendLine(
        $"| Physical-only bodies | "
        + $"{summary.BodyPhysicalOnlyCount} |");
    text.AppendLine();
    text.AppendLine("## Per assembly");
    text.AppendLine();
    text.AppendLine(
        "| Rank | Package | Assembly | Namespaces | Types "
        + "| Signature poles | Body poles | Same | Flipped "
        + "| Signature-only | Body-only | Body qualification |");
    text.AppendLine(
        "| ---: | --- | --- | ---: | ---: | ---: | ---: "
        + "| ---: | ---: | ---: | ---: | --- |");
    foreach (LeverageAssemblyCensus assembly in report.Assemblies)
    {
        text.AppendLine(
            $"| {assembly.Rank} "
            + $"| `{assembly.Package}@{assembly.Version}` "
            + $"| `{assembly.Assembly}` "
            + $"| {assembly.NamespaceCount} "
            + $"| {assembly.TypeCount} "
            + $"| {assembly.SignaturePoles} "
            + $"| {assembly.BodyPoles} "
            + $"| {assembly.SamePoles} "
            + $"| {assembly.FlippedPoles} "
            + $"| {assembly.SignatureOnlyPoles} "
            + $"| {assembly.BodyOnlyPoles} "
            + $"| {assembly.BodyDisposition} |");
    }
    text.AppendLine();
    text.AppendLine("## Representative changed poles");
    text.AppendLine();
    foreach (LeverageAssemblyCensus assembly in report.Assemblies)
    {
        LeveragePoleComparison[] changed =
        [
            .. assembly.PoleComparisons
                .Where(static row =>
                    row.Relation != "same")
                .OrderByDescending(static row =>
                    Math.Max(
                        row.BodyIncomingDegree ?? 0,
                        row.BodyOutgoingDegree ?? 0))
                .ThenBy(static row => row.Type, StringComparer.Ordinal)
                .Take(5),
        ];
        if (changed.Length == 0)
            continue;
        text.AppendLine(
            $"### {assembly.Package}@{assembly.Version}");
        text.AppendLine();
        foreach (LeveragePoleComparison row in changed)
        {
            text.AppendLine(
                $"- `{row.Type}`: signature "
                + $"{row.SignaturePole ?? "none"} "
                + $"({row.SignatureIncomingDegree ?? 0}/"
                + $"{row.SignatureOutgoingDegree ?? 0}), body "
                + $"{row.BodyPole ?? "none"} "
                + $"({row.BodyIncomingDegree ?? 0}/"
                + $"{row.BodyOutgoingDegree ?? 0}) — "
                + row.Relation);
        }
        text.AppendLine();
    }
    text.AppendLine("## Interpretation limits");
    text.AppendLine();
    text.AppendLine(
        "This census compares owner-issued product results. It does not infer "
        + "poles, repair qualified evidence, or establish which evidence mode "
        + "is more useful. CoreCLR timings are diagnostic only and are not "
        + "accepted performance evidence; performance claims require exact "
        + "base/head NativeAOT measurement.");
    return text.ToString();
}

static string Percent(double value) =>
    (value * 100).ToString(
        "F1",
        CultureInfo.InvariantCulture) + "%";

static void SelfTest()
{
    var rows = new[]
    {
        new LeveragePoleComparison(
            "N",
            1,
            "N.Same",
            "same",
            10,
            1,
            "SeaLevel",
            false,
            12,
            2,
            "SeaLevel",
            false),
        new LeveragePoleComparison(
            "N",
            2,
            "N.Flipped",
            "flipped",
            1,
            10,
            "MountainPeak",
            false,
            10,
            1,
            "SeaLevel",
            false),
        new LeveragePoleComparison(
            "N",
            3,
            "N.Signature",
            "signature-only",
            3,
            0,
            "SeaLevel",
            false,
            0,
            0,
            null,
            false),
        new LeveragePoleComparison(
            "N",
            4,
            "N.Body",
            "body-only",
            0,
            0,
            null,
            false,
            0,
            3,
            "MountainPeak",
            false),
    };
    PoleCounts counts = Counts(rows);
    Require(
        counts
            == new PoleCounts(3, 3, 1, 1, 1, 1),
        "pole classification counts");
    Require(
        Relation(
            LibraryStructuralTypePole.SeaLevel,
            LibraryStructuralTypePole.SeaLevel)
            == "same",
        "same-pole relation");
    Require(
        Relation(
            LibraryStructuralTypePole.SeaLevel,
            LibraryStructuralTypePole.MountainPeak)
            == "flipped",
        "flipped-pole relation");
    Require(
        IsGrace(
            LibraryStructuralTypePole.MountainPeak,
            incoming: 0,
            outgoing: 9,
            incomingMaximum: 1,
            outgoingMaximum: 10),
        "near-maximum grace observation");
    Require(
        !IsGrace(
            LibraryStructuralTypePole.SeaLevel,
            incoming: 3,
            outgoing: 0,
            incomingMaximum: 3,
            outgoingMaximum: 0),
        "exact maximum observation");

    LeverageRankDelta delta = RankDelta([0, 1, 2, 3]);
    Require(
        delta
            == new LeverageRankDelta(4, 1.5, 3, 3),
        "rank-delta summary");

    const string manifestJson =
        """
        {
          "schemaVersion": 1,
          "generatedAtUtc": "2026-01-01T00:00:00Z",
          "startRank": 1,
          "requestedPackageCount": 1,
          "selectedPackageCount": 1,
          "packages": [
            {
              "rank": 1,
              "status": "selected",
              "resolvedPackage": "Example",
              "resolvedVersion": "1.0.0",
              "tfm": null,
              "assemblyPath": "packages/Example.dll",
              "sha256": "0123456789abcdef"
            }
          ],
          "unreconciled": null
        }
        """;
    PackageSweepManifest? manifest =
        JsonSerializer.Deserialize(
            manifestJson,
            CensusJsonContext.Default.PackageSweepManifest);
    Require(
        manifest is
        {
            SchemaVersion: 1,
            SelectedPackageCount: 1,
            Packages.Count: 1,
        }
        && manifest.Packages[0].ResolvedPackage == "Example"
        && manifest.Packages[0].Tfm is null
        && HasRequiredSelectedProvenance(manifest.Packages[0]),
        "package-sweep manifest contract");
    Require(
        BodyLeverageDisposition(
            AnalysisLibraryBodyUseDisposition.Complete,
            [LibraryStructuralEvidenceDisposition.Qualified])
            == LibraryStructuralEvidenceDisposition.Qualified,
        "metadata-qualified body leverage");
    Require(
        BodyLeverageDisposition(
            AnalysisLibraryBodyUseDisposition.Partial,
            [LibraryStructuralEvidenceDisposition.Complete])
            == LibraryStructuralEvidenceDisposition.Qualified,
        "source-qualified body leverage");
}

static void Require(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            $"Self-test failed: {name}.");
    }
}

sealed record PackageSweepManifest(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int StartRank,
    int RequestedPackageCount,
    int SelectedPackageCount,
    IReadOnlyList<PackageSweepEntry> Packages,
    string? Unreconciled);

sealed record PackageSweepEntry(
    int Rank,
    string Status,
    string? ResolvedPackage,
    string? ResolvedVersion,
    string? Tfm,
    string? AssemblyPath,
    string? Sha256);

sealed record LeverageCorpusReport(
    int SchemaVersion,
    string MethodologyVersion,
    DateTimeOffset GeneratedAtUtc,
    string SourceManifest,
    DateTimeOffset SourceGeneratedAtUtc,
    int SourceStartRank,
    int SourceRequestedPackageCount,
    LeverageCorpusSummary Summary,
    IReadOnlyList<LeverageAssemblyCensus> Assemblies);

sealed record LeverageCorpusSummary(
    int AssemblyCount,
    int NamespaceCount,
    int TypeCount,
    int BodyCompleteCount,
    int BodyQualifiedCount,
    int BodyUseCompleteCount,
    int BodyUseQualifiedCount,
    int BodyUsePartialCount,
    int BodyPhysicalOnlyCount,
    int SignatureEligibleRows,
    int BodyEligibleRows,
    int SignaturePoles,
    int BodyPoles,
    int SamePoles,
    int FlippedPoles,
    int SignatureOnlyPoles,
    int BodyOnlyPoles,
    int SignatureGracePoles,
    int BodyGracePoles,
    double SignaturePoleDensity,
    double BodyPoleDensity,
    double SameShareOfSignaturePoles,
    double SameShareOfBodyPoles,
    double AnyPoleInBothShareOfSignaturePoles,
    double AnyPoleInBothShareOfBodyPoles);

sealed record LeverageAssemblyCensus(
    int Rank,
    string Package,
    string Version,
    string? TargetFramework,
    string Assembly,
    string AssemblyPath,
    string Sha256,
    Guid ModuleVersionId,
    int NamespaceCount,
    int TypeCount,
    string SignatureDisposition,
    string BodyDisposition,
    string BodyUseDisposition,
    LeverageBodyCoverage BodyCoverage,
    int SignatureEligibleRows,
    int BodyEligibleRows,
    int SignaturePoles,
    int BodyPoles,
    int SamePoles,
    int FlippedPoles,
    int SignatureOnlyPoles,
    int BodyOnlyPoles,
    int SignatureGracePoles,
    int BodyGracePoles,
    LeverageRankDelta IncomingRankDelta,
    LeverageRankDelta OutgoingRankDelta,
    LeverageDiagnosticTiming DiagnosticTiming,
    IReadOnlyList<LeveragePoleComparison> PoleComparisons,
    IReadOnlyList<LeverageRankComparison> RankComparisons);

sealed record LeverageBodyCoverage(
    int BodiesConsidered,
    int BodiesExamined,
    int BodiesPhysicalOnly,
    int BodiesUnavailable,
    int BodiesLimited,
    int OperandsConsidered,
    int OperandsExamined,
    int OperandsUnavailable,
    int OperandsLimited);

sealed record LeveragePoleComparison(
    string Namespace,
    int TypeDefinitionToken,
    string Type,
    string Relation,
    int? SignatureIncomingDegree,
    int? SignatureOutgoingDegree,
    string? SignaturePole,
    bool SignatureGrace,
    int? BodyIncomingDegree,
    int? BodyOutgoingDegree,
    string? BodyPole,
    bool BodyGrace);

sealed record LeverageRankDelta(
    int JoinedTypes,
    double MedianAbsoluteDelta,
    int P95AbsoluteDelta,
    int MaximumAbsoluteDelta);

sealed record LeverageRankComparison(
    string Namespace,
    int TypeDefinitionToken,
    string Type,
    int? SignatureIncomingPosition,
    int? BodyIncomingPosition,
    int? IncomingAbsoluteDelta,
    int? SignatureOutgoingPosition,
    int? BodyOutgoingPosition,
    int? OutgoingAbsoluteDelta);

sealed record LeverageDiagnosticTiming(
    double SignatureMilliseconds,
    double BodyAcquisitionMilliseconds,
    double BodyProjectionMilliseconds);

readonly record struct PoleCounts(
    int SignaturePoles,
    int BodyPoles,
    int SamePoles,
    int FlippedPoles,
    int SignatureOnlyPoles,
    int BodyOnlyPoles);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(PackageSweepManifest))]
[JsonSerializable(typeof(LeverageCorpusReport))]
internal sealed partial class CensusJsonContext
    : JsonSerializerContext;
