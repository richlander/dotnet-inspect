import assert from "node:assert/strict";
import test from "node:test";
import {
  applyCommandCompletion,
  commandCompletions,
  commandPaletteResults,
  commandPaletteRowHtml,
} from "../src/command-bar.ts";
import type { CommandContext } from "../src/command-bar.ts";

const lenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"],
] as const;

function commandContext(
  command: string,
  types: CommandContext["package"]["types"] = [],
): CommandContext {
  return {
    command,
    package: {
      id: "System.Text.Json",
      activeFramework: "net10.0",
      types,
      frameworks: ["net8.0", "net9.0", "net10.0"],
    },
  };
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

test("the empty command scope offers the root command grammar", () => {
  const items = commandCompletions(commandContext(""), lenses);

  assert.deepEqual(
    items.map(item => item.value),
    [
      "type",
      "types",
      "show",
      "framework",
      "find",
      "clear",
      "share",
      "settings",
      "keyboard help",
    ],
  );
  assert.ok(items.every(item => item.category === "command"));
});

test("type completion filters package types and caps an open argument list", () => {
  const types = Array.from({ length: 10 }, (_, index) => ({
    id: `Example.Type${index}`,
    name: index === 9 ? "JsonSerializer" : `Type${index}`,
    namespace: "Example",
    kind: "class",
  }));

  assert.deepEqual(
    commandCompletions(commandContext("type json", types), lenses)
      .map(item => item.value),
    ["JsonSerializer"],
  );
  assert.equal(
    commandCompletions(commandContext("type ", types), lenses).length,
    8,
  );
});

test("an exact type match sorts ahead of capped partial matches", () => {
  const types = [
    ...Array.from({ length: 8 }, (_, index) => ({
      id: `Example.JsonSerializer${index}`,
      name: `JsonSerializer${index}`,
      namespace: "Example",
      kind: "class",
    })),
    {
      id: "Example.JsonSerializer",
      name: "JsonSerializer",
      namespace: "Example",
      kind: "class",
    },
  ];

  assert.equal(
    commandPaletteResults(commandContext("type JsonSerializer", types), lenses)[0]?.command,
    "type JsonSerializer",
  );
});

test("duplicate type names retain the selected type identity", () => {
  const results = commandPaletteResults(commandContext("type Widget", [
    {
      id: "A.Widget",
      name: "Widget",
      namespace: "A",
      kind: "class",
    },
    {
      id: "B.Widget",
      name: "Widget",
      namespace: "B",
      kind: "class",
    },
  ]), lenses);

  assert.deepEqual(
    results.map(result => [result.command, result.hint, result.targetTypeId]),
    [
      ["type Widget", "A", "A.Widget"],
      ["type Widget", "B", "B.Widget"],
    ],
  );
});

test("lens, framework, and type-filter arguments retain command metadata", () => {
  assert.deepEqual(
    commandCompletions(commandContext("show "), lenses),
    [
      { value: "api", hint: "API lens", category: "lens" },
      { value: "metadata", hint: "Metadata lens", category: "lens" },
      { value: "source", hint: "Source lens", category: "lens" },
    ],
  );
  assert.deepEqual(
    commandCompletions(commandContext("framework net9"), lenses),
    [{ value: "net9.0", hint: "compile assets", category: "framework" }],
  );
  assert.deepEqual(
    commandCompletions(commandContext("types kind"), lenses),
    [{ value: "kind", hint: "filter by class, struct, interface, or enum", category: "filter" }],
  );
});

test("completion replaces the active token and preserves append behavior", () => {
  assert.equal(applyCommandCompletion("", "type"), "type ");
  assert.equal(
    applyCommandCompletion("type Json", "JsonSerializer"),
    "type JsonSerializer ",
  );
  assert.equal(
    applyCommandCompletion("  type ", "JsonSerializer"),
    "  type JsonSerializer ",
  );
});

test("command palette results distinguish completion from execution", () => {
  assert.deepEqual(
    commandPaletteResults(commandContext("ty"), lenses)
      .map(result => [result.command, result.action]),
    [["type", "complete"], ["types", "complete"]],
  );
  assert.deepEqual(
    commandPaletteResults(commandContext("cl"), lenses)
      .map(result => [result.command, result.action]),
    [["clear", "execute"]],
  );
  assert.deepEqual(
    commandPaletteResults(commandContext("show met"), lenses)
      .map(result => [result.command, result.action]),
    [["show metadata", "execute"]],
  );
});

test("trailing whitespace preserves completed command arguments", () => {
  const types = [
    {
      id: "A.Widget",
      name: "Widget",
      namespace: "A",
      kind: "class",
    },
    {
      id: "B.Widget",
      name: "Widget",
      namespace: "B",
      kind: "class",
    },
  ];

  assert.deepEqual(
    commandPaletteResults(commandContext("type Widget ", types), lenses)
      .map(result => [result.command, result.targetTypeId]),
    [["type Widget", "A.Widget"], ["type Widget", "B.Widget"]],
  );
  const executableCommands: Array<[string, string]> = [
    ["show metadata ", "show metadata"],
    ["framework net9.0 ", "framework net9.0"],
    ["types kind ", "types kind"],
    ["clear ", "clear"],
    ["share ", "share"],
    ["settings ", "settings"],
    ["keyboard help ", "keyboard help"],
  ];
  for (const [command, expected] of executableCommands) {
    assert.deepEqual(
      commandPaletteResults(commandContext(command), lenses)
        .map(result => [result.command, result.action]),
      [[expected, "execute"]],
    );
  }
  for (const invalid of [
    "show metadata extra ",
    "clear clear",
    "share share ",
    "settings settings ",
    "keyboard help extra ",
    "bogus clear",
    "bogus ",
    "ty ",
  ]) {
    assert.deepEqual(commandPaletteResults(commandContext(invalid), lenses), []);
  }
});

test("an exact root verb advances to its arguments without dropping the verb", () => {
  for (const verb of ["type", "types", "show", "framework", "find"]) {
    assert.deepEqual(
      commandPaletteResults(commandContext(verb), lenses)
        .map(result => [result.command, result.action]),
      [[verb, "complete"]],
    );
  }
  assert.deepEqual(
    commandPaletteResults(commandContext("clear"), lenses)
      .map(result => [result.command, result.action]),
    [["clear", "execute"]],
  );
});

test("free-text find commands remain executable after moving into search", () => {
  assert.deepEqual(
    commandPaletteResults(commandContext("find JsonSerializer"), lenses),
    [{
      kind: "command",
      value: "find JsonSerializer",
      hint: "search the current package",
      category: "command",
      command: "find JsonSerializer",
      action: "execute",
    }],
  );
  assert.deepEqual(
    commandPaletteResults(commandContext("find "), lenses),
    [{
      kind: "command",
      value: "find",
      hint: "enter search text",
      category: "command",
      command: "find",
      action: "complete",
    }],
  );
});

test("command result markup keeps command text and metadata inert", () => {
  const html = commandPaletteRowHtml({
    kind: "command",
    value: 'show "<source>"',
    hint: "A&B",
    category: "lens",
    command: 'show "<source>"',
    action: "execute",
  }, 2, true, escapeHtml);

  assert.match(html, /class="spotlight-item selected"/);
  assert.match(html, /id="spotlight-result-2"/);
  assert.match(html, /data-sl-index="2"/);
  assert.match(html, /show &quot;&lt;source&gt;&quot;/);
  assert.match(html, /A&amp;B · lens/);
});
