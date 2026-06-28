using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>
/// One expectation on a target method's analysis signals. The analysis "oracle" is the set
/// of signals the analyzer actually emits — there is no compile-back equivalent — so an
/// expectation names the exact signal outcome a fixture pins. Unset fields are not checked.
/// </summary>
public sealed record AnalysisExpectation(
    int? AllocationsExactly = null,
    int? AllocationsAtLeast = null,
    string? ExceptionTypePresent = null,
    string? ExceptionTypeAbsent = null,
    string? OpportunityShapePresent = null,
    string? OpportunityShapeAbsent = null);

/// <summary>
/// How a fixture's expected outcome relates to the ideal one. Most targets are
/// <see cref="None"/> — a true positive or a true negative (must-not-flag). The interesting
/// ledger entries are the two owned boundaries: a deliberately-accepted wrong answer at the
/// SRM-only, no-referenced-assembly-loading edge.
/// </summary>
public enum OwnedBoundary
{
    None,
    FalsePositive,
    FalseNegative,
}

/// <summary>A method in the generated consumer assembly and the signal outcome it pins.</summary>
/// <param name="Boundary">
/// Whether the expected outcome is a deliberately-accepted false positive/negative (not a bug),
/// recorded so a future improvement flips the entry on purpose. <see cref="OwnedBoundary.None"/>
/// for ordinary true positives and must-not-flag negatives.
/// </param>
/// <param name="BlockedOn">
/// For a deferred owned false negative, the issue whose work is expected to flip it to detected
/// (e.g. "#1807"). Null for permanent boundaries and non-boundary targets.
/// </param>
public sealed record AnalysisFixtureTarget(
    string Method,
    AnalysisExpectation Expect,
    OwnedBoundary Boundary = OwnedBoundary.None,
    string? BlockedOn = null,
    string? Note = null);

/// <summary>
/// An addressable analysis fixture: a stable id, the consumer source compiled into the
/// inspected assembly, an optional second source compiled into a REFERENCED external assembly
/// (cross-assembly identity is central to analysis, so most fixtures need one), and the target
/// methods with their expected signals.
/// </summary>
public sealed record AnalysisFixtureDefinition(
    string Id,
    string ConsumerSource,
    string? ExternalSource,
    bool ExternalNeedsAlias,
    IReadOnlyList<AnalysisFixtureTarget> Targets,
    IReadOnlyList<string> Tags);

public sealed record AnalysisFixtureResult(
    string FixtureId,
    string Method,
    int Allocations,
    IReadOnlyList<string> ExceptionTypes,
    IReadOnlyList<string> OpportunityShapes,
    bool Passed,
    OwnedBoundary Boundary,
    string? BlockedOn,
    string Expected,
    string Actual,
    string? Failure,
    string? Note);

public sealed record AnalysisFixtureRunResult(
    string WorkspaceDirectory,
    IReadOnlyList<AnalysisFixtureResult> Results)
{
    public bool Passed => Results.All(result => result.Passed);
}

public sealed record AnalysisFixtureRunOptions(bool KeepArtifacts = false, string? TargetFramework = null)
{
    public static readonly AnalysisFixtureRunOptions Default = new();
}

/// <summary>
/// The seed analysis-fixture catalogue (#1819): the smallest, highest-signal entries already
/// pinned in the rung 1-7 tests, made addressable. The external assembly alias used by the
/// name-collision fixture.
/// </summary>
public static class AnalysisFixtureCatalog
{
    public const string ExternalAlias = "externalfix";

    public static readonly AnalysisFixtureDefinition AllocInAssemblyStruct = new(
        "alloc.value-struct-newobj.in-assembly",
        """
        namespace Fix;
        public struct InAssemblyStruct { public int V; public InAssemblyStruct(int v) => V = v; }
        public static class Consumer
        {
            static int Sink(InAssemblyStruct s) => s.V;
            public static int ConstructsInLoop(int n)
            {
                int total = 0;
                for (int i = 0; i < n; i++) total += Sink(new InAssemblyStruct(i));
                return total;
            }
        }
        """,
        ExternalSource: null,
        ExternalNeedsAlias: false,
        [
            new("ConstructsInLoop", new AnalysisExpectation(AllocationsExactly: 0),
                Note: "In-assembly struct newobj resolved via the operand-token TypeDef path (#1804)."),
        ],
        ["alloc", "value-type", "in-assembly", "rung7"]);

    public static readonly AnalysisFixtureDefinition AllocCrossAsmGenericStruct = new(
        "alloc.value-struct-newobj.cross-asm-generic",
        """
        namespace Fix;
        public static class Consumer
        {
            static int Sink(External.GenericStruct<int> s) => s.V;
            public static int ConstructsInLoop(int n)
            {
                int total = 0;
                for (int i = 0; i < n; i++) total += Sink(new External.GenericStruct<int>(i));
                return total;
            }
        }
        """,
        ExternalSource:
        """
        namespace External;
        public struct GenericStruct<T> { public T V; public GenericStruct(T v) => V = v; }
        """,
        ExternalNeedsAlias: false,
        [
            new("ConstructsInLoop", new AnalysisExpectation(AllocationsExactly: 0),
                Note: "Cross-assembly generic struct resolved via the consumer's own TypeSpec blob (#1804)."),
        ],
        ["alloc", "value-type", "cross-assembly", "generic", "rung7"]);

    public static readonly AnalysisFixtureDefinition AllocCrossAsmNonGenericStruct = new(
        "alloc.value-struct-newobj.cross-asm-nongeneric",
        """
        namespace Fix;
        public static class Consumer
        {
            static int Sink(External.ValueStruct s) => s.V;
            public static int ConstructsInLoop(int n)
            {
                int total = 0;
                for (int i = 0; i < n; i++) total += Sink(new External.ValueStruct(i));
                return total;
            }
        }
        """,
        ExternalSource:
        """
        namespace External;
        public struct ValueStruct { public int V; public ValueStruct(int v) => V = v; }
        """,
        ExternalNeedsAlias: false,
        [
            new("ConstructsInLoop", new AnalysisExpectation(AllocationsAtLeast: 1), OwnedBoundary.FalsePositive,
                Note: "Cross-assembly NON-generic user struct is a bare TypeRef, unresolvable single-assembly; counted as an OWNED false positive at the no-referenced-assembly-loading boundary (#1804)."),
        ],
        ["alloc", "value-type", "cross-assembly", "owned-boundary", "rung7"]);

    public static readonly AnalysisFixtureDefinition AllocCrossAsmNameCollision = new(
        "alloc.reftype-newobj.cross-asm-name-collision",
        $$"""
        extern alias {{ExternalAlias}};
        namespace Fix
        {
            public static class Consumer
            {
                static int Sink({{ExternalAlias}}::Collide.Shape s) => s.V;
                public static int ConstructsInLoop(int n)
                {
                    int total = 0;
                    for (int i = 0; i < n; i++) total += Sink(new {{ExternalAlias}}::Collide.Shape(i));
                    return total;
                }
            }
        }
        namespace Collide
        {
            // In-assembly STRUCT sharing the external reference type's fully-qualified name.
            public struct Shape { public int V; public Shape(int v) => V = v; }
        }
        """,
        ExternalSource:
        """
        namespace Collide;
        public sealed class Shape { public int V; public Shape(int v) => V = v; }
        """,
        ExternalNeedsAlias: true,
        [
            new("ConstructsInLoop", new AnalysisExpectation(AllocationsAtLeast: 1),
                Note: "External REFERENCE type whose FQN collides with an in-assembly struct must stay counted; display names omit assembly identity (#1809 review)."),
        ],
        ["alloc", "reference-type", "cross-assembly", "name-collision", "rung7"]);

    public static readonly AnalysisFixtureDefinition ExceptionSuffixLookalikeExternal = new(
        "exception.suffix-lookalike.external",
        """
        namespace Fix;
        public static class Consumer
        {
            static object Sink(object o) => o;
            public static object ConstructsLookalike() => Sink(new External.WidgetException());
        }
        """,
        ExternalSource:
        """
        namespace External;
        // Ends in "Exception" but is NOT a System.Exception.
        public sealed class WidgetException { }
        """,
        ExternalNeedsAlias: false,
        [
            new("ConstructsLookalike", new AnalysisExpectation(ExceptionTypePresent: "WidgetException"), OwnedBoundary.FalsePositive,
                Note: "External *Exception-named non-exception is counted via the suffix fallback; OWNED false positive at the no-referenced-assembly boundary (rung 2, #1709)."),
        ],
        ["exception", "cross-assembly", "owned-boundary", "rung2"]);

    public static readonly AnalysisFixtureDefinition ExceptionUnsuffixedExternal = new(
        "exception.unsuffixed.external",
        """
        namespace Fix;
        public static class Consumer
        {
            public static void Throws() => throw new External.ErrorState();
        }
        """,
        ExternalSource:
        """
        namespace External;
        using System;
        // A real exception whose simple name does NOT end in "Exception".
        public sealed class ErrorState : Exception { }
        """,
        ExternalNeedsAlias: false,
        [
            new("Throws", new AnalysisExpectation(ExceptionTypeAbsent: "ErrorState"), OwnedBoundary.FalseNegative,
                Note: "Real external exception not ending in 'Exception' is missed by the suffix fallback; OWNED false negative (rung 2, #1709)."),
        ],
        ["exception", "cross-assembly", "owned-boundary", "rung2"]);

    public static readonly AnalysisFixtureDefinition StringBuildAccumulationInLoop = new(
        "alloc.string-build.accumulation-in-loop",
        """
        namespace Fix;
        using System.Collections.Generic;
        public static class Consumer
        {
            // True positives: an accumulator concatenated back into itself each iteration (O(n^2)).
            public static string AppendsStringInLoop(string[] items)
            {
                var s = "";
                foreach (var x in items) s += x;
                return s;
            }
            public static string PrependsStringInLoop(string[] items)
            {
                var s = "";
                foreach (var x in items) s = x + " " + s;
                return s;
            }

            // Must-not-flag: a fresh string added to a list (never stored back into an input).
            public static List<string> ConcatsIntoListInLoop(Dictionary<string, string> pairs)
            {
                var parts = new List<string>();
                foreach (var kv in pairs) parts.Add(kv.Key + "=" + kv.Value);
                return parts;
            }
            // Must-not-flag: `s += b` outside any loop (no repeated copy).
            public static string AppendsStringOnce(string a, string b)
            {
                var s = a;
                s += b;
                return s;
            }
            // Must-not-flag (stack-aware guard): the slot is loaded only to pass to an unrelated
            // call, then reassigned from operands that do not include it.
            public static string ReassignsUnrelatedSlotInLoop(string seed, string a, string b, string[] items)
            {
                foreach (var x in items)
                {
                    System.Console.WriteLine(seed);
                    seed = a + b;
                }
                return seed;
            }

            // Owned false negative: the accumulator flows through a call (`s.Trim()`) before the
            // concat, so the bare-load bit does not survive -- conservatively not flagged today.
            public static string DerivedAccumulatorInLoop(string[] items)
            {
                var s = "";
                foreach (var x in items) s = x + " " + s.Trim();
                return s;
            }
        }
        """,
        ExternalSource: null,
        ExternalNeedsAlias: false,
        [
            new("AppendsStringInLoop", new AnalysisExpectation(OpportunityShapePresent: "string-build-in-loop")),
            new("PrependsStringInLoop", new AnalysisExpectation(OpportunityShapePresent: "string-build-in-loop")),
            new("ConcatsIntoListInLoop", new AnalysisExpectation(OpportunityShapeAbsent: "string-build-in-loop")),
            new("AppendsStringOnce", new AnalysisExpectation(OpportunityShapeAbsent: "string-build-in-loop")),
            new("ReassignsUnrelatedSlotInLoop", new AnalysisExpectation(OpportunityShapeAbsent: "string-build-in-loop"),
                Note: "Stack-aware false-positive guard (#1763 MAI review)."),
            new("DerivedAccumulatorInLoop", new AnalysisExpectation(OpportunityShapeAbsent: "string-build-in-loop"),
                OwnedBoundary.FalseNegative, BlockedOn: "#1714",
                Note: "Accumulator flows through a call before the concat; deferred broadening (#1714)."),
        ],
        ["opportunity", "string-build", "in-assembly", "rung5"]);

    public static readonly AnalysisFixtureDefinition BoxThrowPathSuppression = new(
        "suppress.box.throw-path",
        """
        namespace Fix;
        public static class Consumer
        {
            // True positive: an int boxed into an object-typed API argument escapes via the call.
            public static string BoxesIntoStringFormat(int value) => string.Format("v={0}", value);

            // Suppressed: the box feeds an exception thrown immediately -- an error-path
            // allocation (executes at most once before unwinding), so it must NOT be flagged.
            public static void ThrowsWithBoxedValue(int value)
            {
                if (value < 0)
                    throw new System.ArgumentException(string.Format("bad {0}", value));
            }

            // Owned false negative: the boxed value is formatted and passed to a throwing HELPER
            // rather than thrown directly, so the throw-path suppression does not yet reach it and
            // the box is still flagged. Deferred broadening (#1714).
            public static void ThrowsViaHelper(int value)
            {
                if (value < 0)
                    Fail(string.Format("bad {0}", value));
            }
            static void Fail(string message) => throw new System.InvalidOperationException(message);
        }
        """,
        ExternalSource: null,
        ExternalNeedsAlias: false,
        [
            new("BoxesIntoStringFormat", new AnalysisExpectation(OpportunityShapePresent: "box-value-type")),
            new("ThrowsWithBoxedValue", new AnalysisExpectation(OpportunityShapeAbsent: "box-value-type"),
                Note: "Box feeding a throw is suppressed as an error-path allocation (#1747)."),
            new("ThrowsViaHelper", new AnalysisExpectation(OpportunityShapePresent: "box-value-type"),
                OwnedBoundary.FalsePositive, BlockedOn: "#1714",
                Note: "Box feeding a throwing helper is not yet recognized as throw-path, so it is still flagged; deferred suppression broadening (#1714)."),
        ],
        ["opportunity", "box", "suppression", "in-assembly", "rung5"]);

    public static readonly AnalysisFixtureDefinition EnumeratorInterfaceForeachInLoop = new(
        "alloc.enumerator.interface-foreach-in-loop",
        """
        using System.Collections.Generic;
        namespace Fix
        {
            public static class Consumer
            {
                // True positive: foreach over an interface-typed sequence inside a loop allocates
                // the reference-type IEnumerator<T> each outer iteration.
                public static int ForeachInterfaceInLoop(IEnumerable<int> items, int times)
                {
                    var total = 0;
                    for (int i = 0; i < times; i++)
                        foreach (var x in items) total += x;
                    return total;
                }
                // Must-not-flag: foreach over the interface with no enclosing loop (one allocation).
                public static int ForeachInterfaceOnce(IEnumerable<int> items)
                {
                    var total = 0;
                    foreach (var x in items) total += x;
                    return total;
                }
                // Must-not-flag: List<T>.GetEnumerator returns the struct List<T>.Enumerator by
                // value -- no heap allocation.
                public static int ForeachConcreteListInLoop(List<int> items, int times)
                {
                    var total = 0;
                    for (int i = 0; i < times; i++)
                        foreach (var x in items) total += x;
                    return total;
                }
                // Must-not-flag (trust gate): GetEnumerator returns an untrusted lookalike defined
                // in this (unsigned) assembly, so it is not the real framework IEnumerator.
                public static int ForeachLookalikeEnumeratorInLoop(LookalikeEnumeratorCollection c, int times)
                {
                    var total = 0;
                    for (int i = 0; i < times; i++)
                        foreach (var x in c) total += x;
                    return total;
                }
            }

            public sealed class LookalikeEnumeratorCollection
            {
                public System.Collections.Generic.IEnumeratorLookalike GetEnumerator() => new();
            }
        }
        namespace System.Collections.Generic
        {
            // A type in the framework collections namespace whose name starts with "IEnumerator"
            // but is defined in this unsigned assembly, so it carries no framework public-key-token
            // and is trust-gated out of the enumerator-allocation shape (#1814).
            public sealed class IEnumeratorLookalike
            {
                public bool MoveNext() => false;
                public int Current => 0;
            }
        }
        """,
        ExternalSource: null,
        ExternalNeedsAlias: false,
        [
            new("ForeachInterfaceInLoop", new AnalysisExpectation(OpportunityShapePresent: "enumerator-allocation")),
            new("ForeachInterfaceOnce", new AnalysisExpectation(OpportunityShapeAbsent: "enumerator-allocation")),
            new("ForeachConcreteListInLoop", new AnalysisExpectation(OpportunityShapeAbsent: "enumerator-allocation"),
                Note: "Concrete List<T> uses a struct enumerator -- no heap allocation."),
            new("ForeachLookalikeEnumeratorInLoop", new AnalysisExpectation(OpportunityShapeAbsent: "enumerator-allocation"),
                Note: "Untrusted enumerator lookalike is trust-gated out (#1814)."),
        ],
        ["opportunity", "enumerator", "trust-gate", "in-assembly", "rung5"]);

    public static IReadOnlyList<AnalysisFixtureDefinition> All { get; } =
    [
        AllocInAssemblyStruct,
        AllocCrossAsmGenericStruct,
        AllocCrossAsmNonGenericStruct,
        AllocCrossAsmNameCollision,
        ExceptionSuffixLookalikeExternal,
        ExceptionUnsuffixedExternal,
        StringBuildAccumulationInLoop,
        BoxThrowPathSuppression,
        EnumeratorInterfaceForeachInLoop,
    ];

    public static IReadOnlyList<AnalysisFixtureDefinition> Select(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || selector is "all")
            return All;
        return [.. All.Where(fixture =>
            fixture.Id.Equals(selector, StringComparison.OrdinalIgnoreCase)
            || fixture.Id.StartsWith(selector, StringComparison.OrdinalIgnoreCase)
            || fixture.Tags.Contains(selector, StringComparer.OrdinalIgnoreCase))];
    }
}
