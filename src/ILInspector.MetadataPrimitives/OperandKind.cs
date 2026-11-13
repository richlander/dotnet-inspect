using System.Reflection.Metadata;

namespace ILInspector.Instructions;

// The IL byte layer (ILReader, ILOpcodeExtensions, OperandKind) lives in
// ILInspector.MetadataPrimitives, below both ILInspector.Instructions and
// ILInspector.Metadata, so the decoder and Finding Diff Any's Body walk read
// one opcode table without either assembly referencing the other
// (docs/design/instruction-substrate.md, "Layering"). It keeps the
// ILInspector.Instructions namespace, as the substrate's public surface.

/// <summary>
/// Classifies an opcode's inline operand. Determines operand size during decode and
/// lets consumers read tokens/indices/constants without re-deriving opcode shape.
/// </summary>
public enum OperandKind
{
    None,
    ShortInlineVar,   // 1-byte local/argument index
    InlineVar,        // 2-byte local/argument index
    ShortInlineI,     // int8
    InlineI,          // int32
    InlineI8,         // int64
    ShortInlineR,     // float32
    InlineR,          // float64
    InlineString,     // string token
    InlineMethod,     // method token
    InlineField,      // field token
    InlineType,       // type token
    InlineSig,        // standalone signature token (calli)
    InlineTok,        // ldtoken: field/method/type token
    ShortInlineBrTarget,
    InlineBrTarget,
    InlineSwitch,
}
