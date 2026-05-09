import { useState, useCallback, useRef } from 'react';
import { analyze } from '../wasm/WasmLoader';
import type { AnalyzeResult, CompressParams } from '../wasm/types';

interface UseDebugAnalyzeReturn {
  /** The cached analyze result, or null if not yet computed. */
  debugResult: AnalyzeResult | null;
  /** True while an analyze call is in flight. */
  debugLoading: boolean;
  /** Error string if analyze failed, null otherwise. */
  debugError: string | null;
  /** Trigger (or re-trigger) analyze for the given image + params. */
  triggerAnalyze: (rgba: Uint8Array, width: number, height: number, params: CompressParams) => Promise<void>;
  /** Clear the cache and reset state. */
  resetDebug: () => void;
}

/** Build a cache key from image identity and parameters. */
function cacheKey(rgba: Uint8Array, w: number, h: number, params: CompressParams): string {
  // Use first 64 bytes of RGBA + dimensions + params as a proxy for identity.
  // Full hash would be more robust but adds overhead; in practice image data
  // changes are infrequent enough that this is sufficient.
  const head = rgba.slice(0, Math.min(64, rgba.length));
  let hash = 0;
  for (let i = 0; i < head.length; i++) hash = ((hash << 5) - hash + head[i]) | 0;
  return `${w}x${h}-${hash}-${params.threshold}-${params.margin}-${params.minLength}`;
}

export function useDebugAnalyze(): UseDebugAnalyzeReturn {
  const [debugResult, setDebugResult] = useState<AnalyzeResult | null>(null);
  const [debugLoading, setDebugLoading] = useState(false);
  const [debugError, setDebugError] = useState<string | null>(null);
  const lastKeyRef = useRef<string | null>(null);

  const triggerAnalyze = useCallback(async (
    rgba: Uint8Array,
    width: number,
    height: number,
    params: CompressParams,
  ) => {
    const key = cacheKey(rgba, width, height, params);
    // Reuse cached result if the key hasn't changed
    if (lastKeyRef.current === key && debugResult !== null) return;
    lastKeyRef.current = key;

    setDebugLoading(true);
    setDebugError(null);
    try {
      const res = await analyze(rgba, width, height, params.threshold, params.margin, params.minLength);
      setDebugResult(res);
    } catch (e: unknown) {
      setDebugError(e instanceof Error ? e.message : String(e));
      setDebugResult(null);
    } finally {
      setDebugLoading(false);
    }
  }, [debugResult]);

  const resetDebug = useCallback(() => {
    setDebugResult(null);
    setDebugLoading(false);
    setDebugError(null);
    lastKeyRef.current = null;
  }, []);

  return { debugResult, debugLoading, debugError, triggerAnalyze, resetDebug };
}
