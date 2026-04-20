interface ErrorDisplayProps {
  message: string;
}

export function ErrorDisplay({ message }: ErrorDisplayProps) {
  return (
    <div className="card border-l-4 border-red-500 bg-red-50">
      <p className="text-red-700 font-medium">错误</p>
      <p className="text-sm text-red-600 mt-1">{message}</p>
    </div>
  );
}
