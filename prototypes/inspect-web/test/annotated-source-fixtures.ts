import type { AnnotatedSourceDocument } from "../src/annotated-source-view.ts";

const text = "return x => x + first + second;";
const lambdaStart = text.indexOf("x =>");
const firstStart = text.indexOf("first");
const secondStart = text.indexOf("second");

export const captureDocument: AnnotatedSourceDocument = {
  text,
  nodes: [
    {
      id: 0,
      kind: "LambdaExpression",
      medium: "CSharp",
      spans: [{ start: lambdaStart, length: text.length - lambdaStart - 1 }],
    },
    {
      id: 1,
      kind: "NameExpression",
      medium: "CSharp",
      spans: [{ start: firstStart, length: "first".length }],
    },
    {
      id: 2,
      kind: "NameExpression",
      medium: "CSharp",
      spans: [{ start: secondStart, length: "second".length }],
    },
    {
      id: 3,
      kind: "ReturnStatement",
      medium: "CSharp",
      spans: [{ start: 0, length: text.length }],
    },
  ],
  regions: [],
  facts: [],
  targets: [],
  captures: [
    { parent_node_id: 0, display_name: "first", use_node_ids: [1] },
    { parent_node_id: 0, display_name: "second", use_node_ids: [2] },
  ],
};

const safetyCalleeText = "byte* buffer = stackalloc byte[16];";

export const safetyCalleeDocument: AnnotatedSourceDocument = {
  text: safetyCalleeText,
  nodes: [
    {
      id: 0,
      kind: "StackAllocationExpression",
      medium: "CSharp",
      spans: [{
        start: safetyCalleeText.indexOf("stackalloc"),
        length: "stackalloc byte[16]".length,
      }],
    },
  ],
  regions: [],
  facts: [],
  targets: [],
};
