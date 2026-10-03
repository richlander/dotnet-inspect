using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;

if (!Options.TryParse(args, out Options? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

List<Asset> assets = [];
foreach (string path in options!.Paths)
    assets.Add(Asset.Open(path));

foreach (Asset asset in assets)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"# asset: {asset.Name}, methods={asset.MethodCount},"
            + $" calls={asset.CallCount},"
            + $" caller-roots={asset.CallerRoots.Length},"
            + $" callee-roots={asset.CalleeRoots.Length}"));
    Console.WriteLine(
        $"CHECK\t{asset.Name}\t{asset.Fingerprint()}");
}

if (options.Command == Command.Check)
    return 0;

using TextWriter output = options.TsvPath is null
    ? Console.Out
    : File.CreateText(options.TsvPath);
output.WriteLine(
    "asset\toperation\tmedian_us\tp95_us\t"
    + "allocated_median_b\tallocated_p95_b\t"
    + "iterations\tqueries");
foreach (Asset asset in assets)
{
    foreach (Measurement measurement
        in asset.Measure(options.Iterations))
    {
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{asset.Name}\t{measurement.Operation}\t"
                + $"{measurement.MedianMicroseconds:F3}\t"
                + $"{measurement.P95Microseconds:F3}\t"
                + $"{measurement.AllocatedMedianBytes}\t"
                + $"{measurement.AllocatedP95Bytes}\t"
                + $"{options.Iterations}\t"
                + $"{measurement.QueryCount}"));
    }
}
return 0;

static class ScorecardConstants
{
    internal const int DefaultIterations = 20;
    internal const int RootLimit = 32;
    internal const int TreeDepth = 2;
    internal const int TreeNodeLimit = 200;
}

enum Command
{
    Check,
    Time,
}

sealed record Options(
    Command Command,
    int Iterations,
    string? TsvPath,
    ImmutableArray<string> Paths)
{
    internal static bool TryParse(
        string[] args,
        out Options? options,
        out string? error)
    {
        options = null;
        error = null;
        if (args.Length == 0)
        {
            error =
                "usage: check|time [--iterations N] [--tsv path] <assembly>...";
            return false;
        }

        Command command = args[0] switch
        {
            "check" => Command.Check,
            "time" => Command.Time,
            _ => (Command)(-1),
        };
        if (!Enum.IsDefined(command))
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        int iterations = ScorecardConstants.DefaultIterations;
        string? tsvPath = null;
        var paths = ImmutableArray.CreateBuilder<string>();
        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--iterations"
                    when index + 1 < args.Length
                    && int.TryParse(
                        args[++index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int parsed)
                    && parsed > 0:
                    iterations = parsed;
                    break;
                case "--tsv" when index + 1 < args.Length:
                    tsvPath = args[++index];
                    break;
                default:
                    if (args[index].StartsWith(
                            "--",
                            StringComparison.Ordinal))
                    {
                        error = $"Unknown option '{args[index]}'.";
                        return false;
                    }
                    paths.Add(args[index]);
                    break;
            }
        }

        if (paths.Count == 0)
        {
            error = "At least one assembly path is required.";
            return false;
        }
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                error = $"Assembly '{path}' does not exist.";
                return false;
            }
        }

        options = new(command, iterations, tsvPath, paths.ToImmutable());
        return true;
    }
}

sealed class Asset
{
    readonly LibraryBodyAnalysisExecution _analysis;
    readonly ResolvedAssemblyReference _assembly;
    readonly IAssemblyBindingPolicy _policy;

    Asset(
        string path,
        LibraryBodyAnalysisExecution analysis,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy policy,
        ImmutableArray<int> callerRoots,
        ImmutableArray<int> calleeRoots)
    {
        Name = Path.GetFileNameWithoutExtension(path);
        _analysis = analysis;
        _assembly = assembly;
        _policy = policy;
        CallerRoots = callerRoots;
        CalleeRoots = calleeRoots;
    }

    internal string Name { get; }
    internal int MethodCount => _analysis.CallGraph.DeclaredMethods.Length;
    internal int CallCount => _analysis.CallGraph.DirectCalls.Length;
    internal ImmutableArray<int> CallerRoots { get; }
    internal ImmutableArray<int> CalleeRoots { get; }

    internal static Asset Open(string path)
    {
        string fullPath = Path.GetFullPath(path);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                fullPath,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        try
        {
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    fullPath,
                    AssemblyResolutionProvenance.Local(
                        "catalog call-graph scorecard"));
            var policy = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(fullPath)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
            ImmutableArray<int> declaredTokens =
            [
                .. analysis.CallGraph.DeclaredMethods
                    .Select(static method => method.MetadataToken),
            ];
            HashSet<int> declared = [.. declaredTokens];
            ImmutableArray<int> calleeRoots =
            [
                .. analysis.CallGraph.DirectCalls
                    .Select(static call => call.Caller.MetadataToken)
                    .Where(declared.Contains)
                    .Distinct()
                    .Order()
                    .Take(ScorecardConstants.RootLimit),
            ];
            ImmutableArray<int> callerRoots =
            [
                .. analysis.CallGraph.DirectCalls
                    .Select(analysis.CallGraph.ResolveTarget)
                    .OfType<DirectCallTarget.CurrentModule>()
                    .Select(static target =>
                        target.Method.MetadataToken)
                    .Distinct()
                    .Order()
                    .Take(ScorecardConstants.RootLimit),
            ];
            if (calleeRoots.IsEmpty)
            {
                calleeRoots =
                [
                    .. declaredTokens.Take(
                        ScorecardConstants.RootLimit),
                ];
            }
            if (callerRoots.IsEmpty)
            {
                callerRoots =
                [
                    .. declaredTokens.Take(
                        ScorecardConstants.RootLimit),
                ];
            }
            return new(
                fullPath,
                analysis,
                assembly,
                policy,
                callerRoots,
                calleeRoots);
        }
        catch
        {
            throw;
        }
    }

    internal string Fingerprint()
    {
        using CatalogCallGraphScope scope = CreateScope();
        CatalogCallCensus census = scope.Census();
        var text = new StringBuilder();
        text.Append("nodes=")
            .Append(scope.StorageNodeCount)
            .Append(";edges=")
            .Append(scope.StorageEdgeCount)
            .Append(';');
        AppendCensus(text, census);
        foreach (int token in CallerRoots)
            AppendTree(text, 'R', token, scope.BuildCallerTree(
                _analysis.CallGraph,
                token,
                ScorecardConstants.TreeDepth,
                ScorecardConstants.TreeNodeLimit));
        foreach (int token in CalleeRoots)
            AppendTree(text, 'F', token, scope.BuildCallTree(
                _analysis.CallGraph,
                token,
                ScorecardConstants.TreeDepth,
                ScorecardConstants.TreeNodeLimit));
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal IEnumerable<Measurement> Measure(int iterations)
    {
        _ = MeasureBuild(1);
        yield return MeasureBuild(iterations);

        using (CatalogCallGraphScope scope = CreateScope())
        {
            _ = scope.StorageEdgeCount;
            _ = MeasureTrees(scope, CallerRoots, callers: true, 1);
            yield return MeasureTrees(
                scope,
                CallerRoots,
                callers: true,
                iterations);
        }
        using (CatalogCallGraphScope scope = CreateScope())
        {
            _ = scope.StorageEdgeCount;
            _ = MeasureTrees(scope, CalleeRoots, callers: false, 1);
            yield return MeasureTrees(
                scope,
                CalleeRoots,
                callers: false,
                iterations);
        }
        using (CatalogCallGraphScope scope = CreateScope())
        {
            _ = scope.StorageEdgeCount;
            _ = MeasureCensus(scope, 1);
            yield return MeasureCensus(scope, iterations);
        }
    }

    Measurement MeasureBuild(int iterations) =>
        Measurement.Run(
            "Build",
            iterations,
            queryCount: 1,
            () =>
            {
                using CatalogCallGraphScope scope = CreateScope();
                return scope.StorageNodeCount + scope.StorageEdgeCount;
            });

    Measurement MeasureTrees(
        CatalogCallGraphScope scope,
        ImmutableArray<int> roots,
        bool callers,
        int iterations) =>
        Measurement.Run(
            callers ? "Callers" : "Callees",
            iterations,
            roots.Length,
            () =>
            {
                int checksum = 0;
                foreach (int token in roots)
                {
                    CallTreeNode tree = callers
                        ? scope.BuildCallerTree(
                            _analysis.CallGraph,
                            token,
                            ScorecardConstants.TreeDepth,
                            ScorecardConstants.TreeNodeLimit)
                        : scope.BuildCallTree(
                            _analysis.CallGraph,
                            token,
                            ScorecardConstants.TreeDepth,
                            ScorecardConstants.TreeNodeLimit);
                    checksum += tree.Children.Length;
                    checksum += tree.Perf?.MaxDepth ?? 0;
                }
                return checksum;
            });

    static Measurement MeasureCensus(
        CatalogCallGraphScope scope,
        int iterations) =>
        Measurement.Run(
            "Census",
            iterations,
            queryCount: 1,
            () =>
            {
                CatalogCallCensus census = scope.Census();
                return census.Population.Length
                    + census.Occurrences.Length;
            });

    CatalogCallGraphScope CreateScope() =>
        new(
            _policy,
            [new CatalogCallGraphParticipant(
                _analysis.CallGraph,
                _assembly)]);

    static void AppendTree(
        StringBuilder text,
        char direction,
        int token,
        CallTreeNode root)
    {
        text.Append('|').Append(direction).Append(':').Append(token);
        AppendNode(text, root);
    }

    static void AppendNode(StringBuilder text, CallTreeNode node)
    {
        text.Append('[')
            .Append(node.Member.ToQualifiedDisplayString())
            .Append(';')
            .Append(node.Kind)
            .Append(';')
            .Append(node.Status)
            .Append(';')
            .Append(node.Perf?.Fanout ?? 0)
            .Append(';')
            .Append(node.Perf?.Fanin ?? 0)
            .Append(';')
            .Append(node.Perf?.MaxDepth ?? 0)
            .Append(';')
            .Append(node.Perf?.InLoop ?? false)
            .Append(';')
            .Append(node.HasUnresolvedDispatch)
            .Append(';')
            .Append(node.Children.Length)
            .Append(']');
        AppendAssembly(text, node.DefinitionAssemblyIdentity);
        AppendAssembly(text, node.ResolutionAssemblyIdentity);
        AppendEvidence(text, node.GraphEvidence);
        AppendStorage(text, node.ParentEdgeCallerDefinition);
        AppendDiagnostic(text, node.Diagnostic);
        text.Append("{sites=")
            .Append(node.ParentEdgeCallSites.Length)
            .Append('}');
        for (int index = 0;
            index < node.ParentEdgeCallSites.Length;
            index++)
        {
            AppendCall(text, node.ParentEdgeCallSites[index]);
            AppendEvidence(
                text,
                index < node.ParentEdgeCallSiteEvidence.Length
                    ? node.ParentEdgeCallSiteEvidence[index]
                    : null);
        }
        foreach (CallTreeNode child in node.Children)
            AppendNode(text, child);
    }

    static void AppendCensus(
        StringBuilder text,
        CatalogCallCensus census)
    {
        CatalogCallCensusReceipt receipt = census.Receipt;
        text.Append("receipt=")
            .Append(receipt.ParticipantCount).Append(',')
            .Append(receipt.MemberCount).Append(',')
            .Append(receipt.OccurrenceCount).Append(',')
            .Append(receipt.UnresolvedOccurrenceCount).Append(',')
            .Append(receipt.VersionSkewedBindingCount)
            .Append(";complete=")
            .Append(census.IsComplete);

        text.Append(";population=")
            .Append(census.Population.Length);
        foreach (CatalogCallGraphParticipant participant
            in census.Population)
        {
            AppendAssembly(text, participant.Assembly.Identity);
        }

        text.Append(";members=")
            .Append(census.Members.Length);
        foreach (CatalogCallCensusMember member in census.Members)
        {
            text.Append("|M");
            AppendMethodKey(text, member.OrderingKey);
            text.Append(member.HasBody);
            AppendEvidence(text, member.Evidence);
            AppendDiagnostic(text, member.Diagnostic);
        }

        text.Append(";occurrences=")
            .Append(census.Occurrences.Length);
        foreach (CatalogCallCensusOccurrence occurrence
            in census.Occurrences)
        {
            text.Append("|O");
            AppendMethodKey(text, occurrence.SourceOrderingKey);
            AppendMethodKey(text, occurrence.TargetOrderingKey);
            AppendOccurrenceKey(text, occurrence.OrderingKey);
            AppendCall(text, occurrence.Call);
            AppendEvidence(text, occurrence.CallSiteEvidence);
        }

        text.Append(";unresolved=")
            .Append(census.UnresolvedOccurrences.Length);
        foreach (CatalogCallCensusUnresolvedOccurrence occurrence
            in census.UnresolvedOccurrences)
        {
            text.Append("|U");
            AppendMethodKey(text, occurrence.SourceOrderingKey);
            AppendOccurrenceKey(text, occurrence.OrderingKey);
            AppendCall(text, occurrence.Call);
            AppendEvidence(text, occurrence.CallSiteEvidence);
        }

        text.Append(";version-skew=")
            .Append(census.VersionSkewedBindings.Length);
        foreach (CatalogCallCensusVersionSkewEvidence evidence
            in census.VersionSkewedBindings)
        {
            text.Append("|V");
            AppendEvidence(text, evidence.CallSite);
            AppendAssembly(text, evidence.Requested);
            AppendAssembly(text, evidence.Selected);
            text.Append(evidence.AdmittedAlternatives.Length);
            foreach (AssemblyReferenceIdentity alternative
                in evidence.AdmittedAlternatives)
            {
                AppendAssembly(text, alternative);
            }
        }

        AppendDiagnostics(text, census.Diagnostics);
        text.Append(";incomplete-nodes=")
            .Append(census.IncompleteNodes.Length);
        foreach (GraphNodeEvidence evidence in census.IncompleteNodes)
        {
            AppendEvidence(text, evidence);
        }
        text.Append(";incomplete-edges=")
            .Append(census.IncompleteEdges.Length);
        foreach (GraphEdgeEvidence edge in census.IncompleteEdges)
        {
            text.Append("|E");
            AppendEvidence(text, edge.Caller);
            AppendEvidence(text, edge.Callee);
            text.Append(edge.Kind)
                .Append(',')
                .Append(edge.InLoop);
        }
    }

    static void AppendDiagnostics(
        StringBuilder text,
        CatalogCallCensusDiagnostics diagnostics)
    {
        text.Append(";diagnostics=")
            .Append(diagnostics.Graph.IncompleteNodeCount)
            .Append(',')
            .Append(diagnostics.Graph.IncompleteEdgeCount)
            .Append(',')
            .Append(diagnostics.Graph.BindingIdentityConflictCount)
            .Append(',')
            .Append(diagnostics.UnresolvedOccurrenceCount)
            .Append(',')
            .Append(diagnostics.VersionSkewedBindingCount);
    }

    static void AppendCall(StringBuilder text, DirectCall call)
    {
        text.Append("|C")
            .Append(call.Caller.ModuleVersionId)
            .Append(',')
            .Append(call.Caller.MetadataToken)
            .Append(',')
            .Append(call.EvidenceMethod.ModuleVersionId)
            .Append(',')
            .Append(call.EvidenceMethod.MetadataToken)
            .Append(',')
            .Append(call.ILOffset)
            .Append(',')
            .Append(call.OperandToken)
            .Append(',')
            .Append(call.CalleeDefinitionToken)
            .Append(',')
            .Append(call.Kind)
            .Append(',')
            .Append(call.InLoop)
            .Append(',');
        AppendString(text, call.Opcode);
        AppendString(text, call.Callee.ToQualifiedDisplayString());
    }

    static void AppendEvidence(
        StringBuilder text,
        GraphNodeEvidence? evidence)
    {
        if (evidence is null)
        {
            text.Append("|N");
            return;
        }

        text.Append("|G").Append(evidence.Kind);
        AppendStorage(text, evidence.Storage);
        AppendStorage(text, evidence.DefinitionStorage);
    }

    static void AppendStorage(
        StringBuilder text,
        GraphNodeStorageKey? storage)
    {
        if (storage is null)
        {
            text.Append("|S-");
            return;
        }

        text.Append("|S")
            .Append(storage.ModuleVersionId)
            .Append(',')
            .Append(storage.Kind)
            .Append(',')
            .Append(storage.MethodToken)
            .Append(',')
            .Append(storage.ILOffset)
            .Append(',')
            .Append(storage.OperandToken);
    }

    static void AppendMethodKey(
        StringBuilder text,
        CatalogCallCensusMethodOrderingKey key)
    {
        text.Append("|K");
        AppendAssembly(text, key.Assembly);
        text.Append(key.ModuleVersionId)
            .Append(',')
            .Append(key.MetadataToken);
    }

    static void AppendOccurrenceKey(
        StringBuilder text,
        CatalogCallCensusOccurrenceOrderingKey key)
    {
        text.Append("|Q");
        AppendMethodKey(text, key.EvidenceMethod);
        text.Append(key.ILOffset)
            .Append(',')
            .Append(key.OperandToken)
            .Append(',')
            .Append(key.Kind);
    }

    static void AppendAssembly(
        StringBuilder text,
        AssemblyReferenceIdentity? assembly)
    {
        if (assembly is null)
        {
            text.Append("|A-");
            return;
        }

        text.Append("|A");
        AppendString(text, assembly.Name);
        AppendString(text, assembly.Version?.ToString());
        AppendString(text, assembly.Culture);
        AppendString(text, assembly.PublicKeyToken);
    }

    static void AppendDiagnostic(
        StringBuilder text,
        AnalysisDiagnostic? diagnostic)
    {
        if (diagnostic is null)
        {
            text.Append("|D-");
            return;
        }

        text.Append("|D")
            .Append(diagnostic.MethodToken)
            .Append(',')
            .Append(diagnostic.SourceMethodToken);
        AppendString(text, diagnostic.Method);
        AppendString(text, diagnostic.Message);
    }

    static void AppendString(StringBuilder text, string? value)
    {
        text.Append(value?.Length ?? -1)
            .Append(':')
            .Append(value)
            .Append(';');
    }

}

sealed record Measurement(
    string Operation,
    double MedianMicroseconds,
    double P95Microseconds,
    long AllocatedMedianBytes,
    long AllocatedP95Bytes,
    int QueryCount)
{
    static int s_checksum;

    internal static Measurement Run(
        string operation,
        int iterations,
        int queryCount,
        Func<int> action)
    {
        var elapsed = new double[iterations];
        var allocated = new long[iterations];
        for (int index = 0; index < iterations; index++)
        {
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            int checksum = action();
            elapsed[index] =
                Stopwatch.GetElapsedTime(started).TotalMicroseconds;
            allocated[index] =
                GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;
            s_checksum ^= checksum;
        }
        Array.Sort(elapsed);
        Array.Sort(allocated);
        return new(
            operation,
            Median(elapsed),
            P95(elapsed),
            Median(allocated),
            P95(allocated),
            queryCount);
    }

    static double Median(double[] values) =>
        values.Length % 2 == 0
            ? (values[(values.Length / 2) - 1]
                + values[values.Length / 2]) / 2
            : values[values.Length / 2];

    static long Median(long[] values) =>
        values.Length % 2 == 0
            ? (values[(values.Length / 2) - 1]
                + values[values.Length / 2]) / 2
            : values[values.Length / 2];

    static double P95(double[] values) =>
        values[(int)Math.Ceiling(values.Length * 0.95) - 1];

    static long P95(long[] values) =>
        values[(int)Math.Ceiling(values.Length * 0.95) - 1];
}
