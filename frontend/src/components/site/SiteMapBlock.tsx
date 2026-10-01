import 'leaflet/dist/leaflet.css'
import { CircleMarker, MapContainer, TileLayer } from 'react-leaflet'
import { TILE_ATTRIBUTION, TILE_URL } from '../map/shopUtils'

/** Bản đồ nhỏ của điểm bán gắn với site — chỉ xem, không kéo được (đỡ kẹt cuộn trang trên điện thoại). */
export default function SiteMapBlock({ lat, lng, color }: { lat: number; lng: number; color: string }) {
  return (
    <MapContainer center={[lat, lng]} zoom={16} className="h-64 w-full z-0" dragging={false} scrollWheelZoom={false}
                  touchZoom={false} doubleClickZoom={false} zoomControl={false} attributionControl>
      <TileLayer url={TILE_URL} attribution={TILE_ATTRIBUTION} maxZoom={19} maxNativeZoom={18} />
      <CircleMarker center={[lat, lng]} radius={11} pathOptions={{ color: '#fff', weight: 3, fillColor: color, fillOpacity: 1 }} />
    </MapContainer>
  )
}
