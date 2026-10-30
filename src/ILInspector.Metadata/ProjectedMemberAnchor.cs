namespace ILInspector.Metadata;

public readonly record struct ProjectedMemberAnchor(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName);
