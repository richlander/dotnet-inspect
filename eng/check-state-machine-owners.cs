#:project ../src/ILInspector.Analysis/ILInspector.Analysis.csproj
#:property EnablePreviewFeatures=true
#:property NoWarn=CA2252

// SPIKE (#10227): compares the two readings of state-machine ownership.
//   1. StateMachineRelationshipIndex (ILInspector.Metadata) certificate.
//   2. ILInspector.Analysis lifted-owner resolution through
//      StateMachineSourceResolver, which reads [AsyncStateMachine] itself:
//      - reverse: ResolveSourceOwnership(MoveNext), as DeclaredSourceResolver
//        composes it;
//      - forward (assembly-wide): TryResolveStateMachineExecutionMethod, as
//        LiftedSourceOwnerResolver uses it without generated-expansion work;
//      - forward (targeted): TryResolveTargetedStateMachineExecutionMethod, as
//        LiftedSourceOwnerResolver and generated-body expansion use it.
// Also reports the Analysis resolver's coverage beyond async: iterators,
// lambdas, and local functions.
// Usage: dotnet run eng/check-state-machine-owners.cs -- <assembly.dll>...

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: check-state-machine-owners <assembly.dll>...");
    return 2;
}

int totalDisagreements = 0;
foreach (string path in args)
    totalDisagreements += Check(path);
Console.WriteLine();
Console.WriteLine($"TOTAL async disagreements: {totalDisagreements}");
return 0;

static int Check(string path)
{
    using FileStream stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    MetadataReader reader = pe.GetMetadataReader();
    var names = new Names(reader);

    Console.WriteLine();
    Console.WriteLine($"=== {path}");
    Console.WriteLine($"    {reader.GetString(reader.GetAssemblyDefinition().Name)} "
        + $"{reader.GetAssemblyDefinition().Version} "
        + $"types={reader.TypeDefinitions.Count} methods={reader.MethodDefinitions.Count}");

    // ---- Reading 1: the Metadata certificate.
    StateMachineRelationshipIndex index = StateMachineRelationshipIndex.Create(reader);
    var indexByType = new Dictionary<int, StateMachineRelationship>();
    if (index.Relationships is StateMachineRelationshipsResult.Available available)
    {
        foreach (StateMachineRelationship r in available.Relationships)
            indexByType[r.StateMachineType.Definition.Value] = r;
    }
    else if (index.Relationships is StateMachineRelationshipsResult.Rejected globalRejected)
    {
        Console.WriteLine($"    INDEX GLOBAL REJECTION: {globalRejected.Failure.Kind} {globalRejected.Failure.Detail}");
    }

    // ---- Reading 2: the Analysis composition.
    var attribution = new DeclaredSourceAttribution(path, reader, pe);
    StateMachineSourceResolver sm = attribution.StateMachineSourceResolver;
    LibraryBodyPrimaryMetadataResolver primary = attribution.PrimaryMetadataResolver;

    MethodIdentity Identity(MethodDefinitionHandle handle)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        TypeDefinitionHandle typeHandle = method.GetDeclaringType();
        return primary.CreateMethodIdentity(
            typeHandle,
            handle,
            method,
            primary.CreateScope(reader.GetTypeDefinition(typeHandle), method));
    }

    // Forward readings for every MethodDef: kickoff -> state-machine type.
    var indexForward = new Dictionary<int, string>();
    var wideForward = new Dictionary<int, Answer>();
    var targetedForward = new Dictionary<int, Answer>();
    foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        int token = MetadataTokens.GetToken(handle);
        if (method.GetCustomAttributes().Count == 0)
            continue;

        wideForward[token] = Try(() =>
            sm.TryResolveStateMachineExecutionMethod(handle, method, out MethodDefinitionHandle exec)
                ? Answer.Resolved(MetadataTokens.GetToken(exec), Declaring(reader, exec), token)
                : Answer.None);
        targetedForward[token] = Try(() =>
        {
            var work = new MethodDefinitionGeneratedExpansionWork(
                MethodDefinitionGeneratedExpansionLimits.Default);
            return sm.TryResolveTargetedStateMachineExecutionMethod(
                    handle, method, work, out MethodDefinitionHandle exec, out TypeDefinitionHandle type)
                ? Answer.Resolved(MetadataTokens.GetToken(exec), MetadataTokens.GetToken(type), token)
                : Answer.None;
        });
    }

    // Structural state machines.
    var asyncTypes = new List<(TypeDefinitionHandle Handle, bool AsyncIterator)>();
    var iteratorTypes = new List<TypeDefinitionHandle>();
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        var interfaces = new HashSet<string>();
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        foreach (InterfaceImplementationHandle ih in type.GetInterfaceImplementations())
            interfaces.Add(names.InterfaceName(reader.GetInterfaceImplementation(ih).Interface));
        if (interfaces.Contains("System.Runtime.CompilerServices.IAsyncStateMachine"))
            asyncTypes.Add((typeHandle, interfaces.Contains("System.Collections.Generic.IAsyncEnumerator`1")));
        else if (interfaces.Contains("System.Collections.IEnumerator")
            && interfaces.Contains("System.IDisposable")
            && reader.GetString(type.Name).StartsWith('<'))
            iteratorTypes.Add(typeHandle);
    }

    // ---- Async comparison, per structural async state machine.
    var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
    void Count(string key) => tally[key] = tally.GetValueOrDefault(key) + 1;
    var examples = new List<string>();
    int indexResolved = 0, reverseResolved = 0, wideResolved = 0, targetedResolved = 0;
    int classicCount = 0, asyncIteratorCount = 0, setStateMachineAbsent = 0;
    int ultimateDiffers = 0;
    int disagreements = 0;
    var ultimateExamples = new List<string>();

    foreach ((TypeDefinitionHandle typeHandle, bool asyncIterator) in asyncTypes)
    {
        if (asyncIterator) asyncIteratorCount++; else classicCount++;
        int typeToken = MetadataTokens.GetToken(typeHandle);
        string typeName = names.Type(typeHandle);

        // Reading 1.
        string indexAnswer;
        int? indexKickoff = null;
        int? indexMoveNext = null;
        switch (index.GetByStateMachine(typeHandle))
        {
            case StateMachineRelationshipResult.Resolved resolved:
                indexKickoff = resolved.Relationship.Kickoff.Token;
                indexMoveNext = resolved.Relationship.GetRole(StateMachineMethodRole.MoveNext)
                    is StateMachineRoleDisposition.Present p ? p.Method.Token : null;
                indexAnswer = $"Resolved({resolved.Relationship.Kind})";
                indexResolved++;
                if (resolved.Relationship.Kind == StateMachineClaimKind.ClassicAsync
                    && resolved.Relationship.GetRole(StateMachineMethodRole.SetStateMachine)
                        is StateMachineRoleDisposition.AbsentFromArtifact)
                {
                    setStateMachineAbsent++;
                    examples.Add($"      [info] SetStateMachine AbsentFromArtifact: {typeName} <- {names.Method(indexKickoff.Value)}");
                }
                break;
            case StateMachineRelationshipResult.Rejected rejected:
                indexAnswer = $"Rejected({rejected.Failure.Kind}: {rejected.Failure.Detail})";
                break;
            default:
                indexAnswer = "Absent";
                break;
        }

        // Reading 2a: reverse from MoveNext.
        MethodDefinitionHandle moveNext = indexMoveNext is { } m
            ? MetadataTokens.MethodDefinitionHandle(m)
            : FindMoveNext(reader, typeHandle);
        Answer reverse;
        Answer ultimate = Answer.None;
        if (moveNext.IsNil)
        {
            reverse = Answer.NoMoveNext;
        }
        else
        {
            MethodDefinition moveNextDef = reader.GetMethodDefinition(moveNext);
            MethodIdentity moveNextId = Identity(moveNext);
            bool sourceGenerated = attribution.GeneratedProvenanceClassifier
                .IsSourceGeneratedTypeOrEnclosing(typeHandle);
            reverse = Try(() =>
                sm.ResolveSourceOwnership(moveNextId, moveNextDef, sourceGenerated, out MethodIdentity? source) switch
                {
                    AsyncSourceResolution.Resolved => Answer.Resolved(
                        MetadataTokens.GetToken(moveNext), typeToken, source!.MetadataToken),
                    AsyncSourceResolution.Unresolved => Answer.Unresolved,
                    _ => Answer.None,
                });
            ultimate = Try(() =>
                attribution.DeclaredSourceResolver.ResolveUltimateDeclaredMethod(
                    moveNext, moveNextDef, moveNextId, sourceGenerated,
                    out _, out AuthenticatedSourceOwner? ultimateOwner) switch
                {
                    DeclaredOwnerResolution.Resolved => Answer.Resolved(
                        MetadataTokens.GetToken(moveNext), typeToken, ultimateOwner!.Value.Method.MetadataToken),
                    DeclaredOwnerResolution.Rejected => Answer.Rejected("ultimate rejected"),
                    DeclaredOwnerResolution.Unresolved => Answer.Unresolved,
                    _ => Answer.None,
                });
        }
        if (reverse.IsResolved) reverseResolved++;

        // Reading 2b/2c: forward claims into this type.
        int[] wideClaims = wideForward.Where(kv => kv.Value.IsResolved && kv.Value.Type == typeToken)
            .Select(kv => kv.Key).ToArray();
        int[] targetedClaims = targetedForward.Where(kv => kv.Value.IsResolved && kv.Value.Type == typeToken)
            .Select(kv => kv.Key).ToArray();
        if (wideClaims.Length == 1) wideResolved++;
        if (targetedClaims.Length == 1) targetedResolved++;

        // Forward rejection that names this type: a kickoff whose claim throws.
        string kind = asyncIterator ? "async-iterator" : "classic";
        string Classify(string reading, int? analysisKickoff, string analysisState)
        {
            if (indexKickoff is { } ik && analysisKickoff is { } ak)
                return ik == ak ? "agree:resolved" : $"DISAGREE:{reading}:different-owner";
            if (indexKickoff is null && analysisKickoff is null)
                return $"agree:{(indexAnswer.StartsWith("Rejected") ? "index-rejected" : "index-absent")}/{analysisState}";
            return indexKickoff is null
                ? $"DISAGREE:{reading}:index-{indexAnswer.Split('(')[0].ToLowerInvariant()}/analysis-resolves"
                : $"DISAGREE:{reading}:index-resolves/analysis-{analysisState}";
        }

        string reverseClass = Classify("reverse", reverse.IsResolved ? reverse.Kickoff : null, reverse.State);
        string wideClass = Classify("forward-wide",
            wideClaims.Length == 1 ? wideClaims[0] : null,
            wideClaims.Length > 1 ? "multiple" : "none");
        string targetedClass = Classify("forward-targeted",
            targetedClaims.Length == 1 ? targetedClaims[0] : null,
            targetedClaims.Length > 1 ? "multiple" : "none");
        Count($"{kind} reverse {reverseClass}");
        Count($"{kind} forward-wide {wideClass}");
        Count($"{kind} forward-targeted {targetedClass}");

        bool disagree = reverseClass.StartsWith("DISAGREE")
            || wideClass.StartsWith("DISAGREE")
            || targetedClass.StartsWith("DISAGREE");
        if (disagree)
        {
            disagreements++;
            if (examples.Count(e => e.Contains("DISAGREE")) < 12)
            {
                examples.Add($"      DISAGREE {kind} {typeName}");
                examples.Add($"        index:    {indexAnswer} kickoff={(indexKickoff is { } k ? names.Method(k) : "-")}");
                examples.Add($"        reverse:  {reverse.Describe(names)}  ({reverseClass})");
                examples.Add($"        wide:     [{string.Join(", ", wideClaims.Select(names.Method))}]  ({wideClass})");
                examples.Add($"        targeted: [{string.Join(", ", targetedClaims.Select(names.Method))}]  ({targetedClass})");
            }
        }

        if (ultimate.IsResolved && reverse.IsResolved && ultimate.Kickoff != reverse.Kickoff)
        {
            ultimateDiffers++;
            if (ultimateExamples.Count < 3)
                ultimateExamples.Add($"      lifted kickoff {names.Method(reverse.Kickoff)} -> declared {names.Method(ultimate.Kickoff)}");
        }
        Count($"{kind} ultimate-declared {ultimate.State}");
    }

    // Forward-only rejections and claims on non-structural types.
    foreach ((int kickoff, Answer answer) in targetedForward)
    {
        if (answer.State == "rejected")
        {
            Count($"forward-targeted kickoff threw ({answer.Detail})");
            if (examples.Count(e => e.Contains("forward-threw")) < 4)
                examples.Add($"      [forward-threw] {names.Method(kickoff)}: {answer.Detail}");
        }
    }
    foreach ((int kickoff, Answer answer) in wideForward)
    {
        if (answer.State == "rejected")
            Count($"forward-wide kickoff threw ({answer.Detail})");
    }

    var structural = new HashSet<int>(asyncTypes.Select(t => MetadataTokens.GetToken(t.Handle)));
    structural.UnionWith(iteratorTypes.Select(t => MetadataTokens.GetToken(t)));
    Console.WriteLine($"    Index relationships: {string.Join(", ", indexByType.Values.GroupBy(r => r.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))}; "
        + $"on non-structural types {indexByType.Keys.Count(k => !structural.Contains(k))}; "
        + $"analysis forward claims on non-structural types {wideForward.Values.Concat(targetedForward.Values).Count(a => a.IsResolved && !structural.Contains(a.Type))}");
    Console.WriteLine($"    Async state machines (structural, direct IAsyncStateMachine): {asyncTypes.Count} "
        + $"(classic {classicCount}, async-iterator {asyncIteratorCount})");
    Console.WriteLine($"      index resolved {indexResolved}; analysis reverse resolved {reverseResolved}; "
        + $"forward-wide unique {wideResolved}; forward-targeted unique {targetedResolved}");
    Console.WriteLine($"      classic SetStateMachine AbsentFromArtifact: {setStateMachineAbsent}");
    Console.WriteLine($"      types with any disagreement: {disagreements}");
    Console.WriteLine($"      lifted kickoff composed to a different declared owner: {ultimateDiffers}");
    foreach ((string key, int value) in tally)
        Console.WriteLine($"      {value,6}  {key}");
    foreach (string example in examples.Take(40))
        Console.WriteLine(example);
    foreach (string example in ultimateExamples)
        Console.WriteLine(example);

    // ---- Coverage beyond async (context).
    int iterIndex = 0, iterWide = 0, iterTargeted = 0, iterAgree = 0;
    var iterTally = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var iterExamples = new List<string>();
    foreach (TypeDefinitionHandle typeHandle in iteratorTypes)
    {
        int typeToken = MetadataTokens.GetToken(typeHandle);
        int? ik = index.GetByStateMachine(typeHandle) is StateMachineRelationshipResult.Resolved r
            && r.Relationship.Kind == StateMachineClaimKind.Iterator
            ? r.Relationship.Kickoff.Token : null;
        int[] wide = wideForward.Where(kv => kv.Value.IsResolved && kv.Value.Type == typeToken).Select(kv => kv.Key).ToArray();
        int[] targeted = targetedForward.Where(kv => kv.Value.IsResolved && kv.Value.Type == typeToken).Select(kv => kv.Key).ToArray();
        if (ik is not null) iterIndex++;
        if (wide.Length == 1) iterWide++;
        if (targeted.Length == 1) iterTargeted++;
        if ((ik is null && wide.Length == 0 && targeted.Length == 0)
            || (ik is { } k && wide is [var w] && w == k && targeted is [var t] && t == k))
        {
            iterAgree++;
        }
        else
        {
            string key = $"iterator index-{(ik is null ? "unresolved" : "resolves")}/wide-{wide.Length}/targeted-{targeted.Length}";
            iterTally[key] = iterTally.GetValueOrDefault(key) + 1;
            if (iterExamples.Count < 3)
                iterExamples.Add($"      [iterator] {names.Type(typeHandle)} index={(ik is { } k2 ? names.Method(k2) : "-")} "
                    + $"wide=[{string.Join(", ", wide.Select(names.Method))}] targeted=[{string.Join(", ", targeted.Select(names.Method))}]");
        }
    }
    foreach ((string key, int value) in iterTally)
        Console.WriteLine($"      {value,6}  {key}");
    foreach (string example in iterExamples)
        Console.WriteLine(example);
    Console.WriteLine($"    Iterators (structural <..> types with IEnumerator+IDisposable): {iteratorTypes.Count}; "
        + $"index resolved {iterIndex}; analysis forward-wide {iterWide}; forward-targeted {iterTargeted}; "
        + $"all-three agree {iterAgree}; analysis reverse covers 0 (reverse is IAsyncStateMachine-only)");

    int lifted = 0, liftedResolved = 0, liftedNone = 0, liftedRejected = 0, liftedThrew = 0;
    int lambdas = 0, localFunctions = 0, ultimateResolved = 0;
    foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        string name = reader.GetString(method.Name);
        if (!CompilerGeneratedNames.IsLocalFunctionOrLambda(name))
            continue;
        lifted++;
        if (name.Contains(">g__")) localFunctions++; else lambdas++;
        MethodIdentity id;
        try { id = Identity(handle); }
        catch (Exception) { liftedThrew++; continue; }
        try
        {
            switch (attribution.LiftedSourceOwnerResolver.Resolve(handle, method, id, out _))
            {
                case LiftedSourceOwnerResolution.Resolved: liftedResolved++; break;
                case LiftedSourceOwnerResolution.Rejected: liftedRejected++; break;
                default: liftedNone++; break;
            }
            if (attribution.DeclaredSourceResolver.ResolveUltimateDeclaredMethod(
                    handle, method, id,
                    attribution.GeneratedProvenanceClassifier.IsSourceGeneratedTypeOrEnclosing(method.GetDeclaringType()),
                    out _, out _) == DeclaredOwnerResolution.Resolved)
                ultimateResolved++;
        }
        catch (Exception) { liftedThrew++; }
    }
    Console.WriteLine($"    Lifted methods (lambda/local-function names): {lifted} (lambdas {lambdas}, local functions {localFunctions}); "
        + $"immediate owner resolved {liftedResolved}, none {liftedNone}, rejected {liftedRejected}, threw {liftedThrew}; "
        + $"ultimate declared owner resolved {ultimateResolved}; index covers 0");

    return disagreements;
}

static Answer Try(Func<Answer> f)
{
    try { return f(); }
    catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException
        or ImplementationMetricWorkLimitExceededException)
    {
        return Answer.Rejected($"{ex.GetType().Name}: {ex.Message}");
    }
}

static int Declaring(MetadataReader reader, MethodDefinitionHandle method)
    => MetadataTokens.GetToken(reader.GetMethodDefinition(method).GetDeclaringType());

static MethodDefinitionHandle FindMoveNext(MetadataReader reader, TypeDefinitionHandle typeHandle)
{
    foreach (MethodDefinitionHandle handle in reader.GetTypeDefinition(typeHandle).GetMethods())
    {
        string name = reader.GetString(reader.GetMethodDefinition(handle).Name);
        if (name == "MoveNext" || name.EndsWith(".IAsyncStateMachine.MoveNext", StringComparison.Ordinal))
            return handle;
    }
    return default;
}

readonly record struct Answer(string State, int MoveNext, int Type, int Kickoff, string? Detail)
{
    public static Answer None => new("none", 0, 0, 0, null);
    public static Answer Unresolved => new("unresolved", 0, 0, 0, null);
    public static Answer NoMoveNext => new("no-movenext", 0, 0, 0, null);
    public static Answer Resolved(int moveNext, int type, int kickoff) => new("resolved", moveNext, type, kickoff, null);
    public static Answer Rejected(string detail) => new("rejected", 0, 0, 0, detail);
    public bool IsResolved => State == "resolved";
    public string Describe(Names names) => IsResolved
        ? $"resolved kickoff={names.Method(Kickoff)}"
        : Detail is null ? State : $"{State} ({Detail})";
}

sealed class Names(MetadataReader reader)
{
    public string Type(TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return Type(declaring) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public string Method(int token)
    {
        if (token == 0)
            return "-";
        MethodDefinition method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(token));
        return $"{Type(method.GetDeclaringType())}::{reader.GetString(method.Name)}";
    }

    public string InterfaceName(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return Type((TypeDefinitionHandle)handle);
            case HandleKind.TypeReference:
                TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                string ns = reader.GetString(reference.Namespace);
                string name = reader.GetString(reference.Name);
                return ns.Length == 0 ? name : ns + "." + name;
            case HandleKind.TypeSpecification:
                BlobReader blob = reader.GetBlobReader(
                    reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                if (blob.ReadSignatureHeader().RawValue != (byte)SignatureTypeCode.GenericTypeInstance)
                    return "?";
                blob.ReadByte(); // class or valuetype
                return InterfaceName(blob.ReadTypeHandle());
            default:
                return "?";
        }
    }
}
