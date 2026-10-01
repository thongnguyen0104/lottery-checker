import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Icon from '../Icon'
import { primaryBtn, secondaryBtn } from '../map/Sheet'
import ConfirmDialog from '../ConfirmDialog'
import SiteImagePicker from './SiteImagePicker'
import RichTextEditor from './RichTextEditor'
import { formatDate } from '../../utils/date'
import {
  deleteSitePost, getMySitePost, getMySitePosts, saveSitePost, siteImageUrl, type SitePost, type SitePostSummary,
} from '../../api/client'

/** Bài viết của site: danh sách + soạn (TipTap). Bài có trạng thái riêng (nháp / đã đăng), không đi theo nút Đăng site. */
export default function SitePostsTab({ published }: { published: boolean }) {
  const { t } = useTranslation('site')
  const [items, setItems] = useState<SitePostSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  // null = danh sách; 'new' = bài mới; còn lại = bài đang sửa.
  const [editing, setEditing] = useState<SitePost | 'new' | null>(null)
  const [deleting, setDeleting] = useState<SitePostSummary | null>(null)

  const load = () => getMySitePosts().then(setItems).catch(e => setError((e as Error).message))
  useEffect(() => { load() }, [])

  const open = async (id: string) => {
    try {
      setEditing(await getMySitePost(id))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const remove = async (p: SitePostSummary) => {
    try {
      await deleteSitePost(p.id)
      setItems(list => list?.filter(x => x.id !== p.id) ?? null)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (editing) return (
    <PostForm post={editing === 'new' ? null : editing} canCrossPost={published}
              onClose={() => { setEditing(null); load() }} />
  )

  return (
    <div className="space-y-3">
      <button onClick={() => setEditing('new')} className={primaryBtn}>
        <Icon name="plus" className="w-4 h-4 inline -mt-0.5" /> {t('editor.postAdd')}
      </button>
      {error && <p className="text-sm text-bad">{error}</p>}
      {!items ? <div className="card h-32 animate-pulse" /> : items.length === 0 ? (
        <p className="card p-6 text-center text-ink-soft">{t('editor.postsEmpty')}</p>
      ) : (
        <ul className="space-y-2">
          {items.map(p => (
            <li key={p.id} className="card p-3 flex gap-3 items-center">
              {p.coverUrl && <img src={siteImageUrl(p.coverUrl)} alt="" className="w-16 h-16 rounded-xl object-cover shrink-0" />}
              <div className="flex-1 min-w-0">
                <p className="font-semibold truncate">{p.title}</p>
                <p className="text-xs text-ink-soft">
                  <span className={p.status === 'Published' ? 'text-ok' : 'text-warn'}>
                    {t(p.status === 'Published' ? 'editor.postPublished' : 'editor.postDraft')}
                  </span>
                  {p.publishedAt && ` · ${formatDate(p.publishedAt)}`}
                  {p.crossPosted && ` · ${t('editor.postCrossPosted')}`}
                </p>
              </div>
              <button onClick={() => open(p.id)} className={secondaryBtn}><Icon name="edit" className="w-4 h-4" /></button>
              <button onClick={() => setDeleting(p)} className={secondaryBtn}><Icon name="trash" className="w-4 h-4" /></button>
            </li>
          ))}
        </ul>
      )}
      {deleting && (
        <ConfirmDialog title={deleting.title} message={t('editor.confirmDelete')} confirmLabel={t('editor.delete')}
                       onConfirm={() => { remove(deleting); setDeleting(null) }} onClose={() => setDeleting(null)} />
      )}
    </div>
  )
}

function PostForm({ post, canCrossPost, onClose }: { post: SitePost | null; canCrossPost: boolean; onClose: () => void }) {
  const { t } = useTranslation('site')
  const [saved, setSaved] = useState(post)
  const [title, setTitle] = useState(post?.title ?? '')
  const [html, setHtml] = useState(post?.contentHtml ?? '')
  const [cover, setCover] = useState(post?.coverUrl ?? null)
  const [crossPost, setCrossPost] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)

  const save = async (publish: boolean) => {
    setBusy(true)
    setMsg(null)
    try {
      const p = await saveSitePost(saved?.id ?? null, { title: title.trim(), contentHtml: html, coverUrl: cover, publish, crossPostToBlog: crossPost })
      setSaved(p)
      setMsg({ ok: true, text: t(p.status === 'Published' ? 'editor.postPublished' : 'editor.saved') })
    } catch (e) {
      setMsg({ ok: false, text: (e as Error).message })
    } finally {
      setBusy(false)
    }
  }

  const isPublished = saved?.status === 'Published'
  return (
    <div className="card p-5 space-y-4">
      <button onClick={onClose} className="text-sm font-semibold inline-flex items-center gap-1 text-ink-soft hover:text-ink">
        <Icon name="back" className="w-4 h-4" /> {t('public.back')}
      </button>
      <label className="block">
        <span className="block text-sm font-semibold mb-1">{t('editor.postTitle')}</span>
        <input className="field text-lg font-semibold" value={title} onChange={e => setTitle(e.target.value)} maxLength={120} required />
      </label>
      <SiteImagePicker label={t('editor.postCover')} url={cover} onChange={setCover} />
      <RichTextEditor value={post?.contentHtml ?? ''} onChange={setHtml} minHeight="min-h-72" />
      {canCrossPost && !saved?.crossPosted && (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={crossPost} onChange={e => setCrossPost(e.target.checked)} /> {t('editor.postCrossPost')}
        </label>
      )}
      {msg && <p className={`text-sm ${msg.ok ? 'text-ok' : 'text-bad'}`}>{msg.text}</p>}
      <div className="flex flex-wrap gap-2 justify-end">
        <button onClick={() => save(false)} disabled={busy} className={secondaryBtn}>
          {t(isPublished ? 'editor.postUnpublish' : 'editor.postSaveDraft')}
        </button>
        <button onClick={() => save(true)} disabled={busy || title.trim().length < 3} className={primaryBtn}>
          <Icon name="publish" className="w-4 h-4 inline -mt-0.5" /> {t('editor.postPublish')}
        </button>
      </div>
    </div>
  )
}
