import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  root: "./src",
  build: {
    outDir: "../dist",
  },
  server: {
    "port": 8080,
    "proxy": {
        "/api/": 'http://localhost:5000'
        // "/api/": 'https://sharpinowebservice2.azurewebsites.net'
    },
    "open": true
  }
})
