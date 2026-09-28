using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis.Planning.Experiments;
using NLinq;

// args: <budget-ms> <variant> <dll>...   or   alloc <calls> <variant> <dll>
if (args[0] == "fixture-postcard")
{
    Environment.Exit(FixturePostcard.Run(args));
}

if (args[0] == "alloc")
{
    int calls = int.Parse(args[1]);
    var allocImage = ImmutableArray.Create(File.ReadAllBytes(args[3]));
    for (int i = 0; i < 20; i++)
        Run(args[2], args[3], allocImage);
    long before = GC.GetAllocatedBytesForCurrentThread();
    AllocProfile.Debug = Environment.GetEnvironmentVariable("ALLOC_DEBUG") == "1";
    using (var profile = new AllocProfile())
    {
        for (int i = 0; i < calls; i++)
            Run(args[2], args[3], allocImage);
        Console.WriteLine($"{args[2]} {Path.GetFileName(args[3])}: exact {(GC.GetAllocatedBytesForCurrentThread() - before) / calls / 1024.0:F1} KB/call");
        profile.Report(calls, 20);
    }
    return;
}

if (args[0] == "rwcheck")
{
    int mismatches = 0;
    foreach (string dll in args.Skip(1))
    {
        var img = ImmutableArray.Create(File.ReadAllBytes(dll));
        foreach (string pop in (string[])["dense", "sparse"])
        {
            foreach (string req in (string[])["head6", "w4-6", "w100-110", "w1000-1010", "w4-6c", "all"])
            {
                string[] impls = pop == "sparse" ? ["kernel", "hand", "mat", "legacy"] : ["kernel", "hand", "mat"];
                var answers = impls.Select(i => (i, a: Run($"rw:{pop}:{req}:{i}", dll, img))).ToArray();
                string reference = answers[0].a;
                foreach (var (i, a) in answers)
                {
                    string compare = i == "legacy" ? a.Split(";h=")[0] : a;
                    string expected = i == "legacy" ? reference.Split(";h=")[0] : reference;
                    if (compare != expected)
                    {
                        mismatches++;
                        Console.WriteLine($"MISMATCH {Path.GetFileName(dll)} {pop} {req} {i}: {a} vs kernel {reference}");
                    }
                }

                Console.WriteLine($"{Path.GetFileName(dll),-36} {pop,-6} {req,-10} {reference}");
            }
        }
    }

    Console.WriteLine($"mismatches={mismatches}");
    return;
}

if (args[0] == "pvcheck")
{
    // Dense: Naive Planner, LINQ, NLinq, and Planner answer one open query and must agree.
    // Async: Old, NLinq, and Planner must agree on the answer and the method names.
    int mismatches = 0;
    foreach (string dll in args.Skip(1))
    {
        var img = ImmutableArray.Create(File.ReadAllBytes(dll));
        foreach (string closing in (string[])["exists", "count", "head", "tail", "rows", "window"])
        {
            string[] dense = [.. ((string[])["naive", "linq", "nlinq", "after"]).Select(impl => Run($"pc:{closing}:{impl}", dll, img))];
            string[] sparse = [.. ((string[])["old", "nlinq", "after"]).Select(impl => Run($"pa:{closing}:{impl}", dll, img))];
            bool denseAgree = dense.All(a => a == dense[0]);
            bool sparseAgree = sparse.All(a => a == sparse[0]);
            mismatches += (denseAgree ? 0 : 1) + (sparseAgree ? 0 : 1);
            Console.WriteLine(
                $"{Path.GetFileName(dll),-36} {closing,-7} dense {(denseAgree ? dense[0] : "MISMATCH " + string.Join(" / ", dense))}"
                + $"\tasync {(sparseAgree ? sparse[0] : "MISMATCH " + string.Join(" / ", sparse))}");
        }
    }

    Console.WriteLine($"mismatches={mismatches}");
    return;
}

if (args[0] == "pccheck")
{
    int mismatches = 0;
    foreach (string dll in args.Skip(1))
    {
        var img = ImmutableArray.Create(File.ReadAllBytes(dll));
        foreach (string closing in (string[])["exists", "count", "head", "tail", "rows", "window"])
        {
            // mat, linq, nlinq, and after answer the same open query and must agree.
            // old is the legacy classified rows; its difference is reported, not forced.
            string[] answers = [.. ((string[])["mat", "linq", "nlinq", "after"]).Select(impl => Run($"pc:{closing}:{impl}", dll, img))];
            bool agree = answers.All(a => a == answers[0]);
            if (!agree)
                mismatches++;
            string old = Run($"pc:{closing}:old", dll, img);
            Console.WriteLine($"{Path.GetFileName(dll),-36} {closing,-7} {(agree ? answers[0] : "MISMATCH " + string.Join(" / ", answers))}\told={old}{(old == answers[0] ? " (same)" : " (differs)")}");
        }
    }

    Console.WriteLine($"mismatches={mismatches}");
    return;
}

if (args[0] == "calls")
{
    foreach (string dll in args.Skip(1))
    {
        using var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(dll)));
        MetadataReader md = pe.GetMetadataReader();
        int sites = 0;
        var distinct = new HashSet<int>();
        foreach (MethodDefinitionHandle mh in md.MethodDefinitions)
        {
            MethodDefinition m = md.GetMethodDefinition(mh);
            if (m.RelativeVirtualAddress == 0)
                continue;
            ILInspector.Instructions.InstructionDecoder.Visit(pe.GetMethodBody(m.RelativeVirtualAddress), (op, token, size) =>
            {
                if (op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn)
                {
                    sites++;
                    distinct.Add(token);
                }
                return true;
            });
        }
        Console.WriteLine($"{Path.GetFileName(dll),-40} call sites {sites,8}  distinct targets {distinct.Count,7}  ratio {(double)sites / Math.Max(1, distinct.Count):F1}x");
    }
    return;
}

int budgetMs = int.Parse(args[0]);
string variant = args[1];
foreach (string path in args.Skip(2))
{
    var image = ImmutableArray.Create(File.ReadAllBytes(path));
    string answer = Run(variant, path, image);
    // Postcard variants time the closing itself; the answer text is built once, above.
    Action timed = variant.StartsWith("pc:", StringComparison.Ordinal)
        ? () => { using var pe = new PEReader(image); GC.KeepAlive(PostcardProbe.Exec(variant, path, pe)); }
        : variant.StartsWith("pa:", StringComparison.Ordinal)
            ? () => { using var pe = new PEReader(image); GC.KeepAlive(PostcardProbe.ExecAsync(variant, path, pe)); }
            : () => Run(variant, path, image);
    for (int i = 0; i < 5; i++)
        timed();

    var samples = new List<double>();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var total = Stopwatch.StartNew();
    while ((total.ElapsedMilliseconds < budgetMs || samples.Count < 20) && samples.Count < 5000)
    {
        long start = Stopwatch.GetTimestamp();
        timed();
        samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    long allocatedPerCall = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / samples.Count;
    samples.Sort();
    double Q(double q) => samples[(int)Math.Clamp(Math.Round(q * (samples.Count - 1)), 0, samples.Count - 1)];
    Console.WriteLine($"{Path.GetFileName(path)}\t{answer}\t{samples.Count}\t{Q(0.5):F4}\t{Q(0.1):F4}\t{Q(0.9):F4}\t{allocatedPerCall}");
}

static string Run(string variant, string path, ImmutableArray<byte> image)
{
    using var peReader = new PEReader(image);
    return variant switch
    {
        "legacy" => AsyncMethodCount.LegacyRows(peReader).ToString(),
        "hand" => AsyncMethodCount.HandRolled(peReader).ToString(),
        "hand-flat" => HandFlat(peReader.GetMetadataReader()).ToString(),
        "nlinq" => new MethodRows(peReader.GetMetadataReader())
            .Where<MethodRows, MethodRow, IsCountedAsync>(default)
            .CountFold<Filter<MethodRow, MethodRows, IsCountedAsync>, MethodRow>().ToString(),
        "planned" => AsyncMethodCount.Planned(path, peReader).ToString(),
        "hand-exists" => HandExists(peReader.GetMetadataReader()) ? "true" : "false",
        "nlinq-exists" => new MethodRows(peReader.GetMetadataReader())
            .Any<MethodRows, MethodRow, IsCountedAsync>(default) ? "true" : "false",
        "planned-exists" => AsyncMethodCount.PlannedExists(path, peReader) ? "true" : "false",
        "k-interp" => AsyncClosedQueries.Count(false, path, peReader).ToString(),
        "k-kernel" => AsyncClosedQueries.Count(true, path, peReader).ToString(),
        "k-interp-exists" => AsyncClosedQueries.Exists(false, path, peReader) ? "true" : "false",
        "k-kernel-exists" => AsyncClosedQueries.Exists(true, path, peReader) ? "true" : "false",
        "k-hand-exists" => HandExistsHoisted(peReader.GetMetadataReader()) ? "true" : "false",
        "r-hand" => RowsAnswer(AsyncClosedQueriesK2.HandRows(peReader)),
        "r-interp" => RowsAnswer(AsyncClosedQueriesK2.Rows(false, path, peReader)),
        "r-kernel" => RowsAnswer(AsyncClosedQueriesK2.Rows(true, path, peReader)),
        "r-legacy" => $"n={AsyncClosedQueriesK2.LegacyRowCount(peReader)}",
        "n-hand" => AsyncClosedQueriesK2.HandAtLeast(peReader) ? "true" : "false",
        "n-interp" => AsyncClosedQueriesK2.AtLeast(false, path, peReader) ? "true" : "false",
        "n-kernel" => AsyncClosedQueriesK2.AtLeast(true, path, peReader) ? "true" : "false",
        "c-typed-fused" => TypedClassifiedFusion.Run(peReader).ToString(),
        _ when variant.StartsWith("rw:", StringComparison.Ordinal) => RowWindow(variant, peReader),
        _ when variant.StartsWith("pc:", StringComparison.Ordinal) => PostcardProbe.Describe(PostcardProbe.Exec(variant, path, peReader)),
        _ when variant.StartsWith("pa:", StringComparison.Ordinal) => PostcardProbe.DescribeNames(PostcardProbe.ExecAsync(variant, path, peReader)),
        "c-legacy" => ClassifiedFusion.Legacy(peReader).ToString(),
        "c-hand-separate" => ClassifiedFusion.HandSeparate(peReader).ToString(),
        "c-hand-fused" => ClassifiedFusion.HandFused(peReader).ToString(),
        "c-nlinq-fused" => NLinqFused(peReader.GetMetadataReader()).ToString(),
        "c-planned-separate" => ClassifiedFusion.PlannedSeparate(path, peReader).ToString(),
        "c-planned-fused" => ClassifiedFusion.PlannedFused(path, peReader).ToString(),
        "c-planned-fused-shared" => SharedClassifiedFusion.PlannedFused(path, peReader).ToString(),
        "c-planned-fused-guarded" => GuardedClassifiedFusion.PlannedFused(path, peReader).ToString(),
        "c-planned-fused-typescoped" => TypeScopedClassifiedFusion.PlannedFused(path, peReader).ToString(),
        "c-planned-async-only" => ClassifiedFusion.PlannedAsyncOnly(path, peReader).ToString(),
        "unsafe-planned" => ILInspector.Analysis.Planning.UnsafeEvidencePresence.HasEvidence(path, image) ? "true" : "false",
        _ => throw new ArgumentException(variant),
    };
}

// The hand-rolled loop without hoisting the type filter: the same per-method work the
// planned and NLinq variants do, so hoisting and planning overhead separate.
static ClassifiedAnswer NLinqFused(MetadataReader reader)
{
    ClassifiedAccumulator result = new MethodRows(reader)
        .Fold<MethodRows, MethodRow, ClassifiedAccumulator, ClassifyFold>(new ClassifiedAccumulator([], 0, false), default);
    ImmutableArray<int> rows = [.. result.PInvoke];
    return new(rows.Length, result.Async, result.AnyPointer);
}

static string RowWindow(string variant, PEReader peReader)
{
    string[] parts = variant.Split(':');
    bool dense = parts[1] == "dense";
    RowWindowRequest request = parts[2] switch
    {
        "head6" => RowWindowRequest.Head(6),
        "w4-6" => RowWindowRequest.Window(4, 6),
        "w100-110" => RowWindowRequest.Window(100, 110),
        "w1000-1010" => RowWindowRequest.Window(1000, 1010),
        "w4-6c" => RowWindowRequest.Window(4, 6) with { CountAll = true },
        "all" => RowWindowRequest.All,
        _ => throw new ArgumentException(variant),
    };
    RowWindowResult<MethodTextRow> result = parts[3] switch
    {
        "kernel" => RowWindowExperiment.Kernel(dense, peReader, request),
        "hand" => RowWindowExperiment.Hand(dense, peReader, request),
        "mat" => RowWindowExperiment.Materialize(dense, peReader, request),
        "legacy" => RowWindowExperiment.Legacy(peReader, request),
        _ => throw new ArgumentException(variant),
    };
    int hash = 17;
    foreach (MethodTextRow row in result.Rows)
        hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(row.Name + "|" + row.DeclaringType + "|" + row.Signature));
    return $"n={result.Rows.Length};t={result.Total?.ToString() ?? "-"};m={(result.WindowStartMissing ? 1 : 0)};h={hash:X8}";
}

static string RowsAnswer(ImmutableArray<int> rows)
{
    int hash = 17;
    foreach (int row in rows)
        hash = unchecked(hash * 31 + row);
    return $"n={rows.Length};h={hash:X8}";
}

static int HandFlat(MetadataReader reader)
{
    int count = 0;
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            if (AsyncMethodScope.IsCounted(reader, type, reader.GetMethodDefinition(methodHandle)))
                count++;
        }
    }

    return count;
}

static bool HandExistsHoisted(MetadataReader reader)
{
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        if (!AsyncMethodScope.IsCountedType(reader, type))
            continue;
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            if (AsyncMethodScope.IsCountedMethod(reader, reader.GetMethodDefinition(methodHandle)))
                return true;
        }
    }

    return false;
}

static bool HandExists(MetadataReader reader)
{
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            if (AsyncMethodScope.IsCounted(reader, type, reader.GetMethodDefinition(methodHandle)))
                return true;
        }
    }

    return false;
}

readonly record struct ClassifiedAccumulator(List<int> PInvoke, int Async, bool AnyPointer);

struct ClassifyFold : IFunc<ClassifiedAccumulator, MethodRow, ClassifiedAccumulator>
{
    public ClassifiedAccumulator Invoke(ClassifiedAccumulator acc, MethodRow row)
    {
        if (!ClassifiedScope.IsScopedType(row.Reader, row.Type) || !ClassifiedScope.IsScopedMethod(row.Reader, row.Method))
            return acc;
        if (ClassifiedScope.IsPInvoke(row.Method))
        {
            acc.PInvoke.Add(row.Token);
            return acc;
        }

        if (ClassifiedScope.IsAsync(row.Reader, row.Method))
            return acc with { Async = acc.Async + 1 };
        return !acc.AnyPointer && ClassifiedScope.HasPointerSignature(row.Reader, row.Method)
            ? acc with { AnyPointer = true }
            : acc;
    }
}

readonly struct MethodRow(MetadataReader reader, TypeDefinition type, MethodDefinition method, int token = 0)
{
    public int Token => token;
    public MetadataReader Reader => reader;
    public TypeDefinition Type => type;
    public MethodDefinition Method => method;
}

struct IsCountedAsync : IFunc<MethodRow, bool>
{
    public bool Invoke(MethodRow row) => AsyncMethodScope.IsCounted(row.Reader, row.Type, row.Method);
}

/// <summary>An NLinq source over every method definition, type by type.</summary>
struct MethodRows : NLinq.IEnumerator<MethodRows, MethodRow>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;
    MethodDefinitionHandleCollection.Enumerator _methods;
    TypeDefinition _type;
    bool _inType;

    public MethodRows(MetadataReader reader)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
    }

    public MethodRow TryGetNext(out bool hasMore)
    {
        while (true)
        {
            if (_inType && _methods.MoveNext())
            {
                hasMore = true;
                return new(_reader, _type, _reader.GetMethodDefinition(_methods.Current), MetadataTokens.GetToken(_methods.Current));
            }

            if (!_types.MoveNext())
            {
                hasMore = false;
                return default;
            }

            _type = _reader.GetTypeDefinition(_types.Current);
            _methods = _type.GetMethods().GetEnumerator();
            _inType = true;
        }
    }

    static TAcc NLinq.IEnumerator<MethodRows, MethodRow>.Fold<TAcc, TFunc>(scoped ref MethodRows @this, TAcc accumulator, TFunc func)
    {
        MetadataReader reader = @this._reader;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                accumulator = func.Invoke(accumulator, new MethodRow(reader, type, reader.GetMethodDefinition(methodHandle), MetadataTokens.GetToken(methodHandle)));
        }

        return accumulator;
    }
}
