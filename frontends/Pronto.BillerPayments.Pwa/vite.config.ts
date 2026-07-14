import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
export default defineConfig({ base: '/pay/', plugins: [react()], build: { sourcemap: true } });
