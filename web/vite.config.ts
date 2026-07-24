import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Proxy: front (localhost:5173) e API (localhost:5169) na mesma origem do ponto de vista
// do navegador. O cookie de refresh e Secure, mas navegadores tratam localhost como
// contexto seguro mesmo em http, entao o cookie e aceito em dev.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/auth': { target: 'http://localhost:5169', changeOrigin: true },
      '/me': { target: 'http://localhost:5169', changeOrigin: true },
    },
  },
})
