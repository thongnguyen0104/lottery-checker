export default function ImageUpload({ onSelect }: { onSelect: (f: File) => void }) {
  return (
    <label className="block border-2 border-dashed p-8 rounded-lg text-center cursor-pointer">
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
      <span>📁 Chọn ảnh hoặc chụp từ máy</span>
    </label>
  )
}
