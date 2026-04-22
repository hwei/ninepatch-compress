import { useState, useCallback, useRef } from 'react';

interface ImageUploadProps {
  onImageLoaded: (imageData: ImageData, imageUrl: string, width: number, height: number) => void;
  onImageChanged: () => void;
  onFileName?: (name: string) => void;
}

export function ImageUpload({ onImageLoaded, onImageChanged, onFileName }: ImageUploadProps) {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const processFile = useCallback((file: File) => {
    if (!file.type.startsWith('image/')) return;
    onFileName?.(file.name);
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      canvas.width = img.width;
      canvas.height = img.height;
      const ctx = canvas.getContext('2d')!;
      ctx.drawImage(img, 0, 0);
      const imageData = ctx.getImageData(0, 0, img.width, img.height);
      onImageLoaded(imageData, url, img.width, img.height);
      onImageChanged();
    };
    img.onerror = () => URL.revokeObjectURL(url);
    img.src = url;
  }, [onImageLoaded, onImageChanged, onFileName]);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files[0];
    if (file) processFile(file);
  }, [processFile]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(true);
  }, []);

  const handleDragLeave = useCallback(() => setDragOver(false), []);

  const handleFileChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) processFile(file);
    e.target.value = '';
  }, [processFile]);

  return (
    <div
      className="border-2 border-dashed rounded-xl p-6 text-center cursor-pointer transition-colors"
      style={{
        borderColor: dragOver ? 'var(--accent)' : 'var(--line)',
        background: dragOver ? 'var(--accent-dim)' : 'var(--panel-2)',
      }}
      onDrop={handleDrop}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onClick={() => inputRef.current?.click()}
    >
      <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-dim)', marginBottom: 4 }}>
        拖拽 PNG 到此处，或点击选择
      </div>
      <div style={{ fontSize: 10, color: 'var(--text-faint)' }}>RGBA PNG · 最大 1024x1024</div>
      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/*"
        className="hidden"
        onChange={handleFileChange}
      />
    </div>
  );
}
