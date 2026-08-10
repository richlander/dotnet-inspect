const header = "for (var i = 0; i < 2; i++)";
const initialize = "IL_0000: ldc.i4.0";
const open = "{";
const body = "    return new object();";
const allocate = "IL_0001: newobj instance void System.Object::.ctor()";
const close = "}";
const lines = [header, initialize, open, body, allocate, close];
const starts = [];
let cursor = 0;

for (const line of lines) {
  starts.push(cursor);
  cursor += line.length + 1;
}

const text = lines.join("\n");
const objectStart = starts[3] + body.indexOf("new object()");

export const sampleDocument = {
  text,
  nodes: [
    {
      id: 0,
      kind: "ForLoop",
      medium: "CSharp",
      spans: [
        { start: starts[0], length: header.length + 1 },
        { start: starts[2], length: open.length + 1 + body.length + 1 },
        { start: starts[5], length: close.length },
      ],
    },
    {
      id: 1,
      kind: "NewObject",
      medium: "CSharp",
      spans: [{ start: objectStart, length: "new object()".length }],
    },
    {
      id: 2,
      kind: "Instruction",
      medium: "Il",
      spans: [{ start: starts[1], length: initialize.length }],
      il_offset: 0,
    },
    {
      id: 3,
      kind: "Instruction",
      medium: "Il",
      spans: [{ start: starts[4], length: allocate.length }],
      il_offset: 1,
    },
  ],
  regions: [
    {
      role: "Body",
      spans: [
        { start: starts[2], length: open.length + 1 + body.length + 1 },
        { start: starts[5], length: close.length },
      ],
    },
  ],
  facts: [
    {
      id: 0,
      descriptor: "alloc.new",
      category: "Allocation",
      conditionality: "Always",
      detail: "object",
      source_offset: 1,
      origin: "Body",
    },
    {
      id: 1,
      descriptor: "cost.loop",
      category: "Cost",
      conditionality: "Always",
      detail: "2 iterations",
      source_offset: 0,
      origin: "Body",
    },
    {
      id: 2,
      descriptor: "cost.method",
      category: "Cost",
      conditionality: "Always",
      detail: "header-only observation",
      source_offset: -1,
      origin: "MemberHeader",
    },
  ],
  targets: [
    { fact_id: 0, node_id: 1 },
    { fact_id: 0, node_id: 3 },
    { fact_id: 1, node_id: 0 },
    { fact_id: 1, node_id: 2 },
  ],
};
