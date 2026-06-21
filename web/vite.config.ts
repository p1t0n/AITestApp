import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev server proxies API calls to the ASP.NET Core backend (http profile, port 5069),
// so the SPA can call /api/* without CORS friction during development.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5069",
        changeOrigin: true,
      },
      // Roster Q&A agent (Microsoft Agent Framework) — separate sibling service on :5200.
      "/agents": {
        target: "http://localhost:5200",
        changeOrigin: true,
      },
    },
  },
});
