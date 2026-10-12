import { createContext, useContext } from 'react'
import type { SavedTicket, TicketQuery } from '../api/client'

/**
 * Lưu vé dùng chung cho các màn kết quả — App cung cấp. save trả null khi phải đăng nhập trước:
 * App mở form đăng nhập rồi tự lưu tiếp (và mở tab Vé đã lưu) sau khi đăng nhập xong.
 */
export type SaveTicketsApi = {
  available: boolean
  save: (items: TicketQuery[]) => Promise<SavedTicket[] | null>
}

export const SaveTicketsContext = createContext<SaveTicketsApi>({ available: false, save: async () => null })

export const useSaveTickets = () => useContext(SaveTicketsContext)
