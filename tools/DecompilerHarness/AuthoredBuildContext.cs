using System.Collections.Immutable;
using System.Reflection;

using DotnetInspector.RoundTripCompilation;
using ILInspector.CSharp;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

enum BuildContextFactStatus
{
    Agree,
    Different,
    Unknown,
    Failed,
}

sealed record BuildContextFact(
    string Dimension,
    string Name,
    string? Recorded,
    string? Effective,
    BuildContextFactStatus Status,
    string Detail);

sealed record LaneBuildContext(ImmutableArray<BuildContextFact> Facts)
{
    public AuthoredBuildContextStatus Status
        => Facts.Any(fact => fact.Status == BuildContextFactStatus.Failed)
            ? AuthoredBuildContextStatus.Failed
            : Facts.Any(fact => fact.Status == BuildContextFactStatus.Different)
                ? AuthoredBuildContextStatus.Drift
                : Facts.Any(fact => fact.Status == BuildContextFactStatus.Unknown)
                    ? AuthoredBuildContextStatus.Incomplete
                    : AuthoredBuildContextStatus.Recorded;
}

sealed record RebuildCompilationAttempt(
    MetadataMethodAddress Target,
    CSharpSourceArtifact Artifact,
    CSharpParseOptions ParseOptions,
    CSharpCompilationOptions Options,
    RoundTripCompilationProvenance Provenance,
    ImmutableArray<MetadataImageKind> ReferenceKinds,
    string CompilerIdentity)
{
    internal static RebuildCompilationAttempt Capture(
        ProductArtifact artifact,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions options,
        RoundTripCompilationProvenance provenance,
        IReadOnlyList<MetadataReference> references)
        => new(
            MetadataMethodAddress.Create(artifact.Request.Reader, artifact.Request.TargetMethod),
            artifact.SourceArtifact,
            parseOptions,
            options,
            provenance,
            references.Select(reference => reference.Properties.Kind).ToImmutableArray(),
            typeof(CSharpCompilation).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? typeof(CSharpCompilation).Assembly.GetName().Version?.ToString()
                ?? "unknown");
}

sealed record RecordedBuildContext(
    bool? IsDeterministic,
    FindingInspection<CompilationOptionInfo> Options,
    FindingInspection<CompilationReferenceInfo> References)
{
    internal static RecordedBuildContext Failed(FindingSubject subject, string reason)
        => new(
            null,
            new FindingInspection<CompilationOptionInfo>.Failed(
                new(subject, MetadataFindings.CompilationOptionDescriptor, reason)),
            new FindingInspection<CompilationReferenceInfo>.Failed(
                new(subject, MetadataFindings.CompilationReferenceDescriptor, reason)));

    internal string? Option(string name)
    {
        var values = OptionValues(name);
        return values.Length == 1 ? values[0] : null;
    }

    ImmutableArray<string> OptionValues(string name)
        => Options.Value is FindingInspection<CompilationOptionInfo>.Complete complete
            ? complete.Findings
                .Where(finding => string.Equals(finding.Payload.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(finding => finding.Payload.Value)
                .ToImmutableArray()
            : [];

    internal LaneBuildContext Assess(RebuildCompilationAttempt? attempt)
    {
        var facts = ImmutableArray.CreateBuilder<BuildContextFact>();
        AddOption("compiler", "compiler-version", attempt?.CompilerIdentity, value => value);
        AddOption("options", "language-version",
            attempt?.ParseOptions.LanguageVersion.ToDisplayString(), NormalizeLanguage);
        AddOption("options", "define",
            attempt is null ? null : NormalizeNames(attempt.ParseOptions.PreprocessorSymbolNames),
            value => NormalizeNames(value.Split(',')));
        AddOption("options", "optimization",
            attempt?.Options.OptimizationLevel.ToString().ToLowerInvariant(),
            value => value.ToLowerInvariant() is "debug" or "release" ? value.ToLowerInvariant() : null);
        AddOption("options", "unsafe", attempt?.Options.AllowUnsafe.ToString(), NormalizeBoolean);
        AddOption("options", "checked", attempt?.Options.CheckOverflow.ToString(), NormalizeBoolean);
        AddOption("options", "nullable", attempt?.Options.NullableContextOptions.ToString(),
            value => Enum.TryParse<NullableContextOptions>(value, true, out var parsed)
                && Enum.IsDefined(parsed) ? parsed.ToString() : null);

        if (Options.Value is FindingInspection<CompilationOptionInfo>.Complete completeOptions)
        {
            var handled = facts.Select(fact => fact.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var option in completeOptions.Findings.Select(finding => finding.Payload))
            {
                if (!handled.Contains(option.Name))
                {
                    facts.Add(new("options", option.Name, option.Value, null,
                        BuildContextFactStatus.Unknown, "Recorded option is not compared by this harness."));
                }
            }
        }

        AddReferences(facts, attempt);
        facts.Add(new("generators", "invocation", null,
            attempt is null ? null : "No generator invocation; supplied artifact only",
            BuildContextFactStatus.Unknown, "Original generator inputs and invocation are unavailable."));
        facts.Add(new("project", "build", null,
            attempt is null ? null : "Body in RTS artifact; retained compilation closure",
            BuildContextFactStatus.Unknown, "Original project, SDK/MSBuild settings, and build inputs are unavailable."));
        if (attempt is null)
        {
            facts.Add(new("attempt", "compilation", null, null,
                BuildContextFactStatus.Unknown, "No compilation attempt is retained for this artifact."));
        }
        return new(facts.ToImmutable());

        void AddOption(string dimension, string name, string? effective, Func<string, string?> normalize)
        {
            if (Options.Value is FindingInspection<CompilationOptionInfo>.Failed failed)
            {
                facts.Add(new(dimension, name, null, effective,
                    BuildContextFactStatus.Failed, failed.Error.Reason));
                return;
            }

            var values = OptionValues(name);
            string? recorded = values.Length == 0 ? null : string.Join(" | ", values);
            string? normalized = values.Length == 1 ? normalize(values[0]) : null;
            bool comparable = normalized is not null && effective is not null;
            facts.Add(new(dimension, name, recorded, effective,
                !comparable ? BuildContextFactStatus.Unknown
                    : string.Equals(normalized, effective, StringComparison.Ordinal)
                        ? BuildContextFactStatus.Agree : BuildContextFactStatus.Different,
                comparable ? "Comparison covers this recorded setting only."
                    : values.Length > 1 ? "Multiple recorded values; no value was selected."
                    : effective is null ? "No effective compilation setting is retained."
                    : recorded is null ? "Original setting is not recorded."
                    : "Recorded value is unsupported; the effective setting is shown, not assumed applied."));
        }
    }

    void AddReferences(
        ImmutableArray<BuildContextFact>.Builder facts,
        RebuildCompilationAttempt? attempt)
    {
        if (References.Value is FindingInspection<CompilationReferenceInfo>.Failed failed)
        {
            facts.Add(new("references", "inventory", null, null,
                BuildContextFactStatus.Failed, failed.Error.Reason));
            return;
        }
        if (References.Value is not FindingInspection<CompilationReferenceInfo>.Complete complete
            || complete.Findings.IsEmpty)
        {
            facts.Add(new("references", "inventory", null,
                attempt is null ? null : $"{attempt.Provenance.References.Length} retained references",
                BuildContextFactStatus.Unknown, "Original reference inventory is unavailable."));
            return;
        }
        if (attempt is null)
        {
            facts.Add(new("references", "inventory", $"{complete.Findings.Length} recorded references", null,
                BuildContextFactStatus.Unknown, "No effective reference inventory is retained."));
            return;
        }

        var consumed = new HashSet<int>();
        foreach (var recorded in complete.Findings.Select(finding => finding.Payload))
        {
            string name = FileName(recorded.Name);
            var candidates = attempt.Provenance.References
                .Where(reference => string.Equals(FileName(reference.Display), name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var exact = candidates.Where(reference =>
                recorded.ModuleVersionId != Guid.Empty
                && reference.ModuleVersionId == recorded.ModuleVersionId
                && NormalizeNames(reference.Aliases) == NormalizeNames(recorded.Aliases.Split(','))
                && reference.EmbedInteropTypes == recorded.EmbedInteropTypes
                && attempt.ReferenceKinds[reference.Ordinal].ToString() == recorded.ImageKind.ToString()).ToArray();
            var effective = exact.Length == 1 ? exact[0] : candidates.Length == 1 ? candidates[0] : null;
            if (effective is null)
            {
                facts.Add(new("references", name, Describe(recorded), null,
                    candidates.Length == 0
                        ? BuildContextFactStatus.Different : BuildContextFactStatus.Unknown,
                    candidates.Length == 0 ? "Reference is absent from the effective closure."
                        : "Reference selection is ambiguous; filename equality is not identity."));
                continue;
            }

            consumed.Add(effective.Ordinal);
            bool known = recorded.ModuleVersionId != Guid.Empty
                && effective.ModuleVersionId is { } mvid && mvid != Guid.Empty;
            bool equal = exact.Length == 1;
            facts.Add(new("references", name, Describe(recorded),
                $"MVID={effective.ModuleVersionId}; aliases={NormalizeNames(effective.Aliases)}; "
                    + $"kind={attempt.ReferenceKinds[effective.Ordinal]}; embed={effective.EmbedInteropTypes}",
                !known ? BuildContextFactStatus.Unknown
                    : equal ? BuildContextFactStatus.Agree : BuildContextFactStatus.Different,
                known ? "Compared MVID, aliases, image kind, and embed-interop; not original reference-byte equality."
                    : "MVID unavailable; filename and reference properties cannot establish identity."));
            if (recorded.AdditionalFlags != 0)
            {
                facts.Add(new("references", $"{name} flags", recorded.AdditionalFlags.ToString(), null,
                    BuildContextFactStatus.Unknown, "Additional PDB reference flags are not interpreted."));
            }
        }

        foreach (var reference in attempt.Provenance.References.Where(reference => !consumed.Contains(reference.Ordinal)))
        {
            facts.Add(new("references", FileName(reference.Display), null,
                $"MVID={reference.ModuleVersionId}; aliases={NormalizeNames(reference.Aliases)}",
                BuildContextFactStatus.Unknown,
                "Effective reference has no unambiguous recorded counterpart."));
        }
    }

    static string Describe(CompilationReferenceInfo reference)
        => $"MVID={reference.ModuleVersionId}; aliases={NormalizeNames(reference.Aliases.Split(','))}; "
            + $"kind={reference.ImageKind}; embed={reference.EmbedInteropTypes}";

    static string FileName(string value) => Path.GetFileName(value.Replace('\\', '/'));

    static string NormalizeNames(IEnumerable<string> values)
        => string.Join(",", values.Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    static string? NormalizeBoolean(string value)
        => bool.TryParse(value, out bool parsed) ? parsed.ToString() : null;

    static string? NormalizeLanguage(string value)
        => LanguageVersionFacts.TryParse(value, out var parsed)
            ? new CSharpParseOptions(parsed).LanguageVersion.ToDisplayString() : null;
}
