"""Procedurally generates the ServerSyncPlus icon as SVG (no AI imagery).

Usage:  python3 branding/make_icon.py            -> writes branding/icon.svg
Render: node branding/render_icon.js             -> writes package/icon.png (256x256)
"""
import math, os

S = 256            # canvas
C = S / 2          # center
PALETTE = {
    "bg_top": "#16233D", "bg_bottom": "#0A111F",   # deep navy
    "gear_a": "#2DD4BF", "gear_b": "#0EA5E9",       # teal -> sky
    "arrow": "#F8FAFC",                              # near-white
    "plus_a": "#FCD34D", "plus_b": "#F59E0B",       # amber
    "hub": "#0B1424",
}

def gear_path(cx, cy, r_root, r_tip, teeth, tip_frac=0.42, root_frac=0.58, hole=None):
    """Gear outline with trapezoid teeth; optional circular hole (even-odd)."""
    pts = []
    step = 2 * math.pi / teeth
    for i in range(teeth):
        a = i * step - math.pi / 2
        # root -> flank up -> tip -> flank down
        r_half = step * root_frac / 2
        t_half = step * tip_frac / 2
        for ang, r in ((a - r_half, r_root), (a - t_half, r_tip), (a + t_half, r_tip), (a + r_half, r_root)):
            pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
        # root arc to next tooth handled by the straight segment (short at this size)
    d = "M" + " L".join(f"{x:.2f},{y:.2f}" for x, y in pts) + " Z"
    if hole:
        d += (f" M{cx + hole:.2f},{cy:.2f} A{hole},{hole} 0 1,0 {cx - hole:.2f},{cy:.2f}"
              f" A{hole},{hole} 0 1,0 {cx + hole:.2f},{cy:.2f} Z")
    return d

def arc_arrow(cx, cy, r, start_deg, sweep_deg, width, head_len, head_w):
    """Thick circular arc (as a filled shape) ending in an arrowhead, clockwise."""
    a0 = math.radians(start_deg)
    a1 = math.radians(start_deg + sweep_deg)
    ri, ro = r - width / 2, r + width / 2
    large = 1 if sweep_deg > 180 else 0
    p = lambda rad, ang: (cx + rad * math.cos(ang), cy + rad * math.sin(ang))
    # arrowhead at a1, pointing along the clockwise tangent
    tip_ang = a1 + head_len / r
    base_o, base_i = p(r + head_w / 2, a1), p(r - head_w / 2, a1)
    tip = p(r, tip_ang)
    (x0o, y0o), (x1o, y1o) = p(ro, a0), p(ro, a1)
    (x1i, y1i), (x0i, y0i) = p(ri, a1), p(ri, a0)
    return (f"M{x0o:.2f},{y0o:.2f} A{ro},{ro} 0 {large},1 {x1o:.2f},{y1o:.2f}"
            f" L{base_o[0]:.2f},{base_o[1]:.2f} L{tip[0]:.2f},{tip[1]:.2f} L{base_i[0]:.2f},{base_i[1]:.2f}"
            f" L{x1i:.2f},{y1i:.2f} A{ri},{ri} 0 {large},0 {x0i:.2f},{y0i:.2f} Z")

def plus_path(cx, cy, size, thick, radius):
    """Rounded plus sign."""
    h, t = size / 2, thick / 2
    r = radius
    # draw as two rounded rects unioned via a single path with nonzero fill
    def rrect(x, y, w, hgt):
        return (f"M{x + r:.2f},{y:.2f} H{x + w - r:.2f} A{r},{r} 0 0 1 {x + w:.2f},{y + r:.2f}"
                f" V{y + hgt - r:.2f} A{r},{r} 0 0 1 {x + w - r:.2f},{y + hgt:.2f}"
                f" H{x + r:.2f} A{r},{r} 0 0 1 {x:.2f},{y + hgt - r:.2f} V{y + r:.2f} A{r},{r} 0 0 1 {x + r:.2f},{y:.2f} Z")
    return rrect(cx - h, cy - t, size, thick) + " " + rrect(cx - t, cy - h, thick, size)

def build():
    P = PALETTE
    gear = gear_path(C, C, r_root=98, r_tip=118, teeth=10, hole=70)
    ring_r = 84
    arrows = [arc_arrow(C, C, ring_r, start, 128, width=17, head_len=24, head_w=38) for start in (-150, 30)]
    plus = plus_path(C, C, size=74, thick=24, radius=7)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{S}" height="{S}" viewBox="0 0 {S} {S}">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="{P["bg_top"]}"/><stop offset="1" stop-color="{P["bg_bottom"]}"/>
    </linearGradient>
    <linearGradient id="gear" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{P["gear_a"]}"/><stop offset="1" stop-color="{P["gear_b"]}"/>
    </linearGradient>
    <linearGradient id="plus" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="{P["plus_a"]}"/><stop offset="1" stop-color="{P["plus_b"]}"/>
    </linearGradient>
    <radialGradient id="glow" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0" stop-color="{P["gear_a"]}" stop-opacity="0.22"/><stop offset="1" stop-color="{P["gear_a"]}" stop-opacity="0"/>
    </radialGradient>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="3" stdDeviation="3.5" flood-color="#000" flood-opacity="0.45"/>
    </filter>
  </defs>
  <rect width="{S}" height="{S}" rx="44" fill="url(#bg)"/>
  <circle cx="{C}" cy="{C}" r="124" fill="url(#glow)"/>
  <path d="{gear}" fill="url(#gear)" fill-rule="evenodd" filter="url(#shadow)"/>
  <circle cx="{C}" cy="{C}" r="70" fill="{P["hub"]}"/>
  <g fill="{P["arrow"]}" filter="url(#shadow)">
    {"".join(f'<path d="{a}"/>' for a in arrows)}
  </g>
  <path d="{plus}" fill="url(#plus)" filter="url(#shadow)"/>
</svg>
'''

if __name__ == "__main__":
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.svg")
    with open(out, "w") as f:
        f.write(build())
    print("wrote", out)
