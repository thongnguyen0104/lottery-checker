import Webcam from 'react-webcam'
import { useRef, useCallback } from 'react'

export default function CameraCapture({ onCapture }: { onCapture: (blob: Blob) => void }) {
  const webcamRef = useRef<Webcam>(null)

  const capture = useCallback(async () => {
    const screenshot = webcamRef.current?.getScreenshot()
    if (!screenshot) return
    const res = await fetch(screenshot)
    const blob = await res.blob()
    onCapture(blob)
  }, [onCapture])

  return (
    <div className="relative">
      <Webcam
        ref={webcamRef}
        screenshotFormat="image/jpeg"
        videoConstraints={{ facingMode: 'environment' }}
        className="w-full rounded-lg"
      />
      {/* Khung hướng dẫn căn vé */}
      <div className="absolute inset-x-8 top-1/2 -translate-y-1/2 h-32
                      border-4 border-yellow-400 rounded pointer-events-none" />
      <button onClick={capture}
              className="mt-4 w-full bg-blue-600 text-white py-3 rounded-lg">
        📷 Chụp vé
      </button>
    </div>
  )
}
