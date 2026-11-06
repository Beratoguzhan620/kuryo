import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { useEffect, useRef } from "react";

export type Tone = "accent" | "danger" | "muted" | "ink" | "warn";

export interface Pin {
  id: string;
  latitude: number;
  longitude: number;
  label: string;
  /** Shown in the marker: a short code or initials. */
  text: string;
  tone: Tone;
  /** Square for places (merchants, drop-offs), round for people. */
  shape?: "round" | "square";
  onClick?: () => void;
}

export interface Area {
  id: string;
  corners: [number, number][];
  label: string;
  tone: Tone;
  dashed?: boolean;
}

export interface Line {
  points: [number, number][];
  tone: Tone;
  dashed?: boolean;
}

const colors: Record<Tone, string> = { accent: "#1E5A3C", danger: "#9E2B25", muted: "#8A867F", ink: "#1F2A37", warn: "#9A6400" };

/** Samsun, where the demo company works; used when there is nothing to show yet. */
const fallback: L.LatLngTuple = [41.2867, 36.33];

/**
 * OpenStreetMap tiles with labelled pins, zone outlines and trails. Markers are drawn with CSS, labels are set as
 * text, never as HTML. The view fits what is shown the first time and whenever `fitKey` changes, not on every update,
 * so a courier moving does not yank the map away from where a dispatcher is looking.
 */
export function MapView({ pins, areas = [], lines = [], height = 420, onPick, fitKey = "" }: {
  pins: Pin[]; areas?: Area[]; lines?: Line[]; height?: number | string; onPick?: (latitude: number, longitude: number) => void;
  /** Fits the view once per key; null never fits, for drawing, where the map must stay where the user put it. */
  fitKey?: string | null;
}) {
  const element = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const layer = useRef<L.LayerGroup | null>(null);
  const fitted = useRef<string | null>(null);
  const pick = useRef(onPick);
  pick.current = onPick;

  useEffect(() => {
    if (!element.current || map.current) return;
    map.current = L.map(element.current, { zoomControl: true, attributionControl: false }).setView(fallback, 12);
    L.control.attribution({ prefix: "Leaflet" }).addTo(map.current);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: "&copy; OpenStreetMap contributors",
    }).addTo(map.current);
    layer.current = L.layerGroup().addTo(map.current);
    map.current.on("click", (e: L.LeafletMouseEvent) => pick.current?.(Math.round(e.latlng.lat * 1e6) / 1e6, Math.round(e.latlng.lng * 1e6) / 1e6));
    // A map that opens inside a dialog or a grid that is still laying out starts with the wrong size.
    const resize = new ResizeObserver(() => map.current?.invalidateSize({ animate: false }));
    resize.observe(element.current);
    return () => {
      resize.disconnect();
      map.current?.stop();
      map.current?.off();
      map.current?.remove();
      map.current = null;
    };
  }, []);

  useEffect(() => {
    const group = layer.current;
    if (!group || !map.current) return;
    group.clearLayers();
    for (const area of areas) {
      if (area.corners.length < 2) continue;
      const shape = area.corners.length === 2
        ? L.polyline(area.corners, { color: colors[area.tone], weight: 2, dashArray: "4 6" })
        : L.polygon(area.corners, { color: colors[area.tone], weight: 2, fillOpacity: 0.06, dashArray: area.dashed ? "4 6" : undefined });
      shape.bindTooltip(area.label, { sticky: true }).addTo(group);
    }
    for (const line of lines) {
      L.polyline(line.points, { color: colors[line.tone], weight: 3, opacity: 0.75, dashArray: line.dashed ? "4 6" : undefined }).addTo(group);
    }
    for (const pin of pins) {
      const html = document.createElement("span");
      html.className = `pin ${pin.shape ?? "round"}`;
      html.style.background = colors[pin.tone];
      html.textContent = pin.text;
      const marker = L.marker([pin.latitude, pin.longitude], { icon: L.divIcon({ html, className: "pin-wrap", iconSize: undefined, iconAnchor: [14, 14] }), title: pin.label, alt: pin.label, keyboard: true });
      if (pin.onClick) marker.on("click", pin.onClick);
      marker.bindTooltip(pin.label, { direction: "top", offset: [0, -12] });
      marker.addTo(group);
    }

    if (fitKey === null || fitted.current === fitKey) return;
    const points = [...pins.map((p) => [p.latitude, p.longitude] as L.LatLngTuple), ...areas.flatMap((a) => a.corners as L.LatLngTuple[])];
    if (points.length === 0) return;
    fitted.current = fitKey;
    // No animation: a page that is left mid-animation would leave Leaflet moving a map that is gone.
    if (points.length === 1) map.current.setView(points[0]!, 14, { animate: false });
    else map.current.fitBounds(L.latLngBounds(points), { padding: [36, 36], maxZoom: 15, animate: false });
  }, [pins, areas, lines, fitKey]);

  // Leaflet owns the inner element's classes; React only ever sets the outer one, so a re-render cannot strip them.
  return (
    <div className={`map${onPick ? " picking" : ""}`} style={{ height }} role="region" aria-label="Map">
      <div ref={element} className="map-canvas" />
    </div>
  );
}
