import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwind from "@tailwindcss/vite";
export default defineConfig({
  plugins: [react(), tailwind()],
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5080",
      "/hubs": { target: "http://127.0.0.1:5080", ws: true },
    },
  },
  build: { outDir: "../src/AutoPost.Api/wwwroot", emptyOutDir: true },
});
