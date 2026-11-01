import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  build: {
    target: ['es2020', 'safari14', 'chrome87'],
    // Монолитный бандл: один JS и один CSS. Старый iOS Safari (WebKit в клиенте VK) периодически не догружает
    // отдельные чанки — без разбиения на чанки приложению нечего «не догрузить» после старта.
    cssCodeSplit: false,
    rolldownOptions: {
      output: {
        codeSplitting: false,
      },
    },
  },
  server: {
    port: 5183,
    host: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5062',
        changeOrigin: true,
      },
    },
  },
})
