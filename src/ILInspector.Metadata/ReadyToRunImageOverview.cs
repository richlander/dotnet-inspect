using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// The validated ReadyToRun envelope of one PE image.
/// </summary>
public sealed record ReadyToRunImageOverview
{
    public ReadyToRunImageOverview(
        ReadyToRunImageRole Role,
        ReadyToRunAdvertisement Advertisements,
        DirectoryEntry? ManagedNativeHeaderDirectory,
        int? ExportHeaderRelativeVirtualAddress,
        int HeaderRelativeVirtualAddress,
        int HeaderEncodedSize,
        uint Signature,
        ushort MajorVersion,
        ushort MinorVersion,
        ReadyToRunHeaderFlags Flags,
        ImmutableArray<ReadyToRunSectionSummary> Sections)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(HeaderRelativeVirtualAddress);
        ArgumentOutOfRangeException.ThrowIfLessThan(HeaderEncodedSize, ReadyToRunImageInspector.FixedHeaderSize);
        if (Advertisements == ReadyToRunAdvertisement.None)
            throw new ArgumentException("At least one ReadyToRun advertisement is required.", nameof(Advertisements));
        if (Sections.IsDefault)
            throw new ArgumentException("Sections must be initialized.", nameof(Sections));

        this.Role = Role;
        this.Advertisements = Advertisements;
        this.ManagedNativeHeaderDirectory = ManagedNativeHeaderDirectory;
        this.ExportHeaderRelativeVirtualAddress = ExportHeaderRelativeVirtualAddress;
        this.HeaderRelativeVirtualAddress = HeaderRelativeVirtualAddress;
        this.HeaderEncodedSize = HeaderEncodedSize;
        this.Signature = Signature;
        this.MajorVersion = MajorVersion;
        this.MinorVersion = MinorVersion;
        this.Flags = Flags;
        this.Sections = Sections;
    }

    /// <summary>The image's role in a standalone or composite compilation.</summary>
    public ReadyToRunImageRole Role { get; }

    /// <summary>The canonical PE advertisements that identified the header.</summary>
    public ReadyToRunAdvertisement Advertisements { get; }

    /// <summary>The CLI managed-native directory when it advertised this header.</summary>
    public DirectoryEntry? ManagedNativeHeaderDirectory { get; }

    /// <summary>The header RVA obtained from the canonical PE export, when present.</summary>
    public int? ExportHeaderRelativeVirtualAddress { get; }

    /// <summary>The ReadyToRun header RVA.</summary>
    public int HeaderRelativeVirtualAddress { get; }

    /// <summary>The encoded header and section-directory size in bytes.</summary>
    public int HeaderEncodedSize { get; }

    /// <summary>The validated ReadyToRun signature.</summary>
    public uint Signature { get; }

    /// <summary>The format major version, preserved without an execution-compatibility judgment.</summary>
    public ushort MajorVersion { get; }

    /// <summary>The format minor version, preserved without an execution-compatibility judgment.</summary>
    public ushort MinorVersion { get; }

    /// <summary>The complete header flags, including unknown future bits.</summary>
    public ReadyToRunHeaderFlags Flags { get; }

    /// <summary>Every section entry in the strictly increasing order encoded by the image.</summary>
    public ImmutableArray<ReadyToRunSectionSummary> Sections { get; }

    /// <summary>The R2R manifest metadata extent, when section 112 is present.</summary>
    public ReadyToRunSectionSummary? ManifestMetadata
        => Sections.FirstOrDefault(static section => section.Type == ReadyToRunSectionType.ManifestMetadata);
}

/// <summary>The semantic role established by R2R flags and section evidence.</summary>
public enum ReadyToRunImageRole
{
    /// <summary>A self-contained single-assembly ReadyToRun image.</summary>
    Standalone,

    /// <summary>The native container for a composite ReadyToRun compilation.</summary>
    Composite,

    /// <summary>An IL component whose native code is owned by a composite image.</summary>
    Component,

    /// <summary>The header carries both component and composite-container role evidence.</summary>
    Ambiguous,
}

/// <summary>The canonical PE locations that advertised a ReadyToRun header.</summary>
[Flags]
public enum ReadyToRunAdvertisement
{
    None = 0,
    ManagedNativeHeader = 1,
    Export = 2,
}

/// <summary>ReadyToRun header flags. Undefined bits remain preserved by the enum value.</summary>
[Flags]
public enum ReadyToRunHeaderFlags : uint
{
    None = 0,
    PlatformNeutralSource = 0x0001,
    SkipTypeValidation = 0x0002,
    Partial = 0x0004,
    NonSharedPInvokeStubs = 0x0008,
    EmbeddedMSIL = 0x0010,
    Component = 0x0020,
    MultiModuleVersionBubble = 0x0040,
    UnrelatedR2RCode = 0x0080,
    PlatformNativeImage = 0x0100,
    StrippedILBodies = 0x0200,
    StrippedInliningInfo = 0x0400,
    StrippedDebugInfo = 0x0800,
}

/// <summary>One validated ReadyToRun section-directory entry.</summary>
public sealed record ReadyToRunSectionSummary
{
    public ReadyToRunSectionSummary(
        ReadyToRunSectionType Type,
        int RelativeVirtualAddress,
        int Size,
        bool AliasesCliMetadataDirectory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RelativeVirtualAddress);
        ArgumentOutOfRangeException.ThrowIfNegative(Size);

        this.Type = Type;
        this.RelativeVirtualAddress = RelativeVirtualAddress;
        this.Size = Size;
        this.AliasesCliMetadataDirectory = AliasesCliMetadataDirectory;
    }

    /// <summary>The section type, including unknown future numeric values.</summary>
    public ReadyToRunSectionType Type { get; }

    /// <summary>The section payload RVA.</summary>
    public int RelativeVirtualAddress { get; }

    /// <summary>The section payload size in bytes.</summary>
    public int Size { get; }

    /// <summary>
    /// True when this exact RVA and size are also the CLI metadata directory.
    /// Composite images commonly advertise their manifest metadata through both
    /// identities.
    /// </summary>
    public bool AliasesCliMetadataDirectory { get; }
}

/// <summary>
/// ReadyToRun section types. Undefined numeric values remain valid projection
/// values when the directory preserves strict numeric ordering.
/// </summary>
public enum ReadyToRunSectionType : uint
{
    CompilerIdentifier = 100,
    ImportSections = 101,
    RuntimeFunctions = 102,
    MethodDefEntryPoints = 103,
    ExceptionInfo = 104,
    DebugInfo = 105,
    DelayLoadMethodCallThunks = 106,
    AvailableTypes = 108,
    InstanceMethodEntryPoints = 109,
    InliningInfo = 110,
    ProfileDataInfo = 111,
    ManifestMetadata = 112,
    AttributePresence = 113,
    InliningInfo2 = 114,
    ComponentAssemblies = 115,
    OwnerCompositeExecutable = 116,
    PgoInstrumentationData = 117,
    ManifestAssemblyMvids = 118,
    CrossModuleInlineInfo = 119,
    HotColdMap = 120,
    MethodIsGenericMap = 121,
    EnclosingTypeMap = 122,
    TypeGenericInfoMap = 123,
    ExternalTypeMaps = 124,
    ProxyTypeMaps = 125,
    TypeMapAssemblyTargets = 126,
}
