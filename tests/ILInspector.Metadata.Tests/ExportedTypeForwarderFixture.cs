using System.Runtime.CompilerServices;

// Forwards a referenced type so this test assembly carries a deterministic
// ExportedType (0x27) row. That lets MetadataTableProjectionTests assert the
// ExportedType projection's shape against SelfPath uniformly with every other
// supported table (the test assembly otherwise defines no forwarders). The
// forward is inert at runtime: nothing resolves this type name against the test
// assembly, and the type genuinely lives in ILInspector.Metadata.
[assembly: TypeForwardedTo(typeof(ILInspector.Metadata.MetadataTableProjector))]
