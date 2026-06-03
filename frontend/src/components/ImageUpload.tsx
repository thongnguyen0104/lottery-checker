export default function ImageUpload({ onSelect }: { onSelect: (f: File) => void }) {
  return (
    <label className="block border-2 border-dashed p-8 rounded-lg text-center cursor-pointer">
      <input type="file" accept="image/*" capture="environment"
             onChange={e => e.target.files && onSelect(e.target.files[0])}
             className="hidden" />
      <span>📁 Chọn ảnh hoặc chụp từ máy</span>
    </label>
  )
}
