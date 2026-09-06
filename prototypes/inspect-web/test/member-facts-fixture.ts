import type { MemberFacts } from "../src/member-detail-inspection.ts";

export function memberFactsFixture(
  mode: "populated" | "zero" | "long" = "populated",
): MemberFacts {
  const zero = mode === "zero";
  const long = mode === "long";
  return {
    metadataToken: 0x06000125,
    signals: {
      allocations: zero ? 0 : 1,
      copies: long ? 2147483647 : 0,
      reflection: 0,
      throws: zero ? 0 : long ? 2147483647 : 1,
      catches: long ? 2147483647 : 0,
      finallys: long ? 2147483647 : 0,
      unsafe: false,
      allocatesInLoop: false,
      evidenceOffsets: [],
      exceptionTypes: [],
    },
    allocations: zero ? [] : [{
      kind: "newobj",
      type: "System.Text.Json.JsonException",
      offset: "IL_0020",
      countedAsHeap: true,
      frequency: "conditional",
      multiplicity: "once",
      path: "conditional",
      escape: "escapes",
      inLoop: false,
      estimatedSizeBytes: null,
      detail: null,
    }],
    calls: zero ? [] : [
      {
        callee: "JsonTypeInfo.EnsureConfigured()",
        offset: long ? "IL_12345678" : "IL_0008",
        opcode: "call",
        kind: "direct",
        multiplicity: "once",
        inLoop: false,
      },
      {
        callee: "JsonConverter.ReadCore()",
        offset: long ? "IL_23456789" : "IL_0014",
        opcode: "callvirt",
        kind: "virtual",
        multiplicity: "once",
        inLoop: false,
      },
      {
        callee: "JsonException..ctor()",
        offset: "IL_0020",
        opcode: "newobj",
        kind: "constructor",
        multiplicity: "once",
        inLoop: false,
      },
    ],
    safety: [],
    exceptionRegions: [],
    performanceOpportunities: [],
    diagnostics: [],
  };
}

export function allocationFactsFixture(
  mode: "populated" | "long" = "populated",
): MemberFacts {
  const facts = memberFactsFixture();
  const long = mode === "long";
  return {
    ...facts,
    signals: { ...facts.signals, allocations: 2, allocatesInLoop: true },
    allocations: [
      {
        kind: "Object",
        type: long
          ? "Example.Serialization.BufferedDocumentReader<System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Text.Json.JsonElement>>>"
          : "System.Text.Json.JsonException",
        offset: long ? "IL_12345678" : "IL_0020",
        countedAsHeap: true,
        frequency: "Always",
        multiplicity: "Conditional",
        path: "ErrorPath",
        escape: "ThrowPath",
        inLoop: false,
        estimatedSizeBytes: null,
        detail: null,
      },
      {
        kind: "Array",
        type: "System.Byte[]",
        offset: "IL_0048",
        countedAsHeap: true,
        frequency: "PerIteration",
        multiplicity: "Loop",
        path: "LoopBody",
        escape: "LocalOnly",
        inLoop: true,
        estimatedSizeBytes: long ? 2147483647 : 280,
        detail: null,
      },
      {
        kind: "Enumerator",
        type: long ? null
          : "System.Collections.Generic.Dictionary<System.String, System.Text.Json.JsonElement>.Enumerator",
        offset: "IL_009C",
        countedAsHeap: false,
        frequency: "Always",
        multiplicity: "Once",
        path: "StraightLine",
        escape: "Unknown",
        inLoop: false,
        estimatedSizeBytes: null,
        detail: null,
      },
    ],
  };
}

export function callFactsFixture(
  mode: "populated" | "long" = "populated",
): MemberFacts {
  const long = mode === "long";
  return {
    ...memberFactsFixture(),
    calls: [
      {
        offset: "IL_0014",
        opcode: "callvirt",
        kind: "CallVirtual",
        callee: long
          ? "Example.Serialization.BufferedDocumentReader<System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Text.Json.JsonElement>>>.Read<System.Collections.Generic.KeyValuePair<System.String, System.Text.Json.JsonElement>>(System.ReadOnlySpan<System.Byte>, System.Text.Json.Serialization.Metadata.JsonTypeInfo<System.Text.Json.JsonElement>)"
          : "System.Text.Json.Serialization.JsonConverter<System.Text.Json.JsonElement>.Read(System.Text.Json.Utf8JsonReader&, System.Type, System.Text.Json.JsonSerializerOptions)",
        multiplicity: long ? "Unknown" : "Once",
        inLoop: false,
      },
      {
        offset: "IL_0020",
        opcode: "newobj",
        kind: "NewObject",
        callee: "System.Text.Json.JsonException..ctor(System.String)",
        multiplicity: "Conditional",
        inLoop: false,
      },
      {
        offset: "IL_0048",
        opcode: "call",
        kind: "Call",
        callee: "System.Text.Json.Utf8JsonReader.Read()",
        multiplicity: "Loop",
        inLoop: true,
      },
      {
        offset: long ? "IL_12345678" : "IL_0074",
        opcode: "call",
        kind: "Call",
        callee: "System.Text.Json.Utf8JsonReader.Read()",
        multiplicity: "Once",
        inLoop: false,
      },
    ],
  };
}

export function safetyFactsFixture(
  mode: "populated" | "long" = "populated",
): MemberFacts {
  const facts = memberFactsFixture();
  const long = mode === "long";
  return {
    ...facts,
    signals: { ...facts.signals, unsafe: true },
    safety: [
      {
        kind: "Pointer local",
        offset: null,
        operation: "V_0: System.Byte*",
        requirement: "requires unsafe",
        evidence: "local",
      },
      {
        kind: "Unsafe signature",
        offset: null,
        operation: "System.Byte*",
        requirement: "requires unsafe",
        evidence: "signature",
      },
      {
        kind: "stackalloc",
        offset: "IL_0002",
        operation: "byte*",
        requirement: "requires unsafe",
        evidence: "stackalloc",
      },
      {
        kind: "dereference",
        offset: "IL_0008",
        operation: "byte",
        requirement: "requires unsafe",
        evidence: "dereference",
      },
      {
        kind: "Unsafe call",
        offset: long ? "IL_12345678" : "IL_0014",
        operation: long
          ? "Example.Serialization.UnsafeBufferReader<System.Collections.Generic.Dictionary<System.String,System.Collections.Generic.List<System.Text.Json.JsonElement>>>.ReadUnaligned<System.Collections.Generic.KeyValuePair<System.String,System.Text.Json.JsonElement>>(System.Byte*,System.Runtime.CompilerServices.UnsafeBufferReaderOptions)"
          : "System.Runtime.CompilerServices.Unsafe.AsPointer<System.Byte>(System.Byte&)",
        requirement: "requires unsafe",
        evidence: "call",
      },
    ],
  };
}

export function exceptionRegionsFixture(
  mode: "populated" | "long" = "populated",
): MemberFacts {
  const facts = memberFactsFixture();
  const long = mode === "long";
  const regions: MemberFacts["exceptionRegions"] = [
    {
      region: long ? 123456789 : 1,
      clause: "catch",
      tryRange: long ? "IL_01234567..IL_12345678" : "IL_0000..IL_0020",
      handlerRange: long ? "IL_23456789..IL_3456789A" : "IL_0020..IL_0030",
      filterRange: null,
      caughtType: long
        ? "Example.Serialization.BufferedDocumentReader<System.Collections.Generic.Dictionary<System.String,System.Collections.Generic.List<System.Text.Json.JsonElement>>>.NestedReadException"
        : "System.Text.Json.JsonException",
    },
    {
      region: 2,
      clause: "filter",
      tryRange: "IL_0000..IL_0020",
      handlerRange: "IL_0040..IL_0050",
      filterRange: "IL_0030..IL_0040",
      caughtType: null,
    },
    {
      region: 3,
      clause: "finally",
      tryRange: "IL_0000..IL_0050",
      handlerRange: "IL_0050..IL_005A",
      filterRange: null,
      caughtType: null,
    },
    {
      region: 4,
      clause: "fault",
      tryRange: "IL_0060..IL_0070",
      handlerRange: "IL_0070..IL_007A",
      filterRange: null,
      caughtType: null,
    },
  ];
  return {
    ...facts,
    signals: { ...facts.signals, catches: 1, finallys: long ? 0 : 1 },
    exceptionRegions: long ? regions.slice(0, 2) : regions,
  };
}
