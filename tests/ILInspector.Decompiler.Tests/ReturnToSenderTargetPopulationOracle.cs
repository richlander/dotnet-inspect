using System.Reflection.Metadata;

using DotnetInspector.PerformanceOracles;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using NLinq;

namespace ILInspector.DecompilerHarness;

static partial class FidelityCheck
{
    internal static ReturnToSenderTargetSelection
        QueryReturnToSenderTargetPopulationWithNLinq(
        IReadOnlyList<string> assemblies,
        string? typeFilter = null,
        CSharpLanguageProfile? languageProfile = null)
    {
        ReturnToSenderCandidatePopulation population =
            ReadReturnToSenderCandidatePopulation(
                assemblies,
                typeFilter,
                languageProfile);
        ReturnToSenderCandidateRow[] rows = population.Rows;

        var eligible = rows
            .AsNLinq()
            .Where<
                ArrayEnumerator<ReturnToSenderCandidateRow>,
                ReturnToSenderCandidateRow,
                IsEligible>(default);
        int eligibleCount = eligible.CountFold<
            Filter<
                ReturnToSenderCandidateRow,
                ArrayEnumerator<ReturnToSenderCandidateRow>,
                IsEligible>,
            ReturnToSenderCandidateRow>();

        var eligibleRows = rows
            .AsNLinq()
            .Where<
                ArrayEnumerator<ReturnToSenderCandidateRow>,
                ReturnToSenderCandidateRow,
                IsEligible>(default)
            .Select<
                Filter<
                    ReturnToSenderCandidateRow,
                    ArrayEnumerator<ReturnToSenderCandidateRow>,
                    IsEligible>,
                ReturnToSenderCandidateRow,
                CompileBackTarget,
                ToTarget>(default);
        List<CompileBackTarget> targets = eligibleRows.ToList<
            Map<
                ReturnToSenderCandidateRow,
                CompileBackTarget,
                Filter<
                    ReturnToSenderCandidateRow,
                    ArrayEnumerator<ReturnToSenderCandidateRow>,
                    IsEligible>,
                ToTarget>,
            CompileBackTarget>();

        var excludedRows = rows
            .AsNLinq()
            .Where<
                ArrayEnumerator<ReturnToSenderCandidateRow>,
                ReturnToSenderCandidateRow,
                IsExcluded>(default)
            .Select<
                Filter<
                    ReturnToSenderCandidateRow,
                    ArrayEnumerator<ReturnToSenderCandidateRow>,
                    IsExcluded>,
                ReturnToSenderCandidateRow,
                ReturnToSenderTargetExclusion,
                ToExclusion>(default);
        ListEnumerator<ReturnToSenderTargetExclusion> orderedExclusions =
            OracleOperators.OrderBy<
                Map<
                    ReturnToSenderCandidateRow,
                    ReturnToSenderTargetExclusion,
                    Filter<
                        ReturnToSenderCandidateRow,
                        ArrayEnumerator<ReturnToSenderCandidateRow>,
                        IsExcluded>,
                    ToExclusion>,
                ReturnToSenderTargetExclusion>(
                excludedRows,
                ReturnToSenderTargetExclusionComparer.Instance);
        List<ReturnToSenderTargetExclusion> exclusions =
            orderedExclusions.ToList<
                ListEnumerator<ReturnToSenderTargetExclusion>,
                ReturnToSenderTargetExclusion>();

        return new(
            targets,
            exclusions,
            population.ScannedBodyCount,
            rows.Length,
            eligibleCount);
    }

    static ReturnToSenderCandidatePopulation
        ReadReturnToSenderCandidatePopulation(
        IReadOnlyList<string> assemblies,
        string? typeFilter,
        CSharpLanguageProfile? languageProfile)
    {
        var rows = new List<ReturnToSenderCandidateRow>();
        int scannedBodyCount = 0;
        CSharpLanguageProfile profile =
            languageProfile
            ?? new(CSharpLanguageVersion.Preview);
        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (string assemblyPath in assemblies)
        {
            using var source =
                MetadataSource.Open(assemblyPath, context: metadata);
            RegisterSourceContext(source, metadata);
            MetadataReader reader = source.Reader;
            TargetApiEvidence targetApiEvidence =
                CreateTargetApiEvidence(
                    source.Pe,
                    includeCompilerGenerated: true);
            using var declarations =
                new ReturnToSenderDeclarationSession(assemblyPath);
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                string typeName = reader.GetFullTypeName(type);
                var overloads = new Dictionary<string, int>();
                foreach (MethodDefinitionHandle methodHandle
                    in type.GetMethods())
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(methodHandle);
                    string methodName =
                        reader.GetString(method.Name);
                    int overload =
                        overloads.GetValueOrDefault(methodName);
                    overloads[methodName] = overload + 1;
                    if (method.RelativeVirtualAddress == 0)
                        continue;

                    scannedBodyCount++;
                    var candidate =
                        new IrImporter.StableSampleCandidate(
                            typeName,
                            methodName,
                            overload,
                            typeHandle,
                            methodHandle);
                    ReturnToSenderCandidateDecision? decision =
                        DecideStandaloneReturnToSenderCandidate(
                            assemblyPath,
                            reader,
                            candidate,
                            typeFilter,
                            targetApiEvidence,
                            declarations,
                            profile,
                            out bool declarationCandidate);
                    if (!declarationCandidate)
                        continue;
                    if (decision is null)
                    {
                        throw new InvalidOperationException(
                            "An RTS declaration candidate did not produce "
                            + "a target decision.");
                    }
                    if ((decision.Target is null)
                        == (decision.Exclusion is null))
                    {
                        throw new InvalidOperationException(
                            "An RTS target decision must contain exactly "
                            + "one target or exclusion.");
                    }

                    rows.Add(
                        new(
                            decision.Target,
                            decision.Exclusion));
                }
            }
        }

        return new([.. rows], scannedBodyCount);
    }

    readonly record struct ReturnToSenderCandidatePopulation(
        ReturnToSenderCandidateRow[] Rows,
        int ScannedBodyCount);

    readonly record struct ReturnToSenderCandidateRow(
        CompileBackTarget? Target,
        ReturnToSenderTargetExclusion? Exclusion);

    readonly struct IsEligible :
        IFunc<ReturnToSenderCandidateRow, bool>
    {
        public bool Invoke(ReturnToSenderCandidateRow row) =>
            row.Target is not null;
    }

    readonly struct IsExcluded :
        IFunc<ReturnToSenderCandidateRow, bool>
    {
        public bool Invoke(ReturnToSenderCandidateRow row) =>
            row.Exclusion is not null;
    }

    readonly struct ToTarget :
        IFunc<ReturnToSenderCandidateRow, CompileBackTarget>
    {
        public CompileBackTarget Invoke(
            ReturnToSenderCandidateRow row) =>
            row.Target
            ?? throw new InvalidOperationException(
                "An eligible RTS candidate lost its target.");
    }

    readonly struct ToExclusion :
        IFunc<
            ReturnToSenderCandidateRow,
            ReturnToSenderTargetExclusion>
    {
        public ReturnToSenderTargetExclusion Invoke(
            ReturnToSenderCandidateRow row) =>
            row.Exclusion
            ?? throw new InvalidOperationException(
                "An excluded RTS candidate lost its exclusion.");
    }

    sealed class ReturnToSenderTargetExclusionComparer :
        IComparer<ReturnToSenderTargetExclusion>
    {
        internal static ReturnToSenderTargetExclusionComparer Instance
            { get; } = new();

        public int Compare(
            ReturnToSenderTargetExclusion? left,
            ReturnToSenderTargetExclusion? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            int comparison = StringComparer.Ordinal.Compare(
                left.AssemblyPath,
                right.AssemblyPath);
            if (comparison != 0)
                return comparison;
            comparison = StringComparer.Ordinal.Compare(
                left.Type,
                right.Type);
            if (comparison != 0)
                return comparison;
            comparison = StringComparer.Ordinal.Compare(
                left.Method,
                right.Method);
            if (comparison != 0)
                return comparison;
            comparison = StringComparer.Ordinal.Compare(
                left.Signature,
                right.Signature);
            if (comparison != 0)
                return comparison;
            comparison = left.Reason.CompareTo(right.Reason);
            return comparison != 0
                ? comparison
                : left.Overload.CompareTo(right.Overload);
        }
    }
}
