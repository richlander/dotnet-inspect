declare module "https://cdn.jsdelivr.net/npm/mermaid@11.15.0/dist/mermaid.esm.min.mjs" {
  interface MermaidApi {
    initialize(options: Record<string, unknown>): void;
    render(id: string, definition: string): Promise<{ svg: string }>;
  }

  const mermaid: MermaidApi;
  export default mermaid;
}

declare module "https://cdn.jsdelivr.net/npm/marked@15.0.7/lib/marked.esm.js" {
  export const marked: {
    parse(source: string, options?: Record<string, unknown>): string;
    parseInline(source: string, options?: Record<string, unknown>): string;
  };
}

declare module "https://cdn.jsdelivr.net/npm/dompurify@3.2.4/dist/purify.es.mjs" {
  const DOMPurify: {
    sanitize(source: string, options?: Record<string, unknown>): string;
  };

  export default DOMPurify;
}
