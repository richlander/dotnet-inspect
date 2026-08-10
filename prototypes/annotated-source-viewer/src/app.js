import {
  buildLines,
  lineMedium,
  nodeIdsForFact,
  nodesAtOffset,
  parseDocument,
  segmentsForLine,
  unanchoredFacts,
  validateDocument,
} from "./document-model.js";
import { sampleDocument } from "./sample-document.js";

const app = document.querySelector("#app");
let sourceDocument = validateDocument(structuredClone(sampleDocument));
let sourceName = "Built-in multi-span sample";
let selectedFactId = null;
let selectedNodeIds = new Set();
let showCSharp = true;
let showIl = true;
let error = "";

function render() {
  const lines = buildLines(sourceDocument.text);
  const anchoredFacts = sourceDocument.facts.filter(fact => nodeIdsForFact(sourceDocument, fact.id).length > 0);
  const looseFacts = unanchoredFacts(sourceDocument);
  const selectedNodes = [...selectedNodeIds].map(id => sourceDocument.nodes[id]).filter(Boolean);

  app.innerHTML = `
    <header class="titlebar">
      <div>
        <p class="eyebrow">dotnet-inspect prototype</p>
        <h1>Annotated source viewer</h1>
      </div>
      <div class="load-actions">
        <span class="source-name">${escapeHtml(sourceName)}</span>
        <label class="button" for="document-file">load JSON</label>
        <input id="document-file" type="file" accept=".json,application/json" />
        <button id="load-sample" type="button">sample</button>
      </div>
    </header>
    ${error ? `<div class="error" role="alert">${escapeHtml(error)}</div>` : ""}
    <section class="summarybar">
      <span><strong>${sourceDocument.nodes.length}</strong> nodes</span>
      <span><strong>${sourceDocument.facts.length}</strong> facts</span>
      <span><strong>${sourceDocument.targets.length}</strong> targets</span>
      <span><strong>${looseFacts.length}</strong> unanchored</span>
      <div class="medium-toggles" aria-label="Visible source media">
        ${mediumToggle("CSharp", "C#", showCSharp)}
        ${mediumToggle("Il", "IL", showIl)}
      </div>
    </section>
    <div class="workspace">
      <section class="code-panel" aria-label="Annotated source text">
        <div class="panel-heading">
          <div>
            <p class="eyebrow">Canonical text</p>
            <h2>UTF-16 buffer</h2>
          </div>
          <p>Click text to select its tightest node. Line numbers are derived from newlines.</p>
        </div>
        <div class="code-scroll">
          ${lines.map(line => renderLine(line)).join("")}
        </div>
      </section>
      <aside class="inspector">
        <section>
          <div class="panel-heading compact">
            <div><p class="eyebrow">Selection</p><h2>${selectionTitle(selectedNodes)}</h2></div>
            ${selectedNodeIds.size ? `<button id="clear-selection" type="button">clear</button>` : ""}
          </div>
          ${renderSelection(selectedNodes)}
        </section>
        <section>
          <div class="panel-heading compact">
            <div><p class="eyebrow">Semantic plane</p><h2>Facts</h2></div>
          </div>
          <div class="fact-list">
            ${anchoredFacts.map(renderFact).join("")}
          </div>
        </section>
        <section>
          <div class="panel-heading compact">
            <div><p class="eyebrow">No invented coordinate</p><h2>Unanchored facts</h2></div>
          </div>
          <div class="fact-list">
            ${looseFacts.length ? looseFacts.map(renderFact).join("") : `<p class="empty">None</p>`}
          </div>
        </section>
        <section>
          <div class="panel-heading compact">
            <div><p class="eyebrow">Structural plane</p><h2>Nodes</h2></div>
          </div>
          <div class="node-list">
            ${sourceDocument.nodes.map(renderNodeButton).join("")}
          </div>
        </section>
      </aside>
    </div>`;

  bindEvents();
}

function renderLine(line) {
  const medium = lineMedium(sourceDocument, line);
  const segments = segmentsForLine(sourceDocument, line, selectedNodeIds);
  const content = segments.length
    ? segments.map(segment => {
        const nodes = segment.nodeIds.map(id => sourceDocument.nodes[id]);
        const title = nodes.map(node => `#${node.id} ${node.kind}`).join(" · ");
        const segmentMedia = segment.media.length
          ? segment.media
          : medium === "Mixed" ? ["CSharp", "Il"] : [medium];
        const visible = segmentMedia.some(value => value === "Il" ? showIl : showCSharp);
        return `<span
          class="code-segment${segment.selected ? " selected" : ""}${segment.nodeIds.length ? " addressable" : ""}${visible ? "" : " segment-hidden"}"
          data-offset="${segment.start}"
          title="${escapeHtml(title)}"
        >${escapeHtml(segment.text)}</span>`;
      }).join("")
    : " ";

  return `<div class="code-line medium-${medium.toLowerCase()}">
    <span class="line-number">${line.number}</span>
    <span class="medium-label">${medium === "Il" ? "IL" : medium === "Mixed" ? "C#/IL" : "C#"}</span>
    <code>${content}</code>
  </div>`;
}

function renderFact(fact) {
  const nodeIds = nodeIdsForFact(sourceDocument, fact.id);
  const active = selectedFactId === fact.id;
  return `<button class="fact${active ? " active" : ""}" type="button" data-fact-id="${fact.id}">
    <span class="fact-title"><strong>${escapeHtml(fact.descriptor)}</strong><small>${escapeHtml(fact.category)}</small></span>
    ${fact.detail ? `<span>${escapeHtml(fact.detail)}</span>` : ""}
    <span class="fact-meta">${escapeHtml(fact.origin)} · ${nodeIds.length ? `${nodeIds.length} target${nodeIds.length === 1 ? "" : "s"}` : "unanchored"}</span>
  </button>`;
}

function renderNodeButton(node) {
  const active = selectedNodeIds.has(node.id);
  return `<button class="node-chip${active ? " active" : ""}" type="button" data-node-id="${node.id}">
    <span>#${node.id}</span>
    <strong>${escapeHtml(node.kind)}</strong>
    <small>${node.medium} · ${node.spans.length} span${node.spans.length === 1 ? "" : "s"}</small>
  </button>`;
}

function renderSelection(nodes) {
  if (nodes.length === 0) {
    return `<p class="empty">Select a fact, node, or source substring.</p>`;
  }
  return `<div class="selection-list">${nodes.map(node => `
    <article>
      <div><strong>#${node.id} ${escapeHtml(node.kind)}</strong><span class="badge">${node.medium}</span></div>
      <p>${node.spans.map(span => `[${span.start}..${span.start + span.length})`).join(" · ")}</p>
      ${node.il_offset == null ? "" : `<p>IL_${node.il_offset.toString(16).padStart(4, "0").toUpperCase()}</p>`}
    </article>`).join("")}</div>`;
}

function selectionTitle(nodes) {
  if (selectedFactId != null) return `Fact #${selectedFactId} targets`;
  if (nodes.length === 1) return `Node #${nodes[0].id}`;
  if (nodes.length > 1) return `${nodes.length} nodes`;
  return "Nothing selected";
}

function mediumToggle(value, label, checked) {
  return `<label><input type="checkbox" data-medium-toggle="${value}"${checked ? " checked" : ""} /> ${label}</label>`;
}

function bindEvents() {
  document.querySelector("#document-file")?.addEventListener("change", loadFile);
  document.querySelector("#load-sample")?.addEventListener("click", () => {
    sourceDocument = validateDocument(structuredClone(sampleDocument));
    sourceName = "Built-in multi-span sample";
    resetSelection();
    error = "";
    render();
  });
  document.querySelector("#clear-selection")?.addEventListener("click", () => {
    resetSelection();
    render();
  });
  document.querySelectorAll("[data-medium-toggle]").forEach(input => {
    input.addEventListener("change", event => {
      if (event.currentTarget.dataset.mediumToggle === "Il") showIl = event.currentTarget.checked;
      else showCSharp = event.currentTarget.checked;
      render();
    });
  });
  document.querySelectorAll("[data-fact-id]").forEach(button => {
    button.addEventListener("click", event => selectFact(Number(event.currentTarget.dataset.factId)));
  });
  document.querySelectorAll("[data-node-id]").forEach(button => {
    button.addEventListener("click", event => selectNode(Number(event.currentTarget.dataset.nodeId)));
  });
  document.querySelectorAll(".code-segment.addressable").forEach(segment => {
    segment.addEventListener("click", event => {
      const offset = Number(event.currentTarget.dataset.offset);
      const node = nodesAtOffset(sourceDocument, offset)[0];
      if (node) selectNode(node.id);
    });
  });
}

async function loadFile(event) {
  const file = event.currentTarget.files?.[0];
  if (!file) return;
  try {
    sourceDocument = parseDocument(await file.text());
    sourceName = file.name;
    resetSelection();
    error = "";
  } catch (loadError) {
    error = loadError instanceof Error ? loadError.message : String(loadError);
  }
  render();
}

function selectFact(factId) {
  selectedFactId = factId;
  selectedNodeIds = new Set(nodeIdsForFact(sourceDocument, factId));
  render();
}

function selectNode(nodeId) {
  selectedFactId = null;
  selectedNodeIds = new Set([nodeId]);
  render();
}

function resetSelection() {
  selectedFactId = null;
  selectedNodeIds = new Set();
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

render();
