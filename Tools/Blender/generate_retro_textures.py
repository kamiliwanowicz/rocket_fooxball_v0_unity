"""Deterministic PBR texture source for the Rocket Fooxball graphics pass.

The generator is intentionally dependency free.  It can run from Blender's
background Python (the normal invocation) or from CPython for fast auditing.
PNG encoding is done locally instead of through a Blender image datablock so
the source, channel layout, and compressed bytes stay identical between runs.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import struct
import zlib


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTURE_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Textures")
PREVIEW_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "RetroTextures")
SEED = 0xF00B411
MIB = 1024.0 * 1024.0


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def mix(a: float, b: float, amount: float) -> float:
    return a + (b - a) * amount


def smoothstep(edge0: float, edge1: float, value: float) -> float:
    if edge0 == edge1:
        return float(value >= edge1)
    t = clamp01((value - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


def u8(value: float) -> int:
    return int(round(clamp01(value) * 255.0))


def rgba_fill(width: int, height: int, colour=(0, 0, 0, 255)) -> bytearray:
    return bytearray(bytes(colour) * (width * height))


def rgba_set(buffer: bytearray, index: int, colour) -> None:
    offset = index * 4
    buffer[offset : offset + 4] = bytes(colour)


def rgba_get(buffer: bytearray, index: int):
    offset = index * 4
    return buffer[offset], buffer[offset + 1], buffer[offset + 2], buffer[offset + 3]


def close_repeat_edges(buffer: bytearray, width: int, height: int) -> None:
    """Duplicate opposite edges for textures sampled with Repeat wrapping."""
    row_bytes = width * 4
    for y in range(height):
        row = y * row_bytes
        buffer[row + (width - 1) * 4 : row + width * 4] = buffer[row : row + 4]
    last_row = (height - 1) * row_bytes
    buffer[last_row : last_row + row_bytes] = buffer[:row_bytes]


def _png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)


def encode_png(width: int, height: int, rgba: bytearray) -> bytes:
    if len(rgba) != width * height * 4:
        raise ValueError("RGBA buffer length does not match dimensions")
    # A fixed filter byte (None) keeps output independent of image-library heuristics.
    scanlines = bytearray()
    row_bytes = width * 4
    for y in range(height):
        scanlines.append(0)
        start = y * row_bytes
        scanlines.extend(rgba[start : start + row_bytes])
    compressed = zlib.compress(bytes(scanlines), level=9)
    signature = b"\x89PNG\r\n\x1a\n"
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return signature + _png_chunk(b"IHDR", header) + _png_chunk(b"IDAT", compressed) + _png_chunk(b"IEND", b"")


def save_png(name: str, width: int, height: int, rgba: bytearray) -> str:
    path = os.path.join(TEXTURE_DIRECTORY if name.startswith("Retro") else PREVIEW_DIRECTORY, name + ".png")
    if os.path.dirname(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(encode_png(width, height, rgba))
    if not os.path.isfile(path) or os.path.getsize(path) <= 0:
        raise RuntimeError(f"PNG write failed: {path}")
    return path


def periodic_noise(u: float, v: float, phases) -> float:
    value = 0.0
    weight = 0.0
    for fx, fy, phase, amplitude in phases:
        value += math.sin(math.tau * (fx * u + fy * v) + phase) * amplitude
        weight += amplitude
    return value / weight if weight else 0.0


def periodic_phases(seed: int, count: int, max_frequency=19):
    # Local LCG avoids Python-version-dependent random implementation details.
    state = seed & 0xFFFFFFFF
    phases = []
    for index in range(count):
        def step():
            nonlocal state
            state = (1664525 * state + 1013904223) & 0xFFFFFFFF
            return state

        fx = 1 + step() % max_frequency
        fy = 1 + step() % max_frequency
        phase = (step() / 4294967296.0) * math.tau
        phases.append((fx, fy, phase, 1.0 / (index + 1)))
    return phases


SURFACE_PHASES = {
    "grass": periodic_phases(SEED ^ 0x11, 7, 13),
    "wall": periodic_phases(SEED ^ 0x22, 7, 17),
    "trim": periodic_phases(SEED ^ 0x33, 6, 19),
    "hazard": periodic_phases(SEED ^ 0x44, 5, 11),
    "metal": periodic_phases(SEED ^ 0xA1, 7, 17),
    "dark": periodic_phases(SEED ^ 0xA2, 7, 17),
    "accent": periodic_phases(SEED ^ 0xA3, 7, 17),
}


def _surface_fields(kind: str, u: float, v: float):
    """Return base RGB, height, metallic, smoothness, AO for one tiled material."""
    u %= 1.0
    v %= 1.0
    phases = SURFACE_PHASES[kind]
    n = periodic_noise(u, v, phases)
    n01 = clamp01(0.5 + n * 0.50)
    panel = 0.0
    seam = 0.0
    if kind == "grass":
        stripe = 0.5 + 0.5 * math.sin(math.tau * (u * 8.0))
        panel = 1.0 if (u * 8.0) % 1.0 < 0.035 or (v * 8.0) % 1.0 < 0.035 else 0.0
        base = (mix(0.018, 0.055, n01), mix(0.20, 0.43, n01), mix(0.20, 0.36, n01))
        base = tuple(mix(value, value + 0.10, stripe * 0.16) for value in base)
        height = 0.47 + n * 0.06 - panel * 0.08
        return base, height, 0.04 + panel * 0.18, 0.48 + n01 * 0.18, 0.72 - panel * 0.20
    if kind == "wall":
        seam = 1.0 if (u * 16.0) % 1.0 < 0.028 or (v * 16.0) % 1.0 < 0.028 else 0.0
        base = (mix(0.46, 0.76, n01), mix(0.53, 0.80, n01), mix(0.51, 0.72, n01))
        base = tuple(mix(value, (0.06, 0.40, 0.47)[i], seam * 0.62) for i, value in enumerate(base))
        height = 0.50 + n * 0.07 - seam * 0.10
        return base, height, 0.10 + seam * 0.10, 0.46 + n01 * 0.20, 0.77 - seam * 0.24
    if kind == "trim":
        stripe = (u * 12.0 + v * 12.0) % 1.0
        edge = 1.0 if stripe < 0.12 else 0.0
        base = (mix(0.20, 0.56, n01), mix(0.12, 0.35, n01), mix(0.045, 0.17, n01))
        base = tuple(mix(value, (0.74, 0.48, 0.18)[i], edge * 0.38) for i, value in enumerate(base))
        height = 0.50 + n * 0.08 + edge * 0.06
        return base, height, 0.73 + edge * 0.15, 0.55 + n01 * 0.30, 0.86 - edge * 0.10
    if kind == "hazard":
        diagonal = (u * 10.0 + v * 10.0) % 1.0
        yellow = 1.0 if diagonal < 0.50 else 0.0
        dark = (0.045, 0.065, 0.073)
        bright = (0.95, 0.68, 0.08)
        base = tuple(mix(dark[i], bright[i], yellow) for i in range(3))
        base = tuple(clamp01(value * (0.90 + n01 * 0.14)) for value in base)
        height = 0.49 + n * 0.035 + (0.07 if yellow else -0.015)
        return base, height, 0.22 + yellow * 0.30, 0.46 + yellow * 0.24, 0.75 - (0.10 if yellow else 0.0)
    palettes = {
        "metal": ((0.12, 0.085, 0.055), (0.40, 0.29, 0.16), (0.70, 0.51, 0.27)),
        "dark": ((0.014, 0.020, 0.028), (0.065, 0.075, 0.086), (0.16, 0.17, 0.17)),
        "accent": ((0.16, 0.012, 0.008), (0.52, 0.040, 0.018), (0.90, 0.17, 0.028)),
    }
    shadow, base_colour, highlight = palettes[kind]
    value = round(n01 * 8.0) / 8.0
    base = tuple(mix(shadow[i], base_colour[i], value) for i in range(3))
    base = tuple(mix(value, highlight[i], max(0.0, value - 0.58) / 0.42) for i, value in enumerate(base))
    grid = 1.0 if (u * 16.0) % 1.0 < 0.025 or (v * 16.0) % 1.0 < 0.025 else 0.0
    base = tuple(mix(value, shadow[i], grid * 0.45) for i, value in enumerate(base))
    height = 0.50 + n * 0.09 + grid * 0.035
    metallic = {"metal": 0.82, "dark": 0.64, "accent": 0.52}[kind]
    return base, height, metallic, 0.64 + n01 * 0.25, 0.88 - grid * 0.16


def _surface_normal(kind: str, u: float, v: float):
    # Analytic, periodic slopes.  Every edge has the same tangent frame.
    frequency = {"grass": 8.0, "wall": 16.0, "trim": 12.0, "hazard": 10.0, "metal": 16.0, "dark": 16.0, "accent": 16.0}[kind]
    dx = 0.055 * math.cos(math.tau * frequency * u + 0.37) + 0.022 * math.sin(math.tau * (frequency * 0.5 * v + u))
    dy = 0.055 * math.sin(math.tau * frequency * v + 0.93) + 0.022 * math.cos(math.tau * (frequency * 0.5 * u - v))
    if kind == "hazard":
        dx *= 0.70
        dy *= 0.70
    strength = 1.15 if kind in ("grass", "wall") else 0.85
    return clamp01(0.5 - dx * strength), clamp01(0.5 - dy * strength), clamp01(1.0 - 0.45 * (abs(dx) + abs(dy)))


def generate_surface_maps(kind: str, width: int, height: int):
    # Author at 1024? then deterministic nearest-upsample weapon maps to 2048?.
    # This preserves the approved source resolution while keeping background
    # Blender generation practical on laptops.
    source_width = min(width, 1024)
    source_height = min(height, 1024)
    total = source_width * source_height
    base = bytearray(total * 4)
    normal = bytearray(total * 4)
    metallic = bytearray(total * 4)
    occlusion = bytearray(total * 4)
    for y in range(source_height):
        v = y / float(source_height - 1)
        for x in range(source_width):
            u = x / float(source_width - 1)
            colour, _height, metal, smooth, ao = _surface_fields(kind, u, v)
            nx, ny, nz = _surface_normal(kind, u, v)
            index = y * source_width + x
            rgba_set(base, index, (u8(colour[0]), u8(colour[1]), u8(colour[2]), 255))
            rgba_set(normal, index, (u8(nx), u8(ny), u8(nz), 255))
            rgba_set(metallic, index, (u8(metal), 0, 0, u8(smooth)))
            rgba_set(occlusion, index, (u8(ao), u8(ao), u8(ao), 255))
    for buffer in (base, normal, metallic, occlusion):
        close_repeat_edges(buffer, source_width, source_height)
    if (source_width, source_height) != (width, height):
        return tuple(resize_nearest(buffer, source_width, source_height, width, height) for buffer in (base, normal, metallic, occlusion))
    return base, normal, metallic, occlusion


def generate_detail_normal(width=512, height=512):
    buffer = bytearray(width * height * 4)
    for y in range(height):
        v = y / float(height - 1)
        for x in range(width):
            u = x / float(width - 1)
            dx = 0.035 * math.cos(math.tau * (u * 33.0 + v * 7.0))
            dy = 0.035 * math.sin(math.tau * (v * 29.0 - u * 5.0))
            rgba_set(buffer, y * width + x, (u8(0.5 - dx), u8(0.5 - dy), u8(1.0 - abs(dx) - abs(dy)), 255))
    close_repeat_edges(buffer, width, height)
    return buffer


def normalized(vector):
    length = math.sqrt(sum(component * component for component in vector))
    return tuple(component / length for component in vector)


def dot3(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross3(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def add3(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def scale3(a, value):
    return (a[0] * value, a[1] * value, a[2] * value)


def ball_centers():
    phi = (1.0 + math.sqrt(5.0)) / 2.0
    raw = []
    for y in (-1.0, 1.0):
        for z in (-phi, phi):
            raw.append((0.0, y, z))
    for x in (-1.0, 1.0):
        for y in (-phi, phi):
            raw.append((x, y, 0.0))
    for x in (-phi, phi):
        for z in (-1.0, 1.0):
            raw.append((x, 0.0, z))
    centers = [normalized(vector) for vector in raw]
    # Deterministic nearest-neighbour tangent frames.  Ties are resolved by list order.
    frames = []
    for center in centers:
        neighbours = sorted(
            ((math.acos(max(-1.0, min(1.0, dot3(center, other)))), index, other) for index, other in enumerate(centers) if other != center),
            key=lambda item: (item[0], item[1]),
        )
        neighbour = neighbours[0][2]
        tangent_u = normalized(add3(neighbour, scale3(center, -dot3(neighbour, center))))
        tangent_v = normalized(cross3(center, tangent_u))
        frames.append((center, tangent_u, tangent_v))
    return frames


def regular_polygon_boundary(radius, angle, sides=5):
    sector = math.tau / sides
    offset = (angle + sector * 0.5) % sector - sector * 0.5
    return radius * math.cos(math.pi / sides) / max(0.20, math.cos(offset))


def ball_sample(point, frames):
    nearest_index = max(range(len(frames)), key=lambda index: dot3(point, frames[index][0]))
    center, tangent_u, tangent_v = frames[nearest_index]
    cosine = max(-1.0, min(1.0, dot3(point, center)))
    angular = math.acos(cosine)
    local_angle = math.atan2(dot3(point, tangent_v), dot3(point, tangent_u))
    panel_radius = regular_polygon_boundary(0.29, local_angle)
    seam = panel_radius < angular <= panel_radius + 0.032
    panel = angular <= panel_radius
    grain = 0.5 + 0.5 * (0.60 * math.sin(point[0] * 31.0 + point[2] * 17.0) + 0.40 * math.sin(point[1] * 47.0 - point[2] * 13.0))
    if panel:
        base = (0.008 + grain * 0.005, 0.010 + grain * 0.006, 0.014 + grain * 0.008)
        height = 0.39
        smooth = 0.29
        ao = 0.60
    elif seam:
        base = (0.10, 0.105, 0.11)
        height = 0.43
        smooth = 0.34
        ao = 0.53
    else:
        base = (0.86 + grain * 0.08, 0.87 + grain * 0.075, 0.83 + grain * 0.07)
        height = 0.50 + grain * 0.015
        smooth = 0.42 + grain * 0.16
        ao = 0.89
    normal = (0.5 + 0.017 * math.sin(point[2] * 41.0), 0.5 + 0.017 * math.cos(point[0] * 37.0), 0.985)
    return base, normal, 0.0, smooth, ao, panel


def sphere_point(latitude, longitude):
    cos_lat = math.cos(latitude)
    return (cos_lat * math.cos(longitude), math.sin(latitude), cos_lat * math.sin(longitude))


def generate_ball_maps(width=1024, height=512):
    total = width * height
    base = bytearray(total * 4)
    normal = bytearray(total * 4)
    metallic = bytearray(total * 4)
    occlusion = bytearray(total * 4)
    frames = ball_centers()
    for y in range(height):
        latitude = -math.pi / 2.0 + math.pi * y / float(height - 1)
        for x in range(width):
            longitude = -math.pi + math.tau * x / float(width - 1)
            point = sphere_point(latitude, longitude)
            colour, normal_value, metal, smooth, ao, _panel = ball_sample(point, frames)
            index = y * width + x
            rgba_set(base, index, (u8(colour[0]), u8(colour[1]), u8(colour[2]), 255))
            rgba_set(normal, index, (u8(normal_value[0]), u8(normal_value[1]), u8(normal_value[2]), 255))
            rgba_set(metallic, index, (u8(metal), 0, 0, u8(smooth)))
            rgba_set(occlusion, index, (u8(ao), u8(ao), u8(ao), 255))
    # Endpoint duplication and uniform pole rows are explicit, not an emergent float result.
    for buffer in (base, normal, metallic, occlusion):
        close_repeat_edges(buffer, width, height)
        for y in (0, height - 1):
            row = y * width * 4
            first = bytes(buffer[row : row + 4])
            for x in range(1, width):
                buffer[row + x * 4 : row + x * 4 + 4] = first
    return base, normal, metallic, occlusion, frames


def _rocket_region(x, y, width=1024, height=1024):
    # (name, interior rectangle) with 32 px gutters on every side.
    if y < 512:
        return "body" if x < 512 else "hot"
    return "fins" if x < 768 else "nozzle"


def _rocket_region_uv(region, x, y):
    rectangles = {
        "body": (0, 0, 512, 1024),
        "hot": (512, 0, 1024, 512),
        "fins": (512, 512, 768, 1024),
        "nozzle": (768, 512, 1024, 1024),
    }
    left, bottom, right, top = rectangles[region]
    inner_left, inner_bottom = left + 32, bottom + 32
    inner_right, inner_top = right - 32, top - 32
    cx = min(max(x, inner_left), inner_right - 1)
    cy = min(max(y, inner_bottom), inner_top - 1)
    return (cx - inner_left) / max(1.0, inner_right - inner_left - 1), (cy - inner_bottom) / max(1.0, inner_top - inner_bottom - 1)


def _rocket_fields(region, u, v):
    grain = 0.5 + 0.5 * math.sin(math.tau * (u * 7.0 + v * 11.0))
    panel = 1.0 if (u * 8.0) % 1.0 < 0.055 or (v * 10.0) % 1.0 < 0.045 else 0.0
    if region == "body":
        body = (0.29 + grain * 0.12, 0.22 + grain * 0.09, 0.13 + grain * 0.055)
        charcoal = (0.055, 0.062, 0.066)
        base = tuple(mix(body[i], charcoal[i], panel * 0.72) for i in range(3))
        return base, 0.50 + grain * 0.08 - panel * 0.08, 0.78 - panel * 0.22, 0.78 - panel * 0.10, (0.47, 0.50, 0.53)
    if region == "hot":
        radial = math.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
        red = (0.55 + grain * 0.18, 0.035 + grain * 0.025, 0.012)
        orange = (0.96, 0.19, 0.018)
        base = tuple(mix(red[i], orange[i], smoothstep(0.58, 0.12, radial)) for i in range(3))
        emission = (1.0, 0.20 + 0.55 * smoothstep(0.48, 0.0, radial), 0.02)
        return base, 0.48 + grain * 0.06, 0.32, 0.62, emission
    if region == "fins":
        base = (0.38 + grain * 0.25, 0.018 + grain * 0.035, 0.012 + grain * 0.016)
        return base, 0.46 + grain * 0.04, 0.58, 0.58, (0.08, 0.005, 0.001)
    radial = math.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
    base = (0.025 + grain * 0.025, 0.030 + grain * 0.028, 0.032 + grain * 0.030)
    throat = smoothstep(0.24, 0.03, radial)
    base = (mix(base[0], 0.18, throat), mix(base[1], 0.08, throat), mix(base[2], 0.018, throat))
    emission = (1.0, 0.28 + 0.55 * throat, 0.02 + 0.08 * throat)
    return base, 0.42 + grain * 0.06, 0.91, 0.72, emission


def generate_rocket_maps(width=1024, height=1024):
    total = width * height
    outputs = {key: bytearray(total * 4) for key in ("base", "normal", "metallic", "occlusion", "emission")}
    for y in range(height):
        for x in range(width):
            region = _rocket_region(x, y, width, height)
            u, v = _rocket_region_uv(region, x, y)
            colour, _height, metal, smooth, emission = _rocket_fields(region, u, v)
            nx = clamp01(0.5 + 0.035 * math.sin(math.tau * (u * 5.0 + v)))
            ny = clamp01(0.5 + 0.035 * math.cos(math.tau * (v * 6.0 - u)))
            nz = 0.985
            ao = 0.76 if region in ("fins", "nozzle") else 0.85
            index = y * width + x
            rgba_set(outputs["base"], index, (u8(colour[0]), u8(colour[1]), u8(colour[2]), 255))
            rgba_set(outputs["normal"], index, (u8(nx), u8(ny), u8(nz), 255))
            rgba_set(outputs["metallic"], index, (u8(metal), 0, 0, u8(smooth)))
            rgba_set(outputs["occlusion"], index, (u8(ao), u8(ao), u8(ao), 255))
            rgba_set(outputs["emission"], index, (u8(emission[0]), u8(emission[1]), u8(emission[2]), 255))
    return outputs


def generate_shield(width=128, height=128):
    buffer = bytearray(width * height * 4)
    for y in range(height):
        for x in range(width):
            u = (x + 0.5) / 16.0
            v = (y + 0.5) / 16.0
            fu = u - math.floor(u)
            fv_a = (v + u * 0.58) - math.floor(v + u * 0.58)
            fv_b = (v - u * 0.58) - math.floor(v - u * 0.58)
            line_distance = min(min(fu, 1.0 - fu), min(fv_a, 1.0 - fv_a), min(fv_b, 1.0 - fv_b))
            line = 1.0 - smoothstep(0.015, 0.105, line_distance)
            pulse = 0.5 + 0.5 * math.sin(math.tau * (u * 0.13 + v * 0.17))
            alpha = clamp01(0.035 + line * (0.76 + 0.18 * pulse))
            colour = (clamp01(0.44 + 0.18 * pulse + 0.22 * line), clamp01(0.82 + 0.12 * pulse), clamp01(0.92 + 0.08 * line), alpha)
            rgba_set(buffer, y * width + x, tuple(u8(component) for component in colour[:3]) + (u8(alpha),))
    return buffer


def generate_glow(width=128, height=128):
    buffer = bytearray(width * height * 4)
    for y in range(height):
        for x in range(width):
            nx = (x + 0.5 - width / 2.0) / (width / 2.0)
            ny = (y + 0.5 - height / 2.0) / (height / 2.0)
            radius = math.sqrt(nx * nx + ny * ny)
            core = 1.0 - smoothstep(0.0, 0.27, radius)
            ring = smoothstep(0.18, 0.30, radius) * (1.0 - smoothstep(0.30, 0.60, radius))
            halo = 1.0 - smoothstep(0.36, 0.98, radius)
            alpha = clamp01(core * 0.96 + ring * 0.72 + halo * 0.22)
            colour = (1.0, clamp01(core * 0.96 + ring * 0.80 + halo * 0.22), clamp01(core * 0.82 + ring * 0.18), alpha)
            rgba_set(buffer, y * width + x, (u8(colour[0]), u8(colour[1]), u8(colour[2]), u8(alpha)))
    return buffer


def generate_sprite_sheet(kind: str, width=128, height=128):
    buffer = bytearray(width * height * 4)
    for cell_y in range(4):
        for cell_x in range(4):
            variant = cell_y * 4 + cell_x
            phase = (0.61 if kind == "explosion" else 1.73) + variant * 0.79
            edge_base = (0.79 if kind == "explosion" else 0.84) + 0.025 * math.sin(variant * 1.9)
            for py in range(32):
                for px in range(32):
                    nx = (px + 0.5 - 16.0) / 16.0
                    ny = (py + 0.5 - 16.0) / 16.0
                    radius = math.sqrt(nx * nx + ny * ny)
                    angle = math.atan2(ny, nx)
                    irregular = 0.055 * math.sin(5.0 * angle + phase) + 0.030 * math.sin(9.0 * angle - phase * 1.7)
                    edge = edge_base + irregular
                    if kind == "explosion":
                        alpha = 1.0 - smoothstep(edge - 0.20, edge, radius)
                        core = smoothstep(0.33, 0.0, radius)
                        body = smoothstep(0.58, 0.08, radius)
                        edge_mix = smoothstep(edge, 0.46, radius)
                        colour = (
                            1.0,
                            clamp01(0.99 * core + 0.82 * body + 0.48 * edge_mix),
                            clamp01(0.80 * core + 0.12 * body + 0.025 * edge_mix),
                        )
                    else:
                        puff = 0.5 + 0.5 * (0.62 * math.sin(5.0 * angle + phase) + 0.38 * math.sin(9.0 * angle - phase * 1.6))
                        edge += 0.07 * (puff - 0.5)
                        alpha = 1.0 - smoothstep(0.12, edge, radius)
                        density = clamp01(1.0 - radius / max(0.25, edge))
                        colour = (
                            mix(0.055, 0.40, density),
                            mix(0.062, 0.39, density),
                            mix(0.070, 0.37, density),
                        )
                    index = (cell_y * 32 + py) * width + cell_x * 32 + px
                    rgba_set(buffer, index, (u8(colour[0]), u8(colour[1]), u8(colour[2]), u8(alpha)))
    return buffer


def generate_sky(width=2048, height=1024):
    buffer = bytearray(width * height * 4)
    cloud_centres = ((0.16, 0.68, 0.095, 0.060), (0.34, 0.76, 0.120, 0.055), (0.58, 0.64, 0.085, 0.065), (0.79, 0.78, 0.115, 0.050), (0.91, 0.60, 0.070, 0.050))
    for y in range(height):
        v = y / float(height - 1)
        sky_t = smoothstep(0.0, 1.0, v)
        sky = (mix(0.73, 0.30, sky_t), mix(0.86, 0.58, sky_t), mix(0.95, 0.86, sky_t))
        for x in range(width):
            u = x / float(width - 1)
            coverage = 0.0
            for cx, cy, sx, sy in cloud_centres:
                du = abs(u - cx)
                du = min(du, 1.0 - du)
                coverage = max(coverage, math.exp(-((du / sx) ** 2 + ((v - cy) / sy) ** 2) * 1.7))
            coverage *= 0.22
            colour = tuple(mix(sky[i], (0.96, 0.95, 0.89)[i], smoothstep(0.02, 0.20, coverage)) for i in range(3))
            rgba_set(buffer, y * width + x, (u8(colour[0]), u8(colour[1]), u8(colour[2]), 255))
    # U-repeat only; horizon and zenith remain distinct.
    for y in range(height):
        row = y * width * 4
        buffer[row + (width - 1) * 4 : row + width * 4] = buffer[row : row + 4]
    return buffer


def resize_nearest(source: bytearray, source_width: int, source_height: int, width: int, height: int) -> bytearray:
    output = bytearray(width * height * 4)
    for y in range(height):
        sy = min(source_height - 1, int(y * source_height / float(height)))
        for x in range(width):
            sx = min(source_width - 1, int(x * source_width / float(width)))
            rgba_set(output, y * width + x, rgba_get(source, sy * source_width + sx))
    return output


def checker(width, height, cell=12):
    output = bytearray(width * height * 4)
    for y in range(height):
        for x in range(width):
            value = (0.10, 0.13, 0.18, 1.0) if ((x // cell + y // cell) % 2) else (0.42, 0.48, 0.54, 1.0)
            rgba_set(output, y * width + x, tuple(u8(component) for component in value[:3]) + (255,))
    return output


def render_pbr_sphere(base, normal, metallic, occlusion, source_width, source_height, size=256, rotation=0.0):
    output = checker(size, size, max(12, size // 14))
    light = normalized((0.55, 0.75, 0.50))
    for y in range(size):
        ny = (y + 0.5 - size / 2.0) / (size / 2.0)
        for x in range(size):
            nx = (x + 0.5 - size / 2.0) / (size / 2.0)
            radius2 = nx * nx + ny * ny
            if radius2 > 1.0:
                continue
            nz = math.sqrt(1.0 - radius2)
            point = (nx, -ny, nz)
            longitude = (math.atan2(point[2], point[0]) + rotation) % math.tau
            latitude = math.asin(max(-1.0, min(1.0, point[1])))
            sx = int((longitude / math.tau) * (source_width - 1))
            sy = int(((latitude + math.pi / 2.0) / math.pi) * (source_height - 1))
            index = sy * source_width + sx
            br, bg, bb, _ = rgba_get(base, index)
            nr, ng, nb, _ = rgba_get(normal, index)
            mr, _mg, _mb, smooth = rgba_get(metallic, index)
            ar, _ag, _ab, _aa = rgba_get(occlusion, index)
            n = normalized(((nr / 127.5 - 1.0) * 0.65 + point[0], (ng / 127.5 - 1.0) * 0.65 + point[1], nb / 255.0))
            diffuse = max(0.14, dot3(n, light))
            metal = mr / 255.0
            gloss = smooth / 255.0
            highlight = (max(0.0, dot3(n, light)) ** (8.0 + gloss * 44.0)) * (0.25 + 0.65 * metal)
            ao = 0.56 + 0.44 * ar / 255.0
            colour = tuple(clamp01((channel / 255.0) * (0.28 + diffuse * 0.84) * ao + highlight) for channel in (br, bg, bb))
            rgba_set(output, y * size + x, (u8(colour[0]), u8(colour[1]), u8(colour[2]), 255))
    return output


def ball_cardinals(base, width=1024, height=512):
    size = 192
    output = bytearray(size * 3 * size * 2 * 4)
    views = ((0.0, 0.0), (math.pi, 0.0), (math.pi / 2.0, 0.0), (-math.pi / 2.0, 0.0), (0.0, math.pi / 2.0), (0.0, -math.pi / 2.0))
    for view_index, (yaw, pitch) in enumerate(views):
        tile_x = (view_index % 3) * size
        tile_y = (view_index // 3) * size
        for y in range(size):
            ny = (y + 0.5 - size / 2.0) / (size / 2.0)
            for x in range(size):
                nx = (x + 0.5 - size / 2.0) / (size / 2.0)
                radius2 = nx * nx + ny * ny
                if radius2 > 1.0:
                    rgba_set(output, (tile_y + y) * size * 3 + tile_x + x, (18, 22, 30, 255))
                    continue
                nz = math.sqrt(1.0 - radius2)
                # Rotate the visible hemisphere around Y then X for true cardinal views.
                px, py, pz = nx, -ny, nz
                cy, sy = math.cos(yaw), math.sin(yaw)
                px, pz = px * cy + pz * sy, -px * sy + pz * cy
                cp, sp = math.cos(pitch), math.sin(pitch)
                py, pz = py * cp - pz * sp, py * sp + pz * cp
                longitude = (math.atan2(pz, px) + math.tau) % math.tau
                latitude = math.asin(max(-1.0, min(1.0, py)))
                sx = int((longitude / math.tau) * (width - 1))
                sy_index = int(((latitude + math.pi / 2.0) / math.pi) * (height - 1))
                rgba_set(output, (tile_y + y) * size * 3 + tile_x + x, rgba_get(base, sy_index * width + sx))
    return 3 * size, 2 * size, output


def alpha_overlay(source: bytearray, width: int, height: int, scale=3):
    output_width, output_height = width * scale, height * scale
    output = checker(output_width, output_height, max(8, scale * 5))
    for y in range(height):
        for x in range(width):
            sr, sg, sb, sa = rgba_get(source, y * width + x)
            for oy in range(scale):
                for ox in range(scale):
                    index = (y * scale + oy) * output_width + x * scale + ox
                    under = rgba_get(output, index)
                    alpha = sa / 255.0
                    rgba_set(output, index, (u8(mix(under[0] / 255.0, sr / 255.0, alpha)), u8(mix(under[1] / 255.0, sg / 255.0, alpha)), u8(mix(under[2] / 255.0, sb / 255.0, alpha)), 255))
    return output_width, output_height, output


def audit_texture(name, width, height, rgba, expected, seam_u=False, seam_v=False, poles=False, alpha_range=None, map_type="base"):
    if (width, height) != expected or len(rgba) != width * height * 4:
        raise RuntimeError(f"{name}: dimensions/channel mismatch")
    channels = [[rgba[index * 4 + channel] for index in range(width * height)] for channel in range(4)]
    channel_ranges = [(min(values), max(values)) for values in channels]
    if any(not (0 <= minimum <= maximum <= 255) for minimum, maximum in channel_ranges):
        raise RuntimeError(f"{name}: invalid channel range")
    if map_type not in ("metallic", "ao", "normal") and max(channel_ranges[0][1], channel_ranges[1][1], channel_ranges[2][1]) - min(channel_ranges[0][0], channel_ranges[1][0], channel_ranges[2][0]) < 8:
        raise RuntimeError(f"{name}: flat colour range")
    seam_u_error = 0
    seam_v_error = 0
    pole_error = 0
    if seam_u:
        seam_u_error = max(abs(rgba_get(rgba, y * width)[channel] - rgba_get(rgba, y * width + width - 1)[channel]) for y in range(height) for channel in range(4))
        if seam_u_error:
            raise RuntimeError(f"{name}: U seam mismatch {seam_u_error}")
    if seam_v:
        seam_v_error = max(abs(rgba_get(rgba, x)[channel] - rgba_get(rgba, (height - 1) * width + x)[channel]) for x in range(width) for channel in range(4))
        if seam_v_error:
            raise RuntimeError(f"{name}: V seam mismatch {seam_v_error}")
    if poles:
        for row in (0, height - 1):
            reference = rgba_get(rgba, row * width)
            pole_error = max(pole_error, max(abs(rgba_get(rgba, row * width + x)[channel] - reference[channel]) for x in range(width) for channel in range(4)))
        if pole_error:
            raise RuntimeError(f"{name}: pole mismatch {pole_error}")
    if alpha_range is not None:
        minimum, maximum = alpha_range
        if channel_ranges[3][0] > minimum or channel_ranges[3][1] < maximum:
            raise RuntimeError(f"{name}: alpha range {channel_ranges[3]} outside {alpha_range}")
    print(f"AUDIT {name}: {width}x{height} RGBA8; R={channel_ranges[0][0]}..{channel_ranges[0][1]} G={channel_ranges[1][0]}..{channel_ranges[1][1]} B={channel_ranges[2][0]}..{channel_ranges[2][1]} A={channel_ranges[3][0]}..{channel_ranges[3][1]} seamU={seam_u_error} seamV={seam_v_error} poles={pole_error}")
    return {"dimensions": [width, height], "channels": "RGBA8", "ranges": channel_ranges, "seam_u_error": seam_u_error, "seam_v_error": seam_v_error, "pole_error": pole_error, "map_type": map_type}


def estimate_compressed_memory(outputs):
    # Conservative platform estimate: normals use BC5 (16-byte blocks),
    # colour/mask/VFX maps use BC4/BC1/BC3-class 8-byte blocks. Full mip
    # residency is approximately 4/3 of level zero.
    total = 0.0
    for entry in outputs.values():
        width, height = entry["dimensions"]
        blocks = math.ceil(width / 4.0) * math.ceil(height / 4.0)
        map_type = entry.get("map_type")
        block_bytes = 16.0 if map_type in ("normal", "metallic", "vfx") else 8.0
        total += blocks * block_bytes * 4.0 / 3.0
    return total / MIB


def make_previews(outputs):
    previews = {}

    def add(name, width, height, rgba):
        path = save_png(name, width, height, rgba)
        previews[name + ".png"] = {"path": path, "dimensions": [width, height], "sha256": hashlib.sha256(encode_png(width, height, rgba)).hexdigest()}

    for index, family in enumerate(("RetroGrass", "RetroWall", "RetroTrim", "RetroHazard"), 1):
        base = outputs[family]["buffer"]
        add(f"{index:02d}_{family.lower()}_tile", 256, 256, resize_nearest(base, outputs[family]["dimensions"][0], outputs[family]["dimensions"][1], 256, 256))
        pbr = render_pbr_sphere(base, outputs[family + "_Normal"]["buffer"], outputs[family + "_MetallicSmoothness"]["buffer"], outputs[family + "_Occlusion"]["buffer"], outputs[family]["dimensions"][0], outputs[family]["dimensions"][1], 256, index * 0.57)
        add(f"{index:02d}_{family.lower()}_pbr_ball", 256, 256, pbr)
    add("05_detail_normal", 256, 256, resize_nearest(outputs["RetroDetailNormal"]["buffer"], 512, 512, 256, 256))

    ball = outputs["RetroBall"]
    pbr_ball = render_pbr_sphere(ball["buffer"], outputs["RetroBall_Normal"]["buffer"], outputs["RetroBall_MetallicSmoothness"]["buffer"], outputs["RetroBall_Occlusion"]["buffer"], 1024, 512, 256, 0.24)
    add("06_ball_pbr", 256, 256, pbr_ball)
    card_w, card_h, card = ball_cardinals(ball["buffer"])
    add("07_ball_cardinals", card_w, card_h, card)

    rocket = outputs["RetroRocket"]
    add("08_rocket_atlas", 512, 512, resize_nearest(rocket["buffer"], 1024, 1024, 512, 512))
    add("09_rocket_pbr_ball", 256, 256, render_pbr_sphere(rocket["buffer"], outputs["RetroRocket_Normal"]["buffer"], outputs["RetroRocket_MetallicSmoothness"]["buffer"], outputs["RetroRocket_Occlusion"]["buffer"], 1024, 1024, 256, 0.71))
    add("10_rocket_emission", 512, 512, resize_nearest(outputs["RetroRocket_Emission"]["buffer"], 1024, 1024, 512, 512))
    for index, family in enumerate(("RetroWeaponMetal", "RetroWeaponDark", "RetroWeaponAccent"), 11):
        add(f"{index:02d}_{family.lower()}_tile", 256, 256, resize_nearest(outputs[family]["buffer"], 2048, 2048, 256, 256))
        add(f"{index:02d}_{family.lower()}_pbr_ball", 256, 256, render_pbr_sphere(outputs[family]["buffer"], outputs[family + "_Normal"]["buffer"], outputs[family + "_MetallicSmoothness"]["buffer"], outputs[family + "_Occlusion"]["buffer"], 2048, 2048, 256, index * 0.22))

    glow_w, glow_h, glow_preview = alpha_overlay(outputs["RetroRocketGlow"]["buffer"], 128, 128, 3)
    add("17_rocket_glow_alpha", glow_w, glow_h, glow_preview)
    exp_w, exp_h, exp_preview = alpha_overlay(outputs["RetroExplosion"]["buffer"], 128, 128, 2)
    add("18_explosion_sheet_alpha", exp_w, exp_h, exp_preview)
    smoke_w, smoke_h, smoke_preview = alpha_overlay(outputs["RetroSmoke"]["buffer"], 128, 128, 2)
    add("19_smoke_sheet_alpha", smoke_w, smoke_h, smoke_preview)
    add("20_shield_alpha", 256, 256, alpha_overlay(outputs["RetroShield"]["buffer"], 128, 128, 2)[2])
    add("21_sunny_sky_panorama", 512, 256, resize_nearest(outputs["RetroSunnySky"]["buffer"], 2048, 1024, 512, 256))
    return previews


def main():
    os.makedirs(TEXTURE_DIRECTORY, exist_ok=True)
    os.makedirs(PREVIEW_DIRECTORY, exist_ok=True)
    generated = {}

    def add(name, width, height, buffer, map_type="base", seam_u=False, seam_v=False, poles=False, alpha_range=None):
        audit = audit_texture(name, width, height, buffer, (width, height), seam_u, seam_v, poles, alpha_range, map_type)
        path = save_png(name, width, height, buffer)
        generated[name] = {"path": path, "dimensions": [width, height], "channels": "RGBA8", "buffer": buffer, "map_type": map_type, "sha256": hashlib.sha256(encode_png(width, height, buffer)).hexdigest(), "audit": audit}

    arena_specs = (("RetroGrass", "grass"), ("RetroWall", "wall"), ("RetroTrim", "trim"), ("RetroHazard", "hazard"))
    for name, kind in arena_specs:
        base, normal, metallic, ao = generate_surface_maps(kind, 1024, 1024)
        add(name, 1024, 1024, base, "base", True, True)
        add(name + "_Normal", 1024, 1024, normal, "normal", True, True)
        add(name + "_MetallicSmoothness", 1024, 1024, metallic, "metallic", True, True)
        add(name + "_Occlusion", 1024, 1024, ao, "ao", True, True)
    add("RetroDetailNormal", 512, 512, generate_detail_normal(), "normal", True, True)

    for name, kind in (("RetroWeaponMetal", "metal"), ("RetroWeaponDark", "dark"), ("RetroWeaponAccent", "accent")):
        base, normal, metallic, ao = generate_surface_maps(kind, 2048, 2048)
        add(name, 2048, 2048, base, "base", True, True)
        add(name + "_Normal", 2048, 2048, normal, "normal", True, True)
        add(name + "_MetallicSmoothness", 2048, 2048, metallic, "metallic", True, True)
        add(name + "_Occlusion", 2048, 2048, ao, "ao", True, True)
        if kind == "accent":
            emission = bytearray(2048 * 2048 * 4)
            for index in range(2048 * 2048):
                r, g, b, _ = rgba_get(base, index)
                bright = 1.0 if (index % 2048) % 96 in range(48, 56) else 0.0
                rgba_set(emission, index, (r if bright else 0, g if bright else 0, b if bright else 0, 255))
            close_repeat_edges(emission, 2048, 2048)
            add(name + "_Emission", 2048, 2048, emission, "emission", True, True)

    ball_base, ball_normal, ball_metallic, ball_ao, frames = generate_ball_maps()
    add("RetroBall", 1024, 512, ball_base, "base", True, False, True)
    add("RetroBall_Normal", 1024, 512, ball_normal, "normal", True, False, True)
    add("RetroBall_MetallicSmoothness", 1024, 512, ball_metallic, "metallic", True, False, True)
    add("RetroBall_Occlusion", 1024, 512, ball_ao, "ao", True, False, True)

    rocket_maps = generate_rocket_maps()
    add("RetroRocket", 1024, 1024, rocket_maps["base"], "base")
    add("RetroRocket_Normal", 1024, 1024, rocket_maps["normal"], "normal")
    add("RetroRocket_MetallicSmoothness", 1024, 1024, rocket_maps["metallic"], "metallic")
    add("RetroRocket_Occlusion", 1024, 1024, rocket_maps["occlusion"], "ao")
    add("RetroRocket_Emission", 1024, 1024, rocket_maps["emission"], "emission")

    add("RetroRocketGlow", 128, 128, generate_glow(), "vfx", alpha_range=(0, 128))
    add("RetroSmoke", 128, 128, generate_sprite_sheet("smoke"), "vfx", alpha_range=(0, 128))
    add("RetroExplosion", 128, 128, generate_sprite_sheet("explosion"), "vfx", alpha_range=(0, 128))
    add("RetroShield", 128, 128, generate_shield(), "vfx", alpha_range=(16, 220))
    add("RetroSunnySky", 2048, 1024, generate_sky(), "sky", True, False)

    previews = make_previews(generated)
    outputs = {
        name: {key: value for key, value in entry.items() if key != "buffer"}
        for name, entry in generated.items()
    }
    memory_mib = estimate_compressed_memory(outputs)
    manifest = {
        "generator": "Tools/Blender/generate_retro_textures.py",
        "seed": SEED,
        "outputs": outputs,
        "previews": previews,
        "counts": {"outputs": len(outputs), "previews": len(previews)},
        "memory_forecast": {
            "format": "BC1/BC4-like 8-byte blocks for opaque base/AO/emission/sky; BC5/BC7/BC3-like 16-byte blocks for normal/metallic/VFX; full mips ~= 4/3",
            "compressed_mib": round(memory_mib, 3),
            "budget_mib": 96.0,
            "within_budget": memory_mib <= 96.0,
        },
        "ball": {"centers": [list(frame[0]) for frame in frames], "center_count": len(frames), "unit_tolerance": 1e-6, "u_wrap": True, "v_wrap": False},
        "status": "PASS" if memory_mib <= 96.0 else "FAIL",
    }
    manifest_path = os.path.join(PREVIEW_DIRECTORY, "retro_texture_manifest.json")
    with open(manifest_path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(f"MEMORY RetroTextures: compressed forecast {memory_mib:.3f} MiB / 96.000 MiB")
    print(f"AUDIT RetroTextures: PASS ({len(generated)} outputs, {len(previews)} previews, deterministic seed {SEED})")


if __name__ == "__main__":
    main()
