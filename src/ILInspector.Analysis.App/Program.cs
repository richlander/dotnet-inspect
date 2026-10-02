using System.Reflection;

using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;

// `unsafe <assembly> [count]` — detect requires-unsafe methods under the new
// memory-safety model (CallerUnsafeMode) and rank them by direct callers.
if (args.Length > 0 && args[0] == "unsafe")
    return RunUnsafeReport(args);

var options = Parse(args);
if (options is null)
    return 1;

LibraryBodyAnalysisExecution execution =
    LibraryBodyAnalysisService.ExecutePath(
        options.AssemblyPath,
        LibraryBodyAnalysisRequest.Create(
            LibraryBodyAnalysisFeatures.Default));
LibraryCallGraphAnalysisResult callGraph = execution.CallGraph;
MemberPattern target =
    MemberPattern.Method(
        options.DeclaringType,
        options.MemberName);
AnalysisCallGraph graph = AnalysisCallGraph.Create(callGraph);
HashSet<AnalysisCallSubject> targetSubjects =
[
    .. graph.Document.Occurrences
        .Where(occurrence =>
            target.Matches(occurrence.Evidence.Callee))
        .Select(occurrence => occurrence.TargetSubject),
];
int[] targetNodeIds =
[
    .. graph.Document.Nodes
        .Where(node => targetSubjects.Contains(node.Subject))
        .Select(node => node.Id),
];
GraphAdjacencyResult adjacency =
    GraphDocumentExecution.Adjacency(
        graph.Document,
        new GraphNeighborPlan<AnalysisCallRelationship>(
            [AnalysisCallRelationship.Calls],
            GraphTraversalDirection.Incoming,
            GraphSelfLoopPolicy.Include));
DirectCall[] matches =
[
    .. targetNodeIds
        .SelectMany(nodeId => adjacency.Rows[nodeId].EdgeIds)
        .Distinct()
        .Order()
        .SelectMany(edgeId =>
            graph.Document.Edges[edgeId].OccurrenceIds)
        .Select(occurrenceId =>
            graph.Document.Occurrences[occurrenceId].Evidence)
        .Where(call => target.Matches(call.Callee)),
];

Console.WriteLine($"Assembly: {options.AssemblyPath}");
Console.WriteLine($"Methods with IL: {callGraph.Methods.Length:N0}");
Console.WriteLine($"Direct call edges: {callGraph.DirectCalls.Length:N0}");
Console.WriteLine($"Diagnostics: {callGraph.Diagnostics.Length:N0}");
Console.WriteLine($"Target: {options.DeclaringType}.{options.MemberName}");
Console.WriteLine($"Matches: {matches.Length:N0}");
Console.WriteLine();

foreach (var call in matches.OrderBy(c => c.Caller.DeclaringType.ToQualifiedDisplayString(), StringComparer.Ordinal)
             .ThenBy(c => c.Caller.Name, StringComparer.Ordinal)
             .ThenBy(c => c.ILOffset)
             .Take(options.Limit))
{
    string evidence = call.Caller == call.EvidenceMethod
        ? ""
        : $" [{MethodDisplay(call.EvidenceMethod)}]";
    Console.WriteLine($"{MethodDisplay(call.Caller)}{evidence} IL_{call.ILOffset:X4} {call.Kind} -> {MemberDisplay(call.Callee)}");
}

if (matches.Length > options.Limit)
    Console.WriteLine($"... {matches.Length - options.Limit:N0} more matches");

foreach (var diagnostic in callGraph.Diagnostics.Take(options.Limit))
    Console.Error.WriteLine($"diagnostic 0x{diagnostic.MethodToken:X8} {diagnostic.Method}: {diagnostic.Message}");

return 0;

static int RunUnsafeReport(string[] args)
{
    string assemblyPath = args.Length > 1 ? args[1] : Assembly.GetExecutingAssembly().Location;
    int count = 6;
    if (args.Length > 2 && (!int.TryParse(args[2], out count) || count <= 0))
    {
        Console.Error.WriteLine("Error: count must be a positive integer.");
        return 1;
    }
    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"Error: assembly not found: {assemblyPath}");
        return 1;
    }

    LibraryBodyAnalysisExecution execution =
        LibraryBodyAnalysisService.ExecutePath(
            assemblyPath,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.Default));
    LibraryBodyIndex index = execution.CompatibilityIndex();
    var modes = index.UnsafeModes;
    var top = execution.Leverage.TopUnsafe(count);

    Console.WriteLine($"Assembly: {assemblyPath}");
    Console.WriteLine(
        $"Module memory-safety rules: {DescribeMemorySafetyRules(index.MemorySafetyRules)}");
    Console.WriteLine($"Methods: {modes.Total:N0}");
    Console.WriteLine($"  None     (no requires-unsafe):              {modes.None:N0}");
    Console.WriteLine($"  Implicit (legacy compatibility contract):   {modes.Implicit:N0}");
    Console.WriteLine($"  Explicit (updated explicit contract):       {modes.Explicit:N0}");
    Console.WriteLine($"  Unavailable (contract not established):     {modes.Unavailable:N0}");
    Console.WriteLine();
    Console.WriteLine($"Top {top.Length} requires-unsafe methods by direct callers:");
    Console.WriteLine();
    int rank = 1;
    foreach (var entry in top)
        Console.WriteLine($"{rank++}. {entry.DirectCallerCount,6} callers  [{entry.Mode}]  {MethodDisplay(entry.Method)}");

    var opaque = index.OpaqueUnsafeMethods();
    Console.WriteLine();
    Console.WriteLine($"Opaque-contract methods ({opaque.Length}): requires-unsafe with no pointer in the signature");
    Console.WriteLine("  (the obligation is visible only via the attribute / unsafe modifier, not the signature)");
    Console.WriteLine();
    foreach (var entry in opaque)
        Console.WriteLine($"  [{entry.Mode}]  {MethodDisplay(entry.Method)}");

    var hollow = index.HollowUnsafeMethods();
    Console.WriteLine();
    Console.WriteLine($"Hollow-unsafe methods ({hollow.Length}): requires-unsafe with no directly-visible unsafe operation");
    Console.WriteLine("  (an absence claim, never \"safe\" — an optimized-away pointer local can erase a real deref)");
    Console.WriteLine();
    foreach (var entry in hollow)
        Console.WriteLine($"  [{entry.Mode}]  {MethodDisplay(entry.Method)}");

    return 0;
}

static string DescribeMemorySafetyRules(MemorySafetyRulesResult rules)
    => rules switch
    {
        MemorySafetyRulesResult.Available available =>
            available.State.ToString(),
        MemorySafetyRulesResult.Unavailable unavailable =>
            $"Unavailable ({unavailable.Failure.Kind}: "
                + $"{unavailable.Failure.Detail})",
        _ => "Unavailable",
    };

static AppOptions? Parse(string[] args)
{
    string assemblyPath = Assembly.GetExecutingAssembly().Location;
    string target = "System.Console.WriteLine";
    int limit = 40;

    if (args.Length > 0 && args[0] is "-h" or "--help")
    {
        WriteUsage();
        return null;
    }

    if (args.Length > 0)
        assemblyPath = args[0];
    if (args.Length > 1)
        target = args[1];
    if (args.Length > 2 && (!int.TryParse(args[2], out limit) || limit <= 0))
    {
        Console.Error.WriteLine("Error: limit must be a positive integer.");
        WriteUsage();
        return null;
    }

    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"Error: assembly not found: {assemblyPath}");
        WriteUsage();
        return null;
    }

    if (!TryParseTarget(target, out var declaringType, out var memberName))
    {
        Console.Error.WriteLine($"Error: target must be Type.Member or Type::Member: {target}");
        WriteUsage();
        return null;
    }

    return new AppOptions(assemblyPath, declaringType, memberName, limit);
}

static bool TryParseTarget(string target, out string declaringType, out string memberName)
{
    int separator = target.LastIndexOf("::", StringComparison.Ordinal);
    int memberStart = separator >= 0 ? separator + 2 : -1;
    if (separator < 0)
    {
        separator = target.LastIndexOf('.');
        memberStart = separator + 1;
    }

    if (separator <= 0 || memberStart >= target.Length)
    {
        declaringType = "";
        memberName = "";
        return false;
    }

    declaringType = target[..separator];
    memberName = target[memberStart..];
    return true;
}

static string MethodDisplay(MethodIdentity method)
    => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

static string MemberDisplay(MemberRef member)
    => $"{member.DeclaringType.ToQualifiedDisplayString()}.{member.Name}({string.Join(", ", member.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

static void WriteUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project src/ILInspector.Analysis.App -c Release");
    Console.Error.WriteLine("  dotnet run --project src/ILInspector.Analysis.App -c Release -- <assembly-path> [Type.Member|Type::Member] [limit]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Defaults: scan this app for System.Console.WriteLine, limit 40.");
}

enum AnalysisCallRelationship
{
    Calls,
}

sealed record AnalysisCallCharacteristic;

sealed record AnalysisCallLimit;

sealed record AnalysisCallFailure;

sealed record AnalysisCallSubject
{
    AnalysisCallSubject(
        GraphNodeIdentity identity,
        MemberRef member)
    {
        Identity = identity;
        Member = member;
    }

    public GraphNodeIdentity Identity { get; }
    public MemberRef Member { get; }

    public static AnalysisCallSubject FromDefinition(
        MethodIdentity method)
        => new(
            GraphNodeIdentity.FromMethod(method),
            new MemberRef(
                method.DeclaringType,
                method.Name,
                method.ParameterTypes,
                method.ReturnType,
                MemberKind.Method)
            {
                GenericArity = method.GenericArity,
                HasThis = !method.IsStatic,
            });

    public static AnalysisCallSubject FromReference(MemberRef member)
        => new(GraphNodeIdentity.FromMember(member), member);

    public bool Equals(AnalysisCallSubject? other) =>
        other is not null && Identity == other.Identity;

    public override int GetHashCode() => Identity.GetHashCode();
}

sealed class AnalysisCallGraph
{
    AnalysisCallGraph(
        GraphDocument<
            AnalysisCallSubject,
            AnalysisCallRelationship,
            DirectCall,
            AnalysisCallCharacteristic,
            AnalysisCallLimit,
            AnalysisCallFailure> document)
    {
        Document = document;
    }

    public GraphDocument<
        AnalysisCallSubject,
        AnalysisCallRelationship,
        DirectCall,
        AnalysisCallCharacteristic,
        AnalysisCallLimit,
        AnalysisCallFailure> Document { get; }

    public static AnalysisCallGraph Create(
        LibraryCallGraphAnalysisResult callGraph)
    {
        var declaredMethods = callGraph.DeclaredMethods
            .ToDictionary(method =>
                (method.ModuleVersionId, method.MetadataToken));
        var nodeIds = new Dictionary<AnalysisCallSubject, int>();
        var nodes = new List<GraphNode<AnalysisCallSubject>>();
        var occurrences =
            new List<
                GraphOccurrence<
                    AnalysisCallSubject,
                    AnalysisCallRelationship,
                    DirectCall>>();
        var edgeKeys = new List<(int FromNodeId, int ToNodeId)>();
        var edgeOccurrences =
            new Dictionary<(int FromNodeId, int ToNodeId), List<int>>();

        foreach (DirectCall call in callGraph.DirectCalls)
        {
            AnalysisCallSubject source =
                AnalysisCallSubject.FromDefinition(call.Caller);
            AnalysisCallSubject target =
                AnalysisCallSubject.FromReference(call.Callee);
            bool targetIsDefined = declaredMethods.ContainsKey(
                (
                    call.Caller.ModuleVersionId,
                    call.CalleeDefinitionToken));
            int sourceNodeId =
                GetOrAddNode(source, GraphNodeRole.Ordinary);
            int targetNodeId =
                GetOrAddNode(
                    target,
                    targetIsDefined
                        ? GraphNodeRole.Ordinary
                        : GraphNodeRole.External);
            int occurrenceId = occurrences.Count;
            occurrences.Add(
                new(
                    occurrenceId,
                    AnalysisCallRelationship.Calls,
                    source,
                    target,
                    call,
                    []));

            var edgeKey = (sourceNodeId, targetNodeId);
            if (!edgeOccurrences.TryGetValue(
                    edgeKey,
                    out List<int>? occurrenceIds))
            {
                occurrenceIds = [];
                edgeOccurrences.Add(edgeKey, occurrenceIds);
                edgeKeys.Add(edgeKey);
            }
            occurrenceIds.Add(occurrenceId);
        }

        GraphEdge<AnalysisCallRelationship>[] edges =
        [
            .. edgeKeys.Select((key, edgeId) =>
                new GraphEdge<AnalysisCallRelationship>(
                    edgeId,
                    key.FromNodeId,
                    key.ToNodeId,
                    AnalysisCallRelationship.Calls,
                    edgeOccurrences[key])),
        ];

        return new AnalysisCallGraph(
            new(
                GraphDocumentScope.SessionBound,
                nodes,
                groups: [],
                edges,
                occurrences,
                characteristics: [],
                seeds: [],
                limits: [],
                failures: []));

        int GetOrAddNode(
            AnalysisCallSubject subject,
            GraphNodeRole role)
        {
            if (nodeIds.TryGetValue(subject, out int nodeId))
                return nodeId;

            nodeId = nodes.Count;
            nodeIds.Add(subject, nodeId);
            nodes.Add(
                new(
                    nodeId,
                    subject,
                    role,
                    []));
            return nodeId;
        }
    }
}

sealed record AppOptions(string AssemblyPath, string DeclaringType, string MemberName, int Limit);
