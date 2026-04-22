import { useState, useCallback, useEffect } from 'react';
import { compress, loadWasm } from '../wasm/WasmLoader';
import type { CompressResult, CompressParams } from '../wasm/types';

interface UseCompressorReturn {
  wasmReady: boolean;
  wasmLoading: boolean;
  wasmError: string | null;
  result: CompressResult | null;
  compressing: boolean;
  runCompress: (imageData: ImageData, params: CompressParams) => Promise<void>;
  resetResult: () => void;
}

export function useCompressor(): UseCompressorReturn {
  const [wasmReady, setWasmReady] = useState(false);
  const [wasmLoading, setWasmLoading] = useState(true);
  const [wasmError, setWasmError] = useState<string | null>(null);
  const [result, setResult] = useState<CompressResult | null>(null);
  const [compressing, setCompressing] = useState(false);

  // Load WASM on mount
  useEffect(() => {
    loadWasm()
      .then(() => { setWasmReady(true); setWasmLoading(false); })
      .catch((e: Error) => { setWasmError(e.message); setWasmLoading(false); });
  }, []);

  const runCompress = useCallback(async (imageData: ImageData, params: CompressParams) => {
    if (!wasmReady) return;

    setCompressing(true);
    setResult(null);
    try {
      const res = await compress(
        new Uint8Array(imageData.data.buffer),
        imageData.width,
        imageData.height,
        params.threshold,
        params.margin,
      );
      setResult(res);
    } catch (e: unknown) {
      setResult({ status: 1, message: e instanceof Error ? e.message : String(e) });
    } finally {
      setCompressing(false);
    }
  }, [wasmReady]);

  const resetResult = useCallback(() => {
    setResult(null);
  }, []);

  return { wasmReady, wasmLoading, wasmError, result, compressing, runCompress, resetResult };
}
