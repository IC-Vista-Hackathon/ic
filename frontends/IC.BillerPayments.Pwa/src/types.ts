export interface ExperienceDefinition { schema_version: string; biller_id: string; brand: { display_name: string; primary_color: string; secondary_color: string; font_family: string | null }; content: { heading: string; introduction: string; support_text: string; privacy_policy_url: string; terms_of_service_url: string }; pwa: { name: string; short_name: string; theme_color: string; background_color: string }; enabled_payment_capabilities: string[] }
export interface Invoice { id: string; accountNumber: string; amountCents: number; dueDate: string; description: string }
export interface PaymentRequest { invoiceId: string; method: 'card' | 'ach'; autoPay: boolean; paperless: boolean }
export interface PaymentReceipt { confirmation: string; amountCents: number; feeCents: number }
