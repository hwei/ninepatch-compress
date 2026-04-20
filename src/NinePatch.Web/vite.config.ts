import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import UnoCSS from 'unocss/vite'

// Plugin to add COOP/COEP headers required for SharedArrayBuffer in .NET WASM
const coopCoepPlugin = () => ({
  name: 'coop-coep-headers',
  configureServer(server: any) {
    server.middlewares.use((_req: any, res: any, next: any) => {
      res.setHeader('Cross-Origin-Opener-Policy', 'same-origin')
      res.setHeader('Cross-Origin-Embedder-Policy', 'require-corp')
      next()
    })
  },
})

export default defineConfig({
  plugins: [react(), UnoCSS(), coopCoepPlugin()],
})
