import React, { useState, useRef } from "react";

export interface FileUploadProps {
  onFileSelect: (file: File) => void;
  accept?: string;
  maxSizeBytes?: number;
  label?: string;
  helperText?: string;
  uploadProgress?: number;
  isUploading?: boolean;
  error?: string;
  className?: string;
}

export const FileUpload: React.FC<FileUploadProps> = ({
  onFileSelect,
  accept = "image/jpeg,image/png,application/pdf",
  maxSizeBytes = 5 * 1024 * 1024, // 5MB default per SECURITY.md
  label = "Upload File",
  helperText = "PNG, JPG, or PDF up to 5MB",
  uploadProgress,
  isUploading = false,
  error,
  className = "",
}) => {
  const [isDragging, setIsDragging] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFile = (file: File) => {
    setFileError(null);
    if (file.size > maxSizeBytes) {
      setFileError(`File size exceeds maximum limit of ${Math.round(maxSizeBytes / (1024 * 1024))}MB`);
      return;
    }
    setSelectedFile(file);
    onFileSelect(file);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      handleFile(e.dataTransfer.files[0]);
    }
  };

  return (
    <div className={`w-full text-start ${className}`}>
      {label && (
        <label className="block text-sm font-medium text-slate-700 dark:text-slate-200 mb-1.5">
          {label}
        </label>
      )}
      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
        className={`flex flex-col items-center justify-center p-6 border-2 border-dashed rounded-xl cursor-pointer transition-colors ${
          isDragging
            ? "border-[#071832] bg-[#071832]/5 dark:border-[#EB4F42] dark:bg-[#EB4F42]/5"
            : error || fileError
            ? "border-[#EB4F42] bg-[#EB4F42]/5"
            : "border-slate-300 hover:border-slate-400 bg-white dark:bg-slate-900 dark:border-slate-800"
        }`}
      >
        <input
          ref={fileInputRef}
          type="file"
          accept={accept}
          className="hidden"
          onChange={(e) => {
            if (e.target.files && e.target.files[0]) {
              handleFile(e.target.files[0]);
            }
          }}
        />

        <div className="w-12 h-12 rounded-full bg-slate-100 dark:bg-slate-800 flex items-center justify-center text-slate-500 mb-3">
          <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
          </svg>
        </div>

        {selectedFile ? (
          <div className="text-center">
            <p className="text-sm font-medium text-slate-900 dark:text-white truncate max-w-xs">
              {selectedFile.name}
            </p>
            <p className="text-xs text-slate-500 mt-1">
              {(selectedFile.size / (1024 * 1024)).toFixed(2)} MB
            </p>
          </div>
        ) : (
          <div className="text-center">
            <p className="text-sm font-medium text-slate-700 dark:text-slate-300">
              Click to upload or drag and drop
            </p>
            <p className="text-xs text-slate-500 mt-1">{helperText}</p>
          </div>
        )}

        {isUploading && uploadProgress !== undefined && (
          <div className="w-full mt-4 bg-slate-200 rounded-full h-1.5 dark:bg-slate-700 overflow-hidden">
            <div
              className="bg-[#071832] dark:bg-[#EB4F42] h-1.5 rounded-full transition-all duration-300"
              style={{ width: `${uploadProgress}%` }}
            />
          </div>
        )}
      </div>

      {(error || fileError) && (
        <p className="mt-1.5 text-xs text-[#EB4F42] font-medium" role="alert">
          {error ?? fileError}
        </p>
      )}
    </div>
  );
};
