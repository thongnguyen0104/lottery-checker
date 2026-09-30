import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  createBlogPost, deleteBlogPost, getBlogPosts, voteBlogPost,
  type Account, type BlogAuthorMode, type BlogPost,
} from '../api/client'
import { currentLocale } from '../i18n'
import ConfirmDialog from './ConfirmDialog'
import Icon, { IconBadge } from './Icon'

// Khớp BlogService ở backend — kiểm tra trước ở đây để báo lỗi ngay khi gõ.
const TITLE = { min: 3, max: 120 }
const CONTENT = { min: 10, max: 5000 }
const NAME = { min: 2, max: 30 }
/** Nội dung dài hơn chừng này thì thu gọn, bấm "Xem thêm" mới hiện đủ. */
const CLAMP_CHARS = 280

type Sort = 'new' | 'top'

type Props = {
  account: Account | null
  onRequireLogin: () => void
}

/** Blog cho mọi người: đọc, viết (ký tên tài khoản / ẩn danh / tự đặt), like/dislike, xoá bài của mình. */
export default function Blog({ account, onRequireLogin }: Props) {
  const { t } = useTranslation('blog')
  const [sort, setSort] = useState<Sort>('new')
  const [posts, setPosts] = useState<BlogPost[]>([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [writing, setWriting] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getBlogPosts(sort, page)
      .then(d => {
        if (!alive) return
        setPosts(prev => page === 1 ? d.items : [...prev, ...d.items.filter(p => !prev.some(x => x.id === p.id))])
        setHasMore(d.hasMore)
      })
      .catch(e => alive && setError(e?.message ?? ''))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [sort, page, reload])

  // Đổi cách sắp xếp / tải lại / tải thêm: bật trạng thái tải ngay lúc bấm, effect lo gọi máy chủ.
  const load = (s: Sort, p: number) => {
    setLoading(true)
    setError(null)
    setSort(s)
    setPage(p)
    setReload(n => n + 1)
  }

  const update = (id: number, patch: Partial<BlogPost> | null) =>
    setPosts(ps => patch ? ps.map(p => p.id === id ? { ...p, ...patch } : p) : ps.filter(p => p.id !== id))

  return (
    <div className="space-y-4 max-w-2xl mx-auto">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-xl md:text-3xl font-extrabold tracking-tight">{t('title')}</h1>
          <p className="text-sm md:text-base text-ink-soft mt-0.5">{t('hint')}</p>
        </div>
        {!writing && (
          <button onClick={() => setWriting(true)} className="btn btn-primary shrink-0">
            <Icon name="edit" className="w-4 h-4" /> {t('write')}
          </button>
        )}
      </div>

      {writing && (
        <Composer account={account} onRequireLogin={onRequireLogin} onCancel={() => setWriting(false)}
                  onPosted={p => {
                    setWriting(false)
                    // Bài mới luôn lên đầu danh sách "Mới nhất" — đang xem "Nổi bật" thì chuyển về.
                    if (sort === 'new' && page === 1) setPosts(ps => [p, ...ps])
                    else load('new', 1)
                  }} />
      )}

      <div className="grid grid-cols-2 gap-1 p-1 rounded-xl bg-muted border border-line/60 max-w-xs" role="tablist">
        {(['new', 'top'] as const).map(s => (
          <button key={s} role="tab" aria-selected={s === sort} onClick={() => s !== sort && load(s, 1)}
                  className={`py-2 rounded-lg text-sm font-semibold transition active:scale-95 ${s === sort
                    ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                    : 'text-ink-faint hover:text-ink-soft'}`}>
            {t(`sort.${s}`)}
          </button>
        ))}
      </div>

      {error && (
        <div className="card p-6 text-center space-y-3">
          <IconBadge name="error" tone="bad" />
          <div className="text-sm text-bad">{error}</div>
          <button onClick={() => load(sort, page)} className="btn btn-soft">
            <Icon name="retry" className="w-4 h-4" /> {t('retry')}
          </button>
        </div>
      )}

      {!error && !loading && posts.length === 0 && (
        <div className="card p-6 text-center text-sm text-ink-soft">
          <IconBadge name="blog" tone="info" />
          <p className="mt-3">{t('empty')}</p>
        </div>
      )}

      <div className="space-y-3">
        {posts.map(p => <PostCard key={p.id} post={p} onChange={patch => update(p.id, patch)} />)}
        {loading && page === 1 && Array.from({ length: 3 }, (_, i) => (
          <div key={i} className="card p-4 motion-safe:animate-pulse space-y-3">
            <div className="h-5 w-2/3 rounded-full bg-muted" />
            <div className="h-3 w-32 rounded-full bg-muted" />
            <div className="h-3 w-full rounded-full bg-muted" />
            <div className="h-3 w-5/6 rounded-full bg-muted" />
          </div>
        ))}
      </div>

      {!error && hasMore && (
        <button onClick={() => load(sort, page + 1)} disabled={loading} className="btn btn-soft w-full">
          {loading ? t('loading') : t('loadMore')}
        </button>
      )}
    </div>
  )
}

function Composer({ account, onRequireLogin, onCancel, onPosted }: {
  account: Account | null
  onRequireLogin: () => void
  onCancel: () => void
  onPosted: (p: BlogPost) => void
}) {
  const { t } = useTranslation('blog')
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [mode, setMode] = useState<BlogAuthorMode>(account ? 'Account' : 'Anonymous')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Đăng xuất giữa chừng → không còn ký tên tài khoản được nữa.
  const effectiveMode = mode === 'Account' && !account ? 'Anonymous' : mode

  const valid = title.trim().length >= TITLE.min && content.trim().length >= CONTENT.min
    && (effectiveMode !== 'Custom' || name.trim().length >= NAME.min)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      onPosted(await createBlogPost({
        title: title.trim(), content: content.trim(), authorMode: effectiveMode,
        authorName: effectiveMode === 'Custom' ? name.trim() : undefined,
      }))
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="card p-4 space-y-3 fade-up">
      <label className="block">
        <span className="text-sm font-semibold">{t('titleLabel')}</span>
        <input value={title} onChange={e => setTitle(e.target.value)} maxLength={TITLE.max} autoFocus
               placeholder={t('titlePlaceholder')} className="field mt-1" />
      </label>
      <label className="block">
        <span className="flex justify-between text-sm font-semibold">
          {t('contentLabel')}
          <span className="font-normal text-xs text-ink-faint tabular-nums">
            {t('counter', { count: content.length, max: CONTENT.max })}
          </span>
        </span>
        <textarea value={content} onChange={e => setContent(e.target.value)} maxLength={CONTENT.max} rows={5}
                  placeholder={t('contentPlaceholder')} className="field mt-1 resize-y min-h-28" />
      </label>

      <fieldset>
        <legend className="text-sm font-semibold mb-1.5">{t('signAs')}</legend>
        <div role="radiogroup" className="grid grid-cols-3 gap-1 p-1 rounded-xl bg-muted border border-line/60">
          {(['Account', 'Anonymous', 'Custom'] as const).map(m => {
            const active = effectiveMode === m
            const locked = m === 'Account' && !account
            return (
              <button key={m} type="button" role="radio" aria-checked={active}
                      onClick={() => locked ? onRequireLogin() : setMode(m)}
                      title={locked ? t('accountHint') : undefined}
                      className={`flex items-center justify-center gap-1 py-2 rounded-lg text-sm font-semibold transition
                                  ${active ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                                           : locked ? 'text-ink-faint/70' : 'text-ink-faint hover:text-ink-soft'}`}>
                {m === 'Account' && <Icon name="user" className="w-4 h-4 shrink-0" />}
                <span className="truncate">{m === 'Account' && account ? account.username : t(`modes.${m}`)}</span>
              </button>
            )
          })}
        </div>
        {!account && (
          <p className="mt-1.5 text-xs text-ink-faint">
            {t('accountHint')} —{' '}
            <button type="button" onClick={onRequireLogin} className="font-semibold text-brand-700 dark:text-brand-400 underline">
              {t('loginToUse')}
            </button>
          </p>
        )}
        {effectiveMode === 'Custom' && (
          <input value={name} onChange={e => setName(e.target.value)} maxLength={NAME.max}
                 placeholder={t('namePlaceholder', { min: NAME.min, max: NAME.max })} className="field mt-2" />
        )}
      </fieldset>

      {error && <div className="alert border-bad/30 bg-bad/10 text-bad">{error}</div>}

      <div className="flex gap-2 justify-end">
        <button type="button" onClick={onCancel} className="btn btn-soft">{t('cancel')}</button>
        <button type="submit" disabled={!valid || busy} className="btn btn-primary">
          <Icon name="send" className="w-4 h-4" /> {busy ? t('posting') : t('post')}
        </button>
      </div>
    </form>
  )
}

function PostCard({ post, onChange }: { post: BlogPost; onChange: (patch: Partial<BlogPost> | null) => void }) {
  const { t } = useTranslation('blog')
  const [expanded, setExpanded] = useState(false)
  const [voting, setVoting] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const long = post.content.length > CLAMP_CHARS

  // Bấm lại nút đang chọn = bỏ lượt; bấm nút kia = đổi phe.
  const vote = async (value: 1 | -1) => {
    if (voting) return
    setVoting(true)
    setError(null)
    try {
      onChange(await voteBlogPost(post.id, post.myVote === value ? 0 : value))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setVoting(false)
    }
  }

  const remove = async () => {
    setConfirmDelete(false)
    try {
      await deleteBlogPost(post.id)
      onChange(null)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <article className="card p-4">
      <h2 className="font-bold text-lg leading-snug break-words">{post.title}</h2>
      <p className="mt-0.5 flex items-center gap-1 text-xs text-ink-faint">
        {post.authorMode === 'Account' && (
          <Icon name="user" className="w-3.5 h-3.5 text-brand-700 dark:text-brand-400" />
        )}
        <span className={`font-semibold ${post.authorMode === 'Account' ? 'text-brand-700 dark:text-brand-400' : 'text-ink-soft'}`}
              title={post.authorMode === 'Account' ? t('accountBadge') : undefined}>
          {post.authorName ?? t('anonymous')}
        </span>
        <span aria-hidden>·</span>
        <time dateTime={post.createdAt} title={new Date(post.createdAt).toLocaleString(currentLocale())}>
          {timeAgo(post.createdAt)}
        </time>
      </p>

      <p className="mt-3 text-[15px] leading-relaxed whitespace-pre-wrap break-words">
        {long && !expanded ? `${post.content.slice(0, CLAMP_CHARS).trimEnd()}…` : post.content}
      </p>
      {long && (
        <button onClick={() => setExpanded(v => !v)} className="mt-1 text-sm font-semibold text-brand-700 dark:text-brand-400">
          {expanded ? t('less') : t('more')}
        </button>
      )}

      <div className="mt-3 pt-3 border-t border-line/60 flex items-center gap-2">
        <VoteButton icon="like" label={t('like')} count={post.likes} active={post.myVote === 1} tone="ok"
                    disabled={voting} onClick={() => vote(1)} />
        <VoteButton icon="dislike" label={t('dislike')} count={post.dislikes} active={post.myVote === -1} tone="bad"
                    disabled={voting} onClick={() => vote(-1)} />
        {post.mine && (
          <button onClick={() => setConfirmDelete(true)} title={t('delete')} aria-label={t('delete')}
                  className="ml-auto w-9 h-9 rounded-lg flex items-center justify-center text-ink-faint hover:text-bad hover:bg-bad/10 transition">
            <Icon name="trash" className="w-4 h-4" />
          </button>
        )}
      </div>
      {error && <p className="mt-2 text-xs text-bad">{error}</p>}

      {confirmDelete && (
        <ConfirmDialog title={t('deleteConfirmTitle')} message={t('deleteConfirmMessage', { title: post.title })}
                       confirmLabel={t('delete')} onClose={() => setConfirmDelete(false)} onConfirm={remove} />
      )}
    </article>
  )
}

function VoteButton({ icon, label, count, active, tone, disabled, onClick }: {
  icon: 'like' | 'dislike'; label: string; count: number; active: boolean; tone: 'ok' | 'bad'
  disabled: boolean; onClick: () => void
}) {
  const on = tone === 'ok' ? 'border-ok/40 bg-ok/10 text-ok' : 'border-bad/40 bg-bad/10 text-bad'
  return (
    <button onClick={onClick} disabled={disabled} aria-pressed={active} aria-label={label} title={label}
            className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm font-semibold tabular-nums
                        transition active:scale-95 disabled:opacity-60
                        ${active ? on : 'border-line text-ink-soft hover:bg-muted'}`}>
      <Icon name={icon} className="w-4 h-4" /> {count}
    </button>
  )
}

/** "5 phút trước" / "5 minutes ago" — quá 30 ngày thì ghi ngày. */
function timeAgo(iso: string) {
  const sec = (Date.now() - new Date(iso).getTime()) / 1000
  const rtf = new Intl.RelativeTimeFormat(currentLocale(), { numeric: 'auto' })
  if (sec < 60) return rtf.format(0, 'second')
  if (sec < 3600) return rtf.format(-Math.floor(sec / 60), 'minute')
  if (sec < 86400) return rtf.format(-Math.floor(sec / 3600), 'hour')
  if (sec < 30 * 86400) return rtf.format(-Math.floor(sec / 86400), 'day')
  return new Date(iso).toLocaleDateString(currentLocale())
}
