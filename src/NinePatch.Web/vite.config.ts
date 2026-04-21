// Build config file
import { resolve, join } from 'node:path'
import { statSync, readdirSync } from 'node:fs'
import type { ViteDevServer, ResolvedConfig, Plugin } from 'vite'
import type { IncomingMessage, ServerResponse } from 'node:http'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import UnoCSS from 'unocss/vite'

// Plugin to add COOP/COEP headers required for SharedArrayBuffer in .NET WASM
const coopCoepPlugin: Plugin = {
  name: 'coop-coep-headers',
  configureServer(server: ViteDevServer) {
    server.middlewares.use((_req: IncomingMessage, res: ServerResponse, next: () => void) => {
      res.setHeader('Cross-Origin-Opener-Policy', 'same-origin')
      res.setHeader('Cross-Origin-Embedder-Policy', 'require-corp')
      next()
    })
  },
}

// Plugin: check that public/_framework/*.wasm is newer than src/NinePatch.Core/*.cs.
// If not, fail startup with instructions to rebuild WASM.
const checkWasmUpToDatePlugin: Plugin = {
  name: 'check-wasm-up-to-date',
  configResolved(config: ResolvedConfig) {
    const root = config.root
    const frameworkDir = resolve(root, 'public/_framework')

    // Find newest wasm file
    const wasmFiles = readdirSync(frameworkDir).filter(f => f.endsWith('.wasm'))
    let newestWasm = 0
    for (const f of wasmFiles) {
      const m = statSync(join(frameworkDir, f)).mtimeMs
      if (m > newestWasm) newestWasm = m
    }

    if (newestWasm === 0) return // no wasm yet, let dev server start

    // Find newest .cs file in NinePatch.Core
    const coreSrcDir = resolve(root, '../../src/NinePatch.Core')
    let newestCs = 0
    const csFiles = readdirSync(coreSrcDir).filter(f => f.endsWith('.cs'))
    for (const f of csFiles) {
      const m = statSync(join(coreSrcDir, f)).mtimeMs
      if (m > newestCs) newestCs = m
    }

    if (newestCs > newestWasm) {
      const wasmDate = new Date(newestWasm).toLocaleString()
      const csDate = new Date(newestCs).toLocaleString()
      throw new Error(
        `\n[NinePatch.Wasm out of sync]\n` +
        `  public/_framework/*.wasm modified: ${wasmDate}\n` +
        `  src/NinePatch.Core/*.cs modified:  ${csDate}\n` +
        `  Run: dotnet publish ../NinePatch.Wasm/NinePatch.Wasm.csproj -c Debug\n` +
        `  Then copy the new .wasm files from bin/Debug/.../AppBundle/_framework/ to public/_framework/\n`
      )
    }
  },
}

export default defineConfig({
  plugins: [checkWasmUpToDatePlugin, react(), UnoCSS(), coopCoepPlugin],
})
