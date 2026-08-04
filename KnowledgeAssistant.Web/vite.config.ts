import { fileURLToPath, URL } from 'node:url';

import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * The API this client talks to during development.
 *
 * Requests are proxied rather than sent cross-origin because the API configures
 * no CORS policy — and adding one purely so a dev server can reach it would put
 * a permanent hole in the production surface to solve a local problem.
 */
const DEV_API_ORIGIN = 'http://localhost:5111';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // Both prefixes are proxied because the API serves health probes at the
      // root, outside /api. See HealthEndpointRegistration on the server.
      '/api': { target: DEV_API_ORIGIN, changeOrigin: true },
      '/health': { target: DEV_API_ORIGIN, changeOrigin: true },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
