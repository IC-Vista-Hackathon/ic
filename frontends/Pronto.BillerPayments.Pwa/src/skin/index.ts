export { Header, Intro, Footer } from './chrome';
export type {
  HeaderProps,
  IntroProps,
  FooterProps,
  SkinBrand,
  SkinContent,
  SkinPage,
  SkinNavItem,
} from './contract';

// Files an AI generator is permitted to rewrite for a bespoke per-biller skin.
// Consumed by the generation/build pipeline; the core and payment flow are off-limits.
export const SKIN_EDITABLE_FILES = ['src/skin/theme.css', 'src/skin/chrome.tsx'] as const;
