import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

const target = 'http://127.0.0.1:5180'
const proxyPrefixes = ['/api', '/thumb', '/thumbpath', '/cover', '/book', '/mobi', '/txt', '/md', '/mdimg',
  '/image', '/media', '/siblings', '/imglist', '/zip', '/lyrics', '/assets', '/reader.html', '/library',
  '/_next', '/locales', '/vendor', '/manifest.json', '/favicon.ico', '/icon.png', '/apple-touch-icon.png', '/health']

const proxy: Record<string, any> = {}
for (const p of proxyPrefixes) proxy[p] = { target, changeOrigin: false }

export default defineConfig({
  base: '/shelf/',
  plugins: [vue()],
  server: { port: 5173, strictPort: true, proxy },
  build: {
    outDir: '../Assets/shelf',
    emptyOutDir: true,
    target: 'es2020',
    sourcemap: false,
    chunkSizeWarningLimit: 3000,
  },
})
