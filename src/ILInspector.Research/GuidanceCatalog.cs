using System.Collections.Frozen;
using System.Collections.Immutable;

using ILInspector.Decompiler.Annotations;
using ILInspector.Metadata;

namespace ILInspector.Research;

// The guidance catalog: owner-issued, static data keyed by descriptor id, with
// an optional refinement per attribute. Hosts render it; nothing fetches it at
// run time. Entries point at Microsoft Learn, the .NET blog, or a dotnet/runtime
// specification so the product's claims rest on the platform's own guidance.
// The catalog's shape and the preset's entries are owned by
// docs/design/notable-findings.md#guidance-catalog; other owners register
// entries for their own descriptors.

/// <summary>
/// An attribute that selects a more specific catalog entry for a descriptor.
/// </summary>
public enum GuidanceRefinement
{
    /// <summary>The descriptor's base entry.</summary>
    None,

    /// <summary>The fact sits in a loop body or is otherwise loop-multiplied.</summary>
    InLoop,

    /// <summary>The state machine belongs to an iterator or async iterator.</summary>
    Iterator,

    /// <summary>The state machine belongs to an async method that is not an iterator.</summary>
    Async,
}

/// <summary>One upstream link in a catalog entry.</summary>
/// <param name="Title">The page title as published, confirmed when the entry was issued.</param>
/// <param name="Url">The absolute https address.</param>
public sealed record GuidanceLink(string Title, string Url);

/// <summary>
/// One line of advice and its upstream links for a catalog key at a refinement.
/// </summary>
/// <param name="Key">A descriptor id, or one of the non-descriptor keys this catalog names.</param>
/// <param name="Refinement">The attribute this entry refines, or <see cref="GuidanceRefinement.None"/> for the base entry.</param>
/// <param name="Advice">What to do, in one sentence.</param>
/// <param name="Links">Where the platform says why; at least one.</param>
public sealed record GuidanceEntry(
    string Key,
    GuidanceRefinement Refinement,
    string Advice,
    ImmutableArray<GuidanceLink> Links);

/// <summary>
/// The static guidance catalog. Lookup is by key and refinement; a refinement
/// with no entry of its own falls back to the key's base entry, and a key with
/// no entry yields null so hosts render no guidance and no placeholder.
/// </summary>
public static class GuidanceCatalog
{
    /// <summary>Catalog key for the Expectation finding: a state-machine async method in a runtime-async Library.</summary>
    public const string ExpectationStateMachineAsyncKey = "expectation.statemachine-async";

    /// <summary>Catalog key for the Library-level mark naming a Debug compilation profile.</summary>
    public const string LibraryDebugProfileKey = "library.debug";

    /// <summary>Catalog key for the Findings view's throw count column.</summary>
    public const string ThrowCountColumnKey = "column.throws";

    const string BoxingUrl = "https://learn.microsoft.com/dotnet/csharp/programming-guide/types/boxing-and-unboxing";
    const string LambdaUrl = "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/lambda-expressions";
    const string CaptureUrl = LambdaUrl + "#capture-of-outer-variables-and-variable-scope-in-lambda-expressions";
    const string IteratorsUrl = "https://learn.microsoft.com/dotnet/csharp/fundamentals/building-software/iterators";
    const string ValueTaskUrl = "https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/";
    const string PoolingBuilderUrl = "https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.poolingasyncvaluetaskmethodbuilder";
    const string PerformanceUrl = "https://learn.microsoft.com/dotnet/csharp/advanced-topics/performance/";
    const string RefSafeContextUrl = PerformanceUrl + "#ref-safe-context";
    const string ArrayPoolUrl = "https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1";
    const string UnsafeCodeUrl = "https://learn.microsoft.com/dotnet/csharp/language-reference/unsafe-code";
    const string UnsafeBestPracticesUrl = "https://learn.microsoft.com/dotnet/standard/unsafe-code/best-practices";
    const string StackallocUrl = "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/stackalloc";
    const string Ca2014Url = "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2014";
    const string RefStructUrl = "https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/ref-struct";
    const string RuntimeAsyncUrl = "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime#runtime-async";
    const string RuntimeAsyncSpecUrl = "https://github.com/dotnet/runtime/blob/main/docs/design/specs/runtime-async.md";
    const string DebuggableAttributeUrl = "https://learn.microsoft.com/dotnet/api/system.diagnostics.debuggableattribute";
    const string ExceptionsUrl = "https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions";

    static readonly GuidanceLink Boxing = new("Boxing and unboxing", BoxingUrl);
    static readonly GuidanceLink Capture = new("Capture of outer variables", CaptureUrl);
    static readonly GuidanceLink Lambda = new("Lambda expressions", LambdaUrl);
    static readonly GuidanceLink Iterators = new("Iterators", IteratorsUrl);
    static readonly GuidanceLink ValueTask = new("Understanding the whys, whats, and whens of ValueTask", ValueTaskUrl);
    static readonly GuidanceLink PoolingBuilder = new("PoolingAsyncValueTaskMethodBuilder", PoolingBuilderUrl);
    static readonly GuidanceLink Performance = new("Avoid memory allocations and data copies", PerformanceUrl);
    static readonly GuidanceLink ArrayPool = new("ArrayPool<T>", ArrayPoolUrl);
    static readonly GuidanceLink UnsafeCode = new("Unsafe code", UnsafeCodeUrl);
    static readonly GuidanceLink UnsafeBestPractices = new("Unsafe code best practices", UnsafeBestPracticesUrl);
    static readonly GuidanceLink Stackalloc = new("stackalloc expression", StackallocUrl);
    static readonly GuidanceLink Ca2014 = new("CA2014: Do not use stackalloc in loops", Ca2014Url);
    static readonly GuidanceLink RefSafeContext = new("Ref safe context", RefSafeContextUrl);
    static readonly GuidanceLink RefStruct = new("ref struct types", RefStructUrl);
    static readonly GuidanceLink RuntimeAsync = new("Runtime Async", RuntimeAsyncUrl);
    static readonly GuidanceLink RuntimeAsyncSpec = new("Runtime-async specification", RuntimeAsyncSpecUrl);
    static readonly GuidanceLink Debuggable = new("DebuggableAttribute", DebuggableAttributeUrl);
    static readonly GuidanceLink Exceptions = new("Best practices for exceptions", ExceptionsUrl);

    const string UnsafeAdvice =
        "Prefer Span<T> and function pointers behind a validated safe boundary; document the caller's obligation.";
    const string LifetimeAdvice =
        "The compiler enforces the ref-safe context; read the contract before changing the signature.";
    const string IteratorAdvice =
        "A yield method allocates on every call; return a collection or a struct enumerator when the sequence is small or hot.";

    /// <summary>Every entry, in catalog order.</summary>
    public static ImmutableArray<GuidanceEntry> Entries { get; } =
    [
        Entry("alloc.box", "Avoid the conversion to object or an interface; use generics or a span.", Boxing),
        Entry("alloc.closure", "Make the lambda static or pass the captured state as a parameter.", Capture),
        Entry("alloc.delegate", "Cache the delegate in a static field or use a static lambda.", Lambda),
        Entry(
            "alloc.statemachine",
            "A class state machine allocates on every call; an iterator's does in every build, an async method's only in a Debug build or with a non-Task return. The Member's classification selects the specific advice.",
            Iterators,
            ValueTask),
        Entry("alloc.statemachine", GuidanceRefinement.Iterator, IteratorAdvice, Iterators),
        Entry(
            "alloc.statemachine",
            GuidanceRefinement.Async,
            "A class state machine means a Debug build or a non-Task method; see the Library profile.",
            ValueTask,
            PoolingBuilder),
        Entry("alloc.enumerator", "Enumerate the concrete type, or expose a struct enumerator.", Performance),
        Entry("alloc.array", GuidanceRefinement.InLoop, "Hoist the array, or rent from ArrayPool<T>.", ArrayPool),
        Entry("alloc.new", GuidanceRefinement.InLoop, "Hoist the object or reuse one instance; measure the hot path first.", Performance),
        Entry("unsafe.deref", UnsafeAdvice, UnsafeCode, UnsafeBestPractices),
        Entry("unsafe.calli", UnsafeAdvice, UnsafeCode, UnsafeBestPractices),
        Entry("unsafe.stackalloc", "Cap the length and assign to a span.", Stackalloc),
        Entry("unsafe.stackalloc", GuidanceRefinement.InLoop, "Move the allocation outside the loop.", Ca2014),
        Entry("lifetime.ref-return", LifetimeAdvice, RefSafeContext, RefStruct),
        Entry("lifetime.stack-bound", LifetimeAdvice, RefSafeContext, RefStruct),
        Entry("lifetime.ref-struct-return", LifetimeAdvice, RefSafeContext, RefStruct),
        Entry("lifetime.pointer-return", LifetimeAdvice, RefSafeContext, RefStruct),
        Entry("lifetime.stack-escape", LifetimeAdvice, RefSafeContext, RefStruct),
        Entry(
            ExpectationStateMachineAsyncKey,
            "Confirm the method is an async iterator, async void, or uses a custom builder; otherwise recompile with runtime async on.",
            RuntimeAsync,
            RuntimeAsyncSpec),
        Entry(
            LibraryDebugProfileKey,
            "Ship an optimized build; Debug state machines and disabled JIT optimization are not representative.",
            Debuggable),
        Entry(ThrowCountColumnKey, "Prefer Try* patterns and throw helpers on cold paths.", Exceptions),
    ];

    static readonly FrozenDictionary<(string Key, GuidanceRefinement Refinement), GuidanceEntry> ByKey =
        Entries.ToFrozenDictionary(entry => (entry.Key, entry.Refinement));

    /// <summary>
    /// The entry for <paramref name="key"/> at <paramref name="refinement"/>,
    /// falling back to the key's base entry, or null when the key has none.
    /// A key whose only entries are refined, such as <c>alloc.new</c>, yields
    /// null at <see cref="GuidanceRefinement.None"/>: a straight-line visible
    /// allocation has no guidance to give.
    /// </summary>
    public static GuidanceEntry? Find(string key, GuidanceRefinement refinement = GuidanceRefinement.None)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (ByKey.TryGetValue((key, refinement), out var entry))
            return entry;
        return refinement != GuidanceRefinement.None
            && ByKey.TryGetValue((key, GuidanceRefinement.None), out entry)
            ? entry
            : null;
    }

    /// <summary>
    /// The entry for <paramref name="annotation"/>, refined by the attributes
    /// the finding itself carries. Loop context is read from the finding; the
    /// iterator and async refinements need the Member's classification, which
    /// the finding does not carry, so a caller that knows it passes a
    /// <see cref="NotableFindingContext"/> to the other overload and one that
    /// does not receives the base entry, which is written to be true of both.
    /// </summary>
    public static GuidanceEntry? For(IAnnotation annotation) =>
        For(annotation, NotableFindingContext.None);

    /// <summary>
    /// The entry for <paramref name="annotation"/> under the Member-level facts
    /// in <paramref name="context"/>: an <c>alloc.statemachine</c> finding whose
    /// Member claims an iterator or async iterator takes the iterator entry and
    /// a classic async Member the async entry. Every other finding reads its
    /// own attributes as <see cref="For(IAnnotation)"/> does.
    /// </summary>
    public static GuidanceEntry? For(IAnnotation annotation, NotableFindingContext context)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return Find(annotation.Descriptor.Id, RefinementOf(annotation, context));
    }

    /// <summary>The refinement the finding's own attributes select.</summary>
    public static GuidanceRefinement RefinementOf(IAnnotation annotation) =>
        RefinementOf(annotation, NotableFindingContext.None);

    /// <summary>The refinement the finding's attributes and the Member's claimed state machine select.</summary>
    public static GuidanceRefinement RefinementOf(IAnnotation annotation, NotableFindingContext context)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (annotation.Descriptor.Id == "alloc.statemachine")
        {
            switch (context.StateMachine)
            {
                case StateMachineClaimKind.Iterator:
                case StateMachineClaimKind.AsyncIterator:
                    return GuidanceRefinement.Iterator;
                case StateMachineClaimKind.ClassicAsync:
                    return GuidanceRefinement.Async;
            }
        }

        return NotableFindingPreset.IsLoopMultiplied(annotation)
            ? GuidanceRefinement.InLoop
            : GuidanceRefinement.None;
    }

    static GuidanceEntry Entry(string key, string advice, params GuidanceLink[] links) =>
        Entry(key, GuidanceRefinement.None, advice, links);

    static GuidanceEntry Entry(string key, GuidanceRefinement refinement, string advice, params GuidanceLink[] links) =>
        new(key, refinement, advice, [.. links]);
}
