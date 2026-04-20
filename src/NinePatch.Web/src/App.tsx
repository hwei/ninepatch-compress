import { useState, useCallback, useEffect } from 'react'
import { ImageUpload } from './components/ImageUpload'
import { PreviewPane } from './components/PreviewPane'
import { NinePatchOverlay } from './components/NinePatchOverlay'
import { ParameterControls } from './components/ParameterControls'
import { ErrorDisplay } from './components/ErrorDisplay'
import { MetadataDisplay } from './components/MetadataDisplay'
import { useCompressor } from './hooks/useCompressor'
import type { CompressParams } from './wasm/types'

function App() {
  const { wasmReady, wasmError, result, compressing, runCompress } = useCompressor()
  const [params, setParams] = useState<CompressParams>({ threshold: 4, margin: 0, minSavings: 30 })
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [compressedUrl, setCompressedUrl] = useState<string | null>(null)
  const [imageData, setImageData] = useState<ImageData | null>(null)
  const [imgWidth, setImgWidth] = useState(0)
  const [imgHeight, setImgHeight] = useState(0)

  const handleImageLoaded = useCallback((data: ImageData, url: string, w: number, h: number) => {
    setImageData(data)
    setImageUrl(url)
    setCompressedUrl(null)
    setImgWidth(w)
    setImgHeight(h)
  }, [])

  const handleImageChanged = useCallback(() => {
    if (imageUrl) URL.revokeObjectURL(imageUrl);
  }, [imageUrl])

  const handleCompress = useCallback(async () => {
    if (!imageData) return
    setCompressedUrl(null)
    await runCompress(imageData, params)
  }, [imageData, params, runCompress])

  const handleDownload = useCallback(() => {
    if (!result?.metadata) return
    const b64 = result.metadata.compressed_rgba_b64
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

  // Generate compressed image preview when result arrives
  useEffect(() => {
    if (!result?.metadata) return
    try {
      const b64 = result.metadata.compressed_rgba_b64
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
      setCompressedUrl(canvas.toDataURL('image/png'))
    } catch {
      // ignore
    }
  }, [result])

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white border-b border-gray-200 px-6 py-4">
        <h1 className="text-xl font-bold text-gray-800">Nine-Patch Auto-Compressor</h1>
        <p className="text-sm text-gray-500">自动检测九宫格区域并压缩可拉伸区域</p>
      </header>

      <main className="max-w-6xl mx-auto p-6">
        {/* WASM status */}
        <div className="mb-4 flex items-center gap-2 text-sm">
          <span className={`inline-block w-2 h-2 rounded-full ${wasmReady ? 'bg-green-500' : wasmError ? 'bg-red-500' : 'bg-yellow-500 animate-pulse'}`} />
          <span className="text-gray-600">
            {wasmReady ? 'WASM 引擎就绪' : wasmError ? `WASM 加载失败: ${wasmError}` : '正在加载 WASM 引擎...'}
          </span>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Left sidebar */}
          <div className="space-y-4">
            <ImageUpload onImageLoaded={handleImageLoaded} onImageChanged={handleImageChanged} />
            <ParameterControls params={params} onChange={setParams} />

            <button
              className="btn w-full text-lg"
              disabled={!imageData || !wasmReady || compressing}
              onClick={handleCompress}
            >
              {compressing ? '压缩中...' : '开始压缩'}
            </button>

            {result && result.status !== 0 && (
              <ErrorDisplay message={result.message} />
            )}

            {result?.status === 0 && result.metadata && (
              <MetadataDisplay meta={result.metadata} onDownload={handleDownload} />
            )}
          </div>

          {/* Right preview area */}
          <div className="lg:col-span-2 space-y-4">
            {imageUrl && imageData && (
              <PreviewPane
                imageUrl={imageUrl}
                width={imgWidth}
                height={imgHeight}
                label="原始图片"
                overlay={result?.status === 0 && result.metadata ? (
                  <NinePatchOverlay meta={result.metadata} width={imgWidth} height={imgHeight} />
                ) : undefined}
              />
            )}

            {compressedUrl && result?.metadata && (
              <PreviewPane
                imageUrl={compressedUrl}
                width={result.metadata.compressed_width}
                height={result.metadata.compressed_height}
                label="压缩结果"
              />
            )}

            {!imageUrl && (
              <div className="card text-center text-gray-400 py-12">
                <p>请上传一张 PNG 图片开始压缩</p>
              </div>
            )}
          </div>
        </div>
      </main>
    </div>
  )
}

export default App
