import assert from "node:assert/strict";
import test from "node:test";
import {
  applyCommandCompletion,
  commandCompletions,
  commandPaletteResults,
  commandPaletteRowHtml,
} from "../src/command-bar.ts";

const lenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"],
];

function commandContext(command, types = []) {
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

function escapeHtml(value) {
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
    ["type", "types", "show", "framework", "find", "clear", "share"],
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
  assert.deepEqual(commandPaletteResults(commandContext("find "), lenses), []);
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
  assert.match(html, /data-sl-index="2"/);
  assert.match(html, /show &quot;&lt;source&gt;&quot;/);
  assert.match(html, /A&amp;B · lens/);
});
