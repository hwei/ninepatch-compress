export type CompressStatus = 'success' | 'invalid_input' | 'no_valid_split' | 'loading' | 'idle';

export interface NinePatchMeta {
  xb: number;
  xe: number;
  yb: number;
  ye: number;
  original_width: number;
  original_height: number;
  compressed_width: number;
  compressed_height: number;
  nx: number;
  ny: number;
  error_x: number;
  error_y: number;
  error_2d: number;
  savings_pct: number;
}

export interface CompressResult {
  status: number;
  message: string;
  metadata?: NinePatchMeta;
  compressed_rgba_b64?: string;
}

export interface CompressParams {
  threshold: number;
  margin: number;
  minLength: number;
}

// ── Analyze / Debug types ──

export interface DebugAxisResult {
  begin: number;
  end: number;
  n: number;
  is_identity_fallback: boolean;
}

export interface DebugLineCandidates {
  line: number;
  intervals: [number, number][];
}

export interface AnalyzeResult {
  status: number;
  message: string;
  final_x?: DebugAxisResult;
  final_y?: DebugAxisResult;
  x_candidates?: DebugLineCandidates[];
  y_candidates?: DebugLineCandidates[];
}
