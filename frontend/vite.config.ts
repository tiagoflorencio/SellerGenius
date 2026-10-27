import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    // O front chama /api/... e o Vite repassa para a API em C# (sem /api).
    // Assim o navegador fala só com o Vite (sem CORS) e a API continua aceitando só acesso local.
    proxy: {
      '/api': {
        target: process.env.API_URL ?? 'http://localhost:5172',
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
