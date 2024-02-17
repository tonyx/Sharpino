import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// const proxyPort = process.env.SERVER_PROXY_PORT || "5000";
const proxyPort = 5000;
const proxyTarget = "http://localhost:" + proxyPort;
// const proxyTarget = "http://127.0.0.1:" + proxyPort;

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react()],
    build: {
        outDir: "../../deploy/public",
    },
    server: {
        port: 8080,
        proxy: {
            // redirect requests that start with /api/ to the server on port 5000
            "/api/": {
                target: proxyTarget,
                changeOrigin: true,
            }
        }
    }
});
