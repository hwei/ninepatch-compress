import type { NinePatchMeta } from '../wasm/types';

export interface CoordMapping {
  /** Map an original-image (x, y) to compressed-image (cx, cy), or null if out of bounds. */
  originalToCompressed: (ox: number, oy: number) => { cx: number; cy: number } | null;
  /** Map a compressed-image (cx, cy) to original image coordinate info.
   *  In fixed regions returns a single (ox, oy). In stretch regions returns
   *  the original range plus a representative sampled coordinate. */
  compressedToOriginal: (cx: number, cy: number) => CompressedToOriginalResult | null;
}

export interface CompressedToOriginalResult {
  /** Single original coordinate (for fixed regions) or representative sample (for stretch). */
  ox: number;
  oy: number;
  /** For stretch regions: the original coordinate range mapped to this compressed pixel. */
  oxRange?: [number, number];
  oyRange?: [number, number];
}

export function buildCoordMapping(meta: NinePatchMeta): CoordMapping {
  const { xb, xe, yb, ye, nx, ny, original_width: ow, original_height: oh } = meta;
  const cw = meta.compressed_width;
  const ch = meta.compressed_height;

  // Compressed corner sizes
  const cLeft = xb;
  const cTop = yb;
  const cMidW = nx;
  const cMidH = ny;

  // Original stretch sizes
  const origStretchW = xe - xb;
  const origStretchH = ye - yb;

  function originalToCompressed(ox: number, oy: number): { cx: number; cy: number } | null {
    if (ox < 0 || ox >= ow || oy < 0 || oy >= oh) return null;

    let cx: number;
    if (ox < xb) {
      cx = ox;
    } else if (ox < xe) {
      // Stretch region: map proportionally
      const t = (ox - xb) / origStretchW;
      cx = cLeft + Math.round(t * (cMidW - 1));
    } else {
      cx = cLeft + cMidW + (ox - xe);
    }

    let cy: number;
    if (oy < yb) {
      cy = oy;
    } else if (oy < ye) {
      const t = (oy - yb) / origStretchH;
      cy = cTop + Math.round(t * (cMidH - 1));
    } else {
      cy = cTop + cMidH + (oy - ye);
    }

    if (cx < 0 || cx >= cw || cy < 0 || cy >= ch) return null;
    return { cx, cy };
  }

  function compressedToOriginal(cx: number, cy: number): CompressedToOriginalResult | null {
    if (cx < 0 || cx >= cw || cy < 0 || cy >= ch) return null;

    let ox: number;
    let oxRange: [number, number] | undefined;
    if (cx < cLeft) {
      ox = cx;
    } else if (cx < cLeft + cMidW) {
      // Stretch region
      const t = cMidW > 1 ? (cx - cLeft) / (cMidW - 1) : 0;
      ox = Math.round(xb + t * (origStretchW - 1));
      // The range of original pixels that map to this compressed column
      const t0 = cMidW > 1 ? (cx - cLeft - 0.5) / cMidW : 0;
      const t1 = cMidW > 1 ? (cx - cLeft + 0.5) / cMidW : 1;
      const o0 = Math.round(xb + t0 * origStretchW);
      const o1 = Math.round(xb + t1 * origStretchW);
      oxRange = [Math.max(xb, o0), Math.min(xe, o1)];
    } else {
      ox = xe + (cx - cLeft - cMidW);
    }

    let oy: number;
    let oyRange: [number, number] | undefined;
    if (cy < cTop) {
      oy = cy;
    } else if (cy < cTop + cMidH) {
      const t = cMidH > 1 ? (cy - cTop) / (cMidH - 1) : 0;
      oy = Math.round(yb + t * (origStretchH - 1));
      const t0 = cMidH > 1 ? (cy - cTop - 0.5) / cMidH : 0;
      const t1 = cMidH > 1 ? (cy - cTop + 0.5) / cMidH : 1;
      const o0 = Math.round(yb + t0 * origStretchH);
      const o1 = Math.round(yb + t1 * origStretchH);
      oyRange = [Math.max(yb, o0), Math.min(ye, o1)];
    } else {
      oy = ye + (cy - cTop - cMidH);
    }

    return { ox, oy, oxRange, oyRange };
  }

  return { originalToCompressed, compressedToOriginal };
}
