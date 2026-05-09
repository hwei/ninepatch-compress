import type { CompressResult, AnalyzeResult } from './types';

interface DotnetInstance {
  withDiagnosticTracing(enabled: boolean): DotnetInstance;
  create(): Promise<{
    getAssemblyExports(assemblyName: string): Promise<unknown>;
    dispose(): void;
  }>;
}

interface WasmInstance {
  NinePatch: {
    Wasm: {
      WasmExports: {
        Compress: (rgba: Uint8Array, w: number, h: number, threshold: number, margin: number, minLength: number) => string;
        Analyze: (rgba: Uint8Array, w: number, h: number, threshold: number, margin: number, minLength: number) => string;
        GetVersion: () => string;
      };
    };
  };
}

let wasmExports: WasmInstance | null = null;
let loadingPromise: Promise<void> | null = null;

export async function loadWasm(): Promise<void> {
  if (wasmExports) return;
  if (loadingPromise) return loadingPromise;

  loadingPromise = (async () => {
    // Use dynamic import through a function to avoid Vite's static analysis
    // of the public/_framework/ directory.
    const importDotnet = new Function('url', 'return import(url)');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const { dotnet } = await importDotnet('/_framework/dotnet.js') as any;

    const instance = await (dotnet as DotnetInstance).withDiagnosticTracing(false).create();
    wasmExports = await instance.getAssemblyExports('NinePatch.Wasm.dll') as WasmInstance;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__wasmInstance = instance;
  })();

  await loadingPromise;
}

export async function compress(
  rgba: Uint8Array,
  width: number,
  height: number,
  threshold: number,
  margin: number,
  minLength: number,
): Promise<CompressResult> {
  if (!wasmExports) {
    await loadWasm();
  }
  const json = wasmExports!.NinePatch.Wasm.WasmExports.Compress(rgba, width, height, threshold, margin, minLength);
  return JSON.parse(json);
}

export async function analyze(
  rgba: Uint8Array,
  width: number,
  height: number,
  threshold: number,
  margin: number,
  minLength: number,
): Promise<AnalyzeResult> {
  if (!wasmExports) {
    await loadWasm();
  }
  const json = wasmExports!.NinePatch.Wasm.WasmExports.Analyze(rgba, width, height, threshold, margin, minLength);
  return JSON.parse(json);
}

export async function getVersion(): Promise<string> {
  if (!wasmExports) {
    await loadWasm();
  }
  return wasmExports!.NinePatch.Wasm.WasmExports.GetVersion();
}
