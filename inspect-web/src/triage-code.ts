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

/** Returns one line only when all issue offsets have a unique nearest C# location. */
export function triageIssueLine(document: unknown, offsets: readonly number[]): string | null {
  validateDocument(document);
  if (offsets.length === 0) return null;
  const lines = buildLines(document.text);
  let selectedLine: number | null = null;
  for (const offset of offsets) {
    const matches = document.nodes.filter(node => node.medium === "CSharp"
      && node.provenance?.il_offsets.includes(offset) && node.spans.length > 0);
    if (matches.length === 0) return null;
    const width = (node: (typeof matches)[number]) => node.spans.reduce((sum, span) => sum + span.length, 0);
    const nearestWidth = Math.min(...matches.map(width));
    const touched = new Set<number>();
    for (const node of matches.filter(candidate => width(candidate) === nearestWidth)) {
      for (const span of node.spans) {
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
  return selectedLine === null ? null : lines[selectedLine]!.text.trim();
}

export function bindTriageCode(
  root: ParentNode,
  load: (target: TriageCodeTarget) => Promise<string | null>,
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
          if (!element.isConnected || !code || !text || /[\r\n]/.test(text)) return;
          code.innerHTML = highlight(text);
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
