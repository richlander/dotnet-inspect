using System;
using System.Collections.Generic;
using System.Linq;
using DotnetInspector.Output;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Services;

/// <summary>
/// Outcome of resolving the tool-owned <c>.dotnet-inspectconfig</c> at the CLI
/// edge: the <see cref="PrinterOptions"/> to render with, the file the options
/// came from (null when none was found), and any warnings raised while reading
/// or parsing it. Warnings surface to the user (never a silent success); a bad
/// key is skipped while the rest of the file still applies.
/// </summary>
internal sealed record RenderStyleResolution(
    PrinterOptions Options,
    string? Origin,
    IReadOnlyList<string> Warnings)
{
    /// <summary>No config file found: shipped defaults, no origin, no warnings.</summary>
    public static RenderStyleResolution None { get; } = new(PrinterOptions.Default, null, []);
}

/// <summary>
/// Discovers and parses the tool-owned <c>.dotnet-inspectconfig</c> style file and
/// maps it to decompiler <see cref="PrinterOptions"/>. This lives at the CLI edge
/// only: the decompiler library stays a pure function of explicit
/// <see cref="PrinterOptions"/>, so config resolution never leaks into it.
///
/// <para>The file is flat <c>key = value</c> using editorconfig key/value
/// vocabulary (so lines copy directly from a real <c>.editorconfig</c>);
/// <c>#</c>/<c>;</c> comment lines and <c>[section]</c> headers are ignored, and
/// an editorconfig <c>value:severity</c> suffix is tolerated (only the token
/// before <c>:</c> is read). The editorconfig <c>root</c> key is recognized (see
/// <see cref="Discover"/> for how it relates to the boundary). Unknown keys,
/// malformed lines, invalid boolean values, and unreadable files are reported as
/// warnings rather than failing the run.</para>
/// </summary>
internal static class RenderStyleConfig
{
    /// <summary>The tool-owned style file name discovered by walking up from the working directory.</summary>
    public const string FileName = ".dotnet-inspectconfig";

    // The recognized style keys and how they set PrinterOptions come from the
    // library-owned StyleOptionCatalog — the single source of truth — so this
    // resolver never drifts from the option surface. Every catalog VALUE with a
    // config key is honored here (the four byte-preserving this.-qualification
    // spellings, the oracle-endorsed conditional-expression lens value, and the
    // tool-owned branchless "bool hack" value); values with no config key — the
    // off/default value of a knob and any API-only knob — simply do not appear in
    // the file vocabulary. A key = true selects its value; key = false deselects
    // it (setting only that value's own backing state, so, exactly as before, two
    // members of a multi-value axis set independently and the printer resolves any
    // overlap deterministically).
    private static readonly IReadOnlyDictionary<string, StyleOptionValue> KnobsByKey =
        StyleOptionCatalog.Options
            .SelectMany(o => o.Values)
            .Where(v => v.ConfigKey is not null)
            .ToDictionary(v => v.ConfigKey!, StringComparer.Ordinal);

    // The editorconfig boundary marker. Discovery is nearest-wins, so the nearest
    // file is already a hard boundary (nothing above it is read); 'root' is
    // recognized so a file copied from a real .editorconfig does not warn, and it
    // is the conventional way to declare that boundary explicitly.
    private const string RootKey = "root";

    // The tool-owned aggregate key. It maps to no single catalog descriptor;
    // instead it enables (or, when false, disables) the whole oracle-endorsed
    // subset at once via StyleOptionCatalog.ApplyFullTaste. It uses the
    // tool-owned dotnet_inspect_style_* vocabulary because it is a convenience
    // aggregate with no editorconfig equivalent. Applied in file order like every
    // other key, so a later explicit per-knob line overrides it (last write wins).
    private const string FullTasteKey = "dotnet_inspect_style_full_taste";

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to the filesystem root and
    /// returns the path of the nearest <see cref="FileName"/>, or null if none is
    /// found. The nearest file wins; there is no cross-level merge, so the nearest
    /// file is a hard boundary — nothing above it is read. Placing a
    /// <see cref="FileName"/> at a repository root (optionally with
    /// <c>root = true</c>, editorconfig-style, to make the boundary explicit)
    /// therefore isolates every nested run from configs higher up the tree.
    /// </summary>
    public static string? Discover(string startDirectory)
    {
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(startDirectory);
        }
        catch
        {
            return null;
        }

        for (; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Discovers the nearest style file from <paramref name="startDirectory"/>,
    /// reads it, and parses it. Returns <see cref="RenderStyleResolution.None"/>
    /// when no file is found, so the caller renders with shipped defaults.
    /// </summary>
    public static RenderStyleResolution Resolve(string startDirectory)
    {
        var path = Discover(startDirectory);
        if (path is null)
            return RenderStyleResolution.None;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new RenderStyleResolution(
                PrinterOptions.Default,
                path,
                [$"could not read '{path}': {ex.Message}"]);
        }

        return Parse(text, path);
    }

    /// <summary>
    /// Parses flat <c>key = value</c> config <paramref name="text"/> into
    /// <see cref="PrinterOptions"/>. <paramref name="origin"/> is echoed onto the
    /// result for disclosure. Unknown keys, malformed lines, and invalid boolean
    /// values are collected as warnings; recognized keys still apply.
    /// </summary>
    public static RenderStyleResolution Parse(string text, string? origin)
    {
        var options = PrinterOptions.Default;
        List<string>? warnings = null;

        void Warn(string message) => (warnings ??= []).Add(message);

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;

            // editorconfig section headers ([*.cs]) carry no meaning in the flat
            // tool-owned model; skip them without warning so copied files parse.
            if (line[0] == '[' && line[^1] == ']')
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
            {
                Warn($"line {i + 1}: malformed entry '{line}' (expected 'key = value')");
                continue;
            }

            var key = line[..eq].Trim().ToLowerInvariant();
            if (key.Length == 0)
            {
                Warn($"line {i + 1}: malformed entry '{line}' (empty key)");
                continue;
            }

            // Tolerate the editorconfig 'value:severity' form; only the value token matters here.
            var rawValue = line[(eq + 1)..].Trim();
            int severity = rawValue.IndexOf(':');
            var value = (severity >= 0 ? rawValue[..severity] : rawValue).Trim();

            switch (key)
            {
                case RootKey:
                    // editorconfig boundary marker. Discovery is nearest-wins, so
                    // the nearest file is already a hard boundary; 'root' drives no
                    // knob but is recognized (not an "unknown key") and its value is
                    // still validated so a typo surfaces.
                    if (!TryParseBool(value, out _))
                        Warn($"line {i + 1}: key '{key}' expects true/false, got '{value}' (ignored)");
                    break;
                case FullTasteKey:
                    // The "full taste" aggregate: enable (or disable) the whole
                    // oracle-endorsed subset at once. Deterministic — the enabled
                    // subset shares no conflict group.
                    if (TryParseBool(value, out var fullTaste))
                        options = StyleOptionCatalog.ApplyFullTaste(options, fullTaste);
                    else
                        Warn($"line {i + 1}: key '{key}' expects true/false, got '{value}' (ignored)");
                    break;
                default:
                    if (KnobsByKey.TryGetValue(key, out var knob))
                    {
                        if (TryParseBool(value, out var on))
                            options = knob.SetSelected(options, on);
                        else
                            Warn($"line {i + 1}: key '{key}' expects true/false, got '{value}' (ignored)");
                    }
                    else
                    {
                        Warn($"line {i + 1}: unknown key '{key}' (ignored)");
                    }

                    break;
            }
        }

        return new RenderStyleResolution(options, origin, (IReadOnlyList<string>?)warnings ?? []);
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }
}

/// <summary>
/// Carries the <see cref="RenderStyleConfig"/> parse/read warnings from the CLI
/// edge to the point a decompiled-source render actually consumes the resolved
/// <see cref="PrinterOptions"/>, then emits them to stderr exactly once. A
/// reference-typed latch so a single emission survives the record <c>with</c>
/// copies that flow the options, and so warnings surface only on a run that truly
/// reads source (never on, say, a <c>--json</c> or <c>-S Facts</c> run that never
/// touches the config). Emitting at consumption keeps the warning honest: it fires
/// if and only if the config is read, without predicting output mode or verbosity.
/// </summary>
internal sealed class RenderConfigWarningSink
{
    private readonly IReadOnlyList<string> _warnings;
    private bool _emitted;

    public RenderConfigWarningSink(IReadOnlyList<string> warnings) => _warnings = warnings;

    /// <summary>Emits the pending warnings to stderr the first time it is called; a no-op thereafter.</summary>
    public void EmitOnce()
    {
        foreach (var message in TakePending())
            CommandError.WriteWarning(message);
    }

    /// <summary>
    /// Test seam: applies the latch and returns the messages that
    /// <see cref="EmitOnce"/> would write, without touching global console
    /// state.
    /// </summary>
    /// <remarks>
    /// The seam used to be an overload taking a <see cref="TextWriter"/>, which
    /// meant the warning was spelled -- prefix and all -- outside
    /// <c>CommandError</c>, and a config-supplied message reached stderr
    /// uncontained. Handing back the messages keeps the latch checkable while
    /// leaving exactly one place that writes them (issue #3319).
    /// </remarks>
    internal IReadOnlyList<string> TakePending()
    {
        if (_emitted || _warnings.Count == 0)
            return [];

        _emitted = true;
        return [.. _warnings.Select(warning => $"{RenderStyleConfig.FileName}: {warning}")];
    }
}
