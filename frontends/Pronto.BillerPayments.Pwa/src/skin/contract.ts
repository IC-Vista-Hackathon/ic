import type { ExperienceDefinition } from '../types';

// The skin is the ONLY surface an AI generator is allowed to rewrite per biller.
// It controls look and feel (theme.css) and the branded chrome (header, intro, footer)
// through this typed contract. It must never contain payment flow, service calls, or
// business logic — those live in the stable core (App.tsx) and are validated by the
// Playwright gate against fixed data-testids the skin does not own.

export type SkinBrand = ExperienceDefinition['brand'];
export type SkinContent = ExperienceDefinition['content'];

export type SkinPage = 'payment' | 'history' | 'preferences';

export interface SkinNavItem {
  page: SkinPage;
  label: string;
  active: boolean;
  onSelect: () => void;
}

export interface HeaderProps {
  brand: SkinBrand;
}

export interface IntroProps {
  eyebrow: string;
  heading: string;
  subheading: string;
}

export interface FooterProps {
  brand: SkinBrand;
  content: SkinContent;
}
