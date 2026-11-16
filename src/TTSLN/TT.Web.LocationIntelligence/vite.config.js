import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: "wwwroot/dist",
    emptyOutDir: true,
    rollupOptions: {
      input: "Client/main.js",
      output: {
        entryFileNames: "app.js",
        assetFileNames: (asset) => asset.names?.some((name) => name.endsWith(".css"))
          ? "app.css" : "assets/[name]-[hash][extname]"
      }
    }
  },
  base: "/dist/"
});
