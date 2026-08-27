using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.DecompilerHarness;

static class AuthoredSourceOracleManifest
{
    public const int Version = 1;
    public const int PrinterComparisonVersion = 1;
    public const string DefaultPrinterProfile = "default-v1";

    internal sealed record Document(
        [property: JsonRequired] int Version,
        [property: JsonRequired] int PrinterComparisonVersion,
        [property: JsonRequired] IReadOnlyList<FileEntry> Files);

    internal sealed record FileEntry(
        [property: JsonRequired] string SourceUrl,
        [property: JsonRequired] string ChecksumAlgorithm,
        [property: JsonRequired] string Checksum,
        [property: JsonRequired] string PrinterProfile,
        [property: JsonRequired] bool RequirePrinterExact,
        [property: JsonRequired] IReadOnlyList<MemberEntry> Members);

    internal sealed record MemberEntry(
        [property: JsonRequired] string Assembly,
        [property: JsonRequired] string AssemblyVersion,
        [property: JsonRequired] Guid ModuleVersionId,
        [property: JsonRequired] int MetadataToken,
        [property: JsonRequired] string Type,
        [property: JsonRequired] string Method,
        [property: JsonRequired] int Overload);

    internal sealed record EvaluatedRow(
        AuthoredSourceHarvest.CorpusRecord Record,
        ReturnToSenderSourceProbeResult Result);

    internal sealed record Report(
        [property: JsonRequired] int FilesRegistered,
        [property: JsonRequired] int FilesValid,
        [property: JsonRequired] int FilesCorrect,
        [property: JsonRequired] int PrinterExactRequired,
        [property: JsonRequired] int PrinterExactPassing,
        [property: JsonRequired] bool Passed,
        [property: JsonRequired] IReadOnlyList<string> Failures);

    internal static bool TryRead(string path, out Document? document, out string? error)
    {
        document = null;
        error = null;
        if (!File.Exists(path))
        {
            error = $"Source-oracle manifest not found: {path}";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowDuplicateProperties = false,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
            if (document is null)
            {
                error = $"Source-oracle manifest is empty: {path}";
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Source-oracle manifest is not valid JSON: {path}: {ex.Message}";
            return false;
        }
        catch (IOException ex)
        {
            error = $"Source-oracle manifest could not be read: {path}: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"Source-oracle manifest could not be read: {path}: {ex.Message}";
            return false;
        }
    }

    internal static Report Evaluate(
        Document manifest,
        IReadOnlyList<EvaluatedRow> rows)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(rows);

        var failures = new List<string>();
        if (manifest.Version != Version)
            failures.Add($"manifest version {manifest.Version} is unsupported; expected {Version}");
        if (manifest.PrinterComparisonVersion != PrinterComparisonVersion)
        {
            failures.Add(
                $"printer comparison version {manifest.PrinterComparisonVersion} is unsupported; "
                + $"expected {PrinterComparisonVersion}");
        }
        if (manifest.Files is null || manifest.Files.Count == 0)
            failures.Add("manifest contains no files");

        int validFiles = 0;
        int correctFiles = 0;
        int exactRequired = 0;
        int exactPassing = 0;
        var seenFiles = new HashSet<(string Url, string Algorithm, string Checksum)>();

        foreach (var file in manifest.Files ?? [])
        {
            if (file is null)
            {
                failures.Add("manifest contains a null file entry");
                continue;
            }

            string fileId = string.IsNullOrWhiteSpace(file.SourceUrl)
                ? "<missing source URL>"
                : file.SourceUrl;
            bool fileShapeValid = true;
            if (string.IsNullOrWhiteSpace(file.SourceUrl)
                || string.IsNullOrWhiteSpace(file.ChecksumAlgorithm)
                || string.IsNullOrWhiteSpace(file.Checksum))
            {
                failures.Add($"{fileId}: immutable source identity is incomplete");
                fileShapeValid = false;
            }
            if (!string.Equals(
                    file.PrinterProfile,
                    DefaultPrinterProfile,
                    StringComparison.Ordinal))
            {
                failures.Add($"{fileId}: unsupported printer profile '{file.PrinterProfile}'");
                fileShapeValid = false;
            }
            if (!seenFiles.Add((
                    file.SourceUrl,
                    (file.ChecksumAlgorithm ?? "").ToUpperInvariant(),
                    (file.Checksum ?? "").ToUpperInvariant())))
            {
                failures.Add($"{fileId}: file identity is registered more than once");
                fileShapeValid = false;
            }
            if (file.Members is null || file.Members.Count == 0)
            {
                failures.Add($"{fileId}: expected eligible-member set is empty");
                fileShapeValid = false;
            }

            var expected = new HashSet<MemberKey>();
            foreach (var member in file.Members ?? [])
            {
                if (member is null)
                {
                    failures.Add($"{fileId}: expected member entry is null");
                    fileShapeValid = false;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(member.Assembly)
                    || string.IsNullOrWhiteSpace(member.AssemblyVersion)
                    || member.ModuleVersionId == Guid.Empty
                    || member.MetadataToken <= 0
                    || string.IsNullOrWhiteSpace(member.Type)
                    || string.IsNullOrWhiteSpace(member.Method))
                {
                    failures.Add($"{fileId}: expected member identity is incomplete");
                    fileShapeValid = false;
                    continue;
                }
                if (!expected.Add(MemberKey.From(member)))
                {
                    failures.Add($"{fileId}: expected member {Display(member)} is duplicated");
                    fileShapeValid = false;
                }
            }

            var actualRows = rows.Where(row => SameFile(row.Record, file)).ToArray();
            var actual = actualRows
                .Select(row => MemberKey.From(row.Record))
                .ToHashSet();
            foreach (var missing in expected.Except(actual).Order())
            {
                failures.Add($"{fileId}: expected member {missing} is missing");
                fileShapeValid = false;
            }
            foreach (var stale in actual.Except(expected).Order())
            {
                failures.Add($"{fileId}: corpus member {stale} is absent from the expected set");
                fileShapeValid = false;
            }

            bool valid = fileShapeValid
                && actualRows.Length == expected.Count
                && actualRows.All(row => row.Result.Outcome is
                    ReturnToSenderSourceOutcome.ValidMatch
                    or ReturnToSenderSourceOutcome.ValidDifferent);
            if (valid)
                validFiles++;
            else if (fileShapeValid)
                failures.Add($"{fileId}: one or more expected members are not Valid");

            bool correct = valid
                && actualRows.All(row =>
                    row.Result.Outcome == ReturnToSenderSourceOutcome.ValidMatch);
            if (correct)
                correctFiles++;
            else if (valid)
                failures.Add($"{fileId}: one or more expected members are not Correct");

            if (!file.RequirePrinterExact)
                continue;

            exactRequired++;
            if (actualRows.Any(row =>
                    row.Record.PrinterBodyVersion != manifest.PrinterComparisonVersion))
            {
                failures.Add(
                    $"{fileId}: one or more expected members lack Printer body "
                    + $"version {manifest.PrinterComparisonVersion}");
            }
            bool exact = correct
                && actualRows.All(row =>
                    row.Record.PrinterBodyVersion == manifest.PrinterComparisonVersion)
                && actualRows.All(row =>
                    row.Result.PrinterExact == PrinterExactOutcome.Exact);
            if (exact)
                exactPassing++;
            else if (correct)
                failures.Add($"{fileId}: one or more expected members are not Printer exact");
        }

        int filesRegistered = manifest.Files?.Count ?? 0;
        return new Report(
            filesRegistered,
            validFiles,
            correctFiles,
            exactRequired,
            exactPassing,
            failures.Count == 0
                && filesRegistered > 0
                && validFiles == filesRegistered
                && correctFiles == filesRegistered
                && exactPassing == exactRequired,
            failures);
    }

    static bool SameFile(
        AuthoredSourceHarvest.CorpusRecord record,
        FileEntry file)
        => string.Equals(record.SourceUrl, file.SourceUrl, StringComparison.Ordinal)
            && string.Equals(
                record.ChecksumAlgorithm,
                file.ChecksumAlgorithm,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                record.Checksum,
                file.Checksum,
                StringComparison.OrdinalIgnoreCase);

    static string Display(MemberEntry member)
        => $"{member.Assembly}/{member.ModuleVersionId}:0x{member.MetadataToken:X8}:"
            + $"{member.Type}::{member.Method}#{member.Overload}";

    readonly record struct MemberKey(
        string Assembly,
        string AssemblyVersion,
        Guid ModuleVersionId,
        int MetadataToken,
        string Type,
        string Method,
        int Overload) : IComparable<MemberKey>
    {
        public static MemberKey From(MemberEntry member)
            => new(
                member.Assembly,
                member.AssemblyVersion,
                member.ModuleVersionId,
                member.MetadataToken,
                member.Type,
                member.Method,
                member.Overload);

        public static MemberKey From(AuthoredSourceHarvest.CorpusRecord record)
            => new(
                record.Assembly,
                record.AssemblyVersion,
                record.ModuleVersionId.GetValueOrDefault(),
                record.MetadataToken,
                record.Type,
                record.Method,
                record.Overload);

        public int CompareTo(MemberKey other)
        {
            int result = string.Compare(Assembly, other.Assembly, StringComparison.Ordinal);
            if (result != 0)
                return result;
            result = string.Compare(
                AssemblyVersion,
                other.AssemblyVersion,
                StringComparison.Ordinal);
            if (result != 0)
                return result;
            result = ModuleVersionId.CompareTo(other.ModuleVersionId);
            if (result != 0)
                return result;
            result = MetadataToken.CompareTo(other.MetadataToken);
            if (result != 0)
                return result;
            result = string.Compare(Type, other.Type, StringComparison.Ordinal);
            if (result != 0)
                return result;
            result = string.Compare(Method, other.Method, StringComparison.Ordinal);
            return result != 0 ? result : Overload.CompareTo(other.Overload);
        }

        public override string ToString()
            => $"{Assembly}/{ModuleVersionId}:0x{MetadataToken:X8}:"
                + $"{Type}::{Method}#{Overload}";
    }
}
