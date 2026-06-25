import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],

  // La app se sirve desde /pantallas/ en el backend
  base: '/pantallas/',

  build: {
    outDir:       '../PANTALLAS',
    emptyOutDir:  false,           // no borrar las carpetas de dashboards existentes
    rollupOptions: {
      output: {
        assetFileNames: 'assets/[name]-[hash][extname]',
        chunkFileNames: 'assets/[name]-[hash].js',
        entryFileNames: 'assets/[name]-[hash].js',
      }
    }
  },

  // Proxy para desarrollo local (npm run dev)
  server: {
    proxy: {
      '/login':                  'http://localhost:5000',
      '/recuperacion-password':  'http://localhost:5000',
      '/config':                 'http://localhost:5000',
    }
  }
})
