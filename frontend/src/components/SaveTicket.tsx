import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TicketQuery } from '../api/client'
import { isSouth } from '../data/provinces'
import { useSaveTickets } from '../lib/saveTickets'
import Icon from './Icon'

type Props = {
  tickets: TicketQuery[]
  /** Nút chính (vé chưa xổ) hay nút phụ (vé trúng: lưu để nhắc lĩnh). */
  primary?: boolean
  label: string
}

/**
 * Nút "Lưu vé": chỉ hiện khi cờ savedTickets mở. Đài không phải Miền Nam thì thay bằng dòng ghi chú.
 * Lưu xong hiện lời nhắn: có kết quả sẽ báo ở chuông thông báo.
 */
export function SaveTicketButton({ tickets, primary, label }: Props) {
  const { t } = useTranslation('check')
  const { available, save } = useSaveTickets()
  const [state, setState] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState<string | null>(null)
  if (!available) return null

  const south = tickets.filter(x => isSouth(x.province))
  if (south.length === 0) return <p className="text-xs text-ink-faint text-center">{t('save.southOnly')}</p>

  const onSave = async () => {
    setState('saving')
    setError(null)
    try {
      const saved = await save(south)
      setState(saved ? 'saved' : 'idle')
    } catch (e) {
      setError((e as Error)?.message ?? '')
      setState('idle')
    }
  }

  if (state === 'saved') return (
    <div className="card p-4">
      <p className="flex items-start gap-2 text-sm font-medium text-ok">
        <Icon name="bookmarked" className="w-5 h-5 shrink-0" /> {t('save.done')}
      </p>
    </div>
  )

  return (
    <div className="space-y-1.5">
      <button onClick={onSave} disabled={state === 'saving'} className={`btn w-full ${primary ? 'btn-primary' : 'btn-soft'}`}>
        <Icon name="bookmark" /> {state === 'saving' ? t('save.saving') : label}
      </button>
      {error && <p role="alert" className="text-xs text-bad text-center">{error}</p>}
    </div>
  )
}
