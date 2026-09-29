using InertText;

namespace ILInspector.Metadata;

public enum PdbSourceProvenancePathProfileVersion
{
    RoslynSourceGeneratorPathV1,
}

public readonly record struct PdbSourcePathSpan(int Offset, int Length)
{
    internal string SliceRaw(string path) => path.Substring(Offset, Length);
}

public sealed record PdbGeneratedPathEvidence(
    PdbSourceProvenancePathProfileVersion Profile,
    InertString GeneratorAssembly,
    InertString GeneratorType,
    InertString HintName,
    PdbSourcePathSpan GeneratorAssemblySpan,
    PdbSourcePathSpan GeneratorTypeSpan,
    PdbSourcePathSpan HintNameSpan);

public enum PdbGeneratedPathUnknownReason
{
    NoEligibleDecomposition,
    AmbiguousDecomposition,
    EmptySegment,
    DotSegment,
    CharacterLimitExceeded,
    SegmentLimitExceeded,
}

public abstract record PdbGeneratedPathClassification
{
    private protected PdbGeneratedPathClassification()
    {
    }

    public sealed record Generated(PdbGeneratedPathEvidence Evidence) :
        PdbGeneratedPathClassification;

    public sealed record Unknown(PdbGeneratedPathUnknownReason Reason) :
        PdbGeneratedPathClassification;
}

public sealed record PdbSourcePathClassificationLimits
{
    public PdbSourcePathClassificationLimits(
        int maxCharacters = 32 * 1024,
        int maxSegments = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSegments);
        MaxCharacters = maxCharacters;
        MaxSegments = maxSegments;
    }

    public int MaxCharacters { get; }
    public int MaxSegments { get; }
}

public static class PdbSourceProvenancePathClassifier
{
    public static PdbGeneratedPathClassification ClassifyEmbeddedDocument(
        string path,
        PdbSourceProvenancePathProfileVersion profile =
            PdbSourceProvenancePathProfileVersion.RoslynSourceGeneratorPathV1,
        PdbSourcePathClassificationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        limits ??= new();

        if (path.Length > limits.MaxCharacters)
        {
            return new PdbGeneratedPathClassification.Unknown(
                PdbGeneratedPathUnknownReason.CharacterLimitExceeded);
        }

        List<PathSegment> segments = [];
        int start = 0;
        if (path.Length > 0 && IsSeparator(path[0]))
            start = 1;

        for (int index = start; index <= path.Length; index++)
        {
            if (index != path.Length && !IsSeparator(path[index]))
                continue;

            if (index == start)
            {
                return new PdbGeneratedPathClassification.Unknown(
                    PdbGeneratedPathUnknownReason.EmptySegment);
            }

            var segment = new PathSegment(start, index - start);
            if (segment.Is(path, ".") || segment.Is(path, ".."))
            {
                return new PdbGeneratedPathClassification.Unknown(
                    PdbGeneratedPathUnknownReason.DotSegment);
            }

            segments.Add(segment);
            if (segments.Count > limits.MaxSegments)
            {
                return new PdbGeneratedPathClassification.Unknown(
                    PdbGeneratedPathUnknownReason.SegmentLimitExceeded);
            }

            start = index + 1;
        }

        if (profile
            != PdbSourceProvenancePathProfileVersion
                .RoslynSourceGeneratorPathV1)
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        Candidate? match = null;
        bool belowIntermediateOutput = false;
        for (int index = 0; index < segments.Count - 2; index++)
        {
            if (segments[index].Is(path, "obj")
                || (index > 0
                    && segments[index - 1].Is(path, "artifacts")
                    && segments[index].Is(path, "obj")))
            {
                belowIntermediateOutput = true;
                continue;
            }

            if (!belowIntermediateOutput
                || !TryCreateCandidate(path, segments, index, out Candidate candidate))
            {
                continue;
            }

            if (match is not null)
            {
                return new PdbGeneratedPathClassification.Unknown(
                    PdbGeneratedPathUnknownReason.AmbiguousDecomposition);
            }

            match = candidate;
        }

        if (match is not Candidate selected)
        {
            return new PdbGeneratedPathClassification.Unknown(
                PdbGeneratedPathUnknownReason.NoEligibleDecomposition);
        }

        PathSegment assembly = segments[selected.AssemblyIndex];
        PathSegment type = segments[selected.AssemblyIndex + 1];
        PathSegment hintStart = segments[selected.AssemblyIndex + 2];
        PathSegment hintEnd = segments[^1];
        int hintLength = hintEnd.Offset + hintEnd.Length - hintStart.Offset;
        return new PdbGeneratedPathClassification.Generated(
            new PdbGeneratedPathEvidence(
                profile,
                new InertString(
                    TextPolicy.Field,
                    assembly.SliceRaw(path)),
                new InertString(
                    TextPolicy.Field,
                    type.SliceRaw(path)),
                new InertString(
                    TextPolicy.Field,
                    path.Substring(hintStart.Offset, hintLength)),
                new(assembly.Offset, assembly.Length),
                new(type.Offset, type.Length),
                new(hintStart.Offset, hintLength)));
    }

    private static bool TryCreateCandidate(
        string path,
        IReadOnlyList<PathSegment> segments,
        int assemblyIndex,
        out Candidate candidate)
    {
        candidate = default;
        PathSegment assembly = segments[assemblyIndex];
        PathSegment type = segments[assemblyIndex + 1];
        if (assembly.Length == 0
            || type.Length <= assembly.Length + 1
            || !path.AsSpan(type.Offset, assembly.Length)
                .SequenceEqual(path.AsSpan(assembly.Offset, assembly.Length))
            || path[type.Offset + assembly.Length] != '.')
        {
            return false;
        }

        ReadOnlySpan<char> typeText =
            path.AsSpan(type.Offset, type.Length);
        int leafStart = Math.Max(
            typeText.LastIndexOf('.'),
            typeText.LastIndexOf('+')) + 1;
        if (!typeText[leafStart..].EndsWith(
                "Generator",
                StringComparison.Ordinal))
        {
            return false;
        }

        candidate = new(assemblyIndex);
        return true;
    }

    private static bool IsSeparator(char value) => value is '/' or '\\';

    private readonly record struct Candidate(int AssemblyIndex);

    private readonly record struct PathSegment(int Offset, int Length)
    {
        public bool Is(string path, string value) =>
            path.AsSpan(Offset, Length).SequenceEqual(value);

        public string SliceRaw(string path) =>
            path.Substring(Offset, Length);
    }
}
