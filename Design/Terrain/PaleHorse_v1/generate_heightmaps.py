"""Generate numeric lunar terrain maps. Requires Python, NumPy and Pillow.

Only natural terrain and graded ground are represented: no buildings or props.
World +Y is north (the top row of the PNG); world +X is east.
Height encoding: Z = (pixel / 65535 - 0.5) * 64 metres.
"""

from pathlib import Path
import json
import math
import struct
import zipfile

import numpy as np
from PIL import Image, ImageDraw, ImageFont


OUT = Path(__file__).resolve().parent
SIZE = 2048
WORLD_SIZE = 512.0
HEIGHT_RANGE = 64.0
SEED = 17017
STEP = WORLD_SIZE / (SIZE - 1)
X = np.linspace(-256, 256, SIZE, dtype=np.float32)[None, :]
Y = np.linspace(256, -256, SIZE, dtype=np.float32)[:, None]
RNG = np.random.default_rng(SEED)

PADS = [
    (1, "Станция: площадка", -155, -150, 34, 25, 0),
    (2, "Приёмная площадка", 145, -142, 40, 28, 0),
    (3, "Контейнерный каньон: грунт", -145, 50, 32, 25, -12),
    (4, "Сортировочная зона: площадка", 138, 48, 29, 25, -12),
    (5, "Нижняя площадка", 0, 23, 60, 43, -28),
    (6, "Дальний могильник: площадка", 6, 129, 49, 24, -28),
]

PATHS = {
    "south_access": [
        (-155, -150, 0), (-110, -170, 0), (-45, -180, 0),
        (25, -180, 0), (90, -169, 0), (145, -142, 0),
    ],
    "west_serpentine": [
        (-155, -150, 0), (-181, -119, -0.5), (-184, -88, -2),
        (-160, -70, -3.5), (-130, -53, -5.5), (-123, -29, -7),
        (-147, -9, -9.5), (-162, 16, -11), (-145, 50, -12),
        (-112, 60, -12), (-93, 42, -13), (-89, 15, -15),
        (-108, -4, -17), (-108, -30, -20), (-85, -46, -23),
        (-57, -32, -25), (-50, -5, -27), (-33, 18, -28),
        (0, 23, -28),
    ],
    "east_ramp": [
        (145, -142, 0), (178, -115, 0), (186, -80, -1),
        (165, -50, -3), (140, -30, -5), (132, -5, -8),
        (147, 18, -11), (138, 48, -12),
    ],
    "floor_access": [(0, 23, -28), (18, 65, -28), (6, 129, -28)],
    "steep_upgrade_slope": [
        (90, -169, 0), (106, -134, 0), (102, -112, 0),
        (78, -82, -12), (50, -45, -28), (25, -18, -28), (0, 23, -28),
    ],
}


def smoothstep(lo, hi, a):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def noise(cells):
    grid = RNG.uniform(-1, 1, (cells, cells)).astype(np.float32)
    return np.asarray(
        Image.fromarray(grid).resize((SIZE, SIZE), Image.Resampling.BICUBIC),
        dtype=np.float32,
    )


def spline(points, spacing=2):
    """Smooth XY turns, interpolate height by horizontal arc length per span."""
    points = np.asarray(points, dtype=np.float32)
    samples = []
    for i in range(len(points) - 1):
        p0 = points[max(0, i - 1)]
        p1, p2 = points[i:i + 2]
        p3 = points[min(len(points) - 1, i + 2)]
        count = max(8, int(np.linalg.norm(p2[:2] - p1[:2]) / spacing) * 2)
        t = np.linspace(0, 1, count, endpoint=True, dtype=np.float32)[:, None]
        xy = 0.5 * (
            2 * p1[:2] + (-p0[:2] + p2[:2]) * t
            + (2 * p0[:2] - 5 * p1[:2] + 4 * p2[:2] - p3[:2]) * t**2
            + (-p0[:2] + 3 * p1[:2] - 3 * p2[:2] + p3[:2]) * t**3
        )
        arc = np.r_[0, np.cumsum(np.linalg.norm(np.diff(xy, axis=0), axis=1))]
        z = p1[2] + (p2[2] - p1[2]) * arc / max(float(arc[-1]), 1e-6)
        span = np.column_stack((xy, z))
        samples.extend(span[:-1])
    samples.append(points[-1])
    return np.asarray(samples, dtype=np.float32)


def road_fields(paths):
    nearest = np.full((SIZE, SIZE), 1e8, dtype=np.float32)
    road_z = np.zeros((SIZE, SIZE), dtype=np.float32)
    road_width = np.zeros((SIZE, SIZE), dtype=np.float32)
    sampled = {}
    for name, points in paths.items():
        route = spline(points)
        sampled[name] = route
        half_width = 3.6 if name == "steep_upgrade_slope" else 5.0
        margin = half_width + 5.0
        for a, b in zip(route[:-1], route[1:]):
            x0 = max(0, int((min(a[0], b[0]) - margin + 256) / STEP))
            x1 = min(SIZE, int((max(a[0], b[0]) + margin + 256) / STEP) + 2)
            y0 = max(0, int((256 - max(a[1], b[1]) - margin) / STEP))
            y1 = min(SIZE, int((256 - min(a[1], b[1]) + margin) / STEP) + 2)
            xx, yy = X[:, x0:x1], Y[y0:y1, :]
            dx, dy = b[:2] - a[:2]
            t = np.clip(((xx - a[0]) * dx + (yy - a[1]) * dy)
                        / max(float(dx * dx + dy * dy), 1e-9), 0, 1)
            distance = np.hypot(xx - a[0] - t * dx, yy - a[1] - t * dy)
            old = nearest[y0:y1, x0:x1]
            use = distance < old
            old[use] = distance[use]
            road_z[y0:y1, x0:x1][use] = (a[2] + t * (b[2] - a[2]))[use]
            road_width[y0:y1, x0:x1][use] = half_width
    alpha = 1 - smoothstep(road_width, road_width + 5, nearest)
    return road_z, alpha, sampled


def sample_field(field, points):
    points = np.asarray(points)
    u = np.clip((points[:, 0] + 256) / STEP, 0, SIZE - 1.001)
    v = np.clip((256 - points[:, 1]) / STEP, 0, SIZE - 1.001)
    i, j = u.astype(int), v.astype(int)
    fu, fv = u - i, v - j
    return ((1 - fu) * (1 - fv) * field[j, i]
            + fu * (1 - fv) * field[j, i + 1]
            + (1 - fu) * fv * field[j + 1, i]
            + fu * fv * field[j + 1, i + 1])


def save_u16(filename, normalized):
    data = np.rint(np.clip(normalized, 0, 1) * 65535).astype(np.uint16)
    Image.fromarray(data).save(OUT / filename)
    # Round-trip verifies true grayscale 16-bit PNG and numeric preservation.
    with open(OUT / filename, "rb") as f:
        header = f.read(33)
    assert header[:8] == b"\x89PNG\r\n\x1a\n"
    assert struct.unpack(">II", header[16:24]) == (SIZE, SIZE)
    assert header[24:26] == bytes((16, 0))
    restored = np.asarray(Image.open(OUT / filename))
    assert np.array_equal(restored, data)
    return data


def font(size, bold=False):
    name = "arialbd.ttf" if bold else "arial.ttf"
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / name), size)


def shaded_rgb(height):
    dy, dx = np.gradient(height, STEP, STEP)
    dy = -dy
    length = np.sqrt(dx * dx + dy * dy + 1)
    light = (-0.45 * -dx + 0.35 * -dy + 0.82) / length
    shade = np.clip(0.38 + 0.62 * light, 0.18, 1)
    base = 150 + (height + 28) * 0.6
    return np.clip(np.stack((base * shade * 0.97,
                            base * shade,
                            base * shade * 1.05), axis=-1), 0, 255).astype(np.uint8)


def previews(height, sampled):
    gray = np.rint((height / HEIGHT_RANGE + 0.5) * 255).astype(np.uint8)
    Image.fromarray(gray).resize((1024, 1024), Image.Resampling.LANCZOS).save(
        OUT / "pale_horse_height_preview.png")
    top = Image.fromarray(shaded_rgb(height)).resize((1000, 1000), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", (1440, 1120), "#12171d")
    canvas.paste(top, (30, 90))
    draw = ImageDraw.Draw(canvas)
    draw.text((30, 22), "PALE HORSE / рельеф «Подковы»", font=font(30, True), fill="#f1eee6")
    draw.text((30, 61), "512 × 512 м · уровни 0 / −12 / −28 м · только грунт", font=font(18), fill="#bcc5ce")
    colors = {"south_access": "#efb455", "west_serpentine": "#efb455",
              "east_ramp": "#80cbcf", "floor_access": "#efb455",
              "steep_upgrade_slope": "#cda0ed"}

    def pos(x, y):
        return 30 + (x + 256) / 512 * 999, 90 + (256 - y) / 512 * 999

    for name, path in sampled.items():
        draw.line([pos(p[0], p[1]) for p in path], fill=colors[name], width=3)
    for number, _, px, py, _, _, _ in PADS:
        cx, cy = pos(px, py)
        draw.ellipse((cx - 17, cy - 17, cx + 17, cy + 17), fill="#f0ede4", outline="#20252a", width=2)
        label = str(number)
        draw.text((cx, cy), label, anchor="mm", font=font(22, True), fill="#171d23")
    draw.text((960, 106), "N ↑", font=font(22, True), fill="#f1eee6")
    right = 1060
    draw.text((right, 102), "Площадки", font=font(24, True), fill="#f1eee6")
    names = ["1  Станция", "2  Приёмная зона", "3  Контейнерный каньон",
             "4  Сортировочная зона", "5  Нижняя площадка", "6  Дальний могильник"]
    for i, name in enumerate(names):
        draw.text((right, 148 + i * 42), name, font=font(20), fill="#d7dde3")
    draw.text((right, 434), "Маршруты", font=font(24, True), fill="#f1eee6")
    for i, (color, label) in enumerate([
            ("#efb455", "Основной серпантин"), ("#80cbcf", "Восточный спуск"),
            ("#cda0ed", "Крутой короткий путь")]):
        yy = 486 + i * 40
        draw.line((right, yy + 10, right + 30, yy + 10), fill=color, width=4)
        draw.text((right + 42, yy), label, font=font(18), fill="#d7dde3")
    note_lines = ["Цифры и цветные линии", "есть только на этой схеме.", "В картах высот их нет.", "", "Площадки — ровный грунт.",
                  "Здания, мосты, контейнеры", "и техника не добавлены."]
    for i, line in enumerate(note_lines):
        draw.text((right, 660 + i * 28), line, font=font(18), fill="#bcc5ce")
    draw.line((75, 1040, 270, 1040), fill="#f1eee6", width=4)
    draw.text((75, 1050), "100 м", font=font(18), fill="#f1eee6")
    canvas.save(OUT / "pale_horse_layout_preview.png")

    # Software-rendered oblique terrain preview, with height exaggerated x2.
    count = 193
    idx = np.linspace(0, SIZE - 1, count).round().astype(int)
    h = height[np.ix_(idx, idx)]
    rgb = shaded_rgb(height)[np.ix_(idx, idx)]
    coords = np.linspace(-256, 256, count)
    xx, yy = np.meshgrid(coords, coords[::-1])
    pu = 0.95 * xx + 0.32 * yy
    pv = 0.20 * xx - 0.62 * yy - 2.0 * h
    width, tall = 1500, 1050
    scale = min(1380 / np.ptp(pu), 840 / np.ptp(pv))
    px = (pu - pu.min()) * scale + (width - np.ptp(pu) * scale) / 2
    py = (pv - pv.min()) * scale + 135
    view = Image.new("RGB", (width, tall), "#12171d")
    pen = ImageDraw.Draw(view)
    cells = [(0.20 * xx[j, i] - 0.62 * yy[j, i], j, i)
             for j in range(count - 1) for i in range(count - 1)]
    cells.sort()
    for _, j, i in cells:
        corners = [(px[j, i], py[j, i]), (px[j, i + 1], py[j, i + 1]),
                   (px[j + 1, i + 1], py[j + 1, i + 1]), (px[j + 1, i], py[j + 1, i])]
        color = tuple(int(v) for v in rgb[j:j + 2, i:i + 2].mean(axis=(0, 1)))
        pen.polygon(corners, fill=color)
    pen.text((50, 26), "PALE HORSE / форма рельефа", font=font(32, True), fill="#f1eee6")
    pen.text((50, 76), "Вид с юга · высота для наглядности увеличена ×2 · без построек", font=font(21), fill="#bcc5ce")
    pen.text((50, 1000), "Основная карта 2048 × 2048 / 16-bit PNG / Blender: Strength 64, Midlevel 0.5", font=font(20), fill="#bcc5ce")
    view.save(OUT / "pale_horse_terrain_preview.png")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    broad = noise(9)
    medium = noise(35)
    angle = np.arctan2(Y - 20, X)
    radial = np.hypot(X / 208, (Y - 20) / 203)
    radial += 0.025 * np.sin(3 * angle + 0.6) + 0.015 * np.sin(7 * angle - 1.0) + 0.019 * broad
    height = -28 + 16 * smoothstep(0.53, 0.65, radial)
    height += 12 * smoothstep(0.79, 0.91, radial)
    wall = smoothstep(-170, -65, Y)
    ridge = np.exp(-((radial - 1.07) / 0.12)**2)
    height += ridge * wall * (22 + 3 * noise(17))
    ground = 1.4 * broad + 0.55 * medium
    height += ground * (0.35 + 0.65 * smoothstep(0.57, 0.98, radial))

    # Small shallow impact craters: terrain only, no object silhouettes.
    for _ in range(60):
        cx, cy = RNG.uniform(-240, 240, 2)
        radius = float(RNG.uniform(2.0, 9.0))
        depth = min(1.5, radius * 0.16)
        margin = radius * 1.8
        a = max(0, int((cx - margin + 256) / STEP))
        b = min(SIZE, int((cx + margin + 256) / STEP) + 1)
        c = max(0, int((256 - cy - margin) / STEP))
        d = min(SIZE, int((256 - cy + margin) / STEP) + 1)
        r = np.hypot(X[:, a:b] - cx, Y[c:d] - cy) / radius
        crater = -depth * np.exp(-r**4 * 2.3) + depth * 0.20 * np.exp(-((r - 1.0) / 0.18)**2)
        height[c:d, a:b] += crater

    protected = np.zeros_like(height)
    for _, _, cx, cy, rx, ry, elevation in PADS:
        r = np.hypot((X - cx) / rx, (Y - cy) / ry)
        alpha = 1 - smoothstep(0.78, 1.4, r)
        height = height * (1 - alpha) + elevation * alpha
        protected = np.maximum(protected, alpha)

    road_z, road_mask, sampled = road_fields(PATHS)
    height = height * (1 - road_mask) + road_z * road_mask
    protected = np.maximum(protected, road_mask)
    detail = (0.24 * noise(93) + 0.13 * noise(240) + 0.065 * noise(650))
    detail *= (1 - protected)**2
    # A noise strength of 1 in Blender reproduces this detail in metres.
    detail = np.clip(detail, -0.48, 0.48)
    combined = height + detail
    assert np.isfinite(height).all() and np.isfinite(detail).all()
    assert float(combined.min()) > -32 and float(combined.max()) < 32
    save_u16("pale_horse_height_16bit.png", height / HEIGHT_RANGE + 0.5)
    save_u16("pale_horse_noise_16bit.png", detail + 0.5)
    save_u16("pale_horse_height_with_noise_16bit.png", combined / HEIGHT_RANGE + 0.5)
    previews(height, sampled)

    routes = {}
    for name, route in sampled.items():
        actual = sample_field(height, route)
        run = np.linalg.norm(np.diff(route[:, :2], axis=0), axis=1)
        grade = np.degrees(np.arctan2(np.abs(np.diff(actual)), run))
        routes[name] = {
            "length_m": round(float(run.sum()), 2),
            "max_centerline_grade_deg": round(float(grade.max()), 2),
            "height_error_max_m": round(float(np.max(np.abs(actual - route[:, 2]))), 4),
            "control_points_xyz_m": PATHS[name],
        }
    metadata = {
        "name": "PALE HORSE — Подкова, terrain only",
        "resolution": [SIZE, SIZE], "world_size_m": [512, 512],
        "encoding": "z_m = (uint16 / 65535 - 0.5) * 64",
        "black_z_m": -32, "white_z_m": 32, "neutral_z_m": 0,
        "actual_height_range_m": [round(float(height.min()), 4), round(float(height.max()), 4)],
        "levels_m": [0, -12, -28], "png_top_edge": "+Y / north",
        "png_right_edge": "+X / east", "seed": SEED,
        "blender_main_displace": {"strength": 64, "midlevel": 0.5, "direction": "Z", "coordinates": "UV", "color_space": "Non-Color"},
        "blender_noise_displace": {"strength": 1, "midlevel": 0.5, "direction": "Z", "coordinates": "UV", "color_space": "Non-Color"},
        "structures_present": False,
        "pads": [{"id": p[0], "name": p[1], "center_xyz_m": [p[2], p[3], p[6]]} for p in PADS],
        "routes": routes,
    }
    (OUT / "terrain_metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"resolution": SIZE, "height_range_m": metadata["actual_height_range_m"],
                      "routes": {k: {a: b for a, b in v.items() if a != "control_points_xyz_m"} for k, v in routes.items()}}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
