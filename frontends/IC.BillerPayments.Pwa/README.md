# IC Biller Payments PWA

Configuration-driven customer payment shell. It contains no InvoiceCloud customer-facing branding.

```powershell
npm install
npm run dev
```

The app loads `/config.json` by default. Set `VITE_CONFIG_URL` to use a published configuration
endpoint. Invoice lookup and payment execution use a typed demo adapter until the supporting
services implement the documented contracts.
