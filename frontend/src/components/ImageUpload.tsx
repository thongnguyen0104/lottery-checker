import { useState } from 'react'
import Icon from './Icon'

export default function ImageUpload({ onSelect }: { onSelect: (f: File) => void }) {
  // Laptop: kéo thả ảnh vé từ thư mục vào ô này thay vì mở hộp thoại chọn tệp.
  const [dragging, setDragging] = useState(false)

  return (
    <label
      onDragOver={e => { e.preventDefault(); setDragging(true) }}
      // Rê qua icon/chữ bên trong cũng bắn dragleave → chỉ tắt khi thật sự ra khỏi ô.
      onDragLeave={e => { if (!e.currentTarget.contains(e.relatedTarget as Node)) setDragging(false) }}
      onDrop={e => {
        e.preventDefault()
        setDragging(false)
        const file = e.dataTransfer.files[0]
        if (file?.type.startsWith('image/')) onSelect(file)
      }}
      className={`group flex flex-col items-center gap-2 p-6 md:p-8 rounded-3xl border-2 border-dashed
                  text-center cursor-pointer transition
                  ${dragging ? 'border-brand-500 bg-brand-500/10' : 'border-line bg-surface/70 hover:border-brand-400'}`}>
      {/* KHÔNG đặt capture="environment": trên iPhone/Android nó ép mở thẳng camera, mất lựa
          chọn Thư viện ảnh. Bỏ đi thì hệ điều hành hiện menu Thư viện / Chụp ảnh / Chọn tệp. */}
      <input type="file" accept="image/*"
             onChange={e => {
               const file = e.target.files?.[0]
               // Reset để chọn lại ĐÚNG tấm vừa chọn vẫn bắn onChange (vd quét lại sau khi lỗi).
               e.target.value = ''
               if (file) onSelect(file)
             }}
             className="hidden" />
      <span className="w-12 h-12 rounded-2xl flex items-center justify-center bg-brand-500/10 text-brand-600
                       dark:text-brand-300 transition group-hover:scale-110">
        <Icon name="upload" className="w-6 h-6" />
      </span>
      <span className="font-semibold">Chọn ảnh vé từ máy</span>
      <span className="text-xs text-ink-faint">
        <span className="md:hidden">Thư viện ảnh hoặc chụp bằng camera máy</span>
        <span className="hidden md:inline">hoặc kéo thả ảnh vào đây</span>
      </span>
    </label>
  )
}
