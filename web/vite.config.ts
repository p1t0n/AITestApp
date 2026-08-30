/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev server proxies API calls to the ASP.NET Core backend (http profile, port 5069),
// so the SPA can call /api/* without CORS friction during development.
// The e2e harness runs its own stack on its own ports (web/e2e/run.mjs) and points the dev
// server at it through these, so a suite run never collides with a dev stack on the defaults.
const port = Number(process.env.VITE_PORT ?? 5173);
const apiTarget = process.env.VITE_API_TARGET ?? "http://localhost:5069";
const agentsTarget = process.env.VITE_AGENTS_TARGET ?? "http://localhost:5200";

export default defineConfig({
  plugins: [react()],
  server: {
    port,
    strictPort: true,
    proxy: {
      "/api": {
        target: apiTarget,
        changeOrigin: true,
      },
      // Roster Q&A agent (Microsoft Agent Framework) — separate sibling service on :5200.
      "/agents": {
        target: agentsTarget,
        changeOrigin: true,
      },
    },
  },
  // Component tests (vitest + testing-library) run in jsdom; setup registers jest-dom matchers.
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
  },
});
