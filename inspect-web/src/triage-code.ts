import { buildLines, validateDocument } from "./document-model.ts";

export interface TriageCodeTarget {
  assembly: string;
  typeId: string;
  memberName: string;
  selector: string;
  methodToken: number;
  issueOffsets?: readonly number[] | null;
}

export function triageMemberLabel(typeId: string, memberName: string): string {
  return typeId ? `${typeId}.${memberName}` : memberName;
}

export function renderTriageCode(target: TriageCodeTarget, escape: (value: string) => string): string {
  if (!target.issueOffsets?.length) return "";
  return `<pre hidden class="triage-code language-csharp" data-triage-code data-triage-assembly="${escape(target.assembly)}" data-triage-type="${escape(target.typeId)}" data-triage-member="${escape(target.memberName)}" data-triage-selector="${escape(target.selector)}" data-triage-token="${target.methodToken}" data-triage-offsets="${target.issueOffsets.join(",")}"><code class="language-csharp"></code></pre>`;
}

export interface TriageCodePreview {
  code: string;
  carets: readonly string[];
}

export interface TriageIssuePreview {
  code: string;
  focus: readonly { column: number; length: number }[];
}

/** Selects exact source spans on one line; caret geometry remains owned by the managed printer. */
export function triageIssuePreview(document: unknown, offsets: readonly number[]): TriageIssuePreview | null {
  validateDocument(document);
  if (offsets.length === 0) return null;
  const lines = buildLines(document.text);
  let selectedLine: number | null = null;
  const spans = new Map<string, { start: number; length: number }>();
  for (const offset of offsets) {
    const matches = document.nodes.filter(node => node.medium === "CSharp"
      && node.provenance?.il_offsets.includes(offset) && node.spans.length > 0);
    if (matches.length === 0) return null;
    const width = (node: (typeof matches)[number]) => node.spans.reduce((sum, span) => sum + span.length, 0);
    const nearestWidth = Math.min(...matches.map(width));
    const touched = new Set<number>();
    let nearestSpans: string | null = null;
    for (const node of matches.filter(candidate => width(candidate) === nearestWidth)) {
      const identity = node.spans.map(span => `${span.start}:${span.length}`).sort().join(",");
      if (nearestSpans !== null && nearestSpans !== identity) return null;
      nearestSpans = identity;
      for (const span of node.spans) {
        spans.set(`${span.start}:${span.length}`, span);
        if (span.length === 0) return null;
        const first = lines.findIndex(line => span.start >= line.start && span.start <= line.end);
        const last = lines.findIndex(line => span.start + span.length - 1 >= line.start
          && span.start + span.length - 1 <= line.end);
        if (first < 0 || first !== last) return null;
        touched.add(first);
      }
    }
    if (touched.size !== 1) return null;
    const line = [...touched][0]!;
    if (selectedLine !== null && selectedLine !== line) return null;
    selectedLine = line;
  }
  if (selectedLine === null) return null;
  const line = lines[selectedLine]!;
  const indent = line.text.length - line.text.trimStart().length;
  // The existing comment-caret renderer needs space for its // gutter.
  const code = `    ${line.text.trim()}`;
  const focus = [...spans.values()].map(span => ({ column: span.start - line.start - indent + 4, length: span.length }));
  if (focus.some(span => span.column < 4 || span.column + span.length > code.length)) return null;
  return { code, focus };
}

export function triageIssueLine(document: unknown, offsets: readonly number[]): string | null {
  return triageIssuePreview(document, offsets)?.code.trim() ?? null;
}

export function bindTriageCode(
  root: ParentNode,
  load: (target: TriageCodeTarget) => Promise<TriageCodePreview | null>,
  highlight: (source: string) => string,
): void {
  const queue: HTMLElement[] = [];
  const offered = new Set<HTMLElement>();
  let active = 0;
  const run = () => {
    while (active < 2 && queue.length) {
      const element = queue.shift()!;
      if (!element.isConnected) continue;
      active++;
      void (async () => {
        try {
          const text = await load({
            assembly: element.dataset.triageAssembly ?? "",
            typeId: element.dataset.triageType ?? "",
            memberName: element.dataset.triageMember ?? "",
            selector: element.dataset.triageSelector ?? "",
            methodToken: Number(element.dataset.triageToken),
            issueOffsets: (element.dataset.triageOffsets ?? "").split(",").filter(Boolean).map(Number),
          });
          const code = element.querySelector("code");
          if (!element.isConnected || !code || !text || /[\r\n]/.test(text.code)
            || text.carets.some(caret => /[\r\n]/.test(caret) || !caret.startsWith("//"))) return;
          code.innerHTML = `<span class="triage-source-line">${highlight(text.code)}</span>`
            + text.carets.map(caret => `\n<span class="triage-caret">${highlight(caret)}</span>`).join("");
          element.hidden = false;
        } catch (error) {
          // This view permits only an attributed line; failed acquisition leaves no code.
          console.warn("Triage code line unavailable", error);
        } finally {
          active--;
          run();
        }
      })();
    }
  };
  const offer = (element: HTMLElement) => {
    if (offered.has(element)) return;
    offered.add(element);
    queue.push(element);
    run();
  };
  const elements = [...root.querySelectorAll<HTMLElement>("[data-triage-code]")]
    .filter(element => !element.dataset.triageBound);
  if (elements.length === 0) return;
  for (const element of elements) element.dataset.triageBound = "true";
  if (typeof IntersectionObserver === "undefined") {
    elements.forEach(offer);
    return;
  }
  const byRow = new Map<Element, HTMLElement>();
  for (const element of elements) byRow.set(element.closest(".triage-item") ?? element, element);
  const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
      const element = byRow.get(entry.target);
      if (!element) continue;
      if (entry.isIntersecting && element.isConnected) offer(element);
      if (entry.isIntersecting || !element.isConnected) {
        observer.unobserve(entry.target);
        byRow.delete(entry.target);
      }
    }
    if (byRow.size === 0) observer.disconnect();
  });
  for (const row of byRow.keys()) observer.observe(row);
}
