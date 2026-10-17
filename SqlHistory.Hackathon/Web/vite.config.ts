import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on localhost:5180; proxying keeps the browser on a single origin.
export default defineConfig({
  plugins: [react()],
  build: {
    // Monaco is bundled locally on purpose (no CDN at runtime), which makes the main chunk large.
    chunkSizeWarningLimit: 4000,
  },
  server: {
    host: 'localhost',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:5180',
    },
  },
})
