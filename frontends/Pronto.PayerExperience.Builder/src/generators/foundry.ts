import { DefaultAzureCredential } from '@azure/identity';
import type { DesignBrief, GeneratedSkin } from '../types';
import { flowTsx as defaultFlowTsx } from './deterministic';
import type { SkinGenerator } from './index';

// Real bespoke-authoring path: Claude Opus hosted on Azure AI Foundry, called through
// the unified model-inference chat/completions API so the same endpoint that serves the
// OpenAI config models also serves Anthropic. Auth is workload identity / az login by
// default (no key in pods), with an optional key for local runs.
export interface FoundryOptions {
  endpoint?: string;
  deployment?: string;
  chatUrl?: string;
  apiVersion?: string;
  apiKey?: string;
  maxTokens?: number;
}

const COGNITIVE_SERVICES_SCOPE = 'https://cognitiveservices.azure.com/.default';

export class FoundryOpusSkinGenerator implements SkinGenerator {
  readonly name = 'opus';
  private readonly options: FoundryOptions;

  constructor(options: FoundryOptions = {}) {
    this.options = {
      endpoint: options.endpoint ?? process.env.FOUNDRY_ENDPOINT,
      deployment: options.deployment ?? process.env.FOUNDRY_OPUS_DEPLOYMENT ?? 'claude-opus-4-1',
      chatUrl: options.chatUrl ?? process.env.FOUNDRY_CHAT_URL,
      apiVersion: options.apiVersion ?? process.env.FOUNDRY_API_VERSION ?? '2024-05-01-preview',
      apiKey: options.apiKey ?? process.env.FOUNDRY_API_KEY,
      maxTokens: options.maxTokens ?? 8000,
    };
  }

  async generate(brief: DesignBrief): Promise<GeneratedSkin> {
    const url = this.resolveUrl();
    const body = {
      model: this.options.deployment,
      max_tokens: this.options.maxTokens,
      temperature: 0.4,
      messages: [
        { role: 'system', content: SYSTEM_PROMPT },
        { role: 'user', content: JSON.stringify({ design_brief: brief, editable_files: EDITABLE_FILES, contract: CONTRACT_TS }) },
      ],
      response_format: { type: 'json_object' },
    };

    const response = await fetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...(await this.authHeaders()) },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(`Foundry request failed (${response.status}): ${await response.text().catch(() => '')}`);
    }
    const payload = (await response.json()) as { choices?: Array<{ message?: { content?: string } }> };
    const content = payload.choices?.[0]?.message?.content;
    if (!content) throw new Error('Foundry returned no content.');
    return parseSkin(content, brief);
  }

  private resolveUrl(): string {
    if (this.options.chatUrl) return this.options.chatUrl;
    if (!this.options.endpoint) {
      throw new Error('Set FOUNDRY_ENDPOINT (or FOUNDRY_CHAT_URL) to call the Opus generator.');
    }
    const base = this.options.endpoint.replace(/\/$/, '');
    return `${base}/models/chat/completions?api-version=${this.options.apiVersion}`;
  }

  private async authHeaders(): Promise<Record<string, string>> {
    if (this.options.apiKey) return { 'api-key': this.options.apiKey };
    const token = await new DefaultAzureCredential().getToken(COGNITIVE_SERVICES_SCOPE);
    if (!token) throw new Error('Unable to acquire an Azure AD token for Foundry.');
    return { authorization: `Bearer ${token.token}` };
  }
}

function parseSkin(content: string, brief: DesignBrief): GeneratedSkin {
  const json = stripFence(content);
  const parsed = JSON.parse(json) as Partial<GeneratedSkin>;
  if (!parsed.themeCss || !parsed.chromeTsx) {
    throw new Error('Opus response missing themeCss/chromeTsx.');
  }
  // flow.tsx is authorable but optional: if the model doesn't author the multi-invoice
  // structure, fall back to the default presentational flow so the bundle still builds.
  return {
    themeCss: parsed.themeCss,
    chromeTsx: parsed.chromeTsx,
    flowTsx: parsed.flowTsx ?? defaultFlowTsx(brief),
    notes: parsed.notes,
  };
}

function stripFence(text: string): string {
  const trimmed = text.trim();
  const fence = trimmed.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/);
  return fence ? fence[1] : trimmed;
}

const EDITABLE_FILES = ['src/skin/theme.css', 'src/skin/chrome.tsx', 'src/skin/flow.tsx'];

const CONTRACT_TS = `export interface HeaderProps { brand: { display_name: string; primary_color: string; secondary_color: string; font_family: string | null } }
export interface IntroProps { eyebrow: string; heading: string; subheading: string }
export interface FooterProps { brand: HeaderProps['brand']; content: { support_text: string; privacy_policy_url: string; terms_of_service_url: string } }
// chrome.tsx must export: Header(HeaderProps), Intro(IntroProps), Footer(FooterProps)
// --- flow.tsx (feature F3): the authorable STRUCTURE of the multi-invoice checkout ---
export interface SelectableInvoice { id: string; typeLabel?: string; description: string; dueDateLabel: string; amountLabel: string; statusColor?: string; statusLabel?: string; note?: string; noteEmphasis?: boolean; selected: boolean }
export interface InvoiceSelectListProps { heading: string; invoices: SelectableInvoice[]; onToggle: (invoiceId: string) => void; onSelectAll: () => void; onClearAll: () => void; allSelected: boolean }
export interface CartLine { id: string; label: string; typeLabel?: string; amountLabel: string }
export interface CartSummary { lines: CartLine[]; count: number; subtotalLabel: string; feeLabel?: string; totalLabel: string }
export interface CartProps { summary: CartSummary; onRemove?: (invoiceId: string) => void; emptyText: string }
export type BatchLineStatus = 'pending' | 'paid' | 'failed';
export interface BatchReviewLine { id: string; label: string; typeLabel?: string; amountLabel: string; feeLabel: string; totalLabel: string; status: BatchLineStatus; statusMessage?: string }
export interface BatchReviewProps { heading: string; lines: BatchReviewLine[]; totalLabel: string; consentText: string }
// flow.tsx must export: InvoiceSelectList(InvoiceSelectListProps), Cart(CartProps), BatchReview(BatchReviewProps).
// All money strings (amountLabel/feeLabel/totalLabel/subtotalLabel) are preformatted by the core.`;

const SYSTEM_PROMPT = `You are a senior React/CSS designer producing a bespoke skin for one biller's payment PWA.
Return ONLY a JSON object: {"themeCss": string, "chromeTsx": string, "flowTsx": string, "notes": string}.

Hard rules — the build and a scripted Playwright gate will reject violations:
- Edit ONLY the editable files. Never touch payment flow, service calls, or money logic.
- chrome.tsx is PRESENTATIONAL ONLY: no fetch, no network, no state with side effects, no payment logic, no new npm imports. Import types only from './contract'. Export exactly Header, Intro, Footer with the given signatures.
- flow.tsx is PRESENTATIONAL ONLY and authors the multi-invoice selection list, cart, and batch review STRUCTURE: no fetch, no network, no payment logic, no fee/total/amount math. Import types only from './contract'. Export exactly InvoiceSelectList, Cart, BatchReview with the given signatures. Render the preformatted money strings the core supplies; call the provided callbacks to toggle/select/remove — never compute money or settle invoices. Keep the data-testid hooks (invoice-select, select-all/clear-all, invoice-option-<id>, cart, cart-line-<id>, cart-subtotal, cart-fee, cart-total, batch-review, batch-line-<id>, batch-status-<id>, batch-total).
- Preserve every CSS class name the core renders (app, mark, account-nav, intro, card, card-copy, bill, choices, method-chips, check, notice, consent, actions, success, success-icon, pill, history-row, preference-summary, alert, center). Restyle them freely; do not delete them or the data-testid controls will lose styling.
- No <script>, no external URLs in CSS except fonts you declare, no @import of untrusted origins, no inline event handlers.
- Keep strong color contrast (WCAG AA) and respect prefers-reduced-motion.
- Use the brand colors and voice/tone from the brief. Make it look genuinely different from a generic template.`;
