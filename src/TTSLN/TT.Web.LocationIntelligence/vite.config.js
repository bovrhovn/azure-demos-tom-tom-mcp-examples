import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  test: {
    environment: "jsdom",
    setupFiles: ["Client/tests/setup.js"],
    include: ["Client/tests/**/*.spec.js"],
    clearMocks: true
  },
  build: {
    outDir: "wwwroot/dist",
    emptyOutDir: true,
    rollupOptions: {
      input: "Client/main.js",
      output: {
        entryFileNames: "app.js",
        chunkFileNames: "chunks/[name]-[hash].js",
        assetFileNames: (asset) => asset.names?.some((name) => name.endsWith(".css"))
          ? "app.css" : "assets/[name]-[hash][extname]",
        manualChunks(id) {
          const normalized = id.replaceAll("\\", "/");
          if (!normalized.includes("/node_modules/")) return undefined;
          if (normalized.includes("/@microsoft/signalr/")) return "vendor-signalr";
          if (normalized.includes("/marked/") || normalized.includes("/dompurify/")) return "vendor-markdown";
          if (normalized.includes("/vue/") || normalized.includes("/@vue/")) return "vendor-vue";
          return undefined;
        }
      }
    }
  },
  base: "/dist/"
});
