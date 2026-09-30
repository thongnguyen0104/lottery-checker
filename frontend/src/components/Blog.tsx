import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  createBlogComment, createBlogPost, deleteBlogComment, deleteBlogPost, getBlogComments, getBlogPost, getBlogPosts, voteBlogPost,
  type Account, type BlogAuthorMode, type BlogComment, type BlogPost,
} from '../api/client'
import { currentLocale } from '../i18n'
import { timeAgo } from '../utils/date'
import { blogPostPath } from '../views'
import ConfirmDialog from './ConfirmDialog'
import Icon, { IconBadge } from './Icon'

// Khớp BlogService ở backend — kiểm tra trước ở đây để báo lỗi ngay khi gõ.
const TITLE = { min: 3, max: 120 }
const CONTENT = { min: 10, max: 5000 }
const NAME = { min: 2, max: 30 }
const COMMENT = { min: 2, max: 1000 }
/** Nội dung dài hơn chừng này thì thu gọn, bấm "Xem thêm" mới hiện đủ. */
const CLAMP_CHARS = 280

type Sort = 'new' | 'top'

/**
 * Bài mở riêng trên cùng: từ chuông thông báo (postId + commentId) hoặc link chia sẻ /blog/{guid}
 * (publicId). at đổi mỗi lần bấm để bấm lại cùng thông báo vẫn cuộn tới.
 */
export type BlogFocus = { postId?: number; publicId?: string; commentId?: number; at: number }

type Props = {
  account: Account | null
  onRequireLogin: () => void
  focus?: BlogFocus | null
  onClearFocus?: () => void
}

/** Blog cho mọi người: đọc, viết (ký tên tài khoản / ẩn danh / tự đặt), like/dislike, xoá bài của mình. */
export default function Blog({ account, onRequireLogin, focus, onClearFocus }: Props) {
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

      {focus && (
        <FocusedPost key={focus.at} focus={focus} account={account} onRequireLogin={onRequireLogin}
                     onClose={() => onClearFocus?.()}
                     onChange={(id, patch) => update(id, patch)} />
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
        {posts.filter(p => p.id !== focus?.postId && p.publicId !== focus?.publicId).map(p => <PostCard key={p.id} post={p} account={account} onRequireLogin={onRequireLogin}
                                 onChange={patch => update(p.id, patch)} />)}
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

/**
 * Bài mở từ chuông thông báo (mở sẵn bình luận) hoặc từ link chia sẻ: tải riêng bài đó (có thể không
 * nằm ở trang đầu).
 */
function FocusedPost({ focus, account, onRequireLogin, onClose, onChange }: {
  focus: BlogFocus
  account: Account | null
  onRequireLogin: () => void
  onClose: () => void
  onChange: (id: number, patch: Partial<BlogPost> | null) => void
}) {
  const { t } = useTranslation()
  const { t: tb } = useTranslation('blog')
  const [post, setPost] = useState<BlogPost | null>(null)
  const [error, setError] = useState<string | null>(null)
  const key = focus.publicId ?? focus.postId!
  const label = focus.publicId ? tb('sharedPost') : t('notifications.focusedPost')
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getBlogPost(key).then(p => alive && setPost(p)).catch(e => alive && setError(e?.message ?? t('notifications.postGone')))
    return () => { alive = false }
  }, [key, t, reload])

  return (
    <section className="space-y-2 fade-up" aria-label={label}>
      <div className="flex items-center justify-between gap-2 text-sm">
        <span className="inline-flex items-center gap-1.5 font-semibold text-brand-700 dark:text-brand-400">
          <Icon name={focus.publicId ? 'link' : 'bell'} className="w-4 h-4" /> {label}
        </span>
        <button onClick={onClose} className="font-semibold text-ink-faint hover:text-ink">{t('notifications.showAll')}</button>
      </div>
      {error && (
        <div className="card p-4 flex items-center justify-between gap-3 text-sm text-ink-soft">
          <span>{error}</span>
          <button onClick={() => { setError(null); setReload(n => n + 1) }} className="btn btn-soft shrink-0 py-1.5 text-sm">
            <Icon name="retry" className="w-4 h-4" /> {tb('retry')}
          </button>
        </div>
      )}
      {!post && !error && <div className="card h-40 motion-safe:animate-pulse" />}
      {post && (
        <div className="rounded-2xl ring-2 ring-brand-500/40">
          <PostCard post={post} account={account} onRequireLogin={onRequireLogin} highlightCommentId={focus.commentId}
                    onChange={patch => {
                      onChange(post.id, patch)
                      if (patch) setPost({ ...post, ...patch })
                      else onClose()
                    }} />
        </div>
      )}
    </section>
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

      <SignAsPicker account={account} mode={effectiveMode} onMode={setMode} name={name} onName={setName}
                    onRequireLogin={onRequireLogin} />

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

/** Chọn cách ký tên — dùng chung cho form đăng bài và form bình luận. compact: bỏ nhãn + dòng gợi ý đăng nhập. */
function SignAsPicker({ account, mode, onMode, name, onName, onRequireLogin, compact }: {
  account: Account | null
  mode: BlogAuthorMode
  onMode: (m: BlogAuthorMode) => void
  name: string
  onName: (n: string) => void
  onRequireLogin: () => void
  compact?: boolean
}) {
  const { t } = useTranslation('blog')
  return (
    <fieldset>
      <legend className={compact ? 'sr-only' : 'text-sm font-semibold mb-1.5'}>{t('signAs')}</legend>
      <div role="radiogroup" className="grid grid-cols-3 gap-1 p-1 rounded-xl bg-muted border border-line/60">
        {(['Account', 'Anonymous', 'Custom'] as const).map(m => {
          const active = mode === m
          const locked = m === 'Account' && !account
          return (
            <button key={m} type="button" role="radio" aria-checked={active}
                    onClick={() => locked ? onRequireLogin() : onMode(m)}
                    title={locked ? t('accountHint') : undefined}
                    className={`flex items-center justify-center gap-1 rounded-lg font-semibold transition
                                ${compact ? 'py-1.5 text-xs' : 'py-2 text-sm'}
                                ${active ? 'bg-surface dark:bg-brand-500/10 shadow-sm text-brand-700 dark:text-brand-400'
                                         : locked ? 'text-ink-faint/70' : 'text-ink-faint hover:text-ink-soft'}`}>
              {m === 'Account' && <Icon name="user" className={`${compact ? 'w-3.5 h-3.5' : 'w-4 h-4'} shrink-0`} />}
              <span className="truncate">{m === 'Account' && account ? account.username : t(`modes.${m}`)}</span>
            </button>
          )
        })}
      </div>
      {!account && !compact && (
        <p className="mt-1.5 text-xs text-ink-faint">
          {t('accountHint')} —{' '}
          <button type="button" onClick={onRequireLogin} className="font-semibold text-brand-700 dark:text-brand-400 underline">
            {t('loginToUse')}
          </button>
        </p>
      )}
      {mode === 'Custom' && (
        <input value={name} onChange={e => onName(e.target.value)} maxLength={NAME.max}
               placeholder={t('namePlaceholder', { min: NAME.min, max: NAME.max })}
               className={`field mt-2 ${compact ? 'py-2 text-sm' : ''}`} />
      )}
    </fieldset>
  )
}

function PostCard({ post, account, onRequireLogin, onChange, highlightCommentId }: {
  post: BlogPost
  account: Account | null
  onRequireLogin: () => void
  onChange: (patch: Partial<BlogPost> | null) => void
  /** Có = mở sẵn bình luận và cuộn tới bình luận này (từ chuông thông báo). */
  highlightCommentId?: number
}) {
  const { t } = useTranslation('blog')
  const [expanded, setExpanded] = useState(false)
  const [showComments, setShowComments] = useState(highlightCommentId != null)
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
        <button onClick={() => setShowComments(v => !v)} aria-expanded={showComments}
                aria-label={t('comments.toggle', { count: post.commentCount })} title={t('comments.title')}
                className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm font-semibold tabular-nums
                            transition active:scale-95
                            ${showComments ? 'border-brand-500/40 bg-brand-500/10 text-brand-700 dark:text-brand-400'
                                           : 'border-line text-ink-soft hover:bg-muted'}`}>
          <Icon name="comment" className="w-4 h-4" /> {post.commentCount}
        </button>
        <CopyLinkButton publicId={post.publicId} />
        {post.mine && (
          <button onClick={() => setConfirmDelete(true)} title={t('delete')} aria-label={t('delete')}
                  className="ml-auto w-9 h-9 rounded-lg flex items-center justify-center text-ink-faint hover:text-bad hover:bg-bad/10 transition">
            <Icon name="trash" className="w-4 h-4" />
          </button>
        )}
      </div>
      {error && <p className="mt-2 text-xs text-bad">{error}</p>}

      {showComments && (
        <Comments postId={post.id} account={account} onRequireLogin={onRequireLogin} highlightId={highlightCommentId}
                  onCount={commentCount => onChange({ commentCount })} />
      )}

      {confirmDelete && (
        <ConfirmDialog title={t('deleteConfirmTitle')} message={t('deleteConfirmMessage', { title: post.title })}
                       confirmLabel={t('delete')} onClose={() => setConfirmDelete(false)} onConfirm={remove} />
      )}
    </article>
  )
}

/**
 * Bình luận của 1 bài: luồng 1 cấp (bình luận gốc + các trả lời thụt vào). Trả lời một câu trả lời →
 * vẫn vào luồng đó, điền sẵn "@tên" để biết đang trả lời ai. Tải khi mở, không tải sẵn cho mọi bài.
 */
function Comments({ postId, account, onRequireLogin, onCount, highlightId }: {
  postId: number
  highlightId?: number
  account: Account | null
  onRequireLogin: () => void
  onCount: (count: number) => void
}) {
  const { t } = useTranslation('blog')
  const [comments, setComments] = useState<BlogComment[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Đang trả lời luồng nào (id bình luận gốc) + "@tên" điền sẵn khi trả lời một câu trả lời.
  const [replyTo, setReplyTo] = useState<{ rootId: number; mention: string } | null>(null)
  const [deleting, setDeleting] = useState<BlogComment | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let alive = true
    getBlogComments(postId).then(c => alive && setComments(c)).catch(e => alive && setError(e?.message ?? ''))
    return () => { alive = false }
  }, [postId, reload])

  const retry = () => {
    setError(null)
    setReload(n => n + 1)
  }

  const added = (c: BlogComment, count: number) => {
    setComments(cs => [...(cs ?? []), c])
    setReplyTo(null)
    onCount(count)
  }

  const remove = async () => {
    const c = deleting!
    setDeleting(null)
    try {
      const r = await deleteBlogComment(c.id)
      // Xoá gốc thì máy chủ xoá luôn các trả lời của nó.
      setComments(cs => (cs ?? []).filter(x => x.id !== c.id && x.parentId !== c.id))
      onCount(r.commentCount)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const roots = comments?.filter(c => c.parentId == null) ?? []
  const reply = (rootId: number, c: BlogComment) =>
    setReplyTo({ rootId, mention: c.parentId != null && c.authorName ? `@${c.authorName} ` : '' })

  return (
    <section className="mt-3 pt-3 border-t border-line/60 space-y-3" aria-label={t('comments.title')}>
      {error && (
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs text-bad">{error}</p>
          {/* Chưa tải được danh sách thì mới cần tải lại; lỗi lúc xoá bình luận thì danh sách vẫn còn. */}
          {!comments && (
            <button onClick={retry} className="btn btn-soft shrink-0 py-1.5 text-sm">
              <Icon name="retry" className="w-4 h-4" /> {t('retry')}
            </button>
          )}
        </div>
      )}
      {!comments && !error && <div className="h-12 rounded-xl bg-muted motion-safe:animate-pulse" />}
      {comments && roots.length === 0 && <p className="text-sm text-ink-faint">{t('comments.empty')}</p>}

      {roots.map(root => {
        const replies = comments!.filter(c => c.parentId === root.id)
        return (
          <div key={root.id} className="space-y-2">
            <CommentItem c={root} highlight={root.id === highlightId} onReply={() => reply(root.id, root)} onDelete={() => setDeleting(root)} />
            {(replies.length > 0 || replyTo?.rootId === root.id) && (
              <div className="ml-4 pl-3 border-l-2 border-line/70 space-y-2">
                {replies.map(r => (
                  <CommentItem key={r.id} c={r} highlight={r.id === highlightId} onReply={() => reply(root.id, r)} onDelete={() => setDeleting(r)} />
                ))}
                {replyTo?.rootId === root.id && (
                  <CommentForm key={`${root.id}:${replyTo.mention}`} postId={postId} parentId={root.id}
                               initial={replyTo.mention} account={account} onRequireLogin={onRequireLogin}
                               onCancel={() => setReplyTo(null)} onPosted={added} />
                )}
              </div>
            )}
          </div>
        )
      })}

      {comments && (
        <CommentForm postId={postId} account={account} onRequireLogin={onRequireLogin} onPosted={added} />
      )}

      {deleting && (
        <ConfirmDialog title={t('comments.deleteTitle')}
                       message={t(deleting.parentId == null ? 'comments.deleteThread' : 'comments.deleteOne')}
                       confirmLabel={t('comments.delete')} onClose={() => setDeleting(null)} onConfirm={remove} />
      )}
    </section>
  )
}

function CommentItem({ c, highlight, onReply, onDelete }: {
  c: BlogComment; highlight?: boolean; onReply: () => void; onDelete: () => void
}) {
  const { t } = useTranslation('blog')
  const account = c.authorMode === 'Account'
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => { if (highlight) ref.current?.scrollIntoView({ behavior: 'smooth', block: 'center' }) }, [highlight])
  return (
    <div ref={ref} className={`group ${highlight ? '-mx-2 px-2 py-1 rounded-lg bg-brand-500/10 ring-1 ring-brand-500/30' : ''}`}>
      <div className="flex items-center gap-1 text-xs text-ink-faint">
        {account && <Icon name="user" className="w-3.5 h-3.5 text-brand-700 dark:text-brand-400" />}
        <span className={`font-semibold ${account ? 'text-brand-700 dark:text-brand-400' : 'text-ink-soft'}`}
              title={account ? t('accountBadge') : undefined}>
          {c.authorName ?? t('anonymous')}
        </span>
        <span aria-hidden>·</span>
        <time dateTime={c.createdAt} title={new Date(c.createdAt).toLocaleString(currentLocale())}>{timeAgo(c.createdAt)}</time>
      </div>
      <p className="mt-0.5 text-sm leading-relaxed whitespace-pre-wrap break-words">{c.content}</p>
      <div className="mt-0.5 flex items-center gap-3 text-xs font-semibold">
        <button onClick={onReply} className="inline-flex items-center gap-1 text-ink-faint hover:text-brand-700 dark:hover:text-brand-400">
          <Icon name="reply" className="w-3.5 h-3.5" /> {t('comments.reply')}
        </button>
        {c.canDelete && (
          <button onClick={onDelete} className="inline-flex items-center gap-1 text-ink-faint hover:text-bad">
            <Icon name="trash" className="w-3.5 h-3.5" /> {t('comments.delete')}
          </button>
        )}
      </div>
    </div>
  )
}

function CommentForm({ postId, parentId, initial = '', account, onRequireLogin, onCancel, onPosted }: {
  postId: number
  parentId?: number
  initial?: string
  account: Account | null
  onRequireLogin: () => void
  onCancel?: () => void
  onPosted: (c: BlogComment, count: number) => void
}) {
  const { t } = useTranslation('blog')
  const [content, setContent] = useState(initial)
  const [mode, setMode] = useState<BlogAuthorMode>(account ? 'Account' : 'Anonymous')
  const [name, setName] = useState('')
  // Chỉ hiện phần ký tên khi bắt đầu gõ — form gọn khi chỉ đọc bình luận.
  const [focused, setFocused] = useState(!!parentId)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const effectiveMode = mode === 'Account' && !account ? 'Anonymous' : mode
  const valid = content.trim().length >= COMMENT.min && (effectiveMode !== 'Custom' || name.trim().length >= NAME.min)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      const r = await createBlogComment(postId, {
        content: content.trim(), authorMode: effectiveMode, parentId,
        authorName: effectiveMode === 'Custom' ? name.trim() : undefined,
      })
      setContent('')
      onPosted(r.comment, r.commentCount)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="space-y-2">
      <textarea value={content} onChange={e => setContent(e.target.value)} onFocus={() => setFocused(true)}
                maxLength={COMMENT.max} rows={focused ? 3 : 1} autoFocus={!!parentId}
                placeholder={t(parentId ? 'comments.replyPlaceholder' : 'comments.placeholder')}
                aria-label={t(parentId ? 'comments.replyPlaceholder' : 'comments.placeholder')}
                className="field resize-y text-sm py-2" />
      {focused && (
        <>
          <SignAsPicker compact account={account} mode={effectiveMode} onMode={setMode} name={name} onName={setName}
                        onRequireLogin={onRequireLogin} />
          {error && <p className="text-xs text-bad">{error}</p>}
          <div className="flex gap-2 justify-end">
            {onCancel && <button type="button" onClick={onCancel} className="btn btn-soft py-1.5 text-sm">{t('cancel')}</button>}
            <button type="submit" disabled={!valid || busy} className="btn btn-primary py-1.5 text-sm">
              <Icon name="send" className="w-4 h-4" /> {busy ? t('comments.sending') : t(parentId ? 'comments.reply' : 'comments.send')}
            </button>
          </div>
        </>
      )}
    </form>
  )
}

/** Copy link riêng của bài (/blog/{guid}) để chia sẻ; đổi thành dấu tích ~1.5s sau khi copy xong. */
function CopyLinkButton({ publicId }: { publicId: string }) {
  const { t } = useTranslation('blog')
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    const url = `${location.origin}${blogPostPath(publicId)}`
    try {
      await navigator.clipboard.writeText(url)
    } catch {
      // Trình duyệt chặn clipboard (http, WebView cũ) — cho hiện link để tự copy.
      window.prompt(t('copyLink'), url)
      return
    }
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <button onClick={copy} title={t('copyLink')} aria-label={copied ? t('linkCopied') : t('copyLink')}
            className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm font-semibold
                        transition active:scale-95
                        ${copied ? 'border-ok/40 bg-ok/10 text-ok' : 'border-line text-ink-soft hover:bg-muted'}`}>
      <Icon name={copied ? 'check' : 'link'} className="w-4 h-4" />
      <span className="hidden sm:inline" aria-live="polite">{copied ? t('linkCopied') : t('share')}</span>
    </button>
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
