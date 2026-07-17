export { Header, Intro, Footer } from './chrome';
export { InvoiceSelectList, Cart, BatchReview } from './flow';
export type {
  HeaderProps,
  IntroProps,
  FooterProps,
  SkinBrand,
  SkinContent,
  SkinPage,
  SkinNavItem,
  SelectableInvoice,
  InvoiceSelectListProps,
  CartLine,
  CartSummary,
  CartProps,
  BatchLineStatus,
  BatchReviewLine,
  BatchReviewProps,
} from './contract';

// Files an AI generator is permitted to rewrite for a bespoke per-biller experience.
// SINGLE SOURCE OF TRUTH for the authorable surface — the generation/build pipeline and
// the F5 static containment gate + F6 runtime boundary gate all read this allowlist.
// theme.css/chrome.tsx are the look-and-feel skin; flow.tsx is the authorable STRUCTURE
// (invoice selection list, cart, batch review). Everything here must stay presentational:
// no payment logic, no service calls, no money math — those live in the stable core.
export const SKIN_EDITABLE_FILES = ['src/skin/theme.css', 'src/skin/chrome.tsx', 'src/skin/flow.tsx'] as const;
