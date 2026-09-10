// Prism is a core module plus per-language grammar modules that register themselves onto
// the core when imported. The registration is a side effect and the order is load-bearing:
// csharp extends clike, which extends the core. Both facts are invisible at a call site
// that only reads `Prism.languages.csharp`, so the imports live here, in the one module
// that exists to own them, rather than beside a consumer where reordering them would
// silently produce an unhighlighted page instead of an error.
import Prism from "prismjs";
// These two imports are unassigned because each grammar module's only effect is
// registering itself onto the core, so there is nothing to bind. `.oxlintrc.json` allows
// exactly `prismjs/components/*` for that reason rather than suppressing the rule here.
import "prismjs/components/prism-clike";
import "prismjs/components/prism-csharp";

import type { PrismCSharpHighlighter } from "./csharp-highlighting.ts";

// The grammar is registered by the imports above, so this is not the optional
// `window.Prism` the CDN build produced -- a missing grammar here is a build defect, not a
// network condition, and callers no longer need a fallback path for it.
export const prismCSharp: PrismCSharpHighlighter = Prism;
