import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  root: "./src",
  build: {
    outDir: "../dist",
  },
  server: {
    port: 8080,
    proxy: {
        "/api/": {
          target: "http://localhost:5000",
          changeOrigin: true,
        }
    },
    "open": true
  }
});
