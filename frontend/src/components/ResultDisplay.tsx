type Winning = { tierName: string; amount: number }

type Props = {
  result: {
    extractedNumber: string
    drawDate: string | null
    province: string | null
    isWinner: boolean
    winnings: Winning[]
    totalPrize: number
  }
  onRescan: () => void
}

const formatVND = (n: number) =>
  n.toLocaleString('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 })

export default function ResultDisplay({ result, onRescan }: Props) {
  const { isWinner, winnings, totalPrize, extractedNumber, drawDate, province } = result

  return (
    <div className="p-4 space-y-4">
      <div className="bg-white rounded-2xl shadow p-5 text-center">
        <div className="text-sm text-gray-500">Vé số</div>
        <div className="text-3xl font-bold tracking-widest my-1">{extractedNumber}</div>
        <div className="text-sm text-gray-500">
          {province} — {drawDate}
        </div>
      </div>

      {isWinner ? (
        <>
          <div className="bg-green-50 border border-green-300 rounded-2xl p-5 text-center">
            <div className="text-green-700 font-medium mb-1">🎉 Chúc mừng! Vé trúng:</div>
            <div className="text-4xl font-bold text-green-700">{formatVND(totalPrize)}</div>
            {winnings.length > 1 && (
              <div className="text-xs text-green-700/70 mt-2">
                ({winnings.length} giải cộng dồn)
              </div>
            )}
          </div>

          <ul className="bg-white rounded-2xl shadow divide-y">
            {winnings.map((w, i) => (
              <li key={i} className="flex items-center justify-between p-4">
                <span className="font-medium">{w.tierName}</span>
                <span className="text-brand-600 font-semibold">{formatVND(w.amount)}</span>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <div className="bg-gray-50 border border-gray-200 rounded-2xl p-6 text-center">
          <div className="text-2xl mb-2">😔</div>
          <div className="font-medium">Tiếc quá, vé không trúng giải nào</div>
          <div className="text-sm text-gray-500 mt-1">Chúc bạn may mắn lần sau!</div>
        </div>
      )}

      <button onClick={onRescan}
              className="w-full bg-blue-600 text-white py-3 rounded-lg">
        🔄 Dò vé khác
      </button>
    </div>
  )
}
