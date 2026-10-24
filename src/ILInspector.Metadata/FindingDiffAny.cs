using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using InertText;
using Inspector.Findings;

namespace ILInspector.Metadata;

/// <summary>Whether one axis of a Type differs between two Library images.</summary>
public enum FindingDiffAnyState
{
    /// <summary>Every fact the axis compares is equal.</summary>
    Unchanged,

    /// <summary>A compared fact differs. The complete diff may present it differently.</summary>
    Changed,

    /// <summary>The axis could not be decided, for example because a row could not be decoded.</summary>
    Indeterminate,

    /// <summary>The comparison was asked not to decide this axis.</summary>
    NotCompared,
}

/// <summary>The axes one comparison decides.</summary>
public enum FindingDiffAnyAxes
{
    /// <summary>Both the API and the Body axis.</summary>
    ApiAndBody,

    /// <summary>The API axis only; every Body state is <see cref="FindingDiffAnyState.NotCompared"/>.</summary>
    Api,
}

/// <summary>
/// The API and Body states of one Type.
/// </summary>
/// <param name="FullName">
/// The <c>ApiType.FullName</c> spelling: <c>.</c> between nested names and the
/// metadata backtick arity. It is a display name and need not be unique.
/// </param>
/// <param name="Identifier">
/// The injective <see cref="MetadataTypeDefinitionName.ToEscapedFullName"/>
/// spelling, which hosts use to join navigation Types. A Type whose name cannot
/// be read is identified by its row token, prefixed with <c>!</c>.
/// </param>
public sealed record FindingDiffAnyTypeState(
    string FullName,
    FindingDiffAnyState Api,
    FindingDiffAnyState Body,
    string Identifier);

/// <summary>The work one comparison performed.</summary>
public sealed record FindingDiffAnyReceipt(
    int Types,
    int BodiesCompared,
    long IlBytesCompared);

/// <summary>
/// A provision for the Body decision of one paired method, the seam at which
/// Finding census step 2b runs lockstep over two IL cursors in place of the
/// walk (<c>docs/design/finding-census.md#provisions</c>). It receives the
/// two images and the two method rows the comparison paired, under the Body
/// policy, and answers as Exists answers: <see cref="FindingExistsDecision.Unchanged"/>,
/// <see cref="FindingExistsDecision.Changed"/>, or
/// <see cref="FindingExistsDecision.Indeterminate"/>, which the comparison
/// folds as it folds a body the walk could not read. The default is the walk.
/// </summary>
public delegate FindingExistsDecision FindingDiffAnyBodyReader(
    PEReader before,
    MethodDefinitionHandle beforeMethod,
    PEReader after,
    MethodDefinitionHandle afterMethod);

public sealed record FindingDiffAnyResult(
    ImmutableArray<FindingDiffAnyTypeState> Types,
    FindingDiffAnyReceipt Receipt);

/// <summary>
/// Decides, for every Type of one Library image pair, whether its API and its
/// implementation differ, without building either complete diff.
/// </summary>
/// <remarks>
/// <para>
/// The API axis compares the Public API facts of the Type and its members:
/// Type flags, base Type, interfaces, generic
/// parameters and constraints, custom attributes, layout, member signatures
/// and flags, parameter names and defaults, constants, and explicit interface
/// implementations. The Body axis compares the implementation of every
/// member, public or not: IL with the equality of canonical IL comparison
/// under the Body policy, and the declaration facts of non-public members
/// and compiler-generated nested Types.
/// </para>
/// <para>
/// Tokens compare by symbolic name, never by token number or referenced
/// assembly identity: a Type-level row's name is resolved once per side, a
/// member row's once per unit that refers to it, and a pair of operand rows
/// proven equal is remembered for the comparison. Compiler-generated nested Types
/// (state machines, closures, local-function holders) belong to their nearest
/// declared Type. Top-level compiler-generated Types are not reported; a
/// body that references them still compares their names.
/// </para>
/// <para>
/// <c>Unchanged</c> is sound for the compared facts. <c>Changed</c> is
/// decided by a fact a complete view presents, except a declaration fact of
/// a non-public member until the declaration view lands. A row or body that
/// cannot be decoded before a difference is decided makes the unsettled axes
/// of its Type <c>Indeterminate</c>.
/// </para>
/// </remarks>
public static class FindingDiffAny
{
    public static FindingDiffAnyResult Compare(
        PEReader before,
        PEReader after,
        FindingDiffAnyAxes axes = FindingDiffAnyAxes.ApiAndBody,
        CancellationToken cancellationToken = default)
        => Compare(before, after, bodyReader: null, axes, cancellationToken);

    /// <summary>Compares with the Body decision of each paired method read by <paramref name="bodyReader"/> in place of the walk.</summary>
    public static FindingDiffAnyResult Compare(
        PEReader before,
        PEReader after,
        FindingDiffAnyBodyReader? bodyReader,
        FindingDiffAnyAxes axes = FindingDiffAnyAxes.ApiAndBody,
        CancellationToken cancellationToken = default)
    {
        var comparison = new FindingDiffAnyComparison(axes, bodyReader);
        comparison.Step(before, after, Timeout.InfiniteTimeSpan, cancellationToken);
        return comparison.Result!;
    }

    /// <summary>
    /// Returns the first compared fact that differs on each axis of one Type,
    /// or null for an axis whose facts are equal. A body difference inside IL
    /// is named by its method.
    /// </summary>
    public static (string? Api, string? Body) Explain(
        PEReader before,
        PEReader after,
        string fullName)
    {
        try
        {
            return ExplainCore(before, after, fullName);
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            return ("malformed: " + ex.Message, "malformed: " + ex.Message);
        }
    }

    static (string? Api, string? Body) ExplainCore(
        PEReader before,
        PEReader after,
        string fullName)
    {
        var interner = new SymbolKeyInterner<FindingDiffAnyPart>();
        var a = new Side(before, interner);
        var b = new Side(after, interner);
        Unit? unitA = a.Units().Entries.Select(entry => entry.Unit).FirstOrDefault(unit => unit.FullName == fullName);
        Unit? unitB = b.Units().Entries.Select(entry => entry.Unit).FirstOrDefault(unit => unit.FullName == fullName);
        if (unitA is null || unitB is null)
            return ("one-sided", "one-sided");

        string? api = FirstDifference(a, a.ApiCensus(unitA), b, b.ApiCensus(unitB));
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        string? body = FirstDifference(
            a,
            a.BodyCensus(unitA, ownerVisible, out var methodsA),
            b,
            b.BodyCensus(unitB, ownerVisible, out var methodsB));
        if (body is null)
        {
            var work = new Work();
            foreach ((KeyRef key, MethodDefinitionHandle methodA) in methodsA)
            {
                if (methodsB.TryGetValue(key, out MethodDefinitionHandle methodB)
                    && !BodyEqual(a, methodA, b, methodB, work))
                {
                    body = "IL " + Describe(key);
                    break;
                }
            }
        }
        return (api, body);
    }

    // Each side's census is staged in its transient builder; the slots compare
    // through those builders, as the staged keys they name would.
    static bool CensusesEqual(Side a, List<SymbolKeySlot> censusA, Side b, List<SymbolKeySlot> censusB)
        => a.Transient.SequenceEqual(CollectionsMarshal.AsSpan(censusA), b.Transient, CollectionsMarshal.AsSpan(censusB));

    static string? FirstDifference(
        Side a,
        List<SymbolKeySlot> before,
        Side b,
        List<SymbolKeySlot> after)
    {
        List<StagedSymbolKey<FindingDiffAnyPart>> keysBefore = before.ConvertAll(a.Transient.Resolve);
        List<StagedSymbolKey<FindingDiffAnyPart>> keysAfter = after.ConvertAll(b.Transient.Resolve);
        string? onlyBefore = keysBefore.Except(keysAfter).Select(key => SymbolKeyText.Describe(key, a.Interner)).FirstOrDefault();
        string? onlyAfter = keysAfter.Except(keysBefore).Select(key => SymbolKeyText.Describe(key, a.Interner)).FirstOrDefault();
        return onlyBefore is null && onlyAfter is null
            ? null
            : $"- {onlyBefore}\n+ {onlyAfter}";
    }

    internal static FindingDiffAnyTypeState OneSided(Side side, Unit unit, FindingDiffAnyAxes axes)
    {
        FindingDiffAnyState presence = axes is FindingDiffAnyAxes.Api
            ? FindingDiffAnyState.NotCompared
            : FindingDiffAnyState.Changed;
        if (unit.Malformed)
        {
            return new FindingDiffAnyTypeState(
                unit.FullName,
                FindingDiffAnyState.Indeterminate,
                axes is FindingDiffAnyAxes.Api ? FindingDiffAnyState.NotCompared : FindingDiffAnyState.Indeterminate,
                unit.Identifier);
        }
        FindingDiffAnyState api;
        try
        {
            api = side.IsVisible(unit.Owner) ? FindingDiffAnyState.Changed : FindingDiffAnyState.Unchanged;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            side.Abandon();
            api = FindingDiffAnyState.Indeterminate;
        }
        finally
        {
            side.EndUnit();
        }
        return new FindingDiffAnyTypeState(unit.FullName, api, presence, unit.Identifier);
    }

    internal static FindingDiffAnyTypeState CompareUnit(
        Side a,
        Unit unitA,
        Side b,
        Unit unitB,
        FindingDiffAnyAxes axes,
        Work work)
    {
        FindingDiffAnyState api = FindingDiffAnyState.Indeterminate;
        FindingDiffAnyState body = axes is FindingDiffAnyAxes.Api
            ? FindingDiffAnyState.NotCompared
            : FindingDiffAnyState.Indeterminate;
        if (unitA.Malformed || unitB.Malformed)
            return new FindingDiffAnyTypeState(unitA.FullName, api, body, unitA.Identifier);
        try
        {
            api = CensusesEqual(a, a.ApiCensus(unitA), b, b.ApiCensus(unitB))
                ? FindingDiffAnyState.Unchanged
                : FindingDiffAnyState.Changed;
            if (axes is FindingDiffAnyAxes.Api)
                return new FindingDiffAnyTypeState(unitA.FullName, api, body, unitA.Identifier);
            body = BodiesEqual(a, unitA, b, unitB, work)
                ? FindingDiffAnyState.Unchanged
                : FindingDiffAnyState.Changed;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            a.Abandon();
            b.Abandon();
        }
        finally
        {
            a.EndUnit();
            b.EndUnit();
        }
        return new FindingDiffAnyTypeState(unitA.FullName, api, body, unitA.Identifier);
    }

    static bool BodiesEqual(Side a, Unit unitA, Side b, Unit unitB, Work work)
    {
        // A Type visible on either side partitions its facts as visible on
        // both, so a visibility change is an API fact only.
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        List<SymbolKeySlot> censusA = a.BodyCensus(unitA, ownerVisible, out Dictionary<KeyRef, MethodDefinitionHandle> methodsA);
        List<SymbolKeySlot> censusB = b.BodyCensus(unitB, ownerVisible, out Dictionary<KeyRef, MethodDefinitionHandle> methodsB);
        if (!CensusesEqual(a, censusA, b, censusB))
            return false;

        // Equal censuses declare the same non-public members. A public method
        // on one side only is an API fact; its body is not compared.
        foreach ((KeyRef key, MethodDefinitionHandle methodA) in methodsA)
        {
            if (!methodsB.TryGetValue(key, out MethodDefinitionHandle methodB))
                continue;
            work.Bodies++;
            if (!BodyEqual(a, methodA, b, methodB, work))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether two paired methods have equal bodies under the Body policy: the
    /// equality of canonical IL comparison with every operand compared by
    /// name, without assembly identity. A body present on one side only
    /// differs. A body the walk cannot decode or resolve before it decides a
    /// difference is malformed, which the caller reads as Indeterminate.
    /// </summary>
    static bool BodyEqual(
        Side a,
        MethodDefinitionHandle handleA,
        Side b,
        MethodDefinitionHandle handleB,
        Work work)
    {
        if (work.BodyReader is { } reader)
        {
            // The provision reads presence and the body alike; an undecided
            // pair is the malformed body the caller reads as Indeterminate.
            return reader(a.Pe, handleA, b.Pe, handleB) switch
            {
                FindingExistsDecision.Unchanged => true,
                FindingExistsDecision.Changed => false,
                _ => throw new BadImageFormatException("The body reader could not decide the paired methods."),
            };
        }
        MethodDefinition methodA = a.Md.GetMethodDefinition(handleA);
        MethodDefinition methodB = b.Md.GetMethodDefinition(handleB);
        if ((methodA.RelativeVirtualAddress == 0) != (methodB.RelativeVirtualAddress == 0))
            return false;
        if (methodA.RelativeVirtualAddress == 0)
            return true;

        RawMethodBodyView bodyA = RawMethodBodyView.Read(a.Pe, methodA.RelativeVirtualAddress);
        RawMethodBodyView bodyB = RawMethodBodyView.Read(b.Pe, methodB.RelativeVirtualAddress);
        BlobReader readerA = bodyA.IL;
        BlobReader readerB = bodyB.IL;
        work.IlBytes += readerA.Length;
        return work.Walk.Equal(a, readerA, bodyA, b, readerB, bodyB);
    }

    /// <summary>
    /// Gate support: the Body verdict of every method pair under Finding Diff Any's
    /// own pairing of two paired Types' methods, including the methods of
    /// their compiler-generated nested Types. A Type whose census cannot be
    /// read contributes no pairs.
    /// </summary>
    internal static List<BodyVerdict> BodyVerdicts(PEReader before, PEReader after)
    {
        var interner = new SymbolKeyInterner<FindingDiffAnyPart>();
        var a = new Side(before, interner);
        var b = new Side(after, interner);
        var work = new Work();
        var verdicts = new List<BodyVerdict>();
        UnitTable unitsB = b.Units();
        foreach ((SymbolKey<FindingDiffAnyPart> key, Unit unitA) in a.Units().Entries)
        {
            if (!unitsB.TryGetValue(key, out Unit? unitB) || unitA.Malformed || unitB.Malformed)
                continue;
            Dictionary<KeyRef, MethodDefinitionHandle> methodsA;
            Dictionary<KeyRef, MethodDefinitionHandle> methodsB;
            try
            {
                bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
                a.BodyCensus(unitA, ownerVisible, out methodsA);
                b.BodyCensus(unitB, ownerVisible, out methodsB);
            }
            catch (Exception ex) when (IsMalformed(ex))
            {
                a.Abandon();
                b.Abandon();
                a.EndUnit();
                b.EndUnit();
                continue;
            }
            foreach ((KeyRef method, MethodDefinitionHandle methodA) in methodsA)
            {
                if (!methodsB.TryGetValue(method, out MethodDefinitionHandle methodB))
                    continue;
                FindingDiffAnyState body;
                try
                {
                    body = BodyEqual(a, methodA, b, methodB, work)
                        ? FindingDiffAnyState.Unchanged
                        : FindingDiffAnyState.Changed;
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    a.Abandon();
                    b.Abandon();
                    body = FindingDiffAnyState.Indeterminate;
                }
                verdicts.Add(new BodyVerdict(unitA.FullName, Describe(method), methodA, methodB, body));
            }
            a.EndUnit();
            b.EndUnit();
        }
        return verdicts;
    }

    /// <summary>One paired method's Body verdict, for the per-method gates.</summary>
    internal sealed record BodyVerdict(
        string Type,
        string Method,
        MethodDefinitionHandle Before,
        MethodDefinitionHandle After,
        FindingDiffAnyState Body);

    /// <summary>
    /// One lockstep walk over two IL streams under the Body policy, with
    /// scratch buffers reused across bodies so that a walk allocates nothing.
    /// </summary>
    /// <remarks>
    /// The walk pairs the instructions of the two bodies by position and
    /// stops at the first difference: an opcode outside the pair's family,
    /// a differing immediate, slot, user string, or symbolic operand, or a
    /// stream that ends first. Every <c>ldc.i4</c> form is one family
    /// compared by value, and a short branch is the family of its long form.
    /// Branch and switch targets are decided once the instruction map is
    /// complete, by the index of the instruction each target starts; a
    /// completed stream's targets are checked for an instruction start
    /// before any difference the end decides, as a census cursor ends
    /// <c>Failed</c> before <c>Complete</c>.
    /// A <c>calli</c> stand-alone signature compares by its bytes, as
    /// canonical comparison compares it. Once both streams are complete and
    /// aligned, the exception clauses compare as canonical comparison's
    /// clause atoms compare: clause by clause in declaration order, by kind,
    /// by catch Type as a symbolic name, and by each extent boundary as the
    /// instruction it starts, where the end of the body aligns with the end
    /// of the body. Body size, the local signature, stack size, and
    /// <c>init locals</c> are not read.
    /// An undefined opcode, a truncated operand, an operand whose token kind
    /// its opcode does not take, a target that starts no instruction (one
    /// inside an instruction, at the end of the body, or outside it) or
    /// starts one that follows a prefix, and a body that ends with a prefix
    /// are what canonical comparison cannot decode, so they are malformed
    /// here too, unless a difference was decided first.
    /// </remarks>
    internal sealed class BodyWalk
    {
        // Instruction start offsets per side, whether each start may be a
        // branch target, and the pending targets per side.
        int[] _startsA = new int[256];
        int[] _startsB = new int[256];
        bool[] _admitted = new bool[256];
        int[] _targetsA = new int[64];
        int[] _targetsB = new int[64];

        public bool Equal(
            Side a,
            BlobReader readerA,
            RawMethodBodyView bodyA,
            Side b,
            BlobReader readerB,
            RawMethodBodyView bodyB)
        {
            int count = 0;
            int targets = 0;
            bool prefixed = false;
            while (readerA.RemainingBytes > 0 && readerB.RemainingBytes > 0)
            {
                if (count == _startsA.Length)
                {
                    Array.Resize(ref _startsA, count * 2);
                    Array.Resize(ref _startsB, count * 2);
                    Array.Resize(ref _admitted, count * 2);
                }
                _startsA[count] = readerA.Offset;
                _startsB[count] = readerB.Offset;
                _admitted[count] = !prefixed;
                count++;

                ILOpCode opCodeA = ReadOpCode(ref readerA);
                ILOpCode opCodeB = ReadOpCode(ref readerB);
                Operand operandA = OperandOf(opCodeA);
                Operand operandB = OperandOf(opCodeB);
                if (operandA == Operand.LoadInt32 || operandB == Operand.LoadInt32)
                {
                    if (operandA != operandB
                        || LoadedInt32(opCodeA, ref readerA) != LoadedInt32(opCodeB, ref readerB))
                    {
                        return false;
                    }
                    prefixed = false;
                    continue;
                }
                if (Family(opCodeA) != Family(opCodeB))
                    return false;
                prefixed = IsPrefix(opCodeA);

                switch (operandA)
                {
                    case Operand.None:
                        break;
                    case Operand.Int8:
                        if (readerA.ReadByte() != readerB.ReadByte())
                            return false;
                        break;
                    case Operand.Int16:
                        if (readerA.ReadInt16() != readerB.ReadInt16())
                            return false;
                        break;
                    case Operand.Int32:
                        if (readerA.ReadInt32() != readerB.ReadInt32())
                            return false;
                        break;
                    case Operand.Int64:
                        if (readerA.ReadInt64() != readerB.ReadInt64())
                            return false;
                        break;
                    case Operand.Branch:
                    {
                        int displacementA = IsShortBranch(opCodeA) ? readerA.ReadSByte() : readerA.ReadInt32();
                        int displacementB = IsShortBranch(opCodeB) ? readerB.ReadSByte() : readerB.ReadInt32();
                        PushTarget(targets++, readerA.Offset + displacementA, readerB.Offset + displacementB);
                        break;
                    }
                    case Operand.Switch:
                    {
                        int arms = readerA.ReadInt32();
                        if (arms != readerB.ReadInt32())
                            return false;
                        if (arms < 0 || arms > readerA.RemainingBytes / sizeof(int) || arms > readerB.RemainingBytes / sizeof(int))
                            throw new BadImageFormatException("The switch table does not fit the body.");
                        int baseA = readerA.Offset + sizeof(int) * arms;
                        int baseB = readerB.Offset + sizeof(int) * arms;
                        for (int i = 0; i < arms; i++)
                            PushTarget(targets++, baseA + readerA.ReadInt32(), baseB + readerB.ReadInt32());
                        break;
                    }
                    case Operand.String:
                        if (!UserStrings.Equal(a.Strings, readerA.ReadInt32(), b.Strings, readerB.ReadInt32()))
                            return false;
                        break;
                    case Operand.Signature:
                        if (!SignatureBytesEqual(
                                a, MetadataTokens.EntityHandle(readerA.ReadInt32()),
                                b, MetadataTokens.EntityHandle(readerB.ReadInt32())))
                        {
                            return false;
                        }
                        break;
                    default:
                    {
                        EntityHandle handleA = MetadataTokens.EntityHandle(readerA.ReadInt32());
                        EntityHandle handleB = MetadataTokens.EntityHandle(readerB.ReadInt32());
                        RequireOperandKind(operandA, a, handleA);
                        RequireOperandKind(operandA, b, handleB);
                        if (!Side.OperandsEqual(a, handleA, b, handleB))
                            return false;
                        break;
                    }
                }
            }
            if (prefixed)
                throw new BadImageFormatException("The IL ends with a prefix.");

            // A stream that has completed holds a complete instruction map,
            // so each of its targets is decidable now; one that starts no
            // instruction makes the body malformed before any difference the
            // end decides. A stream that continues keeps its targets beyond
            // the paired instructions undecided.
            bool completeA = readerA.RemainingBytes == 0;
            bool completeB = readerB.RemainingBytes == 0;
            for (int i = 0; i < targets; i++)
            {
                if (completeA || _targetsA[i] < readerA.Offset)
                    TargetIndex(_startsA, count, _targetsA[i]);
                if (completeB || _targetsB[i] < readerB.Offset)
                    TargetIndex(_startsB, count, _targetsB[i]);
            }
            if (!completeA || !completeB)
            {
                // The stream that continues is the difference, once what it
                // continues with is an opcode that can be decoded.
                OperandOf(ReadOpCode(ref (completeA ? ref readerB : ref readerA)));
                return false;
            }
            for (int i = 0; i < targets; i++)
            {
                if (TargetIndex(_startsA, count, _targetsA[i]) != TargetIndex(_startsB, count, _targetsB[i]))
                    return false;
            }
            return ClausesEqual(a, bodyA, readerA.Length, b, bodyB, readerB.Length, count);
        }

        /// <summary>
        /// The exception clauses of two bodies whose streams are complete and
        /// aligned. Every boundary of both tables is checked for an
        /// instruction start first, so a table with such a boundary is
        /// undecidable before any difference it holds. The decoder's further
        /// topology rules (nesting, ordering, empty extents) are canonical
        /// comparison's to reject; a table they reject and this walk decides
        /// is a contained disagreement on malformed input.
        /// </summary>
        bool ClausesEqual(
            Side a,
            RawMethodBodyView bodyA,
            int lengthA,
            Side b,
            RawMethodBodyView bodyB,
            int lengthB,
            int count)
        {
            RawExceptionRegionEnumerator validationA = bodyA.ExceptionRegions;
            while (validationA.MoveNext(out RawExceptionRegion regionA))
                RequireBoundaries(_startsA, count, lengthA, regionA);
            RawExceptionRegionEnumerator validationB = bodyB.ExceptionRegions;
            while (validationB.MoveNext(out RawExceptionRegion regionB))
                RequireBoundaries(_startsB, count, lengthB, regionB);

            RawExceptionRegionEnumerator regionsA = bodyA.ExceptionRegions;
            RawExceptionRegionEnumerator regionsB = bodyB.ExceptionRegions;
            while (regionsA.MoveNext(out RawExceptionRegion x))
            {
                if (!regionsB.MoveNext(out RawExceptionRegion y))
                    return false;
                if (x.Kind != y.Kind)
                    return false;
                if (x.Kind == ExceptionRegionKind.Catch)
                {
                    RequireOperandKind(Operand.Type, a, x.CatchType);
                    RequireOperandKind(Operand.Type, b, y.CatchType);
                    if (a.ReferenceKey(x.CatchType) != b.ReferenceKey(y.CatchType))
                        return false;
                }
                if (BoundaryIndex(_startsA, count, lengthA, x.TryOffset) != BoundaryIndex(_startsB, count, lengthB, y.TryOffset)
                    || BoundaryIndex(_startsA, count, lengthA, x.TryOffset + x.TryLength) != BoundaryIndex(_startsB, count, lengthB, y.TryOffset + y.TryLength)
                    || BoundaryIndex(_startsA, count, lengthA, x.HandlerOffset) != BoundaryIndex(_startsB, count, lengthB, y.HandlerOffset)
                    || BoundaryIndex(_startsA, count, lengthA, x.HandlerOffset + x.HandlerLength) != BoundaryIndex(_startsB, count, lengthB, y.HandlerOffset + y.HandlerLength))
                {
                    return false;
                }
                if (x.Kind == ExceptionRegionKind.Filter
                    && BoundaryIndex(_startsA, count, lengthA, x.FilterOffset) != BoundaryIndex(_startsB, count, lengthB, y.FilterOffset))
                {
                    return false;
                }
            }
            return !regionsB.MoveNext(out _);
        }

        void RequireBoundaries(int[] starts, int count, int length, RawExceptionRegion region)
        {
            BoundaryIndex(starts, count, length, region.TryOffset);
            BoundaryIndex(starts, count, length, region.TryOffset + region.TryLength);
            BoundaryIndex(starts, count, length, region.HandlerOffset);
            BoundaryIndex(starts, count, length, region.HandlerOffset + region.HandlerLength);
            if (region.Kind == ExceptionRegionKind.Filter)
                BoundaryIndex(starts, count, length, region.FilterOffset);
        }

        /// <summary>
        /// The index of the instruction an extent boundary starts, or the
        /// instruction count for the end of the body; any other boundary is
        /// one the decoder rejects.
        /// </summary>
        int BoundaryIndex(int[] starts, int count, int length, int offset)
        {
            if (offset == length)
                return count;
            if (offset < 0 || offset > length)
                throw new BadImageFormatException($"Exception clause boundary IL_{offset:X4} lies outside the body.");
            return TargetIndex(starts, count, offset);
        }

        void PushTarget(int index, int targetA, int targetB)
        {
            if (index == _targetsA.Length)
            {
                Array.Resize(ref _targetsA, index * 2);
                Array.Resize(ref _targetsB, index * 2);
            }
            _targetsA[index] = targetA;
            _targetsB[index] = targetB;
        }

        /// <summary>
        /// The index of the instruction that starts at <paramref name="target"/>.
        /// A target that starts no instruction, inside one, at the end of the
        /// body, or outside it, or that starts an instruction following a
        /// prefix, cannot be decoded.
        /// </summary>
        int TargetIndex(int[] starts, int count, int target)
        {
            int index = Array.BinarySearch(starts, 0, count, target);
            if (index < 0 || !_admitted[index])
                throw new BadImageFormatException($"Branch target IL_{target:X4} does not align with an instruction.");
            return index;
        }

        static ILOpCode ReadOpCode(ref BlobReader reader)
        {
            int opCode = reader.ReadByte();
            if (opCode == 0xFE)
                opCode = 0xFE00 | reader.ReadByte();
            return (ILOpCode)opCode;
        }

        /// <summary>The value every <c>ldc.i4</c> form loads.</summary>
        static int LoadedInt32(ILOpCode opCode, ref BlobReader reader) => opCode switch
        {
            ILOpCode.Ldc_i4_s => reader.ReadSByte(),
            ILOpCode.Ldc_i4 => reader.ReadInt32(),
            _ => (int)opCode - (int)ILOpCode.Ldc_i4_0,
        };

        /// <summary>A short branch belongs to the family of its long form.</summary>
        static ILOpCode Family(ILOpCode opCode) => opCode switch
        {
            >= ILOpCode.Br_s and <= ILOpCode.Blt_un_s
                => (ILOpCode)((int)opCode - (int)ILOpCode.Br_s + (int)ILOpCode.Br),
            ILOpCode.Leave_s => ILOpCode.Leave,
            _ => opCode,
        };

        static bool IsShortBranch(ILOpCode opCode)
            => opCode is (>= ILOpCode.Br_s and <= ILOpCode.Blt_un_s) or ILOpCode.Leave_s;

        static bool IsPrefix(ILOpCode opCode)
            => opCode is ILOpCode.Constrained or ILOpCode.Readonly or ILOpCode.Tail
                or ILOpCode.Unaligned or ILOpCode.Volatile or NoPrefix;

        /// <summary>Canonical comparison compares a stand-alone signature by its bytes.</summary>
        static bool SignatureBytesEqual(Side a, EntityHandle handleA, Side b, EntityHandle handleB)
        {
            if (handleA.Kind != HandleKind.StandaloneSignature || handleB.Kind != HandleKind.StandaloneSignature)
                throw new BadImageFormatException("A calli operand is not a stand-alone signature.");
            BlobReader x = a.Md.GetBlobReader(a.Md.GetStandaloneSignature((StandaloneSignatureHandle)handleA).Signature);
            BlobReader y = b.Md.GetBlobReader(b.Md.GetStandaloneSignature((StandaloneSignatureHandle)handleB).Signature);
            if (x.Length != y.Length)
                return false;
            while (x.RemainingBytes > 0)
            {
                if (x.ReadByte() != y.ReadByte())
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Rejects a token canonical comparison's operand resolution rejects:
        /// a kind the opcode does not take, a member reference whose parent is
        /// not a Type (such as Roslyn's vararg call site, whose parent is the
        /// method definition), or a method specification of such a reference.
        /// </summary>
        static void RequireOperandKind(Operand operand, Side side, EntityHandle handle)
        {
            bool admitted = operand switch
            {
                Operand.Method => handle.Kind is HandleKind.MethodDefinition
                    || IsMemberReference(side, handle, MemberReferenceKind.Method)
                    || IsMethodSpecification(side, handle),
                Operand.Field => handle.Kind is HandleKind.FieldDefinition
                    || IsMemberReference(side, handle, MemberReferenceKind.Field),
                Operand.Type => IsType(handle),
                _ => IsType(handle)
                    || handle.Kind is HandleKind.MethodDefinition or HandleKind.FieldDefinition
                    || IsMemberReference(side, handle, kind: null)
                    || IsMethodSpecification(side, handle),
            };
            if (!admitted)
                throw new BadImageFormatException($"An operand names a {handle.Kind} canonical comparison does not resolve.");
        }

        static bool IsType(EntityHandle handle)
            => handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification;

        /// <summary>A member reference of the kind, or any kind, whose parent is a Type.</summary>
        static bool IsMemberReference(Side side, EntityHandle handle, MemberReferenceKind? kind)
        {
            if (handle.Kind != HandleKind.MemberReference)
                return false;
            MemberReference member = side.Md.GetMemberReference((MemberReferenceHandle)handle);
            return (kind is null || member.GetKind() == kind) && IsType(member.Parent);
        }

        static bool IsMethodSpecification(Side side, EntityHandle handle)
        {
            if (handle.Kind != HandleKind.MethodSpecification)
                return false;
            EntityHandle method = side.Md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
            return method.Kind is HandleKind.MethodDefinition
                || IsMemberReference(side, method, MemberReferenceKind.Method);
        }
    }

    enum Operand
    {
        None,
        Int8,
        Int16,
        Int32,
        Int64,
        LoadInt32,
        Branch,
        Switch,
        String,
        Signature,
        Method,
        Field,
        Type,
        Token,
    }

    // The `no.` prefix (0xFE 0x19) takes a one-byte operand and has no ILOpCode member.
    const ILOpCode NoPrefix = (ILOpCode)0xFE19;

    // Every defined opcode, indexed by its second byte for the 0xFE page.
    static readonly bool[] KnownOneByte = new bool[256];
    static readonly bool[] KnownTwoByte = new bool[256];

    static FindingDiffAny()
    {
        foreach (ILOpCode opCode in Enum.GetValues<ILOpCode>())
        {
            int value = (int)opCode;
            if (value <= 0xFF)
                KnownOneByte[value] = true;
            else if ((value & 0xFF00) == 0xFE00)
                KnownTwoByte[value & 0xFF] = true;
        }
    }

    static bool IsKnown(ILOpCode opCode)
    {
        int value = (int)opCode;
        return value <= 0xFF ? KnownOneByte[value] : KnownTwoByte[value & 0xFF];
    }

    /// <summary>The operand an opcode takes, or malformed for an opcode ECMA-335 does not define.</summary>
    static Operand OperandOf(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            or ILOpCode.Unaligned or NoPrefix => Operand.Int8,
        >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4 => Operand.LoadInt32,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => Operand.Int16,
        ILOpCode.Ldc_r4 => Operand.Int32,
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => Operand.Int64,
        (>= ILOpCode.Br_s and <= ILOpCode.Blt_un_s) or ILOpCode.Leave_s
            or (>= ILOpCode.Br and <= ILOpCode.Blt_un) or ILOpCode.Leave => Operand.Branch,
        ILOpCode.Switch => Operand.Switch,
        ILOpCode.Ldstr => Operand.String,
        ILOpCode.Calli => Operand.Signature,
        ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj
            or ILOpCode.Ldftn or ILOpCode.Ldvirtftn => Operand.Method,
        ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
            or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld => Operand.Field,
        ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Castclass or ILOpCode.Isinst
            or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Stobj or ILOpCode.Box
            or ILOpCode.Newarr or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
            or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Initobj
            or ILOpCode.Constrained or ILOpCode.Sizeof => Operand.Type,
        ILOpCode.Ldtoken => Operand.Token,
        _ when IsKnown(opCode) => Operand.None,
        _ => throw new BadImageFormatException($"Unknown IL opcode 0x{(int)opCode:X}."),
    };

    // A Symbol Key state error is the producer's, never the image's: it must
    // surface, not classify as a malformed Type.
    static bool IsMalformed(Exception ex)
        => ex is not SymbolKeyStateException
            && ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentOutOfRangeException
                or ArgumentException
                or IndexOutOfRangeException;

    /// <summary>The Type units of one side, read row by row.</summary>
    internal sealed class UnitTable
    {
        /// <summary>Committed units in row order, keyed by frozen Type keys.</summary>
        public List<(SymbolKey<FindingDiffAnyPart> Key, Unit Unit)> Entries { get; } = [];

        public Dictionary<SymbolKey<FindingDiffAnyPart>, int> Index { get; } =
            new(SymbolKeyComparer<FindingDiffAnyPart>.Instance);

        /// <summary>The chunk being read: its keys are staged until the chunk commits.</summary>
        public List<(KeyRef Key, Unit Unit)> Pending { get; } = [];

        public Dictionary<KeyRef, int> PendingIndex { get; } = [];

        public List<(TypeDefinitionHandle Type, TypeDefinitionHandle Owner)> Generated { get; } = [];

        // Row 1 is the <Module> pseudo-Type.
        public int NextRow { get; set; } = 2;

        public int Count => Entries.Count;

        public bool TryGetValue(SymbolKey<FindingDiffAnyPart> key, [MaybeNullWhen(false)] out Unit unit)
        {
            if (Index.TryGetValue(key, out int index))
            {
                unit = Entries[index].Unit;
                return true;
            }
            unit = null;
            return false;
        }

        /// <summary>Finds a committed or pending unit by a retained key, frozen or staged.</summary>
        public bool TryFind(KeyRef key, out bool pending, out int index)
        {
            if (PendingIndex.TryGetValue(key, out index))
            {
                pending = true;
                return true;
            }
            pending = false;
            return key.IsFrozen
                ? Index.TryGetValue(key.Frozen, out index)
                : Index.GetAlternateLookup<ReadOnlySpan<byte>>().TryGetValue(key.Bytes, out index);
        }

        public Unit UnitAt(bool pending, int index) => pending ? Pending[index].Unit : Entries[index].Unit;

        public void MarkMalformed(bool pending, int index)
        {
            if (pending)
                Pending[index] = (Pending[index].Key, Pending[index].Unit with { Malformed = true });
            else
                Entries[index] = (Entries[index].Key, Entries[index].Unit with { Malformed = true });
        }

        public void Add(KeyRef key, Unit unit)
        {
            PendingIndex[key] = Pending.Count;
            Pending.Add((key, unit));
        }

        /// <summary>Commits the chunk's units once their keys' population froze.</summary>
        public void Commit()
        {
            foreach ((KeyRef key, Unit unit) in Pending)
            {
                SymbolKey<FindingDiffAnyPart> frozen = key.Frozen;
                Index[frozen] = Entries.Count;
                Entries.Add((frozen, unit));
            }
            Pending.Clear();
            PendingIndex.Clear();
        }
    }

    internal sealed class Work
    {
        public int Bodies;
        public long IlBytes;

        public BodyWalk Walk { get; } = new();

        /// <summary>The Body provision in place of the walk, or null for the walk.</summary>
        public FindingDiffAnyBodyReader? BodyReader { get; init; }
    }

    internal sealed record Unit(
        string FullName,
        string Identifier,
        TypeDefinitionHandle Owner,
        List<TypeDefinitionHandle> Generated,
        bool Malformed = false);

    /// <summary>
    /// One side of the comparison. Its reference keys (Types, members,
    /// signature types) are staged in <see cref="Retained"/> and frozen into
    /// shared backings in chunks; its per-unit census facts are staged in
    /// <see cref="Transient"/>, compared as staged keys, and discarded after the
    /// unit, so no fact key is ever copied into storage of its own.
    /// </summary>
    internal sealed class Side
    {
        // The retained population freezes at unit boundaries once this many bytes are staged.
        const int RetainedChunkBytes = 64 * 1024;

        readonly Guid _mvid;
        // Type-level rows (Type definitions, references, specifications, and
        // primitives) memoized by token for the whole comparison, each a slot in
        // a retained chunk: the current one, still staged, or a frozen one,
        // resolved on demand. Member rows are not retained: a unit spells the
        // members it refers to into the transient population and forgets them.
        readonly Dictionary<int, KeyRef> _keys = [];
        // Member rows (methods, fields, member references, method
        // specifications, standalone signatures) memoized for one unit, each a
        // slot in the transient population; cleared when the unit ends.
        readonly Dictionary<int, KeyRef> _unitKeys = [];
        // Operand row pairs of this side and the other already proven to spell
        // the same key, so a callee referenced from many Types is spelled once
        // per comparison rather than once per referencing unit. The pair is a
        // tuple, whose hash mixes both tokens; two images that number their rows
        // alike would collide a packed long's XOR hash into one bucket.
        readonly HashSet<(int A, int B)> _operandPairs = [];
        readonly List<SymbolKeyPopulation<FindingDiffAnyPart>> _chunks = [];
        readonly Dictionary<int, string> _displayNames = [];
        readonly HashSet<SymbolKey<FindingDiffAnyPart>> _duplicateTypeKeys =
            new(SymbolKeyComparer<FindingDiffAnyPart>.Instance);
        MetadataSymbolSpeller<FindingDiffAnyPart, KeyRef, SideRows> _speller;
        readonly Dictionary<TypeDefinitionHandle, HashSet<MethodDefinitionHandle>> _explicitImplementations = [];
        SymbolKeyInterner<FindingDiffAnyPart>? _interner;

        const MetadataReaderOptions StoredMetadata = MetadataReaderOptions.None;

        public Side(PEReader pe, SymbolKeyInterner<FindingDiffAnyPart> interner)
        {
            ArgumentNullException.ThrowIfNull(interner);
            _interner = interner;
            Pe = pe;
            Md = MetadataFormatAdmission.GetMetadataReader(pe, StoredMetadata);
            _mvid = Md.GetGuid(Md.GetModuleDefinition().Mvid);
            Names = new MetadataStringHeap(Md);
            Retained = new SymbolKeyBuilder<FindingDiffAnyPart>(RetainedChunkBytes) { Names = Names };
            Transient = new SymbolKeyBuilder<FindingDiffAnyPart>(16 * 1024) { Names = Names };
            Strings = new UserStrings(pe, Md);
            _speller = new MetadataSymbolSpeller<FindingDiffAnyPart, KeyRef, SideRows>(Md, Names, MetadataSymbolScope.Omitted, new SideRows(this));
        }

        /// <summary>Reference and lookup keys, memoized per token and frozen in chunks.</summary>
        public SymbolKeyBuilder<FindingDiffAnyPart> Retained { get; }

        /// <summary>One unit's census facts and member keys, discarded after the unit.</summary>
        public SymbolKeyBuilder<FindingDiffAnyPart> Transient { get; }

        /// <summary>
        /// The comparison's one interning table, shared with the other side: a
        /// Type-level key receives its id when memoized, and a named Type's id
        /// is what a spelling embeds in place of its bytes. The table serves the
        /// comparison and is released when the side completes.
        /// </summary>
        public SymbolKeyInterner<FindingDiffAnyPart> Interner
            => _interner ?? throw new InvalidOperationException("The comparison completed and released its interning table.");

        /// <summary>The one speller of metadata rows; this side resolves and embeds the rows it refers to.</summary>
        public MetadataSymbolSpeller<FindingDiffAnyPart, KeyRef, SideRows> Speller => _speller;

        // Primitives are memoized under tokens no metadata row uses.
        const int PrimitiveTokenBase = -1;

        public KeyRef Primitive(PrimitiveTypeCode typeCode)
        {
            int token = PrimitiveTokenBase - (byte)typeCode;
            if (TryGetKey(token, out KeyRef key))
                return key;
            return Memoize(token, _speller.Primitive(Retained, typeCode));
        }

        /// <summary>
        /// The side as the speller's consumer: a struct, so the speller's
        /// instantiation is specialized and these are direct calls.
        /// </summary>
        internal readonly struct SideRows(Side side) : IMetadataRowKeys<FindingDiffAnyPart, KeyRef>
        {
            public KeyRef Resolve(EntityHandle row, MetadataRowRole role)
                => role == MetadataRowRole.Declaring ? side.TypeKey((TypeDefinitionHandle)row) : side.ReferenceKey(row);

            public void Embed(SymbolKeyBuilder<FindingDiffAnyPart> into, KeyRef row) => into.Key(row);

            public KeyRef Primitive(PrimitiveTypeCode typeCode) => side.Primitive(typeCode);

            // Names are the heap's as stored, and no scope is spelled under Omitted.
            public void Name(SymbolKeyBuilder<FindingDiffAnyPart> into, EntityHandle definition, StringHandle name) => into.Name(name);

            public ReadOnlySpan<byte> ScopeName(EntityHandle scope) => default;
        }

        public MetadataStringHeap Names { get; private set; }

        public UserStrings Strings { get; private set; }

        public void Bind(PEReader pe)
        {
            if (ReferenceEquals(pe, Pe))
                return;
            MetadataReader md = MetadataFormatAdmission.GetMetadataReader(pe, StoredMetadata);
            if (md.GetGuid(md.GetModuleDefinition().Mvid) != _mvid)
                throw new InvalidOperationException("A Finding Diff Any comparison continues over the same images.");
            Pe = pe;
            Md = md;
            Names = new MetadataStringHeap(md);
            Retained.Names = Names;
            Transient.Names = Names;
            _speller.Bind(md, Names);
            Strings = new UserStrings(pe, md);
        }

        /// <summary>Discards any key left open by an abandoned build; staged and frozen keys stay valid.</summary>
        public void Abandon()
        {
            Retained.Abandon();
            Transient.Abandon();
            _speller.Abandon();
        }

        /// <summary>Ends a unit: its facts are discarded, and a large retained chunk freezes.</summary>
        public void EndUnit()
        {
            _unitKeys.Clear();
            Transient.Reset();
            if (Retained.Length >= RetainedChunkBytes)
                FreezeRetained();
        }

        /// <summary>
        /// Ends the comparison: the last chunk freezes and the builders release
        /// their storage, so a completed comparison retains only its frozen
        /// chunks, memo, and tables.
        /// </summary>
        public void Complete()
        {
            FreezeRetained();
            _unitKeys.Clear();
            _unitKeys.TrimExcess();
            // The pairing served the body comparison; nothing reads it after.
            _operandPairs.Clear();
            _operandPairs.TrimExcess();
            Retained.Release();
            Transient.Release();
            _speller.Release();
            _interner = null;
        }

        /// <summary>
        /// Freezes the staged retained keys into one shared backing, the chunk
        /// every slot of that generation resolves through from now on.
        /// </summary>
        public void FreezeRetained()
        {
            if (Retained.Length == 0)
                return;
            SymbolKeyPopulation<FindingDiffAnyPart> population = Retained.Freeze();
            if (population.Generation != _chunks.Count)
                throw new InvalidOperationException("The retained chunks are not the builder's generations.");
            _chunks.Add(population);
        }

        /// <summary>The frozen chunk of one generation.</summary>
        public SymbolKeyPopulation<FindingDiffAnyPart> Chunk(int generation) => _chunks[generation];

        bool TryGetKey(int token, out KeyRef key) => _keys.TryGetValue(token, out key);

        /// <summary>Memoizes a named Type's key (a definition, reference, or primitive), which spellings embed as its Ref.</summary>
        KeyRef Memoize(int token, StagedSymbolKey<FindingDiffAnyPart> staged)
        {
            var key = new KeyRef(this, staged, Interner.Intern(staged), named: true);
            _keys[token] = key;
            return key;
        }

        /// <summary>
        /// Memoizes a Type specification's composite, which spellings embed as
        /// a Key part of its bytes, the form a composite spelled inline in a
        /// signature takes, so the two encodings of one composite are one key.
        /// </summary>
        KeyRef MemoizeComposite(int token, StagedSymbolKey<FindingDiffAnyPart> staged)
        {
            var key = new KeyRef(this, staged, Interner.Intern(staged), named: false);
            _keys[token] = key;
            return key;
        }

        KeyRef MemoizeUnit(int token, StagedSymbolKey<FindingDiffAnyPart> staged)
        {
            var key = new KeyRef(this, staged);
            _unitKeys[token] = key;
            return key;
        }

        /// <summary>
        /// Whether two operand rows, one of each side, spell the same key. Type-level
        /// rows compare their retained keys; member rows spell into the transient
        /// populations once per distinct pair, and an equal pair is remembered.
        /// </summary>
        public static bool OperandsEqual(Side a, EntityHandle handleA, Side b, EntityHandle handleB)
        {
            if (IsTypeLevel(handleA.Kind) || IsTypeLevel(handleB.Kind))
                return a.ReferenceKey(handleA) == b.ReferenceKey(handleB);
            (int, int) pair = (MetadataTokens.GetToken(handleA), MetadataTokens.GetToken(handleB));
            if (a._operandPairs.Contains(pair))
                return true;
            bool equal = a.ReferenceKey(handleA) == b.ReferenceKey(handleB);
            if (equal)
                a._operandPairs.Add(pair);
            return equal;
        }

        static bool IsTypeLevel(HandleKind kind)
            => kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification;

        HashSet<MethodDefinitionHandle> ExplicitImplementations(TypeDefinitionHandle type)
        {
            if (_explicitImplementations.TryGetValue(type, out var methods))
                return methods;
            methods = [];
            foreach (MethodImplementationHandle handle in Md.GetTypeDefinition(type).GetMethodImplementations())
            {
                if (Md.GetMethodImplementation(handle).MethodBody is
                    { Kind: HandleKind.MethodDefinition } body)
                {
                    methods.Add((MethodDefinitionHandle)body);
                }
            }
            _explicitImplementations[type] = methods;
            return methods;
        }

        public PEReader Pe { get; private set; }

        public MetadataReader Md { get; private set; }

        public UnitTable Units()
        {
            var table = new UnitTable();
            ReadUnits(table, static () => false);
            return table;
        }

        public bool ReadUnits(UnitTable table, Func<bool> overBudget)
        {
            List<(TypeDefinitionHandle Type, TypeDefinitionHandle Owner)> generated = table.Generated;
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            int rows = Md.TypeDefinitions.Count;
            for (; table.NextRow <= rows; table.NextRow++)
            {
                if (overBudget())
                {
                    FreezeRetained();
                    table.Commit();
                    return false;
                }
                TypeDefinitionHandle handle = MetadataTokens.TypeDefinitionHandle(table.NextRow);

                try
                {
                    if (!TryDeclaringChain(handle, chain, out int length))
                    {
                        AddMalformed(table, handle);
                        continue;
                    }

                    // A Type with a compiler-generated Type in its declaring chain
                    // belongs to the nearest declared Type above that generated
                    // Type; under a top-level generated Type it belongs to none.
                    int generatedAt = -1;
                    for (int i = 0; i < length; i++)
                    {
                        if (IsGeneratedName(Md.GetTypeDefinition(chain[i])))
                        {
                            generatedAt = i;
                            break;
                        }
                    }
                    if (generatedAt < 0)
                    {
                        // A name that is not well-formed UTF-8 has no decodable
                        // spelling to identify it by, like a malformed row.
                        if (EscapedName(handle) is not { } identifier)
                        {
                            AddMalformed(table, handle);
                            continue;
                        }
                        KeyRef key = TypeKey(handle);
                        if (table.TryFind(key, out bool pending, out int index))
                        {
                            // Two rows that spell one name cannot be told apart, so
                            // neither is decided.
                            table.MarkMalformed(pending, index);
                            _duplicateTypeKeys.Add(key.IsFrozen ? key.Frozen : key.Detach());
                            AddMalformed(table, handle);
                            continue;
                        }
                        table.Add(key, new Unit(DisplayName(handle), identifier, handle, []));
                        continue;
                    }
                    if (generatedAt > 0)
                        generated.Add((handle, chain[generatedAt - 1]));
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    // A name or row that cannot be read affects only its own Type.
                    Abandon();
                    AddMalformed(table, handle);
                }
            }
            foreach ((TypeDefinitionHandle type, TypeDefinitionHandle owner) in generated)
            {
                try
                {
                    if (table.TryFind(TypeKey(owner), out bool pending, out int index))
                        table.UnitAt(pending, index).Generated.Add(type);
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    // The owner's own row is malformed and already reported.
                    Abandon();
                }
            }
            generated.Clear();
            FreezeRetained();
            table.Commit();
            return true;
        }

        void AddMalformed(UnitTable table, TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            int mark = Retained.Begin();
            StagedSymbolKey<FindingDiffAnyPart> row = Retained.Mark(FindingDiffAnyPart.Malformed).Int(token).End(mark);
            table.Add(new KeyRef(this, row, Interner.Intern(row), named: true), new Unit(SafeName(handle), $"!{token:X8}", handle, [], Malformed: true));
        }

        bool TryDeclaringChain(TypeDefinitionHandle handle, Span<TypeDefinitionHandle> chain, out int length)
            => MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                    Md, handle, chain, out length, out _, out _)
                && length > 0;

        string SafeName(TypeDefinitionHandle handle)
        {
            try
            {
                return DisplayName(handle);
            }
            catch (Exception ex) when (IsMalformed(ex))
            {
                return $"<row 0x{MetadataTokens.GetToken(handle):X8}>";
            }
        }

        bool IsGeneratedName(TypeDefinition type)
            => Md.StringComparer.StartsWith(type.Name, "<");

        public bool IsVisible(TypeDefinitionHandle handle)
            => (Md.GetTypeDefinition(handle).Attributes & TypeAttributes.VisibilityMask)
                    is TypeAttributes.Public
                    or TypeAttributes.NestedPublic
                    or TypeAttributes.NestedFamily
                    or TypeAttributes.NestedFamORAssem
                && MetadataVisibility.AreEnclosingTypesExternallyReachable(Md, handle);

        bool IsVisible(MethodDefinitionHandle handle, MethodDefinition method)
            => IsVisibleAccess((int)(method.Attributes & MethodAttributes.MemberAccessMask))
                || ExplicitImplementations(method.GetDeclaringType()).Contains(handle);

        bool IsVisible(FieldDefinition field)
            => IsVisibleAccess((int)(field.Attributes & FieldAttributes.FieldAccessMask));

        // Public, Family, and FamORAssem share their encoding across method and field access.
        static bool IsVisibleAccess(int access)
            => access is (int)MethodAttributes.Public
                or (int)MethodAttributes.Family
                or (int)MethodAttributes.FamORAssem;

        bool IsNonApi(SymbolKeySlot fact) => Transient.Kind(fact) is FindingDiffAnyPart.NonApi;

        public List<SymbolKeySlot> ApiCensus(Unit unit)
        {
            var census = new List<SymbolKeySlot>();
            bool visible = IsVisible(unit.Owner);
            int mark = Transient.Begin();
            census.Add(Transient.Mark(visible ? FindingDiffAnyPart.Visible : FindingDiffAnyPart.Hidden).End(mark).Slot);
            if (!visible)
                return census;
            census.AddRange(VisibleFacts(unit).Where(fact => !IsNonApi(fact)));
            Transient.Sort(census);
            return census;
        }

        List<SymbolKeySlot> VisibleFacts(Unit unit)
        {
            var facts = new List<SymbolKeySlot>();
            TypeFacts(facts, unit.Owner);
            MemberFacts(facts, unit.Owner, api: true, out _);
            return facts;
        }

        public List<SymbolKeySlot> BodyCensus(
            Unit unit,
            bool visible,
            out Dictionary<KeyRef, MethodDefinitionHandle> methods)
        {
            var census = new List<SymbolKeySlot>();
            methods = [];
            if (!visible)
                TypeFacts(census, unit.Owner);
            else
                census.AddRange(VisibleFacts(unit).Where(IsNonApi));
            MemberFacts(census, unit.Owner, api: !visible ? null : false, out var ownerMethods);
            foreach ((KeyRef key, MethodDefinitionHandle method) in ownerMethods)
                AddMethod(methods, key, method);
            foreach (TypeDefinitionHandle generated in unit.Generated)
            {
                TypeFacts(census, generated);
                MemberFacts(census, generated, api: null, out var generatedMethods);
                foreach ((KeyRef key, MethodDefinitionHandle method) in generatedMethods)
                    AddMethod(methods, key, method);
            }
            Transient.Sort(census);
            return census;
        }

        // Two method rows that spell one key, which valid metadata does not
        // allow, cannot be told apart, so the body is not decided.
        static void AddMethod(
            Dictionary<KeyRef, MethodDefinitionHandle> methods,
            KeyRef key,
            MethodDefinitionHandle method)
        {
            if (!methods.TryAdd(key, method))
                throw new BadImageFormatException($"Two methods share the compared key {Describe(key)}.");
        }

        void TypeFacts(List<SymbolKeySlot> census, TypeDefinitionHandle handle)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            KeyRef name = TypeKey(handle);
            int mark = Transient.Begin();
            Transient.Mark(FindingDiffAnyPart.TypeFact).Key(name).Int((int)(type.Attributes & ~TypeAttributes.BeforeFieldInit));
            OptionalKey(type.BaseType);
            census.Add(Transient.End(mark).Slot);
            // beforefieldinit only changes when a static constructor runs.
            if ((type.Attributes & TypeAttributes.BeforeFieldInit) != 0
                && type.GetMethods().Any(method =>
                    Md.StringComparer.Equals(Md.GetMethodDefinition(method).Name, ".cctor")))
            {
                mark = Transient.Begin();
                census.Add(Transient.Mark(FindingDiffAnyPart.NonApi).Mark(FindingDiffAnyPart.BeforeFieldInit).Key(name).End(mark).Slot);
            }
            foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
            {
                InterfaceImplementation value = Md.GetInterfaceImplementation(implementation);
                KeyRef @interface = Key(value.Interface);
                mark = Transient.Begin();
                StagedSymbolKey<FindingDiffAnyPart> fact =
                    Transient.Mark(FindingDiffAnyPart.InterfaceFact).Key(name).Key(@interface).End(mark);
                census.Add(fact.Slot);
                Attributes(census, value.GetCustomAttributes(), fact);
            }
            GenericParameters(census, type.GetGenericParameters(), name);
            Attributes(census, type.GetCustomAttributes(), name);
            // A nested Type inherits its declaring Type's nullable context
            // without an attribute row of its own, and that context shapes
            // the rendered signatures of every member that does not override it.
            mark = Transient.Begin();
            census.Add(Transient.Mark(FindingDiffAnyPart.NullableContext).Key(name)
                .Int(NullabilityReader.GetTypeNullableContext(Md, handle)).End(mark).Slot);
            TypeLayout layout = type.GetLayout();
            if (!layout.IsDefault)
            {
                mark = Transient.Begin();
                census.Add(Transient.Mark(FindingDiffAnyPart.Layout).Key(name).Int(layout.PackingSize).Int(layout.Size).End(mark).Slot);
            }
            foreach (MethodImplementationHandle implementation in type.GetMethodImplementations())
            {
                MethodImplementation value = Md.GetMethodImplementation(implementation);
                KeyRef body = Key(value.MethodBody);
                KeyRef declaration = Key(value.MethodDeclaration);
                mark = Transient.Begin();
                census.Add(Transient.Mark(FindingDiffAnyPart.MethodImplementation).Key(name)
                    .Key(body).Key(declaration).End(mark).Slot);
            }
        }

        void MemberFacts(
            List<SymbolKeySlot> census,
            TypeDefinitionHandle handle,
            bool? api,
            out List<(KeyRef Key, MethodDefinitionHandle Method)> methods)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            KeyRef name = TypeKey(handle);
            methods = [];
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = Md.GetFieldDefinition(fieldHandle);
                if (api is { } wanted && IsVisible(field) != wanted)
                    continue;
                KeyRef key = Key(fieldHandle);
                int mark = Transient.Begin();
                Transient.Mark(FindingDiffAnyPart.FieldFact).Key(key).Int((int)field.Attributes);
                Constant(field.GetDefaultValue());
                census.Add(Transient.Int(field.GetOffset()).End(mark).Slot);
                Attributes(census, field.GetCustomAttributes(), key);
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = Md.GetMethodDefinition(methodHandle);
                KeyRef key = Key(methodHandle);
                methods.Add((key, methodHandle));
                if (api is { } wanted && IsVisible(methodHandle, method) != wanted)
                    continue;
                int mark = Transient.Begin();
                census.Add(Transient.Mark(FindingDiffAnyPart.MethodFact).Key(key)
                    .Int((int)method.Attributes).Int((int)method.ImplAttributes).End(mark).Slot);
                GenericParameters(census, method.GetGenericParameters(), key);
                Attributes(census, method.GetCustomAttributes(), key);
                foreach (ParameterHandle parameterHandle in method.GetParameters())
                {
                    Parameter parameter = Md.GetParameter(parameterHandle);
                    mark = Transient.Begin();
                    StagedSymbolKey<FindingDiffAnyPart> parameterKey = Transient.Mark(FindingDiffAnyPart.Parameter).Key(key)
                        .Int(parameter.SequenceNumber).End(mark);
                    mark = Transient.Begin();
                    Transient.Mark(FindingDiffAnyPart.ParameterFact).Key(parameterKey)
                        .Name(parameter.Name).Int((int)parameter.Attributes);
                    Constant(parameter.GetDefaultValue());
                    census.Add(Transient.End(mark).Slot);
                    Attributes(census, parameter.GetCustomAttributes(), parameterKey);
                }
                MethodImport import = method.GetImport();
                if (!import.Module.IsNil)
                {
                    mark = Transient.Begin();
                    census.Add(Transient.Mark(FindingDiffAnyPart.Import).Key(key).Name(import.Name)
                        .Name(Md.GetModuleReference(import.Module).Name).Int((int)import.Attributes).End(mark).Slot);
                }
            }
            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = Md.GetPropertyDefinition(propertyHandle);
                PropertyAccessors accessors = property.GetAccessors();
                if (api is { } wanted
                    && AccessorVisible(accessors.Getter, accessors.Setter, accessors.Others) != wanted)
                {
                    continue;
                }
                StagedSymbolKey<FindingDiffAnyPart> key = PropertyKey(name, property);
                // Accessor keys are member keys of this population: resolved before
                // the fact opens, since a row may name a method of another Type.
                KeyRef? getter = Accessor(accessors.Getter);
                KeyRef? setter = Accessor(accessors.Setter);
                int mark = Transient.Begin();
                Transient.Mark(FindingDiffAnyPart.PropertyFact).Key(key).Int((int)property.Attributes);
                Constant(property.GetDefaultValue());
                Optional(getter);
                Optional(setter);
                census.Add(Transient.End(mark).Slot);
                Attributes(census, property.GetCustomAttributes(), key);
            }
            foreach (EventDefinitionHandle eventHandle in type.GetEvents())
            {
                EventDefinition value = Md.GetEventDefinition(eventHandle);
                EventAccessors accessors = value.GetAccessors();
                if (api is { } wanted
                    && AccessorVisible(accessors.Adder, accessors.Remover, accessors.Others) != wanted)
                {
                    continue;
                }
                // The Event marker keeps an event from spelling the key of its backing field.
                KeyRef eventType = Key(value.Type);
                KeyRef? adder = Accessor(accessors.Adder);
                KeyRef? remover = Accessor(accessors.Remover);
                KeyRef? raiser = Accessor(accessors.Raiser);
                int mark = Transient.Begin();
                StagedSymbolKey<FindingDiffAnyPart> key =
                    Transient.Mark(FindingDiffAnyPart.Event).Key(name).Name(value.Name).Key(eventType).End(mark);
                mark = Transient.Begin();
                Transient.Mark(FindingDiffAnyPart.EventFact).Key(key).Int((int)value.Attributes);
                Optional(adder);
                Optional(remover);
                Optional(raiser);
                census.Add(Transient.End(mark).Slot);
                Attributes(census, value.GetCustomAttributes(), key);
            }
        }

        bool AccessorVisible(
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            foreach (MethodDefinitionHandle accessor in (MethodDefinitionHandle[])[first, second, .. others])
            {
                if (!accessor.IsNil && IsVisible(accessor, Md.GetMethodDefinition(accessor)))
                    return true;
            }
            return false;
        }

        /// <summary>An accessor's key, resolved before the fact that embeds it opens; null when the row names none.</summary>
        KeyRef? Accessor(MethodDefinitionHandle accessor) => accessor.IsNil ? null : Key(accessor);

        void Optional(KeyRef? key)
        {
            if (key is { } present)
                Transient.Key(present);
            else
                Transient.Mark(FindingDiffAnyPart.Absent);
        }

        // A Type-level key (a base Type) may be built while a transient fact is
        // open: it is staged in the retained population, a different builder.
        void OptionalKey(EntityHandle handle)
        {
            if (handle.IsNil)
            {
                Transient.Mark(FindingDiffAnyPart.Absent);
                return;
            }
            KeyRef key = Key(handle);
            Transient.Key(key);
        }

        void GenericParameters(
            List<SymbolKeySlot> census,
            GenericParameterHandleCollection parameters,
            KeyRef owner)
        {
            foreach (GenericParameterHandle handle in parameters)
            {
                GenericParameter parameter = Md.GetGenericParameter(handle);
                int mark = Transient.Begin();
                StagedSymbolKey<FindingDiffAnyPart> key =
                    Transient.Mark(FindingDiffAnyPart.GenericParameter).Key(owner).Int(parameter.Index).End(mark);
                mark = Transient.Begin();
                census.Add(Transient.Mark(FindingDiffAnyPart.GenericParameterFact).Key(key)
                    .Name(parameter.Name).Int((int)parameter.Attributes).End(mark).Slot);
                foreach (GenericParameterConstraintHandle constraintHandle in parameter.GetConstraints())
                {
                    GenericParameterConstraint constraint = Md.GetGenericParameterConstraint(constraintHandle);
                    KeyRef constraintType = Key(constraint.Type);
                    mark = Transient.Begin();
                    StagedSymbolKey<FindingDiffAnyPart> fact =
                        Transient.Mark(FindingDiffAnyPart.Constraint).Key(key).Key(constraintType).End(mark);
                    census.Add(fact.Slot);
                    Attributes(census, constraint.GetCustomAttributes(), fact);
                }
                Attributes(census, parameter.GetCustomAttributes(), key);
            }
        }

        void Attributes(
            List<SymbolKeySlot> census,
            CustomAttributeHandleCollection attributes,
            KeyRef owner)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = Md.GetCustomAttribute(handle);
                KeyRef constructor = Key(attribute.Constructor);
                int mark = Transient.Begin();
                Transient.Mark(FindingDiffAnyPart.Attribute).Key(owner).Key(constructor);
                Blob(attribute.Value);
                census.Add(Transient.End(mark).Slot);
            }
        }

        void Attributes(
            List<SymbolKeySlot> census,
            CustomAttributeHandleCollection attributes,
            StagedSymbolKey<FindingDiffAnyPart> owner)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = Md.GetCustomAttribute(handle);
                KeyRef constructor = Key(attribute.Constructor);
                int mark = Transient.Begin();
                Transient.Mark(FindingDiffAnyPart.Attribute).Key(owner).Key(constructor);
                Blob(attribute.Value);
                census.Add(Transient.End(mark).Slot);
            }
        }

        void Constant(ConstantHandle handle)
        {
            if (handle.IsNil)
            {
                Transient.Mark(FindingDiffAnyPart.Absent);
                return;
            }
            Constant constant = Md.GetConstant(handle);
            Transient.Mark(FindingDiffAnyPart.Constant).Int((int)constant.TypeCode);
            Blob(constant.Value);
        }

        void Blob(BlobHandle handle)
        {
            BlobReader reader = Md.GetBlobReader(handle);
            Transient.Blob(reader);
        }

        StagedSymbolKey<FindingDiffAnyPart> PropertyKey(KeyRef type, PropertyDefinition property)
        {
            MethodSignature<MetadataSignatureKey> signature = _speller.DecodeProperty(property);
            try
            {
                int mark = Transient.Begin();
                Transient.Mark(FindingDiffAnyPart.Property).Key(type).Name(property.Name).Int(signature.Header.IsInstance ? 1 : 0);
                _speller.Signature(Transient, signature);
                return Transient.End(mark);
            }
            finally
            {
                _speller.Discard();
            }
        }

        public KeyRef TypeKey(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            return TryGetKey(token, out KeyRef key) ? key : Memoize(token, _speller.TypeDefinition(Retained, handle));
        }

        string DisplayName(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (!_displayNames.TryGetValue(token, out string? name))
                _displayNames[token] = name = DefinitionName(handle);
            return name;
        }

        string? EscapedName(TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!TryDeclaringChain(handle, chain, out int length))
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            TypeDefinition root = Md.GetTypeDefinition(chain[0]);
            string? space = root.Namespace.IsNil ? "" : Decoded(root.Namespace);
            var segments = ImmutableArray.CreateBuilder<string>(length);
            for (int i = 0; i < length; i++)
            {
                if (Decoded(Md.GetTypeDefinition(chain[i]).Name) is not { } segment)
                    return null;
                segments.Add(segment);
            }
            if (space is null)
                return null;
            return MetadataTypeDefinitionName.Create(space, segments.MoveToImmutable())
                is MetadataTypeDefinitionNameResult.Valid { Name: var name }
                ? name.ToEscapedFullName()
                : throw new BadImageFormatException("The Type name cannot be represented.");
        }

        string? Decoded(StringHandle handle)
        {
            string name = Md.GetString(handle);
            return name.Contains('�') && !Names.IsWellFormed(handle) ? null : name;
        }

        string DefinitionName(TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!TryDeclaringChain(handle, chain, out int length))
                throw new BadImageFormatException("The Type has an invalid declaring chain.");
            TypeDefinition root = Md.GetTypeDefinition(chain[0]);
            var name = new System.Text.StringBuilder();
            if (!root.Namespace.IsNil)
                name.Append(Md.GetString(root.Namespace)).Append('.');
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                    name.Append('.');
                name.Append(Md.GetString(Md.GetTypeDefinition(chain[i]).Name));
            }
            return name.ToString();
        }

        public KeyRef ReferenceKey(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    RejectDuplicate((TypeDefinitionHandle)handle);
                    break;
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition method = Md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    if ((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.PrivateScope)
                        throw new BadImageFormatException("A reference names a compiler-controlled method.");
                    RejectDuplicate(method.GetDeclaringType());
                    break;
                }
                case HandleKind.FieldDefinition:
                {
                    FieldDefinition field = Md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.PrivateScope)
                        throw new BadImageFormatException("A reference names a compiler-controlled field.");
                    RejectDuplicate(field.GetDeclaringType());
                    break;
                }
            }
            return Key(handle);
        }

        void RejectDuplicate(TypeDefinitionHandle handle)
        {
            if (_duplicateTypeKeys.Count == 0)
                return;
            KeyRef key = TypeKey(handle);
            bool duplicate = key.IsFrozen
                ? _duplicateTypeKeys.Contains(key.Frozen)
                : _duplicateTypeKeys.GetAlternateLookup<ReadOnlySpan<byte>>().Contains(key.Bytes);
            if (duplicate)
                throw new BadImageFormatException("A reference names one of two Type rows that share a key.");
        }

        public KeyRef Key(EntityHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (IsTypeLevel(handle.Kind))
            {
                if (TryGetKey(token, out KeyRef retained))
                    return retained;
                switch (handle.Kind)
                {
                    case HandleKind.TypeDefinition:
                        return TypeKey((TypeDefinitionHandle)handle);
                    case HandleKind.TypeReference:
                        return Memoize(token, _speller.TypeReference(Retained, (TypeReferenceHandle)handle));
                    default:
                        // A specification that names a plain row is that row's key.
                        if (_speller.TryTypeSpecification(Retained, (TypeSpecificationHandle)handle, out var spelled, out KeyRef alias))
                            return MemoizeComposite(token, spelled);
                        _keys[token] = alias;
                        return alias;
                }
            }
            if (_unitKeys.TryGetValue(token, out KeyRef key))
                return key;
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => MemoizeUnit(token, _speller.Method(Transient, (MethodDefinitionHandle)handle)),
                HandleKind.FieldDefinition => MemoizeUnit(token, _speller.Field(Transient, (FieldDefinitionHandle)handle)),
                HandleKind.MemberReference => MemoizeUnit(token, _speller.MemberReference(Transient, (MemberReferenceHandle)handle)),
                HandleKind.MethodSpecification => MemoizeUnit(token, _speller.MethodSpecification(Transient, (MethodSpecificationHandle)handle)),
                HandleKind.StandaloneSignature => MemoizeUnit(token, _speller.StandaloneSignature(Transient, (StandaloneSignatureHandle)handle)),
                _ => throw new BadImageFormatException(
                    $"Unsupported metadata token 0x{token:X8} in a compared fact."),
            };
        }

    }

    internal static string Describe(KeyRef key)
        => SymbolKeyText.Describe(key.IsFrozen ? key.Frozen : key.Detach(), key.Owner.Interner);

    internal sealed class UserStrings
    {
        readonly BlobReader _heap;

        public UserStrings(PEReader pe, MetadataReader md)
        {
            // The heap offset is not defined for an absent stream.
            int size = md.GetHeapSize(HeapIndex.UserString);
            _heap = size == 0
                ? default
                : pe.GetMetadata().GetReader(md.GetHeapMetadataOffset(HeapIndex.UserString), size);
        }

        /// <summary>
        /// Whether two <c>ldstr</c> tokens name strings of the same code
        /// units, read as <see cref="MetadataReader.GetUserString"/> reads
        /// them: the entry's length rounded down to whole code units, so its
        /// terminal byte is not compared, and an offset at the heap's end is
        /// the empty string. An entry that does not fit the heap is malformed.
        /// </summary>
        public static unsafe bool Equal(UserStrings a, int tokenA, UserStrings b, int tokenB)
        {
            BlobReader x = a.Entry(tokenA, out int lengthA);
            BlobReader y = b.Entry(tokenB, out int lengthB);
            return lengthA == lengthB
                && new ReadOnlySpan<byte>(x.CurrentPointer, lengthA)
                    .SequenceEqual(new ReadOnlySpan<byte>(y.CurrentPointer, lengthB));
        }

        /// <summary>The heap positioned at an entry's code units, with their byte length.</summary>
        BlobReader Entry(int token, out int units)
        {
            int offset = token & 0xFFFFFF;
            if (_heap.Length == 0 || offset > _heap.Length)
                throw new BadImageFormatException("A user string lies outside the #US heap.");
            BlobReader entry = _heap;
            entry.Offset = offset;
            if (offset == _heap.Length)
            {
                units = 0;
                return entry;
            }
            int length = entry.ReadCompressedInteger();
            if (length > entry.RemainingBytes)
                throw new BadImageFormatException("A #US entry does not fit the heap.");
            units = length & ~1;
            return entry;
        }
    }

}

/// <summary>Finding Diff Any's Symbol Key domain: its markers, from the first marker tag up.</summary>
internal enum FindingDiffAnyPart : byte
{
    // Markers of this domain alone.
    Absent = SymbolKeyDomain.FirstMarker,
    NonApi,
    Visible,
    Hidden,
    Malformed,

    // The shared metadata row vocabulary, declared under this domain's names
    // with the shared values, so the speller's bytes are this domain's keys.
    Namespace = MetadataSymbolPart.Namespace,
    Vararg = MetadataSymbolPart.Vararg,
    Return = MetadataSymbolPart.Return,
    Type = MetadataSymbolPart.Type,
    Method = MetadataSymbolPart.Method,
    Field = MetadataSymbolPart.Field,
    MethodSpecification = MetadataSymbolPart.MethodSpecification,
    LocalSignature = MetadataSymbolPart.LocalSignature,
    StandaloneSignature = MetadataSymbolPart.StandaloneSignature,
    Primitive = MetadataSymbolPart.Primitive,
    SZArray = MetadataSymbolPart.SZArray,
    Array = MetadataSymbolPart.Array,
    ByReference = MetadataSymbolPart.ByReference,
    Pointer = MetadataSymbolPart.Pointer,
    FunctionPointer = MetadataSymbolPart.FunctionPointer,
    GenericInstance = MetadataSymbolPart.GenericInstance,
    TypeParameter = MetadataSymbolPart.TypeParameter,
    MethodParameter = MetadataSymbolPart.MethodParameter,
    RequiredModifier = MetadataSymbolPart.RequiredModifier,
    OptionalModifier = MetadataSymbolPart.OptionalModifier,
    Pinned = MetadataSymbolPart.Pinned,

    // Keys of this domain alone.
    Property = 0x1B,
    Event = 0x1C,
    Parameter = 0x1D,
    GenericParameter = 0x1E,

    // Facts.
    TypeFact = 0x2E,
    BeforeFieldInit,
    InterfaceFact,
    NullableContext,
    Layout,
    MethodImplementation,
    FieldFact,
    MethodFact,
    ParameterFact,
    Import,
    PropertyFact,
    EventFact,
    GenericParameterFact,
    Constraint,
    Attribute,
    Constant,
}

/// <summary>
/// A key of one side: a slot in the side's retained population (the current
/// chunk while staged, or a frozen one) for a Type-level row, or a slot in the
/// side's transient population (its generation complemented, below zero) for a
/// member row the current unit spelled. Both resolve on demand through the
/// side, so memoizing a key is one write and freezing a chunk touches no
/// reference. A Type-level key carries the id the comparison's interning table
/// gave it and the form it embeds in, and compares and hashes by that id
/// against the comparison's other Type-level keys; a member key compares and
/// hashes by its bytes.
/// </summary>
internal readonly struct KeyRef : IEquatable<KeyRef>
{
    readonly FindingDiffAny.Side _owner;
    readonly int _generation;
    readonly int _offset;
    readonly int _length;
    // The interned id and the embedding form in one field, so the struct stays
    // at 24 bytes: positive is a named Type (id + 1), which embeds as its Ref;
    // negative is a composite (-(id + 1)), which embeds as a Key part of its
    // bytes; zero is a member key or the default value, which has no id.
    readonly int _form;

    /// <summary>
    /// A Type-level key, staged in the side's retained population and interned
    /// under <paramref name="id"/>: a named Type (definition, reference, or
    /// primitive) when <paramref name="named"/>, otherwise a Type
    /// specification's composite.
    /// </summary>
    public KeyRef(FindingDiffAny.Side owner, StagedSymbolKey<FindingDiffAnyPart> staged, int id, bool named)
    {
        if (!staged.IsStagedIn(owner.Retained))
            throw new ArgumentException("A Type-level key is staged in its side's retained population.", nameof(staged));
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        _owner = owner;
        _generation = staged.Generation;
        _offset = staged.Offset;
        _length = staged.Length;
        _form = named ? id + 1 : -(id + 1);
    }

    /// <summary>A member key, staged in the side's transient population for the current unit.</summary>
    public KeyRef(FindingDiffAny.Side owner, StagedSymbolKey<FindingDiffAnyPart> staged)
    {
        if (!staged.IsStagedIn(owner.Transient))
            throw new ArgumentException("A member key is staged in its side's transient population.", nameof(staged));
        _owner = owner;
        _generation = ~staged.Generation;
        _offset = staged.Offset;
        _length = staged.Length;
        _form = 0;
    }

    public bool IsDefault => _owner is null;

    internal int Generation => _generation;

    internal int Offset => _offset;

    internal int Length => _length;

    /// <summary>The id the comparison's table gave a Type-level key; a member key has none.</summary>
    internal int Id
        => _form > 0 ? _form - 1
            : _form < 0 ? -_form - 1
            : throw new InvalidOperationException("A member key has no interned id.");

    /// <summary>Whether a spelling embeds this key as the Ref of its id: a named Type does, a composite or member key embeds as its bytes.</summary>
    internal bool EmbedsAsRef => _form > 0;

    internal FindingDiffAny.Side Owner => _owner;

    /// <summary>Whether the key is a member row's, staged in the unit's transient population.</summary>
    public bool IsTransient => _generation < 0;

    public bool IsFrozen => _generation >= 0 && _generation < _owner.Retained.Generation;

    /// <summary>The key's view, once its chunk froze.</summary>
    public SymbolKey<FindingDiffAnyPart> Frozen => _owner.Chunk(_generation).Resolve(_offset, _length);

    internal OpaqueByteRange FrozenRange => _owner.Chunk(_generation).RangeAt(_offset, _length);

    /// <summary>The key's bytes while staged, in the retained or the transient population.</summary>
    public ReadOnlySpan<byte> Bytes
        => IsTransient
            ? _owner.Transient.Staged(~_generation, _offset, _length)
            : _owner.Retained.Staged(_generation, _offset, _length);

    public FindingDiffAnyPart Kind
        => IsFrozen ? Frozen.Kind : SymbolKeyDomain.FromMarker<FindingDiffAnyPart>(Bytes[0]);

    /// <summary>Retains this key alone, in its own backing.</summary>
    public SymbolKey<FindingDiffAnyPart> Detach()
        => IsFrozen ? Frozen : new SymbolKey<FindingDiffAnyPart>(OpaqueByteRange.Copy(Bytes));

    public bool Equals(KeyRef other)
    {
        if (!IsTransient && !other.IsTransient)
            return _form == other._form;
        // A Type-level and a member key begin with different markers, so a mixed pair compares unequal by its bytes.
        bool frozen = IsFrozen;
        bool otherFrozen = other.IsFrozen;
        if (frozen && otherFrozen)
            return FrozenRange.Equals(other.FrozenRange);
        if (frozen)
            return FrozenRange.SequenceEqual(other.Bytes);
        if (otherFrozen)
            return other.FrozenRange.SequenceEqual(Bytes);
        return Bytes.SequenceEqual(other.Bytes);
    }

    public override bool Equals(object? obj) => obj is KeyRef other && Equals(other);

    // A Type-level key hashes by its id; a member key by its staged bytes.
    public override int GetHashCode()
    {
        if (!IsTransient)
            return _form;
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    public static bool operator ==(KeyRef left, KeyRef right) => left.Equals(right);

    public static bool operator !=(KeyRef left, KeyRef right) => !left.Equals(right);
}

internal static class FindingDiffAnyKeyBuilderExtensions
{
    /// <summary>
    /// Embeds a key in the form its kind determines: a named Type as the Ref
    /// part of its shared id; a composite, frozen or staged, and a member key
    /// as a nested Key part of their bytes.
    /// </summary>
    public static SymbolKeyBuilder<FindingDiffAnyPart> Key(this SymbolKeyBuilder<FindingDiffAnyPart> builder, KeyRef key)
        => key.EmbedsAsRef ? builder.Ref(key.Id)
            : key.IsFrozen ? builder.KeyRange(key.FrozenRange)
            : builder.KeyBytes(key.Bytes);

}

public sealed class FindingDiffAnyComparison(FindingDiffAnyAxes axes = FindingDiffAnyAxes.ApiAndBody, FindingDiffAnyBodyReader? bodyReader = null)
{
    readonly FindingDiffAny.UnitTable _unitsA = new();
    readonly FindingDiffAny.UnitTable _unitsB = new();
    readonly ImmutableArray<FindingDiffAnyTypeState>.Builder _states =
        ImmutableArray.CreateBuilder<FindingDiffAnyTypeState>();
    readonly FindingDiffAny.Work _work = new() { BodyReader = bodyReader };
    SymbolKeyInterner<FindingDiffAnyPart>? _interner;
    FindingDiffAny.Side? _a;
    FindingDiffAny.Side? _b;
    bool _readA;
    bool _readB;
    List<FindingDiffAny.Unit>? _onlyB;
    int _next;

    public FindingDiffAnyAxes Axes { get; } = axes;

    /// <summary>The complete result, once a step has finished the comparison.</summary>
    public FindingDiffAnyResult? Result { get; private set; }

    /// <summary>
    /// Advances the comparison for about <paramref name="budget"/>, or to
    /// completion for <see cref="Timeout.InfiniteTimeSpan"/>. Returns true when
    /// <see cref="Result"/> is available.
    /// </summary>
    public bool Step(
        PEReader before,
        PEReader after,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (Result is not null)
            return true;

        if (_a is null || _b is null)
        {
            _interner = new SymbolKeyInterner<FindingDiffAnyPart>();
            _a = new FindingDiffAny.Side(before, _interner);
            _b = new FindingDiffAny.Side(after, _interner);
        }
        else
        {
            _a.Bind(before);
            _b.Bind(after);
        }

        long started = Stopwatch.GetTimestamp();
        bool progressed = false;
        bool OverBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!progressed)
            {
                progressed = true;
                return false;
            }
            return budget != Timeout.InfiniteTimeSpan
                && Stopwatch.GetElapsedTime(started) >= budget;
        }

        if (!_readA && !(_readA = _a.ReadUnits(_unitsA, OverBudget)))
            return false;
        if (!_readB && !(_readB = _b.ReadUnits(_unitsB, OverBudget)))
            return false;

        FindingDiffAny.UnitTable unitsA = _unitsA;
        FindingDiffAny.UnitTable unitsB = _unitsB;
        _onlyB ??= [.. unitsB.Entries.Where(entry => !unitsA.Index.ContainsKey(entry.Key)).Select(entry => entry.Unit)];
        int total = unitsA.Count + _onlyB.Count;
        for (; _next < total; _next++)
        {
            if (OverBudget())
                return false;
            if (_next < unitsA.Count)
            {
                (SymbolKey<FindingDiffAnyPart> key, FindingDiffAny.Unit unitA) = unitsA.Entries[_next];
                _states.Add(unitsB.TryGetValue(key, out FindingDiffAny.Unit? unitB)
                    ? FindingDiffAny.CompareUnit(_a, unitA, _b, unitB, Axes, _work)
                    : FindingDiffAny.OneSided(_a, unitA, Axes));
            }
            else
            {
                _states.Add(FindingDiffAny.OneSided(_b, _onlyB[_next - unitsA.Count], Axes));
            }
        }

        _a.Complete();
        _b.Complete();
        _states.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
        // The sides released the table when they completed; the comparison holds only its result.
        _interner = null;
        Result = new FindingDiffAnyResult(
            _states.ToImmutable(),
            new FindingDiffAnyReceipt(_states.Count, _work.Bodies, _work.IlBytes));
        return true;
    }
}
