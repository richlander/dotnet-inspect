using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;

namespace DotnetInspector.Sections;

public sealed record LibraryAddressInspectionLimits
{
    public const int DefaultMaximumAssemblyBytes = 512 * 1024 * 1024;
    public const int DefaultMaximumPortablePdbBytes = 64 * 1024 * 1024;
    public const int DefaultMaximumSourceLinkMapBytes = 4 * 1024 * 1024;
    public const int DefaultMaximumSourceLinkMappings = 16 * 1024;

    public LibraryAddressInspectionLimits(
        int maximumAssemblyBytes = DefaultMaximumAssemblyBytes,
        int maximumPortablePdbBytes = DefaultMaximumPortablePdbBytes,
        int maximumSourceLinkMapBytes = DefaultMaximumSourceLinkMapBytes,
        int maximumSourceLinkMappings = DefaultMaximumSourceLinkMappings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumPortablePdbBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumSourceLinkMapBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumSourceLinkMappings);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumPortablePdbBytes = maximumPortablePdbBytes;
        MaximumSourceLinkMapBytes = maximumSourceLinkMapBytes;
        MaximumSourceLinkMappings = maximumSourceLinkMappings;
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumPortablePdbBytes { get; }
    public int MaximumSourceLinkMapBytes { get; }
    public int MaximumSourceLinkMappings { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryAddressIntent.IlPoint), "ilPoint")]
[JsonDerivedType(typeof(LibraryAddressIntent.HeapPoint), "heapPoint")]
[JsonDerivedType(typeof(LibraryAddressIntent.Population), "population")]
public abstract record LibraryAddressIntent
{
    private protected LibraryAddressIntent()
    {
    }

    public sealed record IlPoint : LibraryAddressIntent
    {
        public IlPoint(
            int methodToken,
            int ilOffset,
            ILOffsetProjectionCapabilities capabilities,
            bool browsableUrls = false)
        {
            LibraryAddressInspectionContract.ValidateIlPoint(
                methodToken,
                ilOffset);
            LibraryAddressInspectionContract.ValidateCapabilities(
                capabilities);
            MethodToken = methodToken;
            ILOffset = ilOffset;
            Capabilities = capabilities;
            BrowsableUrls = browsableUrls;
        }

        public int MethodToken { get; }
        public int ILOffset { get; }
        public ILOffsetProjectionCapabilities Capabilities { get; }
        public bool BrowsableUrls { get; }
    }

    public sealed record HeapPoint : LibraryAddressIntent
    {
        public HeapPoint(
            MetadataRootKind root,
            HeapKind heap,
            int address)
        {
            if (!Enum.IsDefined(root))
                throw new ArgumentOutOfRangeException(nameof(root));
            if (!Enum.IsDefined(heap))
                throw new ArgumentOutOfRangeException(nameof(heap));
            ArgumentOutOfRangeException.ThrowIfNegative(address);

            Root = root;
            Heap = heap;
            Address = address;
        }

        public MetadataRootKind Root { get; }
        public HeapKind Heap { get; }
        public int Address { get; }
    }

    public sealed record Population : LibraryAddressIntent
    {
        public const int MaximumRecords = 1024;

        public Population(
            IEnumerable<LibraryAddressPopulationRecord> records,
            ILOffsetProjectionCapabilities capabilities,
            bool browsableUrls = false,
            bool allowNonBoundaryContextAbsence = false)
        {
            ArgumentNullException.ThrowIfNull(records);
            LibraryAddressInspectionContract.ValidateCapabilities(
                capabilities);
            ImmutableArray<LibraryAddressPopulationRecord> snapshot =
                records.ToImmutableArray();
            if (snapshot.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A Library Address population must contain at least one record.",
                    nameof(records));
            }
            if (snapshot.Length > MaximumRecords)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(records),
                    snapshot.Length,
                    $"A Library Address population cannot exceed "
                        + $"{MaximumRecords} records.");
            }
            if (snapshot.Any(static record => record is null))
            {
                throw new ArgumentException(
                    "A Library Address population cannot contain null records.",
                    nameof(records));
            }

            Records = snapshot;
            Capabilities = capabilities;
            BrowsableUrls = browsableUrls;
            AllowNonBoundaryContextAbsence =
                allowNonBoundaryContextAbsence;
        }

        public ImmutableArray<LibraryAddressPopulationRecord> Records
        {
            get;
        }

        public ILOffsetProjectionCapabilities Capabilities { get; }
        public bool BrowsableUrls { get; }
        public bool AllowNonBoundaryContextAbsence { get; }
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryAddressPopulationRecord.Coordinate),
    "coordinate")]
[JsonDerivedType(
    typeof(LibraryAddressPopulationRecord.Malformed),
    "malformed")]
public abstract record LibraryAddressPopulationRecord
{
    private protected LibraryAddressPopulationRecord(
        int lineNumber,
        string? label)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        LineNumber = lineNumber;
        Label = label;
    }

    public int LineNumber { get; }
    public string? Label { get; }

    public sealed record Coordinate : LibraryAddressPopulationRecord
    {
        public Coordinate(
            int lineNumber,
            string value,
            string? label,
            int methodToken,
            int ilOffset)
            : base(lineNumber, label)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            LibraryAddressInspectionContract.ValidateIlPoint(
                methodToken,
                ilOffset);
            Value = value;
            MethodToken = methodToken;
            ILOffset = ilOffset;
        }

        public string Value { get; }
        public int MethodToken { get; }
        public int ILOffset { get; }
    }

    public sealed record Malformed : LibraryAddressPopulationRecord
    {
        public Malformed(
            int lineNumber,
            string? label,
            string error)
            : base(lineNumber, label)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(error);
            Error = error;
        }

        public string Error { get; }
    }
}

public sealed record LibraryAddressInspectionRequest
{
    public LibraryAddressInspectionRequest(
        LibraryReference library,
        LibraryAddressIntent intent,
        LibraryAddressInspectionLimits? limits = null)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Intent = intent
            ?? throw new ArgumentNullException(nameof(intent));
        Limits = limits ?? new();
    }

    public LibraryReference Library { get; }
    public LibraryAddressIntent Intent { get; }
    public LibraryAddressInspectionLimits Limits { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryAddressDocument.IlPoint), "ilPoint")]
[JsonDerivedType(typeof(LibraryAddressDocument.HeapPoint), "heapPoint")]
[JsonDerivedType(typeof(LibraryAddressDocument.Population), "population")]
public abstract record LibraryAddressDocument
{
    private protected LibraryAddressDocument()
    {
    }

    public sealed record IlPoint(LibraryIlAddressOutcome Result)
        : LibraryAddressDocument;

    public sealed record HeapPoint(LibraryHeapAddressOutcome Result)
        : LibraryAddressDocument;

    public sealed record Population(LibraryAddressPopulationResult Result)
        : LibraryAddressDocument;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryIlAddressOutcome.Resolved), "resolved")]
[JsonDerivedType(typeof(LibraryIlAddressOutcome.Unresolved), "unresolved")]
public abstract record LibraryIlAddressOutcome
{
    private protected LibraryIlAddressOutcome()
    {
    }

    public sealed record Resolved(
        int MethodToken,
        int ILOffset,
        ILOffsetProjection Projection)
        : LibraryIlAddressOutcome;

    public sealed record Unresolved(
        int MethodToken,
        int ILOffset,
        ILOffsetProjectionFailure Failure)
        : LibraryIlAddressOutcome;
}

public enum LibraryHeapAddressFailure
{
    MetadataUnavailable,
    RootUnavailable,
    MalformedRoot,
    MalformedValue,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LibraryHeapAddressOutcome.Resolved), "resolved")]
[JsonDerivedType(typeof(LibraryHeapAddressOutcome.Unresolved), "unresolved")]
public abstract record LibraryHeapAddressOutcome
{
    private protected LibraryHeapAddressOutcome()
    {
    }

    public sealed record Resolved(
        MetadataRootKind RequestedRoot,
        MetadataRootIdentity Root,
        HeapKind Heap,
        int Address,
        MetadataValue Value)
        : LibraryHeapAddressOutcome;

    public sealed record Unresolved
        : LibraryHeapAddressOutcome
    {
        public Unresolved(
            MetadataRootKind root,
            HeapKind heap,
            int address,
            LibraryHeapAddressFailure reason,
            string detail)
            : this(
                root,
                heap,
                address,
                reason,
                new InertString(TextPolicy.Field, detail))
        {
        }

        [JsonConstructor]
        public Unresolved(
            MetadataRootKind root,
            HeapKind heap,
            int address,
            LibraryHeapAddressFailure reason,
            InertString detail)
        {
            Root = root;
            Heap = heap;
            Address = address;
            Reason = reason;
            Detail = detail;
        }

        public MetadataRootKind Root { get; }
        public HeapKind Heap { get; }
        public int Address { get; }
        public LibraryHeapAddressFailure Reason { get; }

        [JsonConverter(typeof(InertStringJsonConverter))]
        public InertString Detail { get; }
    }
}

public sealed record LibraryAddressPopulationResult
{
    [JsonConstructor]
    public LibraryAddressPopulationResult(
        ImmutableArray<LibraryAddressPopulationRow> rows)
    {
        if (rows.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A Library Address population result must contain at least one row.",
                nameof(rows));
        }

        Rows = rows;
    }

    public ImmutableArray<LibraryAddressPopulationRow> Rows { get; }

    public bool IsComplete =>
        Rows.All(static row =>
            row is LibraryAddressPopulationRow.Resolved);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryAddressPopulationRow.Resolved),
    "resolved")]
[JsonDerivedType(
    typeof(LibraryAddressPopulationRow.Malformed),
    "malformed")]
[JsonDerivedType(
    typeof(LibraryAddressPopulationRow.Unresolved),
    "unresolved")]
public abstract record LibraryAddressPopulationRow
{
    private protected LibraryAddressPopulationRow(
        int lineNumber,
        string? label)
    {
        LineNumber = lineNumber;
        Label = label;
    }

    public int LineNumber { get; }
    public string? Label { get; }

    public sealed record Resolved : LibraryAddressPopulationRow
    {
        public Resolved(
            int lineNumber,
            string value,
            string? label,
            int methodToken,
            int ilOffset,
            ILOffsetProjection projection)
            : base(lineNumber, label)
        {
            Value = value;
            MethodToken = methodToken;
            ILOffset = ilOffset;
            Projection = projection;
        }

        public string Value { get; }
        public int MethodToken { get; }
        public int ILOffset { get; }
        public ILOffsetProjection Projection { get; }
    }

    public sealed record Malformed : LibraryAddressPopulationRow
    {
        public Malformed(
            int lineNumber,
            string? label,
            string error)
            : base(lineNumber, label) =>
            Error = error;

        public string Error { get; }
    }

    public sealed record Unresolved : LibraryAddressPopulationRow
    {
        public Unresolved(
            int lineNumber,
            string value,
            string? label,
            int methodToken,
            int ilOffset,
            ILOffsetProjectionFailure failure)
            : base(lineNumber, label)
        {
            Value = value;
            MethodToken = methodToken;
            ILOffset = ilOffset;
            Failure = failure;
        }

        public string Value { get; }
        public int MethodToken { get; }
        public int ILOffset { get; }
        public ILOffsetProjectionFailure Failure { get; }
    }
}

public enum LibraryAddressInspectionRejection
{
    LeaseReferenceMismatch,
    MissingImplementationAssembly,
    AssemblyIdentityMismatch,
    PortablePdbCompanionAmbiguous,
    PortablePdbCorrespondenceMismatch,
}

public enum LibraryAddressInspectionBound
{
    AssemblyBytes,
    PortablePdbBytes,
    EmbeddedPortablePdbBytes,
}

public enum LibraryAddressInspectionFailure
{
    ContentAccess,
    NotManagedAssembly,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    Inspection,
    ResourceDisposal,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(LibraryAddressInspectionOutcome.Completed),
    "completed")]
[JsonDerivedType(
    typeof(LibraryAddressInspectionOutcome.Partial),
    "partial")]
[JsonDerivedType(
    typeof(LibraryAddressInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(LibraryAddressInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(LibraryAddressInspectionOutcome.Failed),
    "failed")]
public abstract record LibraryAddressInspectionOutcome
{
    private protected LibraryAddressInspectionOutcome()
    {
    }

    public sealed record Completed(LibraryAddressDocument Document)
        : LibraryAddressInspectionOutcome;

    public sealed record Partial(
        LibraryAddressDocument.Population Document)
        : LibraryAddressInspectionOutcome;

    public sealed record Rejected(LibraryAddressInspectionRejection Reason)
        : LibraryAddressInspectionOutcome;

    public sealed record Incomplete(
        LibraryAddressInspectionBound Bound,
        long Limit,
        long Measured)
        : LibraryAddressInspectionOutcome;

    public sealed record Failed
        : LibraryAddressInspectionOutcome
    {
        public Failed(
            LibraryAddressInspectionFailure reason,
            string detail)
            : this(
                reason,
                new InertString(TextPolicy.Field, detail))
        {
        }

        [JsonConstructor]
        public Failed(
            LibraryAddressInspectionFailure reason,
            InertString detail)
        {
            Reason = reason;
            Detail = detail;
        }

        public LibraryAddressInspectionFailure Reason { get; }

        [JsonConverter(typeof(InertStringJsonConverter))]
        public InertString Detail { get; }
    }
}

internal static class LibraryAddressInspectionContract
{
    private const ILOffsetProjectionCapabilities AllCapabilities =
        ILOffsetProjectionCapabilities.SourceLocation
        | ILOffsetProjectionCapabilities.InstructionContext
        | ILOffsetProjectionCapabilities.ExceptionContext
        | ILOffsetProjectionCapabilities.CallsiteContext
        | ILOffsetProjectionCapabilities.ReturnAddressContext
        | ILOffsetProjectionCapabilities.AllocationContext
        | ILOffsetProjectionCapabilities.SafetyContext
        | ILOffsetProjectionCapabilities.CostContext;

    internal static void ValidateIlPoint(
        int methodToken,
        int ilOffset)
    {
        if ((methodToken & unchecked((int)0xFF000000)) != 0x06000000
            || (methodToken & 0x00FFFFFF) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(methodToken),
                methodToken,
                "A Library IL address requires a non-nil MethodDef token.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(ilOffset);
    }

    internal static void ValidateCapabilities(
        ILOffsetProjectionCapabilities capabilities)
    {
        if ((capabilities & ~AllCapabilities) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capabilities),
                capabilities,
                "Unknown IL-offset projection capability.");
        }
    }
}
