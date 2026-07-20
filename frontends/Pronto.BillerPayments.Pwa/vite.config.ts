import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Local dev proxy mirrors the production gateway's path prefixes so the PWA's relative service
// calls resolve to the four in-memory hosts (see scripts/payer-agent-demo/README.md). Only used by
// `vite dev`; production is served behind the real gateway, which owns these same rewrites.
const target = (port: string) => `http://localhost:${port}`;
export default defineConfig({
  base: '/pay/',
  plugins: [react()],
  build: { sourcemap: true },
  server: {
    proxy: {
      // Biller Experience API (payer-chat + published config): gateway strips the /api prefix.
      '/api': { target: target('5000'), changeOrigin: true, rewrite: path => path.replace(/^\/api/, '') },
      // Invoice service: gateway strips the /invoices prefix.
      '/invoices': { target: target('5101'), changeOrigin: true, rewrite: path => path.replace(/^\/invoices/, '') },
      // Payment and Payer Account services keep their prefix (their own routes are /payments, /payers).
      '/payments': { target: target('5102'), changeOrigin: true },
      '/payers': { target: target('5103'), changeOrigin: true },
    },
  },
});
