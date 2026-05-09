import { useState, useCallback, useEffect, useMemo, useRef } from 'react'
import { ImageUpload } from './components/ImageUpload'
import { ComparePane } from './components/ComparePane'
import { OutputPane } from './components/OutputPane'
import { ImageViewport } from './components/ImageViewport'
import { PixelInspector, type PixelInfo } from './components/PixelInspector'
import { BackgroundPicker, getBgClass } from './components/BackgroundPicker'
import { ZoomPicker } from './components/ZoomPicker'
import { ViewSelector, type ViewId } from './components/ViewSelector'
import { ErrorDisplay } from './components/ErrorDisplay'
import { useCompressor } from './hooks/useCompressor'
import { useDebugAnalyze } from './hooks/useDebugAnalyze'
import { buildCoordMapping } from './utils/coordinateMapping'
import { createXCandidateOverlay, createYCandidateOverlay, startAnimationLoop } from './utils/debugOverlay'
import type { CompressParams, NinePatchMeta } from './wasm/types'

/** Upscale a compressed image back to original dims using nine-patch mapping. */
function buildReconstructed(
  compressedUrl: string,
  origW: number, origH: number,
  meta: NinePatchMeta,
): Promise<string> {
  return new Promise((resolve) => {
    const img = new Image()
    img.onload = () => {
      const cW = img.width
      const cH = img.height
      const canvas = document.createElement('canvas')
      canvas.width = origW
      canvas.height = origH
      const ctx = canvas.getContext('2d')!
      ctx.imageSmoothingEnabled = true
      ctx.imageSmoothingQuality = 'high'

      const { xb, xe, yb, ye } = meta
      const cornerL = xb, cornerR = origW - xe
      const cornerT = yb, cornerB = origH - ye
      const compCornerL = cornerL, compCornerR = cornerR
      const compCornerT = cornerT, compCornerB = cornerB

      const srcX = [0, compCornerL, cW - compCornerR]
      const srcW = [compCornerL, cW - compCornerL - compCornerR, compCornerR]
      const dstX = [0, xb, origW - cornerR]
      const dstW = [cornerL, xe - xb, cornerR]

      const srcY = [0, compCornerT, cH - compCornerB]
      const srcH = [compCornerT, cH - compCornerT - compCornerB, compCornerB]
      const dstY = [0, yb, origH - cornerB]
      const dstH = [cornerT, ye - yb, cornerB]

      for (let j = 0; j < 3; j++) {
        for (let i = 0; i < 3; i++) {
          ctx.drawImage(img, srcX[i], srcY[j], srcW[i], srcH[j], dstX[i], dstY[j], dstW[i], dstH[j])
        }
      }
      resolve(canvas.toDataURL('image/png'))
    }
    img.src = compressedUrl
  })
}

/** Read pixel from image URL into {r,g,b,a}. Uses a temp canvas. */
function readPixel(img: HTMLImageElement, x: number, y: number) {
  if (x < 0 || x >= img.naturalWidth || y < 0 || y >= img.naturalHeight) return null
  const c = document.createElement('canvas')
  c.width = img.naturalWidth
  c.height = img.naturalHeight
  const ctx = c.getContext('2d')!
  ctx.drawImage(img, 0, 0)
  const d = ctx.getImageData(x, y, 1, 1).data
  return { r: d[0], g: d[1], b: d[2], a: d[3] }
}

function App() {
  const { wasmReady, wasmError, result, compressing, runCompress, resetResult } = useCompressor()
  const { debugResult, debugLoading, debugError, triggerAnalyze, resetDebug } = useDebugAnalyze()
  const [params, setParams] = useState<CompressParams>({ threshold: 4, margin: 0, minLength: 8 })
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [imageData, setImageData] = useState<ImageData | null>(null)
  const [imgWidth, setImgWidth] = useState(0)
  const [imgHeight, setImgHeight] = useState(0)
  const [fileName, setFileName] = useState('')
  const [reconstructedUrl, setReconstructedUrl] = useState<string | null>(null)
  const [compressedUrl, setCompressedUrl] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  // Shared view settings
  const [bg, setBg] = useState('checker-dark')
  const [zoom, setZoom] = useState(2)
  const [view, setView] = useState<ViewId>('compare')

  // Animation phase for debug overlays
  const [animPhase, setAnimPhase] = useState(0)
  useEffect(() => startAnimationLoop(setAnimPhase), [])

  // Pixel inspector state
  const [inspectorSamples, setInspectorSamples] = useState<[PixelInfo, PixelInfo] | null>(null)
  const [inspectorVisible, setInspectorVisible] = useState(false)
  const origImgRef = useRef<HTMLImageElement | null>(null)
  const compImgRef = useRef<HTMLImageElement | null>(null)

  const bgCls = getBgClass(bg)
  const meta = result?.status === 0 ? result.metadata ?? null : null

  // Load original and compressed images as HTMLImageElements for pixel reads
  useEffect(() => {
    if (imageUrl) {
      const img = new Image()
      img.onload = () => { origImgRef.current = img }
      img.src = imageUrl
    }
  }, [imageUrl])

  useEffect(() => {
    if (compressedUrl) {
      const img = new Image()
      img.onload = () => { compImgRef.current = img }
      img.src = compressedUrl
    }
  }, [compressedUrl])

  // Coordinate mapping
  const coordMapping = useMemo(() => meta ? buildCoordMapping(meta) : null, [meta])

  // Compressed nine-patch rect in compressed coordinates
  const compNp = useMemo(() => {
    if (!meta) return null
    const cornerL = meta.xb
    const cornerT = meta.yb
    return {
      xb: cornerL,
      xe: cornerL + meta.nx,
      yb: cornerT,
      ye: cornerT + meta.ny,
    }
  }, [meta])

  // Trigger lazy debug analyze when entering debug views
  useEffect(() => {
    if ((view === 'debug-x' || view === 'debug-y') && imageData && !debugResult && !debugLoading) {
      triggerAnalyze(
        new Uint8Array(imageData.data.buffer),
        imageData.width, imageData.height,
        params,
      )
    }
  }, [view, imageData, debugResult, debugLoading, params, triggerAnalyze])

  // Debug overlay renderers
  const xOverlayRenderer = useMemo(() => {
    if (!debugResult?.x_candidates) return undefined
    return createXCandidateOverlay(debugResult.x_candidates, animPhase)
  }, [debugResult?.x_candidates, animPhase])

  const yOverlayRenderer = useMemo(() => {
    if (!debugResult?.y_candidates) return undefined
    return createYCandidateOverlay(debugResult.y_candidates, animPhase)
  }, [debugResult?.y_candidates, animPhase])

  const handleImageLoaded = useCallback((data: ImageData, url: string, w: number, h: number) => {
    setImageData(data)
    setImageUrl(url)
    setCompressedUrl(null)
    setReconstructedUrl(null)
    setDone(false)
    setImgWidth(w)
    setImgHeight(h)
    resetResult()
    resetDebug()
  }, [resetResult, resetDebug])

  const handleCompress = useCallback(async () => {
    if (!imageData) return
    setCompressedUrl(null)
    setReconstructedUrl(null)
    setDone(false)
    await runCompress(imageData, params)
    resetDebug()
    // Auto-switch to compare view after compression
    setView('compare')
  }, [imageData, params, runCompress, resetDebug])

  // When WASM result arrives, generate compressed + reconstructed previews
  useEffect(() => {
    if (!result?.compressed_rgba_b64 || !result.metadata) return
    try {
      const b64 = result.compressed_rgba_b64
      const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0))
      const w = result.metadata.compressed_width
      const h = result.metadata.compressed_height
      const canvas = document.createElement('canvas')
      canvas.width = w
      canvas.height = h
      const ctx = canvas.getContext('2d')!
      const outData = ctx.createImageData(w, h)
      outData.data.set(bytes)
      ctx.putImageData(outData, 0, 0)
      const cUrl = canvas.toDataURL('image/png')
      setCompressedUrl(cUrl)

      buildReconstructed(cUrl, result.metadata!.original_width, result.metadata!.original_height, result.metadata!)
        .then((rUrl) => {
          setReconstructedUrl(rUrl)
          setDone(true)
        })
    } catch {
      // ignore
    }
  }, [result])

  // Keyboard shortcuts
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return
      if (['1', '2', '4', '8'].includes(e.key)) {
        setZoom(Number(e.key))
      } else if (e.key.toLowerCase() === 'b') {
        const opts = ['checker-dark', 'checker-light', 'black', 'white'] as const
        setBg(opts[(opts.indexOf(bg as typeof opts[number]) + 1) % opts.length])
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [bg])

  const handleDownload = useCallback(() => {
    if (!result?.metadata || !result.compressed_rgba_b64) return
    const b64 = result.compressed_rgba_b64
    const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0))
    const w = result.metadata.compressed_width
    const h = result.metadata.compressed_height
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')!
    const outData = ctx.createImageData(w, h)
    outData.data.set(bytes)
    ctx.putImageData(outData, 0, 0)
    canvas.toBlob((blob) => {
      if (!blob) return
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'compressed_ninepatch.png'
      a.click()
      URL.revokeObjectURL(url)
    })
  }, [result])

  const handleReset = useCallback(() => {
    setReconstructedUrl(null)
    setCompressedUrl(null)
    setDone(false)
  }, [])

  // Pixel inspector logic: compute both original and compressed pixel info
  const handlePixelInspector = useCallback((
    imgX: number, imgY: number,
    sourceView: 'original' | 'compressed',
  ) => {
    if (!meta || !coordMapping) {
      setInspectorVisible(false)
      return
    }

    const origImg = origImgRef.current
    const compImg = compImgRef.current

    if (sourceView === 'original') {
      // Mouse is over an original-image viewport
      const origPx = origImg ? readPixel(origImg, imgX, imgY) : null
      const mapped = coordMapping.originalToCompressed(imgX, imgY)

      const s1: PixelInfo = {
        label: 'Original',
        x: imgX, y: imgY,
        r: origPx?.r ?? 0, g: origPx?.g ?? 0, b: origPx?.b ?? 0, a: origPx?.a ?? 0,
      }

      let s2: PixelInfo
      if (mapped) {
        const compPx = compImg ? readPixel(compImg, mapped.cx, mapped.cy) : null
        s2 = {
          label: 'Compressed',
          x: mapped.cx, y: mapped.cy,
          r: compPx?.r ?? 0, g: compPx?.g ?? 0, b: compPx?.b ?? 0, a: compPx?.a ?? 0,
        }
      } else {
        s2 = { label: 'Compressed', x: -1, y: -1, r: 0, g: 0, b: 0, a: 0 }
      }

      setInspectorSamples([s1, s2])
      setInspectorVisible(true)
    } else {
      // Mouse is over compressed-image viewport
      const compPx = compImg ? readPixel(compImg, imgX, imgY) : null
      const mapped = coordMapping.compressedToOriginal(imgX, imgY)

      const s1: PixelInfo = {
        label: 'Compressed',
        x: imgX, y: imgY,
        r: compPx?.r ?? 0, g: compPx?.g ?? 0, b: compPx?.b ?? 0, a: compPx?.a ?? 0,
      }

      let s2: PixelInfo
      if (mapped) {
        const origPx = origImg ? readPixel(origImg, mapped.ox, mapped.oy) : null
        s2 = {
          label: 'Original',
          x: mapped.ox, y: mapped.oy,
          r: origPx?.r ?? 0, g: origPx?.g ?? 0, b: origPx?.b ?? 0, a: origPx?.a ?? 0,
          xRange: mapped.oxRange,
          yRange: mapped.oyRange,
        }
      } else {
        s2 = { label: 'Original', x: -1, y: -1, r: 0, g: 0, b: 0, a: 0 }
      }

      setInspectorSamples([s2, s1])
      setInspectorVisible(true)
    }
  }, [meta, coordMapping])

  const hideInspector = useCallback(() => setInspectorVisible(false), [])

  const origNp = meta ? { xb: meta.xb, xe: meta.xe, yb: meta.yb, ye: meta.ye } : null

  return (
    <div className="flex flex-col h-screen" style={{ background: 'var(--bg)' }}>
      {/* ───── Top bar ───── */}
      <header className="flex items-center justify-between px-5 py-3 border-b"
              style={{ borderColor: 'var(--line)', background: 'var(--panel)' }}>
        <div className="flex items-center gap-3">
          <div className="w-7 h-7 rounded-md flex items-center justify-center"
               style={{ background: 'var(--accent)', color: '#0a0d12', fontWeight: 700 }}>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
              <rect x="1" y="1" width="14" height="14" rx="2" stroke="currentColor" strokeWidth="1.5"/>
              <line x1="6" y1="1" x2="6" y2="15" stroke="currentColor" strokeWidth="1.2"/>
              <line x1="10" y1="1" x2="10" y2="15" stroke="currentColor" strokeWidth="1.2"/>
              <line x1="1" y1="6" x2="15" y2="6" stroke="currentColor" strokeWidth="1.2"/>
              <line x1="1" y1="10" x2="15" y2="10" stroke="currentColor" strokeWidth="1.2"/>
            </svg>
          </div>
          <div className="flex flex-col leading-tight">
            <span className="font-semibold">Nine-Patch Auto-Compressor</span>
            <span className="hint">自动检测九宫格区域 · 压缩可拉伸区域</span>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <span className="hint flex items-center gap-1.5">
            <span className="inline-block w-1.5 h-1.5 rounded-full"
                  style={{ background: wasmReady ? 'var(--ok)' : wasmError ? 'var(--danger)' : 'var(--text-faint)',
                           animation: !wasmReady && !wasmError ? 'pulse 1.5s ease-in-out infinite' : 'none' }} />
            {wasmReady ? 'WASM 引擎就绪' : wasmError ? `WASM 错误: ${wasmError}` : '正在加载 WASM 引擎...'}
          </span>
        </div>
        <style>{`@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }`}</style>
      </header>

      <div className="flex flex-1 min-h-0">
        {/* ───── Left sidebar ───── */}
        <aside className="w-[300px] flex-shrink-0 border-r flex flex-col overflow-y-auto scrollbar-slim"
               style={{ borderColor: 'var(--line)', background: 'var(--panel)' }}>
          {/* Source */}
          <section className="p-4 border-b" style={{ borderColor: 'var(--line)' }}>
            <div className="label mb-2">源文件</div>
            {imageUrl ? (
              <>
                <div className="panel-flush rounded-lg p-3 flex items-center gap-3"
                     style={{ background: 'var(--panel-2)' }}>
                  <div className={`w-10 h-10 rounded flex-shrink-0 relative overflow-hidden ${bgCls}`}
                       style={{ border: '1px solid var(--line-hi)' }}>
                    <img src={imageUrl} alt="" className="absolute inset-0 pixelated"
                         style={{ width: '100%', height: '100%', objectFit: 'contain' }}/>
                  </div>
                  <div className="flex flex-col min-w-0 leading-tight">
                    <span className="text-[13px] font-medium truncate">{fileName}</span>
                    <span className="hint mono">{imgWidth}x{imgHeight} · RGBA</span>
                  </div>
                </div>
                <button className="btn-ghost w-full mt-2 text-center"
                        onClick={() => document.getElementById('file-input')?.click()}>
                  替换图片…
                </button>
              </>
            ) : (
              <ImageUpload
                onImageLoaded={handleImageLoaded}
                onImageChanged={() => {}}
                onFileName={setFileName}
              />
            )}
            <input
              id="file-input"
              type="file"
              accept="image/png,image/*"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (!file) return
                setFileName(file.name)
                const url = URL.createObjectURL(file)
                const img = new Image()
                img.onload = () => {
                  const canvas = document.createElement('canvas')
                  canvas.width = img.width
                  canvas.height = img.height
                  const ctx = canvas.getContext('2d')!
                  ctx.drawImage(img, 0, 0)
                  handleImageLoaded(ctx.getImageData(0, 0, img.width, img.height), url, img.width, img.height)
                }
                img.onerror = () => URL.revokeObjectURL(url)
                img.src = url
                e.target.value = ''
              }}
            />
          </section>

          {/* Params */}
          <section className="p-4 border-b flex flex-col gap-4"
                   style={{ borderColor: 'var(--line)' }}>
            <div className="label">压缩参数</div>

            <ParamField label="误差阈值" value={params.threshold}
                        onChange={(v) => setParams(p => ({ ...p, threshold: v }))}
                        min={0} max={32} step={0.5} suffix=" / 255" />

            <ParamField label="最小角尺寸" value={params.margin}
                        onChange={(v) => setParams(p => ({ ...p, margin: v }))}
                        min={0} max={32} step={1} suffix=" px" />

            <ParamField label="最小拉伸长度" value={params.minLength}
                        onChange={(v) => setParams(p => ({ ...p, minLength: v }))}
                        min={2} max={64} step={1} suffix=" px" />
          </section>

          {/* Action */}
          <section className="p-4 flex flex-col gap-3">
            <button
              className="btn w-full flex items-center justify-center gap-2"
              disabled={!imageData || !wasmReady || compressing}
              onClick={done ? handleReset : handleCompress}
            >
              {compressing ? (
                <>
                  <span className="inline-block w-3 h-3 rounded-full border-2 border-transparent"
                        style={{
                          borderTopColor: 'currentColor', borderRightColor: 'currentColor',
                          animation: 'spin 0.8s linear infinite',
                        }}/>
                  压缩中…
                </>
              ) : done ? '重新压缩' : '开始压缩'}
            </button>
            {done && (
              <button className="btn-ghost w-full" onClick={handleDownload}>
                下载 PNG + 元数据
              </button>
            )}
          </section>

          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>

          {/* Error display */}
          {result && result.status !== 0 && (
            <section className="p-4">
              <ErrorDisplay message={result.message} />
            </section>
          )}

          {/* Shortcuts */}
          <div className="mt-auto p-4 border-t" style={{ borderColor: 'var(--line)' }}>
            <div className="label mb-2">快捷键</div>
            <div className="flex flex-col gap-1.5 hint">
              <div className="flex items-center justify-between">
                <span>切换缩放</span><span><kbd>1</kbd> <kbd>2</kbd> <kbd>4</kbd> <kbd>8</kbd></span>
              </div>
              <div className="flex items-center justify-between">
                <span>切换背景</span><span><kbd>B</kbd></span>
              </div>
            </div>
          </div>
        </aside>

        {/* ───── Main workbench ───── */}
        <main className="flex-1 min-w-0 flex flex-col">
          {/* Toolbar */}
          <div className="flex items-center justify-between gap-4 px-5 py-2.5 border-b"
               style={{ borderColor: 'var(--line)', background: 'var(--panel)' }}>
            <div className="flex items-center gap-5">
              <ViewSelector value={view} onChange={setView} hasResult={done} />
              <BackgroundPicker value={bg} onChange={setBg} />
              <ZoomPicker value={zoom} onChange={setZoom} />
            </div>
            <div className="flex items-center gap-2">
              <div className="flex items-center gap-2 pr-3 border-r"
                   style={{ borderColor: 'var(--line)' }}>
                <span className="label" style={{ fontSize: 10 }}>九宫格</span>
                <div className="flex items-center gap-1.5">
                  <span className="inline-block w-3 h-[2px]" style={{ background: 'var(--np-x)' }}/>
                  <span className="mono text-[11px]" style={{ color: 'var(--text-dim)' }}>X</span>
                  <span className="inline-block w-3 h-[2px] ml-2" style={{ background: 'var(--np-y)' }}/>
                  <span className="mono text-[11px]" style={{ color: 'var(--text-dim)' }}>Y</span>
                </div>
              </div>
              {meta && (
                <span className="hint mono">
                  {meta.original_width}x{meta.original_height} → {meta.compressed_width}x{meta.compressed_height}
                  <span style={{ color: 'var(--ok)', marginLeft: 8 }}>
                    −{meta.savings_pct.toFixed(1)}%
                  </span>
                </span>
              )}
            </div>
          </div>

          {/* Canvas area */}
          <div className="flex-1 min-h-0 overflow-auto scrollbar-slim workbench p-8">
            <div className="flex flex-col gap-8 items-start">

              {/* Compare View */}
              {view === 'compare' && imageUrl && imageData && (
                <div className="relative">
                  <div className="flex items-center gap-3 mb-2">
                    <span className="label-lg">原图 · 重建图 对比</span>
                  </div>
                  <ComparePane
                    originalUrl={imageUrl}
                    reconstructedUrl={reconstructedUrl}
                    width={imgWidth}
                    height={imgHeight}
                    zoom={zoom}
                    bgClass={bgCls}
                    np={origNp}
                    compressing={compressing}
                    onMousePixel={(imgX, imgY) => handlePixelInspector(imgX, imgY, 'original')}
                    onMouseLeave={hideInspector}
                  />
                  {inspectorVisible && inspectorSamples && (
                    <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-30">
                      <PixelInspector samples={inspectorSamples} visible={true} />
                    </div>
                  )}
                </div>
              )}

              {/* Compressed View */}
              {view === 'compressed' && meta && (
                <div className="relative">
                  <div className="flex items-center gap-3 mb-2">
                    <span className="label-lg">压缩输出</span>
                    {done && (
                      <span className="hint mono">
                        {meta.compressed_width}x{meta.compressed_height}
                      </span>
                    )}
                  </div>
                  <OutputPane
                    url={compressedUrl}
                    width={meta.compressed_width}
                    height={meta.compressed_height}
                    zoom={zoom}
                    bgClass={bgCls}
                    np={compNp}
                    onMousePixel={(imgX, imgY) => handlePixelInspector(imgX, imgY, 'compressed')}
                    onMouseLeave={hideInspector}
                  />
                  {inspectorVisible && inspectorSamples && (
                    <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-30">
                      <PixelInspector samples={inspectorSamples} visible={true} />
                    </div>
                  )}
                </div>
              )}

              {/* Debug X Rows View */}
              {view === 'debug-x' && imageUrl && meta && (
                <div className="relative">
                  <div className="flex items-center gap-3 mb-2">
                    <span className="label-lg">Debug X 行候选</span>
                    {debugLoading && (
                      <span className="hint">分析中…</span>
                    )}
                    {debugError && (
                      <span className="hint" style={{ color: 'var(--danger)' }}>分析失败: {debugError}</span>
                    )}
                  </div>
                  {debugResult?.status === 0 && xOverlayRenderer && (
                    <ImageViewport
                      src={imageUrl}
                      width={imgWidth}
                      height={imgHeight}
                      zoom={zoom}
                      bgClass={bgCls}
                      np={origNp}
                      renderOverlay={xOverlayRenderer}
                      onMousePixel={(imgX, imgY) => handlePixelInspector(imgX, imgY, 'original')}
                      onMouseLeave={hideInspector}
                    />
                  )}
                  {inspectorVisible && inspectorSamples && (
                    <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-30">
                      <PixelInspector samples={inspectorSamples} visible={true} />
                    </div>
                  )}
                </div>
              )}

              {/* Debug Y Columns View */}
              {view === 'debug-y' && imageUrl && meta && (
                <div className="relative">
                  <div className="flex items-center gap-3 mb-2">
                    <span className="label-lg">Debug Y 列候选</span>
                    {debugLoading && (
                      <span className="hint">分析中…</span>
                    )}
                    {debugError && (
                      <span className="hint" style={{ color: 'var(--danger)' }}>分析失败: {debugError}</span>
                    )}
                  </div>
                  {debugResult?.status === 0 && yOverlayRenderer && (
                    <ImageViewport
                      src={imageUrl}
                      width={imgWidth}
                      height={imgHeight}
                      zoom={zoom}
                      bgClass={bgCls}
                      np={origNp}
                      renderOverlay={yOverlayRenderer}
                      onMousePixel={(imgX, imgY) => handlePixelInspector(imgX, imgY, 'original')}
                      onMouseLeave={hideInspector}
                    />
                  )}
                  {inspectorVisible && inspectorSamples && (
                    <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-30">
                      <PixelInspector samples={inspectorSamples} visible={true} />
                    </div>
                  )}
                </div>
              )}

              {/* Stats row */}
              {meta && done && view === 'compare' && (
                <div className="panel p-4 w-full" style={{ maxWidth: 720 }}>
                  <StatsRow meta={meta} />
                  <div className="mono text-[11px] mt-3 pt-3 border-t"
                       style={{ color: 'var(--text-faint)', borderColor: 'var(--line)' }}>
                    np = (xb={meta.xb}, xe={meta.xe}, yb={meta.yb}, ye={meta.ye}){' '}
                    · original = {meta.original_width}x{meta.original_height}
                  </div>
                </div>
              )}

              {/* Empty state */}
              {!imageUrl && (
                <div className="card text-center py-12" style={{ background: 'var(--panel)', borderColor: 'var(--line)', color: 'var(--text-faint)' }}>
                  <p>请上传一张 PNG 图片开始压缩</p>
                </div>
              )}
            </div>
          </div>
        </main>
      </div>
    </div>
  )
}

function ParamField({ label, value, onChange, min, max, step, suffix }: {
  label: string; value: number; onChange: (v: number) => void;
  min: number; max: number; step: number; suffix?: string;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between">
        <span className="label-lg">{label}</span>
        <span className="mono text-xs" style={{ color: 'var(--text-dim)' }}>
          {typeof value === 'number' ? (Number.isInteger(value) ? value : value.toFixed(1)) : value}{suffix ?? ''}
        </span>
      </div>
      <input type="range" min={min} max={max} step={step} value={value}
             onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  )
}

function StatsRow({ meta }: { meta: NinePatchMeta }) {
  const items: [string, string, boolean][] = [
    ['原始', `${meta.original_width}x${meta.original_height}`, false],
    ['压缩后', `${meta.compressed_width}x${meta.compressed_height}`, false],
    ['Nx / Ny', `${meta.nx} / ${meta.ny}`, false],
    ['误差 2D', meta.error_2d.toFixed(2), false],
    ['节省', `${meta.savings_pct.toFixed(1)}%`, true],
  ]
  return (
    <div className="flex flex-wrap gap-x-6 gap-y-2">
      {items.map(([k, v], i) => (
        <div key={i} className="flex flex-col">
          <span className="label" style={{ fontSize: 10 }}>{k}</span>
          <span className="mono" style={{ fontWeight: 600,
                 color: k === '节省' ? 'var(--ok)' : 'var(--text)' }}>
            {v}
          </span>
        </div>
      ))}
    </div>
  )
}

export default App
