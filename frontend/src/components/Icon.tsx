import {
  Award, Bot, CircleUserRound, LogOut, Download, HandHeart, CalendarDays, Camera, Check, ChevronDown, ChevronLeft, ChevronRight, CircleAlert, Globe, CircleCheck,
  CircleX, Clock, Copy, Dices, Focus, History, Hourglass, ImageUp, Inbox, Layers, Lightbulb, ListOrdered, MapPin, MessageSquareText,
  Monitor, Moon, Palette, PencilLine, RotateCcw, Scan, ScanSearch, Sparkles, Sun, Ticket, TicketX, Timer,
  TrendingUp, Flame, Snowflake, Newspaper, ThumbsUp, ThumbsDown, Send, Trash2, TriangleAlert, Trophy, Wallet, X,
  KeyRound, ShieldCheck, Users, Search, MessageCircle, Reply, Bell, CheckCheck, Link2, ImagePlus,
  MapIcon, Star, Navigation, Phone, Flag, LocateFixed, Plus, SlidersHorizontal, Share2, Eye, EyeOff, Store, Footprints,
  type LucideIcon,
} from 'lucide-react'

// Icon Lucide nét mảnh dùng chung toàn app — thay cho emoji (mỗi hệ điều hành vẽ một kiểu, không
// đổi màu theo theme). Đặt tên theo nghĩa trong app để chỗ gọi khỏi import từng icon.
const ICONS = {
  ticket: Ticket,
  ticketX: TicketX,
  tickets: Layers,
  dice: Dices,
  calendar: CalendarDays,
  palette: Palette,
  sun: Sun,
  moon: Moon,
  monitor: Monitor,
  close: X,
  check: Check,
  copy: Copy,
  upload: ImageUp,
  camera: Camera,
  back: ChevronLeft,
  next: ChevronRight,
  down: ChevronDown,
  globe: Globe,
  retry: RotateCcw,
  edit: PencilLine,
  search: ScanSearch,
  sparkles: Sparkles,
  tip: Lightbulb,
  frame: Scan,
  focus: Focus,
  ok: CircleCheck,
  fail: CircleX,
  error: CircleAlert,
  warn: TriangleAlert,
  expired: Hourglass,
  clock: Clock,
  timer: Timer,
  empty: Inbox,
  trophy: Trophy,
  award: Award,
  pin: MapPin,
  list: ListOrdered,
  ai: Bot,
  history: History,
  donate: HandHeart,
  download: Download,
  sms: MessageSquareText,
  user: CircleUserRound,
  logout: LogOut,
  predict: TrendingUp,
  hot: Flame,
  cold: Snowflake,
  blog: Newspaper,
  like: ThumbsUp,
  dislike: ThumbsDown,
  send: Send,
  trash: Trash2,
  wallet: Wallet,
  key: KeyRound,
  admin: ShieldCheck,
  users: Users,
  find: Search,
  comment: MessageCircle,
  reply: Reply,
  bell: Bell,
  readAll: CheckCheck,
  link: Link2,
  addImage: ImagePlus,
  map: MapIcon,
  star: Star,
  directions: Navigation,
  phone: Phone,
  flag: Flag,
  locate: LocateFixed,
  plus: Plus,
  filter: SlidersHorizontal,
  share: Share2,
  show: Eye,
  hide: EyeOff,
  store: Store,
  street: Footprints,
} satisfies Record<string, LucideIcon>

export type IconName = keyof typeof ICONS

/** strokeWidth: 1.75 hợp cỡ 16–28px; icon rất nhỏ trên nền màu (dấu ✓ 14px) nên tăng lên cho rõ. */
export default function Icon({ name, className = 'w-5 h-5', strokeWidth = 1.75 }: {
  name: IconName; className?: string; strokeWidth?: number
}) {
  const Svg = ICONS[name]
  return <Svg className={className} strokeWidth={strokeWidth} aria-hidden />
}

const TONES = {
  brand: 'bg-brand-500/10 text-brand-700 ring-brand-500/25 dark:text-brand-400',
  ok: 'bg-ok/10 text-ok ring-ok/25',
  warn: 'bg-warn/10 text-warn ring-warn/25',
  bad: 'bg-bad/10 text-bad ring-bad/25',
  info: 'bg-info/10 text-info ring-info/25',
}

/** Ô icon lớn đầu các thẻ trạng thái (lỗi, trúng, chưa xổ...) — màu theo sắc thái. */
export function IconBadge({ name, tone }: { name: IconName; tone: keyof typeof TONES }) {
  return (
    <span className={`mx-auto w-14 h-14 rounded-2xl ring-1 flex items-center justify-center ${TONES[tone]}`}>
      <Icon name={name} className="w-7 h-7" />
    </span>
  )
}

/** Chấm nhỏ góc trên phải icon tab: tính năng đó đang xử lý (quét/dò) trong lúc user ở tab khác. */
export function BusyDot() {
  return (
    <span aria-hidden className="absolute top-0.5 right-1.5 flex w-2.5 h-2.5">
      <span className="absolute inset-0 rounded-full bg-brand-500 opacity-75 motion-safe:animate-ping" />
      <span className="relative w-2.5 h-2.5 rounded-full bg-brand-500 ring-2 ring-surface" />
    </span>
  )
}
