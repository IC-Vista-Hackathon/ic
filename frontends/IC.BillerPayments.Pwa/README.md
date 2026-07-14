# IC Biller Payments PWA

Configuration-driven customer payment shell. It contains no InvoiceCloud customer-facing branding.

```powershell
npm install
npm run dev
```

The shared renderer extracts the biller slug from `/pay/{slug}/` and loads
`/api/public/experiences/{slug}`. Set `VITE_CONFIG_URL=/config.json` for standalone local UI work.
Invoice lookup and payment execution use a typed demo adapter until the supporting services
implement the documented contracts.
