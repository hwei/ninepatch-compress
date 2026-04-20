import { useState, useCallback, useRef } from 'react';

interface ImageUploadProps {
  onImageLoaded: (imageData: ImageData, imageUrl: string, width: number, height: number) => void;
  onImageChanged: () => void;
}

export function ImageUpload({ onImageLoaded, onImageChanged }: ImageUploadProps) {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const processFile = useCallback((file: File) => {
    if (!file.type.startsWith('image/')) return;
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      canvas.width = img.width;
      canvas.height = img.height;
      const ctx = canvas.getContext('2d')!;
      ctx.drawImage(img, 0, 0);
      const imageData = ctx.getImageData(0, 0, img.width, img.height);
      // Don't revoke URL here — it's used by PreviewPane.
      // App.tsx handles revocation when a new image replaces the old one.
      onImageLoaded(imageData, url, img.width, img.height);
      onImageChanged();
    };
    img.onerror = () => URL.revokeObjectURL(url);
    img.src = url;
  }, [onImageLoaded, onImageChanged]);

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
    // Reset input so same file can be re-selected
    e.target.value = '';
  }, [processFile]);

  return (
    <div
      className={`border-2 border-dashed rounded-xl p-8 text-center cursor-pointer transition-colors ${dragOver ? 'border-blue-500 bg-blue-50' : 'border-gray-300 hover:border-gray-400'}`}
      onDrop={handleDrop}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onClick={() => inputRef.current?.click()}
    >
      <div className="text-4xl mb-2">📁</div>
      <p className="text-gray-600">拖拽 PNG 图片到此处，或点击选择文件</p>
      <p className="text-xs text-gray-400 mt-1">支持 RGBA PNG，最大 1024x1024</p>
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
