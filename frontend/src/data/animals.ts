// Bảng "số đề – con vật" dân gian: 2 số cuối → con vật/đồ vật. Tên hiển thị nằm ở i18n
// (predict:animals.<key>). Dùng emoji vì Lucide không có đủ các con vật này.
// Số chưa có trong bảng (21, 36–40, 61, 76–79) thì không gắn con vật nào.
import type vi from '../i18n/locales/vi/predict.json'

type AnimalKey = keyof typeof vi.animals

const GROUPS: [key: AnimalKey, emoji: string, numbers: number[]][] = [
  ['duckEgg', '🥚', [0]],
  ['whiteFish', '🐟', [1, 41, 81]],
  ['snail', '🐌', [2, 42, 82]],
  ['duck', '🦆', [3, 43, 83]],
  ['peacock', '🦚', [4, 44, 84]],
  ['insect', '🪲', [5, 45, 85]],
  ['tiger', '🐯', [6, 46, 86]],
  ['pig', '🐷', [7, 47, 87]],
  ['rabbit', '🐰', [8, 48, 88]],
  ['buffalo', '🐃', [9, 49, 89]],
  ['lyingDragon', '🐉', [10, 50, 90]],
  ['dog', '🐕', [11, 51, 91]],
  ['horse', '🐴', [12, 52, 92]],
  ['elephant', '🐘', [13, 53, 93]],
  ['cat', '🐈', [14, 54, 94]],
  ['rat', '🐭', [15, 55, 95]],
  ['bee', '🐝', [16, 56, 96]],
  ['crane', '🦩', [17, 57, 97]],
  ['wildcat', '🐆', [18, 58, 98]],
  ['butterfly', '🦋', [19, 59, 99]],
  ['centipede', '🐛', [20, 60, 80]],
  ['pigeon', '🕊️', [22, 62]],
  ['monkey', '🐒', [23, 63]],
  ['frog', '🐸', [24, 64]],
  ['eagle', '🦅', [25, 65]],
  ['flyingDragon', '🐲', [26, 66]],
  ['turtle', '🐢', [27, 67]],
  ['rooster', '🐓', [28, 68]],
  ['eel', '🪱', [29, 69]],
  ['blackFish', '🐠', [30, 70]],
  ['shrimp', '🦐', [31, 71]],
  ['snake', '🐍', [32, 72]],
  ['spider', '🕷️', [33, 73]],
  ['deer', '🦌', [34, 74]],
  ['goat', '🐐', [35, 75]],
]

export interface Animal { key: AnimalKey; emoji: string }

const BY_NUMBER = new Map<string, Animal>(
  GROUPS.flatMap(([key, emoji, nums]) => nums.map(n => [String(n).padStart(2, '0'), { key, emoji }] as const)),
)

/** Con vật của số 2 chữ số ("07" → heo); undefined nếu số chưa có trong bảng. */
export const animalOf = (number: string) => BY_NUMBER.get(number.slice(-2).padStart(2, '0'))
