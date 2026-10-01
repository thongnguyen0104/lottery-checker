import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { EditorContent, useEditor, type Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Image from '@tiptap/extension-image'
import Icon, { type IconName } from '../Icon'
import { uploadSiteImage } from '../../api/client'

/**
 * Soạn nội dung có định dạng (giới thiệu, bài viết). Chỉ bật đúng các định dạng máy chủ cho qua
 * (SiteHtmlSanitizer) — dán HTML lạ vào cũng bị TipTap lọc trước, máy chủ lọc lần nữa lúc lưu.
 */
export default function RichTextEditor({ value, onChange, minHeight = 'min-h-48', allowImages = true }: {
  value: string
  onChange: (html: string) => void
  minHeight?: string
  allowImages?: boolean
}) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        link: { openOnClick: false, autolink: true, protocols: ['https', 'http', 'mailto', 'tel'] },
      }),
      Image,
    ],
    content: value,
    // Thanh công cụ cần biết định dạng đang bật ở con trỏ → vẽ lại mỗi lần gõ (nội dung ngắn, không đáng kể).
    shouldRerenderOnTransaction: true,
    editorProps: { attributes: { class: `site-prose ${minHeight} px-3 py-2 focus:outline-none` } },
    onUpdate: ({ editor }) => onChange(editor.isEmpty ? '' : editor.getHTML()),
  })

  if (!editor) return null
  return (
    <div className="rounded-xl border border-line bg-surface overflow-hidden">
      <Toolbar editor={editor} allowImages={allowImages} />
      <EditorContent editor={editor} className="bg-white text-stone-800" />
    </div>
  )
}

function Toolbar({ editor, allowImages }: { editor: Editor; allowImages: boolean }) {
  const { t } = useTranslation('site')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const link = () => {
    const prev = editor.getAttributes('link').href as string | undefined
    const href = prompt(t('editor.rte.linkPrompt'), prev ?? 'https://')
    if (href === null) return
    if (href === '' || href === 'https://') editor.chain().focus().unsetLink().run()
    else editor.chain().focus().extendMarkRange('link').setLink({ href }).run()
  }

  const image = async (f: File | undefined) => {
    if (!f) return
    setBusy(true)
    setError(null)
    try {
      const { url } = await uploadSiteImage(f)
      // src lưu đường dẫn tương đối /api/sites/images/... — máy chủ chỉ cho ảnh dạng này.
      editor.chain().focus().setImage({ src: url }).run()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const btn = (icon: IconName, label: string, onClick: () => void, active = false, disabled = false) => (
    <button key={icon} type="button" title={label} aria-label={label} aria-pressed={active} onClick={onClick} disabled={disabled}
            className={`w-8 h-8 rounded-lg flex items-center justify-center transition disabled:opacity-40
                        ${active ? 'bg-brand-500/15 text-brand-700 dark:text-brand-400' : 'text-ink-soft hover:bg-muted'}`}>
      <Icon name={icon} className="w-4 h-4" />
    </button>
  )

  return (
    <div className="border-b border-line bg-muted/50">
      <div className="flex flex-wrap gap-0.5 p-1">
        {btn('bold', t('editor.rte.bold'), () => editor.chain().focus().toggleBold().run(), editor.isActive('bold'))}
        {btn('italic', t('editor.rte.italic'), () => editor.chain().focus().toggleItalic().run(), editor.isActive('italic'))}
        {btn('underline', t('editor.rte.underline'), () => editor.chain().focus().toggleUnderline().run(), editor.isActive('underline'))}
        {btn('heading', t('editor.rte.heading'), () => editor.chain().focus().toggleHeading({ level: 2 }).run(), editor.isActive('heading'))}
        {btn('bullets', t('editor.rte.bullets'), () => editor.chain().focus().toggleBulletList().run(), editor.isActive('bulletList'))}
        {btn('quote', t('editor.rte.quote'), () => editor.chain().focus().toggleBlockquote().run(), editor.isActive('blockquote'))}
        {btn('link', t('editor.rte.link'), link, editor.isActive('link'))}
        {/* Ảnh: label bọc input file — bấm là mở hộp chọn ảnh, khỏi cần ref. */}
        {allowImages && (
          <label title={t('editor.rte.image')} aria-label={t('editor.rte.image')}
                 className={`w-8 h-8 rounded-lg flex items-center justify-center text-ink-soft hover:bg-muted cursor-pointer ${busy ? 'opacity-40 pointer-events-none' : ''}`}>
            <Icon name="addImage" className="w-4 h-4" />
            <input type="file" accept="image/*" hidden onChange={e => { image(e.target.files?.[0]); e.target.value = '' }} />
          </label>
        )}
        <span className="flex-1" />
        {btn('undo', t('editor.rte.undo'), () => editor.chain().focus().undo().run(), false, !editor.can().undo())}
        {btn('redo', t('editor.rte.redo'), () => editor.chain().focus().redo().run(), false, !editor.can().redo())}
      </div>
      {error && <p className="px-2 pb-1 text-xs text-bad">{error}</p>}
    </div>
  )
}
