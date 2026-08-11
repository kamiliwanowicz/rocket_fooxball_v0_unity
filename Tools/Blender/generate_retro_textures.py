"""Deterministic PBR texture source for the Rocket Fooxball graphics pass.

The generator requires NumPy and can run from Blender's background Python (the
normal invocation) or from CPython for fast auditing.
PNG encoding is done locally instead of through a Blender image datablock so
the source, channel layout, and compressed bytes stay identical between runs.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import math
import os
import platform
import struct
import time
import zlib

try:
    import numpy as np
except ImportError as exc:  # pragma: no cover - exercised by Blender installs without NumPy
    raise RuntimeError(
        "Retro texture generation requires NumPy. Install NumPy in the active "
        "CPython/Blender Python environment (for example: python -m pip install numpy)."
    ) from exc


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTURE_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Textures")
PREVIEW_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "RetroTextures")
SEED = 0xF00B411
MIB = 1024.0 * 1024.0
BALL_PANEL_RADIUS = 0.29
BALL_SEAM_WIDTH = 0.032
BALL_NORMAL_SCALE = 0.05
ROCKET_GUTTER_PX = 32
ROCKET_ATLAS_REGIONS = {
    # UV bounds use half-open intervals. Pixel bounds derive from these values.
    "body": (0.00, 0.50, 0.00, 1.00),
    "hot": (0.50, 1.00, 0.00, 0.50),
    "fins": (0.50, 0.75, 0.50, 1.00),
    "nozzle": (0.75, 1.00, 0.50, 1.00),
}


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


def _u8_array(values):
    """Apply scalar ``u8`` round/clamp semantics to a float64 array."""
    values = np.asarray(values, dtype=np.float64)
    return np.rint(np.clip(values, 0.0, 1.0) * 255.0).astype(np.uint8)


def _smoothstep_array(edge0: float, edge1: float, values):
    values = np.asarray(values, dtype=np.float64)
    edge0 = np.asarray(edge0, dtype=np.float64)
    edge1 = np.asarray(edge1, dtype=np.float64)
    equal = edge0 == edge1
    with np.errstate(divide="ignore", invalid="ignore"):
        t = np.clip((values - edge0) / (edge1 - edge0), 0.0, 1.0)
    stepped = t * t * (3.0 - 2.0 * t)
    return np.where(equal, np.where(values >= edge1, 1.0, 0.0), stepped)


def _periodic_noise_array(kind: str, u, v):
    """Vectorized periodic noise; phase/summation order matches ``periodic_noise``."""
    u = np.asarray(u, dtype=np.float64)
    v = np.asarray(v, dtype=np.float64)
    value = np.zeros(np.broadcast_shapes(u.shape, v.shape), dtype=np.float64)
    weight = 0.0
    for fx, fy, phase, amplitude in SURFACE_PHASES[kind]:
        value += np.sin(np.float64(math.tau) * (np.float64(fx) * u + np.float64(fy) * v) + np.float64(phase)) * np.float64(amplitude)
        weight += amplitude
    return value / np.float64(weight) if weight else value


def _rgba_array(width: int, height: int, fill=(0, 0, 0, 255)):
    """Allocate the canonical C-order uint8 RGBA storage used by every kernel."""
    array = np.empty((height, width, 4), dtype=np.uint8, order="C")
    array[:, :, :] = np.asarray(fill, dtype=np.uint8)
    return array


def _rgba_view(buffer, width=None, height=None):
    """Return a C-order ``(height,width,4)`` view without changing channel bytes."""
    if isinstance(buffer, np.ndarray):
        if buffer.dtype != np.uint8:
            raise TypeError(f"RGBA array must use uint8 storage, got {buffer.dtype}")
        array = buffer
    else:
        array = np.frombuffer(buffer, dtype=np.uint8)
    if array.ndim == 3:
        if array.shape[2] != 4:
            raise ValueError("RGBA array must have four channels")
        if width is not None and height is not None and array.shape != (height, width, 4):
            raise ValueError(f"RGBA array shape {array.shape} does not match {(height, width, 4)}")
        return array if array.flags.c_contiguous else np.ascontiguousarray(array)
    if width is None or height is None:
        raise ValueError("width and height are required for flat RGBA buffers")
    expected = width * height * 4
    if array.size != expected:
        raise ValueError(f"RGBA buffer length {array.size} does not match {expected}")
    return array.reshape((height, width, 4), order="C")


def _rgba_bytes(buffer, width=None, height=None) -> bytes:
    """Pack RGBA storage once in row-major order for PNG encoding/hash/write."""
    if isinstance(buffer, np.ndarray):
        array = _rgba_view(buffer, width, height)
        return array.tobytes(order="C")
    return bytes(buffer)


def _rgba_flat(buffer, width=None, height=None):
    if isinstance(buffer, np.ndarray):
        return _rgba_view(buffer, width, height).reshape(-1, 4, order="C")
    return np.frombuffer(buffer, dtype=np.uint8).reshape((-1, 4), order="C")


def rgba_fill(width: int, height: int, colour=(0, 0, 0, 255)):
    return _rgba_array(width, height, colour)


def rgba_set(buffer: bytearray, index: int, colour) -> None:
    if isinstance(buffer, np.ndarray):
        if buffer.ndim == 3:
            buffer.reshape(-1, 4, order="C")[index, :] = np.asarray(colour, dtype=np.uint8)
        else:
            buffer.reshape(-1, 4, order="C")[index, :] = np.asarray(colour, dtype=np.uint8)
        return
    offset = index * 4
    buffer[offset : offset + 4] = bytes(colour)


def rgba_get(buffer: bytearray, index: int):
    if isinstance(buffer, np.ndarray):
        value = buffer.reshape(-1, 4, order="C")[index]
        return int(value[0]), int(value[1]), int(value[2]), int(value[3])
    offset = index * 4
    return buffer[offset], buffer[offset + 1], buffer[offset + 2], buffer[offset + 3]


def close_repeat_edges(buffer: bytearray, width: int, height: int) -> None:
    """Duplicate opposite edges for textures sampled with Repeat wrapping."""
    if isinstance(buffer, np.ndarray):
        array = _rgba_view(buffer, width, height)
        array[:, -1, :] = array[:, 0, :]
        array[-1, :, :] = array[0, :, :]
        return
    row_bytes = width * 4
    for y in range(height):
        row = y * row_bytes
        buffer[row + (width - 1) * 4 : row + width * 4] = buffer[row : row + 4]
    last_row = (height - 1) * row_bytes
    buffer[last_row : last_row + row_bytes] = buffer[:row_bytes]


def _png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)


_SYSTEM_ZLIB = None
_SYSTEM_ZLIB_ATTEMPTED = False


def _load_system_zlib():
    """Load a classic zlib compressor when the host Python uses zlib-ng.

    Accepted texture bytes were authored with the reference zlib stream.  Some
    newer Python builds link zlib-ng, whose match finder emits different but
    valid DEFLATE bytes.  Prefer a classic zlib DLL when available and retain
    the Python module as a portable fallback (Blender's bundled zlib is classic
    on supported authoring machines).
    """
    global _SYSTEM_ZLIB, _SYSTEM_ZLIB_ATTEMPTED
    if _SYSTEM_ZLIB_ATTEMPTED:
        return _SYSTEM_ZLIB
    _SYSTEM_ZLIB_ATTEMPTED = True
    candidates = []
    override = os.environ.get("ROCKET_FOOXBALL_ZLIB_DLL")
    if override:
        candidates.append(override)
    if os.name == "nt":
        program_files = os.environ.get("ProgramW6432") or os.environ.get("ProgramFiles") or r"C:\Program Files"
        candidates.extend(
            (
                os.path.join(program_files, "Git", "mingw64", "bin", "zlib1.dll"),
                os.path.join(program_files, "Git", "usr", "bin", "zlib1.dll"),
                "zlib1.dll",
            )
        )
    else:
        candidates.extend(("libz.so.1", "libz.so"))
    for candidate in candidates:
        try:
            library = ctypes.CDLL(candidate)
            version_fn = library.zlibVersion
            version_fn.restype = ctypes.c_char_p
            version = version_fn() or b""
            if b"zlib-ng" in version.lower():
                continue
            compress_bound = library.compressBound
            compress_bound.argtypes = [ctypes.c_ulong]
            compress_bound.restype = ctypes.c_ulong
            compress2 = library.compress2
            compress2.argtypes = [
                ctypes.c_void_p,
                ctypes.POINTER(ctypes.c_ulong),
                ctypes.c_void_p,
                ctypes.c_ulong,
                ctypes.c_int,
            ]
            compress2.restype = ctypes.c_int
            _SYSTEM_ZLIB = (library, version.decode("ascii", errors="replace"), compress_bound, compress2)
            return _SYSTEM_ZLIB
        except (AttributeError, OSError, UnicodeError):
            continue
    return None


def _compress_png_payload(payload: bytes) -> bytes:
    """Compress one scanline stream at level 9 with canonical zlib bytes."""
    system_zlib = _load_system_zlib()
    if system_zlib is None:
        return zlib.compress(payload, level=9)
    _library, _version, compress_bound, compress2 = system_zlib
    source = ctypes.create_string_buffer(payload)
    capacity = int(compress_bound(len(payload)))
    destination = ctypes.create_string_buffer(capacity)
    output_length = ctypes.c_ulong(capacity)
    status = compress2(destination, ctypes.byref(output_length), source, len(payload), 9)
    if status != 0:
        raise RuntimeError(f"zlib compression failed with status {status}")
    return destination.raw[: output_length.value]


def encode_png(width: int, height: int, rgba) -> bytes:
    if np.asarray(rgba, dtype=np.uint8).size != width * height * 4:
        raise ValueError("RGBA buffer length does not match dimensions")
    # A fixed filter byte (None) keeps output independent of image-library heuristics.
    scanlines = bytearray()
    row_bytes = width * 4
    packed = _rgba_bytes(rgba, width, height)
    for y in range(height):
        scanlines.append(0)
        start = y * row_bytes
        scanlines.extend(packed[start : start + row_bytes])
    compressed = _compress_png_payload(bytes(scanlines))
    signature = b"\x89PNG\r\n\x1a\n"
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return signature + _png_chunk(b"IHDR", header) + _png_chunk(b"IDAT", compressed) + _png_chunk(b"IEND", b"")


def save_png(name: str, width: int, height: int, rgba, encoded: bytes | None = None) -> str:
    path = os.path.join(TEXTURE_DIRECTORY if name.startswith("Retro") else PREVIEW_DIRECTORY, name + ".png")
    if os.path.dirname(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
    encoded = encode_png(width, height, rgba) if encoded is None else encoded
    # Avoid touching timestamps when a targeted run reproduces accepted bytes.
    if os.path.isfile(path):
        with open(path, "rb") as handle:
            if handle.read() == encoded:
                return path
    with open(path, "wb") as handle:
        handle.write(encoded)
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
        # Concrete remains non-metallic. Seam response lives in height/AO,
        # never in metallic R.
        return base, height, 0.0, 0.46 + n01 * 0.20, 0.77 - seam * 0.24
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


def _surface_fields_array(kind: str, u, v):
    """Vectorized surface fields for one bounded row chunk."""
    u = np.mod(np.asarray(u, dtype=np.float64), 1.0)
    v = np.mod(np.asarray(v, dtype=np.float64), 1.0)
    n = _periodic_noise_array(kind, u, v)
    n01 = np.clip(0.5 + n * 0.50, 0.0, 1.0)
    shape = np.broadcast_shapes(u.shape, v.shape)
    zero = np.zeros(shape, dtype=np.float64)
    if kind == "grass":
        stripe = 0.5 + 0.5 * np.sin(np.float64(math.tau) * (u * 8.0))
        panel = np.logical_or(np.mod(u * 8.0, 1.0) < 0.035, np.mod(v * 8.0, 1.0) < 0.035).astype(np.float64)
        base = np.stack((0.018 + (0.055 - 0.018) * n01, 0.20 + (0.43 - 0.20) * n01, 0.20 + (0.36 - 0.20) * n01), axis=-1)
        base += stripe[..., None] * 0.16 * np.array((0.10, 0.10, 0.10), dtype=np.float64)
        height = 0.47 + n * 0.06 - panel * 0.08
        return base, height, 0.04 + panel * 0.18, 0.48 + n01 * 0.18, 0.72 - panel * 0.20
    if kind == "wall":
        seam = np.logical_or(np.mod(u * 16.0, 1.0) < 0.028, np.mod(v * 16.0, 1.0) < 0.028).astype(np.float64)
        base = np.stack((0.46 + (0.76 - 0.46) * n01, 0.53 + (0.80 - 0.53) * n01, 0.51 + (0.72 - 0.51) * n01), axis=-1)
        base = base * (1.0 - seam[..., None] * 0.62) + np.array((0.06, 0.40, 0.47), dtype=np.float64) * (seam[..., None] * 0.62)
        height = 0.50 + n * 0.07 - seam * 0.10
        return base, height, zero, 0.46 + n01 * 0.20, 0.77 - seam * 0.24
    if kind == "trim":
        stripe = np.mod(u * 12.0 + v * 12.0, 1.0)
        edge = (stripe < 0.12).astype(np.float64)
        base = np.stack((0.20 + (0.56 - 0.20) * n01, 0.12 + (0.35 - 0.12) * n01, 0.045 + (0.17 - 0.045) * n01), axis=-1)
        base = base * (1.0 - edge[..., None] * 0.38) + np.array((0.74, 0.48, 0.18), dtype=np.float64) * (edge[..., None] * 0.38)
        height = 0.50 + n * 0.08 + edge * 0.06
        return base, height, 0.73 + edge * 0.15, 0.55 + n01 * 0.30, 0.86 - edge * 0.10
    if kind == "hazard":
        diagonal = np.mod(u * 10.0 + v * 10.0, 1.0)
        yellow = (diagonal < 0.50).astype(np.float64)
        dark = np.array((0.045, 0.065, 0.073), dtype=np.float64)
        bright = np.array((0.95, 0.68, 0.08), dtype=np.float64)
        base = dark + (bright - dark) * yellow[..., None]
        base = np.clip(base * (0.90 + n01[..., None] * 0.14), 0.0, 1.0)
        height = 0.49 + n * 0.035 + np.where(yellow > 0.0, 0.07, -0.015)
        return base, height, 0.22 + yellow * 0.30, 0.46 + yellow * 0.24, 0.75 - yellow * 0.10
    palettes = {
        "metal": ((0.12, 0.085, 0.055), (0.40, 0.29, 0.16), (0.70, 0.51, 0.27)),
        "dark": ((0.014, 0.020, 0.028), (0.065, 0.075, 0.086), (0.16, 0.17, 0.17)),
        "accent": ((0.16, 0.012, 0.008), (0.52, 0.040, 0.018), (0.90, 0.17, 0.028)),
    }
    shadow, base_colour, highlight = (np.array(values, dtype=np.float64) for values in palettes[kind])
    value = np.rint(n01 * 8.0) / 8.0
    base = shadow + (base_colour - shadow) * value[..., None]
    # Scalar code shadows ``value`` with each channel's interpolated value in
    # the highlight pass; retain that per-channel amount here.
    highlight_amount = np.clip((base - 0.58) / 0.42, 0.0, 1.0)
    base = base + (highlight - base) * highlight_amount
    grid = np.logical_or(np.mod(u * 16.0, 1.0) < 0.025, np.mod(v * 16.0, 1.0) < 0.025).astype(np.float64)
    base = base * (1.0 - grid[..., None] * 0.45) + shadow * (grid[..., None] * 0.45)
    height = 0.50 + n * 0.09 + grid * 0.035
    metallic = {"metal": 0.82, "dark": 0.64, "accent": 0.52}[kind]
    return base, height, np.full_like(n, metallic), 0.64 + n01 * 0.25, 0.88 - grid * 0.16


def generate_surface_maps(kind: str, width: int, height: int):
    # Author at 1024? then deterministic nearest-upsample weapon maps to 2048?.
    # This preserves the approved source resolution while keeping background
    # Blender generation practical on laptops.
    source_width = min(width, 1024)
    source_height = min(height, 1024)
    base = _rgba_array(source_width, source_height)
    normal = _rgba_array(source_width, source_height)
    metallic = _rgba_array(source_width, source_height)
    occlusion = _rgba_array(source_width, source_height)
    x_values = np.arange(source_width, dtype=np.float64) / float(source_width - 1)
    chunk_rows = max(1, min(source_height, 64))
    frequency = {"grass": 8.0, "wall": 16.0, "trim": 12.0, "hazard": 10.0, "metal": 16.0, "dark": 16.0, "accent": 16.0}[kind]
    strength = 1.15 if kind in ("grass", "wall") else 0.85
    for start in range(0, source_height, chunk_rows):
        stop = min(source_height, start + chunk_rows)
        y_values = np.arange(start, stop, dtype=np.float64) / float(source_height - 1)
        u = x_values[None, :]
        v = y_values[:, None]
        colour, _height, metal, smooth, ao = _surface_fields_array(kind, u, v)
        dx = 0.055 * np.cos(np.float64(math.tau) * frequency * u + 0.37) + 0.022 * np.sin(np.float64(math.tau) * (frequency * 0.5 * v + u))
        dy = 0.055 * np.sin(np.float64(math.tau) * frequency * v + 0.93) + 0.022 * np.cos(np.float64(math.tau) * (frequency * 0.5 * u - v))
        if kind == "hazard":
            dx *= 0.70
            dy *= 0.70
        nx = np.clip(0.5 - dx * strength, 0.0, 1.0)
        ny = np.clip(0.5 - dy * strength, 0.0, 1.0)
        nz = np.clip(1.0 - 0.45 * (np.abs(dx) + np.abs(dy)), 0.0, 1.0)
        base[start:stop, :, :3] = _u8_array(colour)
        normal[start:stop, :, :3] = _u8_array(np.stack((nx, ny, nz), axis=-1))
        metallic[start:stop, :, 0] = _u8_array(metal)
        metallic[start:stop, :, 3] = _u8_array(smooth)
        occlusion[start:stop, :, :3] = _u8_array(np.repeat(ao[..., None], 3, axis=-1))
    for buffer in (base, normal, metallic, occlusion):
        close_repeat_edges(buffer, source_width, source_height)
    if (source_width, source_height) != (width, height):
        return tuple(resize_nearest(buffer, source_width, source_height, width, height) for buffer in (base, normal, metallic, occlusion))
    return base, normal, metallic, occlusion


def generate_detail_normal(width=512, height=512):
    buffer = _rgba_array(width, height)
    x_values = np.arange(width, dtype=np.float64) / float(width - 1)
    chunk_rows = max(1, min(height, 128))
    for start in range(0, height, chunk_rows):
        stop = min(height, start + chunk_rows)
        u = x_values[None, :]
        v = (np.arange(start, stop, dtype=np.float64) / float(height - 1))[:, None]
        dx = 0.035 * np.cos(np.float64(math.tau) * (u * 33.0 + v * 7.0))
        dy = 0.035 * np.sin(np.float64(math.tau) * (v * 29.0 - u * 5.0))
        buffer[start:stop, :, :3] = _u8_array(np.stack((0.5 - dx, 0.5 - dy, 1.0 - np.abs(dx) - np.abs(dy)), axis=-1))
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


def _ball_surface_fields(point, frames):
    """Return football albedo fields plus continuous leather/panel height."""
    nearest_index = max(range(len(frames)), key=lambda index: dot3(point, frames[index][0]))
    center, tangent_u, tangent_v = frames[nearest_index]
    cosine = max(-1.0, min(1.0, dot3(point, center)))
    angular = math.acos(cosine)
    local_angle = math.atan2(dot3(point, tangent_v), dot3(point, tangent_u))
    panel_radius = regular_polygon_boundary(BALL_PANEL_RADIUS, local_angle)
    seam = panel_radius < angular <= panel_radius + BALL_SEAM_WIDTH
    panel = angular <= panel_radius
    grain = 0.5 + 0.5 * (0.60 * math.sin(point[0] * 31.0 + point[2] * 17.0) + 0.40 * math.sin(point[1] * 47.0 - point[2] * 13.0))

    # Height transitions are smooth even though albedo classification stays
    # crisp. This gives finite tangent slopes at panel and seam boundaries.
    panel_blend = 1.0 - smoothstep(panel_radius - 0.014, panel_radius + 0.014, angular)
    seam_blend = smoothstep(panel_radius + 0.006, panel_radius + 0.014, angular)
    seam_blend *= 1.0 - smoothstep(panel_radius + BALL_SEAM_WIDTH - 0.010, panel_radius + BALL_SEAM_WIDTH + 0.010, angular)
    leather_height = 0.50 + grain * 0.015
    height = mix(leather_height, 0.39, panel_blend) + 0.045 * seam_blend

    if panel:
        base = (0.008 + grain * 0.005, 0.010 + grain * 0.006, 0.014 + grain * 0.008)
        smooth = 0.29
        ao = 0.60
    elif seam:
        base = (0.10, 0.105, 0.11)
        smooth = 0.34
        ao = 0.53
    else:
        base = (0.86 + grain * 0.08, 0.87 + grain * 0.075, 0.83 + grain * 0.07)
        smooth = 0.42 + grain * 0.16
        ao = 0.89
    return base, height, smooth, ao, panel, tangent_u, tangent_v


def ball_sample(point, frames):
    """Sample base fields and derive tangent normal from height slope."""
    base, height, smooth, ao, panel, tangent_u, tangent_v = _ball_surface_fields(point, frames)
    epsilon = 0.003
    plus_u = normalized(add3(point, scale3(tangent_u, epsilon)))
    minus_u = normalized(add3(point, scale3(tangent_u, -epsilon)))
    plus_v = normalized(add3(point, scale3(tangent_v, epsilon)))
    minus_v = normalized(add3(point, scale3(tangent_v, -epsilon)))
    height_u = (_ball_surface_fields(plus_u, frames)[1] - _ball_surface_fields(minus_u, frames)[1]) / (2.0 * epsilon)
    height_v = (_ball_surface_fields(plus_v, frames)[1] - _ball_surface_fields(minus_v, frames)[1]) / (2.0 * epsilon)
    # Height-gradient tangent normal: panel recess and raised seams produce
    # opposite-signed slopes, with shallow relief tuned for leather scale.
    tangent_normal = normalized((-height_u * BALL_NORMAL_SCALE, -height_v * BALL_NORMAL_SCALE, 1.0))
    normal = (0.5 + tangent_normal[0] * 0.5, 0.5 + tangent_normal[1] * 0.5, 0.5 + tangent_normal[2] * 0.5)
    return base, normal, 0.0, smooth, ao, panel


def _ball_frames_arrays(frames):
    centres = np.asarray([frame[0] for frame in frames], dtype=np.float64)
    tangent_u = np.asarray([frame[1] for frame in frames], dtype=np.float64)
    tangent_v = np.asarray([frame[2] for frame in frames], dtype=np.float64)
    return centres, tangent_u, tangent_v


def _normalise_array(vector):
    vector = np.asarray(vector, dtype=np.float64)
    length = np.sqrt(np.sum(vector * vector, axis=-1))
    return vector / length[..., None]


def _ball_surface_fields_array(points, frames):
    """Vectorized football field sampling for bounded point chunks."""
    centres, tangent_us, tangent_vs = _ball_frames_arrays(frames)
    dots = (
        points[..., None, 0] * centres[None, None, :, 0]
        + points[..., None, 1] * centres[None, None, :, 1]
        + points[..., None, 2] * centres[None, None, :, 2]
    )
    nearest = np.argmax(dots, axis=-1)
    centre = centres[nearest]
    tangent_u = tangent_us[nearest]
    tangent_v = tangent_vs[nearest]
    cosine = np.clip(points[..., 0] * centre[..., 0] + points[..., 1] * centre[..., 1] + points[..., 2] * centre[..., 2], -1.0, 1.0)
    angular = np.arccos(cosine)
    local_angle = np.arctan2(
        points[..., 0] * tangent_v[..., 0] + points[..., 1] * tangent_v[..., 1] + points[..., 2] * tangent_v[..., 2],
        points[..., 0] * tangent_u[..., 0] + points[..., 1] * tangent_u[..., 1] + points[..., 2] * tangent_u[..., 2],
    )
    sector = np.float64(math.tau / 5.0)
    offset = np.mod(local_angle + sector * 0.5, sector) - sector * 0.5
    panel_radius = np.float64(BALL_PANEL_RADIUS) * np.cos(np.float64(math.pi / 5.0)) / np.maximum(0.20, np.cos(offset))
    seam = (panel_radius < angular) & (angular <= panel_radius + BALL_SEAM_WIDTH)
    panel = angular <= panel_radius
    grain = 0.5 + 0.5 * (0.60 * np.sin(points[..., 0] * 31.0 + points[..., 2] * 17.0) + 0.40 * np.sin(points[..., 1] * 47.0 - points[..., 2] * 13.0))
    panel_blend = 1.0 - _smoothstep_array(panel_radius - 0.014, panel_radius + 0.014, angular)
    seam_blend = _smoothstep_array(panel_radius + 0.006, panel_radius + 0.014, angular)
    seam_blend *= 1.0 - _smoothstep_array(panel_radius + BALL_SEAM_WIDTH - 0.010, panel_radius + BALL_SEAM_WIDTH + 0.010, angular)
    leather_height = 0.50 + grain * 0.015
    height = leather_height + (0.39 - leather_height) * panel_blend + 0.045 * seam_blend
    panel_base = np.stack((0.008 + grain * 0.005, 0.010 + grain * 0.006, 0.014 + grain * 0.008), axis=-1)
    seam_base = np.broadcast_to(np.array((0.10, 0.105, 0.11), dtype=np.float64), panel_base.shape)
    leather_base = np.stack((0.86 + grain * 0.08, 0.87 + grain * 0.075, 0.83 + grain * 0.07), axis=-1)
    base = np.where(panel[..., None], panel_base, np.where(seam[..., None], seam_base, leather_base))
    smooth = np.where(panel, 0.29, np.where(seam, 0.34, 0.42 + grain * 0.16))
    ao = np.where(panel, 0.60, np.where(seam, 0.53, 0.89))
    return base, height, smooth, ao, panel, tangent_u, tangent_v


def _ball_sample_array(points, frames):
    base, height, smooth, ao, panel, tangent_u, tangent_v = _ball_surface_fields_array(points, frames)
    epsilon = np.float64(0.003)
    plus_u = _normalise_array(points + tangent_u * epsilon)
    minus_u = _normalise_array(points - tangent_u * epsilon)
    plus_v = _normalise_array(points + tangent_v * epsilon)
    minus_v = _normalise_array(points - tangent_v * epsilon)
    height_u = (_ball_surface_fields_array(plus_u, frames)[1] - _ball_surface_fields_array(minus_u, frames)[1]) / (2.0 * epsilon)
    height_v = (_ball_surface_fields_array(plus_v, frames)[1] - _ball_surface_fields_array(minus_v, frames)[1]) / (2.0 * epsilon)
    tangent_normal = _normalise_array(np.stack((-height_u * BALL_NORMAL_SCALE, -height_v * BALL_NORMAL_SCALE, np.ones_like(height_u)), axis=-1))
    normal = 0.5 + tangent_normal * 0.5
    return base, normal, np.zeros_like(smooth), smooth, ao, panel


def sphere_point(latitude, longitude):
    cos_lat = math.cos(latitude)
    return (cos_lat * math.cos(longitude), math.sin(latitude), cos_lat * math.sin(longitude))


def generate_ball_maps(width=1024, height=512):
    base = _rgba_array(width, height)
    normal = _rgba_array(width, height)
    metallic = _rgba_array(width, height)
    occlusion = _rgba_array(width, height)
    frames = ball_centers()
    longitudes = -math.pi + math.tau * np.arange(width, dtype=np.float64) / float(width - 1)
    chunk_rows = max(1, min(height, 16))
    for start in range(0, height, chunk_rows):
        stop = min(height, start + chunk_rows)
        latitudes = -math.pi / 2.0 + math.pi * np.arange(start, stop, dtype=np.float64) / float(height - 1)
        cos_lat = np.cos(latitudes)[:, None]
        sin_lat = np.broadcast_to(np.sin(latitudes)[:, None], (stop - start, width))
        points = np.stack((cos_lat * np.cos(longitudes)[None, :], sin_lat, cos_lat * np.sin(longitudes)[None, :]), axis=-1)
        colour, normal_value, metal, smooth, ao, _panel = _ball_sample_array(points, frames)
        base[start:stop, :, :3] = _u8_array(colour)
        normal[start:stop, :, :3] = _u8_array(normal_value)
        metallic[start:stop, :, 0] = _u8_array(metal)
        metallic[start:stop, :, 3] = _u8_array(smooth)
        occlusion[start:stop, :, :3] = _u8_array(np.repeat(ao[..., None], 3, axis=-1))
        base[start:stop, :, 3] = 255
        normal[start:stop, :, 3] = 255
        metallic[start:stop, :, 1:3] = 0
        occlusion[start:stop, :, 3] = 255
    # Endpoint duplication and uniform pole rows are explicit, not an emergent float result.
    for buffer in (base, normal, metallic, occlusion):
        close_repeat_edges(buffer, width, height)
        for y in (0, height - 1):
            buffer[y, :, :] = buffer[y, 0, :]
    return base, normal, metallic, occlusion, frames


def rocket_region_rectangles(width=1024, height=1024):
    """Return disjoint outer and gutter-safe pixel rectangles for atlas regions."""
    rectangles = {}
    for name, (u0, u1, v0, v1) in ROCKET_ATLAS_REGIONS.items():
        left = int(round(u0 * width))
        right = int(round(u1 * width))
        bottom = int(round(v0 * height))
        top = int(round(v1 * height))
        gutter = ROCKET_GUTTER_PX
        if right - left <= gutter * 2 or top - bottom <= gutter * 2:
            raise RuntimeError(f"Rocket region {name} too small for {gutter}px gutters")
        rectangles[name] = {
            "outer": (left, bottom, right, top),
            "inner": (left + gutter, bottom + gutter, right - gutter, top - gutter),
            "uv": (u0, u1, v0, v1),
        }
    return rectangles


def _rocket_region(x, y, width=1024, height=1024, rectangles=None):
    rectangles = rectangles or rocket_region_rectangles(width, height)
    matches = [name for name, entry in rectangles.items() if entry["outer"][0] <= x < entry["outer"][2] and entry["outer"][1] <= y < entry["outer"][3]]
    if len(matches) != 1:
        raise RuntimeError(f"Rocket atlas region overlap/gap at pixel ({x},{y}): {matches}")
    return matches[0]


def _rocket_region_uv(region, x, y, rectangles=None):
    entry = (rectangles or rocket_region_rectangles())[region]
    inner_left, inner_bottom, inner_right, inner_top = entry["inner"]
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
        return base, 0.50 + grain * 0.08 - panel * 0.08, 0.78 - panel * 0.22, 0.78 - panel * 0.10, (0.0, 0.0, 0.0)
    if region == "hot":
        radial = math.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
        red = (0.55 + grain * 0.18, 0.035 + grain * 0.025, 0.012)
        orange = (1.0, 0.70, 0.035)
        base = tuple(mix(red[i], orange[i], smoothstep(0.58, 0.12, radial)) for i in range(3))
        emission = (1.0, 0.20 + 0.55 * smoothstep(0.48, 0.0, radial), 0.02)
        return base, 0.48 + grain * 0.06, 0.32, 0.62, emission
    if region == "fins":
        base = (0.38 + grain * 0.25, 0.018 + grain * 0.035, 0.012 + grain * 0.016)
        return base, 0.46 + grain * 0.04, 0.58, 0.58, (0.0, 0.0, 0.0)
    radial = math.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
    base = (0.025 + grain * 0.025, 0.030 + grain * 0.028, 0.032 + grain * 0.030)
    throat = smoothstep(0.24, 0.03, radial)
    base = (mix(base[0], 0.18, throat), mix(base[1], 0.08, throat), mix(base[2], 0.018, throat))
    emission = (1.0, 0.28 + 0.55 * throat, 0.02 + 0.08 * throat)
    return base, 0.42 + grain * 0.06, 0.91, 0.72, emission


def _rocket_fields_array(region: str, u, v):
    """Vectorized rocket atlas fields for one region mask."""
    u = np.asarray(u, dtype=np.float64)
    v = np.asarray(v, dtype=np.float64)
    grain = 0.5 + 0.5 * np.sin(np.float64(math.tau) * (u * 7.0 + v * 11.0))
    panel = np.logical_or(np.mod(u * 8.0, 1.0) < 0.055, np.mod(v * 10.0, 1.0) < 0.045).astype(np.float64)
    if region == "body":
        body = np.stack((0.29 + grain * 0.12, 0.22 + grain * 0.09, 0.13 + grain * 0.055), axis=-1)
        charcoal = np.array((0.055, 0.062, 0.066), dtype=np.float64)
        base = body * (1.0 - panel[..., None] * 0.72) + charcoal * (panel[..., None] * 0.72)
        return base, 0.50 + grain * 0.08 - panel * 0.08, np.full_like(grain, 0.78) - panel * 0.22, np.full_like(grain, 0.78) - panel * 0.10, np.zeros((*grain.shape, 3), dtype=np.float64)
    if region == "hot":
        radial = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
        red = np.stack((0.55 + grain * 0.18, 0.035 + grain * 0.025, np.full_like(grain, 0.012)), axis=-1)
        orange = np.array((1.0, 0.70, 0.035), dtype=np.float64)
        amount = _smoothstep_array(0.58, 0.12, radial)
        base = red + (orange - red) * amount[..., None]
        emission = np.stack((np.ones_like(grain), 0.20 + 0.55 * _smoothstep_array(0.48, 0.0, radial), np.full_like(grain, 0.02)), axis=-1)
        return base, 0.48 + grain * 0.06, np.full_like(grain, 0.32), np.full_like(grain, 0.62), emission
    if region == "fins":
        base = np.stack((0.38 + grain * 0.25, 0.018 + grain * 0.035, 0.012 + grain * 0.016), axis=-1)
        return base, 0.46 + grain * 0.04, np.full_like(grain, 0.58), np.full_like(grain, 0.58), np.zeros((*grain.shape, 3), dtype=np.float64)
    radial = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
    base = np.stack((0.025 + grain * 0.025, 0.030 + grain * 0.028, 0.032 + grain * 0.030), axis=-1)
    throat = _smoothstep_array(0.24, 0.03, radial)
    base = base + (np.array((0.18, 0.08, 0.018), dtype=np.float64) - base) * throat[..., None]
    emission = np.stack((np.ones_like(grain), 0.28 + 0.55 * throat, 0.02 + 0.08 * throat), axis=-1)
    return base, 0.42 + grain * 0.06, np.full_like(grain, 0.91), np.full_like(grain, 0.72), emission


def generate_rocket_maps(width=1024, height=1024):
    outputs = {key: _rgba_array(width, height) for key in ("base", "normal", "metallic", "occlusion", "emission")}
    rectangles = rocket_region_rectangles(width, height)
    chunk_rows = max(1, min(height, 64))
    x_grid = np.arange(width, dtype=np.int64)[None, :]
    for start in range(0, height, chunk_rows):
        stop = min(height, start + chunk_rows)
        y_grid = np.arange(start, stop, dtype=np.int64)[:, None]
        chunk_shape = (stop - start, width)
        base_chunk = np.zeros((*chunk_shape, 4), dtype=np.uint8)
        normal_chunk = np.zeros((*chunk_shape, 4), dtype=np.uint8)
        metallic_chunk = np.zeros((*chunk_shape, 4), dtype=np.uint8)
        occlusion_chunk = np.zeros((*chunk_shape, 4), dtype=np.uint8)
        emission_chunk = np.zeros((*chunk_shape, 4), dtype=np.uint8)
        for region, entry in rectangles.items():
            left, bottom, right, top = entry["outer"]
            inner_left, inner_bottom, inner_right, inner_top = entry["inner"]
            mask = (x_grid >= left) & (x_grid < right) & (y_grid >= bottom) & (y_grid < top)
            cx = np.clip(x_grid, inner_left, inner_right - 1)
            cy = np.clip(y_grid, inner_bottom, inner_top - 1)
            u = (cx - inner_left) / float(max(1, inner_right - inner_left - 1))
            v = (cy - inner_bottom) / float(max(1, inner_top - inner_bottom - 1))
            colour, _height, metal, smooth, emission = _rocket_fields_array(region, u, v)
            nx = np.clip(0.5 + 0.035 * np.sin(np.float64(math.tau) * (u * 5.0 + v)), 0.0, 1.0)
            ny = np.clip(0.5 + 0.035 * np.cos(np.float64(math.tau) * (v * 6.0 - u)), 0.0, 1.0)
            nz = np.full_like(nx, 0.985)
            ao = np.full_like(nx, 0.76 if region in ("fins", "nozzle") else 0.85)
            base_chunk[mask, :3] = _u8_array(colour)[mask]
            base_chunk[mask, 3] = 255
            normal_chunk[mask, :3] = _u8_array(np.stack((nx, ny, nz), axis=-1))[mask]
            normal_chunk[mask, 3] = 255
            metallic_chunk[mask, 0] = _u8_array(metal)[mask]
            metallic_chunk[mask, 3] = _u8_array(smooth)[mask]
            occlusion_chunk[mask, :3] = _u8_array(np.repeat(ao[..., None], 3, axis=-1))[mask]
            occlusion_chunk[mask, 3] = 255
            emission_chunk[mask, :3] = _u8_array(emission)[mask]
            emission_chunk[mask, 3] = 255
        outputs["base"][start:stop] = base_chunk
        outputs["normal"][start:stop] = normal_chunk
        outputs["metallic"][start:stop] = metallic_chunk
        outputs["occlusion"][start:stop] = occlusion_chunk
        outputs["emission"][start:stop] = emission_chunk
    return outputs


def generate_shield(width=128, height=128):
    buffer = _rgba_array(width, height)
    x = (np.arange(width, dtype=np.float64) + 0.5) / 16.0
    y = (np.arange(height, dtype=np.float64) + 0.5)[:, None] / 16.0
    u = x[None, :]
    v = y
    fu = u - np.floor(u)
    fv_a = (v + u * 0.58) - np.floor(v + u * 0.58)
    fv_b = (v - u * 0.58) - np.floor(v - u * 0.58)
    line_distance = np.minimum.reduce(np.broadcast_arrays(fu, 1.0 - fu, fv_a, 1.0 - fv_a, fv_b, 1.0 - fv_b))
    line = 1.0 - _smoothstep_array(0.015, 0.105, line_distance)
    pulse = 0.5 + 0.5 * np.sin(np.float64(math.tau) * (u * 0.13 + v * 0.17))
    alpha = np.clip(0.035 + line * (0.76 + 0.18 * pulse), 0.0, 1.0)
    colour = np.stack(
        (
            np.clip(0.44 + 0.18 * pulse + 0.22 * line, 0.0, 1.0),
            np.clip(0.82 + 0.12 * pulse, 0.0, 1.0),
            np.clip(0.92 + 0.08 * line, 0.0, 1.0),
            alpha,
        ),
        axis=-1,
    )
    buffer[:, :, :] = _u8_array(colour)
    return buffer


def generate_glow(width=128, height=128):
    buffer = _rgba_array(width, height)
    nx = (np.arange(width, dtype=np.float64) + 0.5 - width / 2.0) / (width / 2.0)
    ny = (np.arange(height, dtype=np.float64) + 0.5 - height / 2.0)[:, None] / (height / 2.0)
    radius = np.sqrt(nx[None, :] * nx[None, :] + ny * ny)
    core = 1.0 - _smoothstep_array(0.0, 0.27, radius)
    ring = _smoothstep_array(0.18, 0.30, radius) * (1.0 - _smoothstep_array(0.30, 0.60, radius))
    halo = 1.0 - _smoothstep_array(0.36, 0.98, radius)
    alpha = np.clip(core * 0.96 + ring * 0.72 + halo * 0.22, 0.0, 1.0)
    colour = np.stack((np.ones_like(alpha), np.clip(core * 0.96 + ring * 0.80 + halo * 0.22, 0.0, 1.0), np.clip(core * 0.82 + ring * 0.18, 0.0, 1.0), alpha), axis=-1)
    buffer[:, :, :] = _u8_array(colour)
    return buffer


def generate_sprite_sheet(kind: str, width=128, height=128):
    buffer = _rgba_array(width, height)
    for cell_y in range(4):
        for cell_x in range(4):
            variant = cell_y * 4 + cell_x
            phase = (0.61 if kind == "explosion" else 1.73) + variant * 0.79
            edge_base = (0.79 if kind == "explosion" else 0.84) + 0.025 * math.sin(variant * 1.9)
            px = np.arange(32, dtype=np.float64)
            py = np.arange(32, dtype=np.float64)[:, None]
            nx = (px + 0.5 - 16.0) / 16.0
            ny = (py + 0.5 - 16.0) / 16.0
            radius = np.sqrt(nx[None, :] * nx[None, :] + ny * ny)
            angle = np.arctan2(ny, nx[None, :])
            irregular = 0.055 * np.sin(5.0 * angle + phase) + 0.030 * np.sin(9.0 * angle - phase * 1.7)
            edge = edge_base + irregular
            if kind == "explosion":
                alpha = 1.0 - _smoothstep_array(edge - 0.20, edge, radius)
                core = _smoothstep_array(0.37, 0.0, radius)
                body = _smoothstep_array(0.67, 0.10, radius)
                edge_mix = _smoothstep_array(edge, 0.48, radius)
                green = np.clip(0.99 * core + 0.88 * body + 0.68 * edge_mix, 0.0, 1.0)
                green = np.where(alpha >= 0.50, np.maximum(green, 0.72), green)
                colour = np.stack((np.ones_like(alpha), green, np.clip(0.82 * core + 0.22 * body + 0.035 * edge_mix, 0.0, 1.0), alpha), axis=-1)
            else:
                puff = 0.5 + 0.5 * (0.62 * np.sin(5.0 * angle + phase) + 0.38 * np.sin(9.0 * angle - phase * 1.6))
                edge = edge + 0.07 * (puff - 0.5)
                alpha = 1.0 - _smoothstep_array(0.12, edge, radius)
                density = np.clip(1.0 - radius / np.maximum(0.25, edge), 0.0, 1.0)
                colour = np.stack((0.055 + (0.40 - 0.055) * density, 0.062 + (0.39 - 0.062) * density, 0.070 + (0.37 - 0.070) * density, alpha), axis=-1)
            buffer[cell_y * 32 : cell_y * 32 + 32, cell_x * 32 : cell_x * 32 + 32, :] = _u8_array(colour)
    return buffer


def generate_sky(width=2048, height=1024):
    buffer = _rgba_array(width, height)
    cloud_centres = ((0.16, 0.68, 0.095, 0.060), (0.34, 0.76, 0.120, 0.055), (0.58, 0.64, 0.085, 0.065), (0.79, 0.78, 0.115, 0.050), (0.91, 0.60, 0.070, 0.050))
    x = np.arange(width, dtype=np.float64) / float(width - 1)
    chunk_rows = max(1, min(height, 64))
    for start in range(0, height, chunk_rows):
        stop = min(height, start + chunk_rows)
        u = x[None, :]
        v = (np.arange(start, stop, dtype=np.float64) / float(height - 1))[:, None]
        sky_t = _smoothstep_array(0.0, 1.0, v)
        sky = np.stack((0.73 + (0.30 - 0.73) * sky_t, 0.86 + (0.58 - 0.86) * sky_t, 0.95 + (0.86 - 0.95) * sky_t), axis=-1)
        coverage = np.zeros((stop - start, width), dtype=np.float64)
        for cx, cy, sx, sy in cloud_centres:
            du = np.abs(u - cx)
            du = np.minimum(du, 1.0 - du)
            coverage = np.maximum(coverage, np.exp(-((du / sx) ** 2 + ((v - cy) / sy) ** 2) * 1.7))
        coverage *= 0.22
        cloud_mix = _smoothstep_array(0.02, 0.20, coverage)
        colour = sky * (1.0 - cloud_mix[..., None]) + np.array((0.96, 0.95, 0.89), dtype=np.float64) * cloud_mix[..., None]
        buffer[start:stop, :, :3] = _u8_array(colour)
    # U-repeat only; horizon and zenith remain distinct.
    buffer[:, -1, :] = buffer[:, 0, :]
    return buffer


def resize_nearest(source: bytearray, source_width: int, source_height: int, width: int, height: int) -> bytearray:
    source_array = _rgba_view(source, source_width, source_height)
    # Keep the scalar float64 multiply/divide ordering before flooring; this
    # avoids rounding drift for non-divisible preview dimensions.
    source_y = np.minimum(source_height - 1, np.floor(np.arange(height, dtype=np.float64) * np.float64(source_height) / np.float64(height)).astype(np.int64))
    source_x = np.minimum(source_width - 1, np.floor(np.arange(width, dtype=np.float64) * np.float64(source_width) / np.float64(width)).astype(np.int64))
    return np.ascontiguousarray(source_array[np.ix_(source_y, source_x)], dtype=np.uint8)


def checker(width, height, cell=12):
    output = _rgba_array(width, height)
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
    output = _rgba_array(size * 3, size * 2)
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
    if (width, height) != expected or np.asarray(rgba, dtype=np.uint8).size != width * height * 4:
        raise RuntimeError(f"{name}: dimensions/channel mismatch")
    channels = _rgba_flat(rgba, width, height)
    channel_ranges = [(int(np.min(channels[:, channel])), int(np.max(channels[:, channel]))) for channel in range(4)]
    if any(not (0 <= minimum <= maximum <= 255) for minimum, maximum in channel_ranges):
        raise RuntimeError(f"{name}: invalid channel range")
    if map_type not in ("metallic", "ao", "normal") and max(channel_ranges[0][1], channel_ranges[1][1], channel_ranges[2][1]) - min(channel_ranges[0][0], channel_ranges[1][0], channel_ranges[2][0]) < 8:
        raise RuntimeError(f"{name}: flat colour range")
    seam_u_error, seam_v_error, pole_error = _seam_errors(rgba, width, height, seam_v, poles)
    if seam_u and seam_u_error:
        raise RuntimeError(f"{name}: U seam mismatch {seam_u_error}")
    if seam_v and seam_v_error:
        raise RuntimeError(f"{name}: V seam mismatch {seam_v_error}")
    if poles and pole_error:
        raise RuntimeError(f"{name}: pole mismatch {pole_error}")
    if alpha_range is not None:
        minimum, maximum = alpha_range
        if channel_ranges[3][0] > minimum or channel_ranges[3][1] < maximum:
            raise RuntimeError(f"{name}: alpha range {channel_ranges[3]} outside {alpha_range}")
    print(f"AUDIT {name}: {width}x{height} RGBA8; R={channel_ranges[0][0]}..{channel_ranges[0][1]} G={channel_ranges[1][0]}..{channel_ranges[1][1]} B={channel_ranges[2][0]}..{channel_ranges[2][1]} A={channel_ranges[3][0]}..{channel_ranges[3][1]} seamU={seam_u_error} seamV={seam_v_error} poles={pole_error}")
    return {"dimensions": [width, height], "channels": "RGBA8", "ranges": channel_ranges, "seam_u_error": seam_u_error, "seam_v_error": seam_v_error, "pole_error": pole_error, "map_type": map_type}


def _seam_errors(rgba, width, height, check_v=False, check_poles=False):
    array = _rgba_view(rgba, width, height)
    seam_u = int(np.max(np.abs(array[:, 0, :].astype(np.int16) - array[:, -1, :].astype(np.int16))))
    seam_v = int(np.max(np.abs(array[0, :, :].astype(np.int16) - array[-1, :, :].astype(np.int16)))) if check_v else 0
    poles = 0
    if check_poles:
        poles = int(max(np.max(np.abs(array[row, :, :].astype(np.int16) - array[row, 0, :].astype(np.int16))) for row in (0, height - 1)))
    return seam_u, seam_v, poles


def _normal_map_audit(rgba, width, height):
    pixels = _rgba_flat(rgba, width, height).astype(np.float64)
    vectors = pixels[:, :3] / 127.5 - 1.0
    lengths = np.sqrt(np.sum(vectors * vectors, axis=1))
    minimum = float(np.min(lengths)) if lengths.size else float("inf")
    maximum = float(np.max(lengths)) if lengths.size else 0.0
    finite = np.isfinite(vectors).all(axis=1) & np.isfinite(lengths)
    invalid = int(np.count_nonzero(~finite | (vectors[:, 2] <= 0.0) | (lengths < 0.70) | (lengths > 1.30) | (pixels[:, 3] != 255)))
    return {
        "valid": invalid == 0,
        "invalid_pixels": invalid,
        "decoded_length": [round(minimum, 6), round(maximum, 6)],
    }


def audit_ball_layout(base, normal, metallic, occlusion, frames, width=1024, height=512):
    """Hard football semantic gates. Return serializable metrics and pass bits."""
    centres = [frame[0] for frame in frames]
    finite = all(math.isfinite(component) for centre in centres for component in centre)
    unique = len({tuple(round(component, 12) for component in centre) for centre in centres}) == 12
    unit_error = max(abs(math.sqrt(dot3(centre, centre)) - 1.0) for centre in centres) if centres else float("inf")
    centroid = tuple(sum(centre[index] for centre in centres) / max(1, len(centres)) for index in range(3))
    centroid_error = math.sqrt(dot3(centroid, centroid))
    antipodal_error = max(min(math.sqrt(sum((centre[index] + other[index]) ** 2 for index in range(3))) for other in centres) for centre in centres)
    neighbour_counts = []
    neighbour_spread = 0.0
    for centre in centres:
        distances = sorted(math.acos(max(-1.0, min(1.0, dot3(centre, other)))) for other in centres if other is not centre)
        nearest = distances[:5]
        neighbour_counts.append(sum(1 for distance in distances if abs(distance - nearest[0]) <= 1e-6))
        neighbour_spread = max(neighbour_spread, max(nearest) - min(nearest))
    five_neighbours = all(count == 5 for count in neighbour_counts)
    nearest_separation = min(math.acos(max(-1.0, min(1.0, dot3(a, b)))) for index, a in enumerate(centres) for b in centres[index + 1 :])
    separation_ok = nearest_separation > BALL_PANEL_RADIUS * 2.0 + 1e-6

    centre_dark = True
    centre_samples = []
    base_array = _rgba_view(base, width, height)
    for centre in centres:
        longitude = math.atan2(centre[2], centre[0])
        latitude = math.asin(max(-1.0, min(1.0, centre[1])))
        x = int(round((longitude + math.pi) / math.tau * (width - 1)))
        y = int(round((latitude + math.pi / 2.0) / math.pi * (height - 1)))
        x = max(0, min(width - 1, x))
        y = max(0, min(height - 1, y))
        sample = tuple(int(value) for value in base_array[y, x])
        centre_samples.append({"xy": [x, y], "rgb": list(sample[:3])})
        centre_dark &= max(sample[:3]) <= 80
    dark_pixels = int(np.count_nonzero((base_array[:, :, 0] <= 80) & (base_array[:, :, 1] <= 80) & (base_array[:, :, 2] <= 90)))
    dark_ratio = dark_pixels / float(width * height)
    dark_area_ok = 0.08 <= dark_ratio <= 0.42
    seam_u, seam_v, poles = _seam_errors(base, width, height, False, True)
    for map_buffer in (normal, metallic, occlusion):
        map_u, map_v, map_poles = _seam_errors(map_buffer, width, height, False, True)
        seam_u = max(seam_u, map_u)
        seam_v = max(seam_v, map_v)
        poles = max(poles, map_poles)
    normal_audit = _normal_map_audit(normal, width, height)
    normal_array = _rgba_view(normal, width, height).astype(np.float64)
    normal_xy = np.sqrt((normal_array[:, :, 0] / 127.5 - 1.0) ** 2 + (normal_array[:, :, 1] / 127.5 - 1.0) ** 2)
    normal_xy_max = float(np.max(normal_xy))
    metallic_r_max = int(np.max(_rgba_view(metallic, width, height)[:, :, 0]))
    nonmetal = metallic_r_max == 0
    gates = {
        "finite_unique_unit_centres": finite and unique and unit_error <= 1e-6,
        "centroid_near_zero": centroid_error <= 1e-6,
        "antipodal_symmetry": antipodal_error <= 1e-6,
        "five_nearest_neighbours": five_neighbours and neighbour_spread <= 1e-6,
        "panel_separation": separation_ok,
        "black_centre_pixels": centre_dark,
        "bounded_dark_area": dark_area_ok,
        "u_seam": seam_u == 0,
        "uniform_poles": poles == 0,
        "decoded_normal_valid": normal_audit["valid"],
        "shallow_panel_relief": normal_xy_max <= 0.65,
        "nonmetallic": nonmetal,
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "centres": {
            "count": len(centres),
            "unit_error": unit_error,
            "centroid": centroid,
            "centroid_error": centroid_error,
            "antipodal_error": antipodal_error,
            "neighbour_counts": neighbour_counts,
            "neighbour_spread": neighbour_spread,
            "nearest_separation_rad": nearest_separation,
            "panel_radius_rad": BALL_PANEL_RADIUS,
            "samples": centre_samples,
        },
        "dark_area_ratio": dark_ratio,
        "seam_u_error": seam_u,
        "seam_v_error": seam_v,
        "pole_error": poles,
        "normal": normal_audit,
        "normal_xy_max": normal_xy_max,
        "metallic_r_max": metallic_r_max,
    }


def _region_stats(rgba, rectangle, width=1024, height=None):
    """Compute atlas statistics for any width, not only the canonical 1024px atlas."""
    left, bottom, right, top = rectangle
    count = max(1, (right - left) * (top - bottom))
    if height is None:
        height = int(np.asarray(rgba, dtype=np.uint8).size // (max(1, width) * 4))
    array = _rgba_view(rgba, width, height)
    pixels = array[bottom:top, left:right, :3].reshape(-1, 3).astype(np.float64)
    colour = pixels / 255.0
    luminance = colour[:, 0] * 0.2126 + colour[:, 1] * 0.7152 + colour[:, 2] * 0.0722
    # ``cumsum`` preserves the scalar row-major accumulation order used by the
    # pre-NumPy audit; a tree reduction changes only trailing float digits.
    luminance_sum = float(np.cumsum(luminance, dtype=np.float64)[-1]) if luminance.size else 0.0
    luminance_min = float(np.min(luminance)) if luminance.size else 1.0
    luminance_max = float(np.max(luminance)) if luminance.size else 0.0
    channel_min = [int(value) for value in np.min(pixels, axis=0)] if pixels.size else [255, 255, 255]
    channel_max = [int(value) for value in np.max(pixels, axis=0)] if pixels.size else [0, 0, 0]
    red_dominant = int(np.count_nonzero((pixels[:, 0] > pixels[:, 1] * 1.45) & (pixels[:, 0] > pixels[:, 2] * 1.65)))
    return {
        "count": count,
        "mean_luminance": luminance_sum / count,
        "luminance_range": [luminance_min, luminance_max],
        "channel_ranges": [[channel_min[index], channel_max[index]] for index in range(3)],
        "red_dominant_ratio": red_dominant / float(count),
    }


def audit_rocket_atlas(maps, width=1024, height=1024):
    """Hard atlas gates: disjoint UV regions, gutters, palette, emission mask."""
    rectangles = rocket_region_rectangles(width, height)
    entries = list(rectangles.values())
    area = sum((entry["outer"][2] - entry["outer"][0]) * (entry["outer"][3] - entry["outer"][1]) for entry in entries)
    overlap = any(
        max(a["outer"][0], b["outer"][0]) < min(a["outer"][2], b["outer"][2]) and max(a["outer"][1], b["outer"][1]) < min(a["outer"][3], b["outer"][3])
        for index, a in enumerate(entries) for b in entries[index + 1 :]
    )
    regions_exact = area == width * height and not overlap

    gutter_errors = {}
    for map_name, rgba in maps.items():
        array = _rgba_view(rgba, width, height)
        errors = 0
        for entry in entries:
            left, bottom, right, top = entry["outer"]
            inner_left, inner_bottom, inner_right, inner_top = entry["inner"]
            x = np.arange(left, right, dtype=np.int64)[None, :]
            y = np.arange(bottom, top, dtype=np.int64)[:, None]
            outside = np.logical_not((x >= inner_left) & (x < inner_right) & (y >= inner_bottom) & (y < inner_top))
            cx = np.clip(x, inner_left, inner_right - 1)
            cy = np.clip(y, inner_bottom, inner_top - 1)
            expected = array[cy, cx]
            errors += int(np.count_nonzero(np.any(array[bottom:top, left:right] != expected, axis=-1) & outside))
        gutter_errors[map_name] = errors

    region_stats = {}
    for name, entry in rectangles.items():
        region_stats[name] = _region_stats(maps["base"], entry["inner"], width, height)
    body = region_stats["body"]
    fins = region_stats["fins"]
    hot = region_stats["hot"]
    nozzle = region_stats["nozzle"]
    body_panel_contrast = body["luminance_range"][1] - body["luminance_range"][0]
    palette = {
        "body_nonflat": body_panel_contrast > 0.08,
        "body_bronze_above_charcoal": body["mean_luminance"] > 0.12 and body["luminance_range"][0] < body["mean_luminance"] * 0.65,
        "fins_red_dominant": fins["red_dominant_ratio"] >= 0.90,
        "hot_has_red_orange_yellow": hot["channel_ranges"][0][1] >= 200 and hot["channel_ranges"][1][1] >= 90 and hot["channel_ranges"][2][1] >= 4,
        "nozzle_dark": nozzle["mean_luminance"] < 0.16,
    }

    emission = maps["emission"]
    emission_array = _rgba_view(emission, width, height)
    body_fin_emission = 0
    hot_nozzle_emission = 0
    for name, entry in rectangles.items():
        left, bottom, right, top = entry["outer"]
        region_pixels = emission_array[bottom:top, left:right]
        bright = np.any(region_pixels[:, :, :3] > 0, axis=-1)
        count = int(np.count_nonzero(bright))
        if name in ("body", "fins"):
            body_fin_emission += count
        else:
            hot_nozzle_emission += count
        if np.any(region_pixels[:, :, 3] != 255):
            body_fin_emission += int(np.count_nonzero(region_pixels[:, :, 3] != 255))
    emission_gates = {
        "body_fins_zero": body_fin_emission == 0,
        "hot_nozzle_isolated": hot_nozzle_emission > 0,
    }
    normal_audit = _normal_map_audit(maps["normal"], width, height)
    gates = {
        "regions_exact_nonoverlap": regions_exact,
        "gutters_all_maps": all(error == 0 for error in gutter_errors.values()),
        "palette": all(palette.values()),
        "emission_containment": all(emission_gates.values()),
        "decoded_normal_valid": normal_audit["valid"],
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "regions": {name: {"uv": list(entry["uv"]), "outer_px": list(entry["outer"]), "inner_px": list(entry["inner"])} for name, entry in rectangles.items()},
        "gutter_errors": gutter_errors,
        "palette": palette,
        "region_stats": region_stats,
        "emission": {"body_fins_nonzero_pixels": body_fin_emission, "hot_nozzle_nonzero_pixels": hot_nozzle_emission, "gates": emission_gates},
        "normal": normal_audit,
    }


def audit_explosion_semantics(rgba, width=128, height=128):
    """Check contiguous yellow fireballs and high-alpha yellow:red ratio."""
    array = _rgba_view(rgba, width, height)
    local_x = (np.arange(32, dtype=np.float64) + 0.5 - 16.0) / 16.0
    local_y = (np.arange(32, dtype=np.float64) + 0.5 - 16.0)[:, None] / 16.0
    radius_grid = np.sqrt(local_x[None, :] * local_x[None, :] + local_y * local_y)
    high_yellow = 0
    high_red = 0
    high_pixels = 0
    core_near_white = 0
    components = []
    edge_green_ratios = []
    for cell_y in range(4):
        for cell_x in range(4):
            visited = set()
            high = set()
            cell = array[cell_y * 32 : cell_y * 32 + 32, cell_x * 32 : cell_x * 32 + 32]
            red = cell[:, :, 0].astype(np.float64)
            green = cell[:, :, 1].astype(np.float64)
            blue = cell[:, :, 2].astype(np.float64)
            alpha = cell[:, :, 3]
            high_mask = alpha >= 128
            high_pixels += int(np.count_nonzero(high_mask))
            high_yellow += int(np.count_nonzero(high_mask & (green >= red * 0.60) & (blue >= red * 0.025)))
            high_red += int(np.count_nonzero(high_mask & (green < red * 0.60) & (red >= 120)))
            core_near_white += int(np.count_nonzero((radius_grid <= 0.34) & high_mask & (red >= 240) & (green >= 220)))
            edge_mask = (alpha >= 32) & (radius_grid >= 0.60)
            edge_green_ratios.extend((green / np.maximum(1.0, red))[edge_mask].tolist())
            high = {(int(px), int(py)) for py, px in np.argwhere(high_mask)}
            component_count = 0
            largest = 0
            for start in high:
                if start in visited:
                    continue
                component_count += 1
                stack = [start]
                visited.add(start)
                size = 0
                while stack:
                    px, py = stack.pop()
                    size += 1
                    for neighbour in ((px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1)):
                        if neighbour in high and neighbour not in visited:
                            visited.add(neighbour)
                            stack.append(neighbour)
                largest = max(largest, size)
            components.append({"count": component_count, "pixels": len(high), "largest": largest})
    yellow_red_ratio = high_yellow / float(max(1, high_red))
    edge_mean_ratio = sum(edge_green_ratios) / float(max(1, len(edge_green_ratios)))
    gates = {
        "alpha_high_pixels": high_pixels > 0,
        "yellow_red_ratio": high_yellow > 0 and yellow_red_ratio >= 3.0,
        "near_white_body": core_near_white > 0,
        "contiguous_body": all(component["count"] == 1 and component["largest"] >= 24 for component in components),
        "subdued_orange_edge": bool(edge_green_ratios) and 0.20 <= edge_mean_ratio <= 0.95,
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "high_alpha_pixels": high_pixels,
        "yellow_pixels": high_yellow,
        "red_pixels": high_red,
        "yellow_red_ratio": yellow_red_ratio,
        "near_white_core_pixels": core_near_white,
        "components": components,
        "edge_green_ratio": [min(edge_green_ratios), max(edge_green_ratios), edge_mean_ratio] if edge_green_ratios else [0.0, 0.0, 0.0],
    }


def run_semantic_audits(generated, frames=None, selected_families=None):
    """Run only semantic checks whose family buffers were selected/generated."""
    selected = set(selected_families or FAMILY_IDS)
    checks = {}
    if "ball" in selected and "RetroBall" in generated and frames is not None:
        checks["ball"] = audit_ball_layout(
            generated["RetroBall"]["buffer"], generated["RetroBall_Normal"]["buffer"], generated["RetroBall_MetallicSmoothness"]["buffer"], generated["RetroBall_Occlusion"]["buffer"], frames
        )
    if "rocket" in selected and "RetroRocket" in generated:
        checks["rocket"] = audit_rocket_atlas(
            {
                "base": generated["RetroRocket"]["buffer"],
                "normal": generated["RetroRocket_Normal"]["buffer"],
                "metallic": generated["RetroRocket_MetallicSmoothness"]["buffer"],
                "occlusion": generated["RetroRocket_Occlusion"]["buffer"],
                "emission": generated["RetroRocket_Emission"]["buffer"],
            }
        )
    if "explosion" in selected and "RetroExplosion" in generated:
        checks["explosion"] = audit_explosion_semantics(generated["RetroExplosion"]["buffer"])
    if "wall" in selected and "RetroWall_MetallicSmoothness" in generated:
        wall_metallic = generated["RetroWall_MetallicSmoothness"]["buffer"]
        wall_width, wall_height = generated["RetroWall_MetallicSmoothness"]["dimensions"]
        wall_metallic_r_max = int(np.max(_rgba_view(wall_metallic, wall_width, wall_height)[:, :, 0]))
        checks["wall"] = {
            "pass": wall_metallic_r_max == 0,
            "gates": {"concrete_nonmetallic": wall_metallic_r_max == 0},
            "metallic_r_max": wall_metallic_r_max,
        }
    normal_maps = {}
    for name, entry in generated.items():
        if entry["map_type"] == "normal":
            width, height = entry["dimensions"]
            normal_maps[name] = _normal_map_audit(entry["buffer"], width, height)
    normal_pass = all(result["valid"] for result in normal_maps.values())
    ball_nonmetallic = checks.get("ball", {}).get("gates", {}).get("nonmetallic", True)
    wall_nonmetallic = checks.get("wall", {}).get("gates", {}).get("concrete_nonmetallic", True)
    pbr = {"pass": normal_pass and ball_nonmetallic and wall_nonmetallic, "normal_maps": normal_maps, "nonmetallic_ball": ball_nonmetallic, "nonmetallic_concrete": wall_nonmetallic}
    checks["pbr_channels"] = pbr
    failed = []
    for name, result in checks.items():
        if not result["pass"]:
            failed.append(name)
    return {"pass": not failed, "failed": failed, "checks": checks}


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


def make_previews(outputs, selected_families=None):
    """Create only previews owned by selected families.

    ``outputs`` is intentionally a generated-buffer mapping, so targeted runs
    cannot accidentally dereference or write an unselected family.
    """
    selected = set(selected_families or FAMILY_IDS)
    previews = {}

    def add(name, width, height, rgba):
        encoded = encode_png(width, height, rgba)
        path = save_png(name, width, height, rgba, encoded)
        previews[name + ".png"] = {
            "path": path,
            "dimensions": [width, height],
            "sha256": hashlib.sha256(encoded).hexdigest(),
        }

    for index, (family_id, family) in enumerate(
        (("grass", "RetroGrass"), ("wall", "RetroWall"), ("trim", "RetroTrim"), ("hazard", "RetroHazard")), 1
    ):
        if family_id not in selected or family not in outputs:
            continue
        base = outputs[family]["buffer"]
        add(f"{index:02d}_{family.lower()}_tile", 256, 256, resize_nearest(base, outputs[family]["dimensions"][0], outputs[family]["dimensions"][1], 256, 256))
        pbr = render_pbr_sphere(base, outputs[family + "_Normal"]["buffer"], outputs[family + "_MetallicSmoothness"]["buffer"], outputs[family + "_Occlusion"]["buffer"], outputs[family]["dimensions"][0], outputs[family]["dimensions"][1], 256, index * 0.57)
        add(f"{index:02d}_{family.lower()}_pbr_ball", 256, 256, pbr)
    if "detail-normal" in selected and "RetroDetailNormal" in outputs:
        add("05_detail_normal", 256, 256, resize_nearest(outputs["RetroDetailNormal"]["buffer"], 512, 512, 256, 256))

    if "ball" in selected and "RetroBall" in outputs:
        ball = outputs["RetroBall"]
        pbr_ball = render_pbr_sphere(ball["buffer"], outputs["RetroBall_Normal"]["buffer"], outputs["RetroBall_MetallicSmoothness"]["buffer"], outputs["RetroBall_Occlusion"]["buffer"], 1024, 512, 256, 0.24)
        add("06_ball_pbr", 256, 256, pbr_ball)
        card_w, card_h, card = ball_cardinals(ball["buffer"])
        add("07_ball_cardinals", card_w, card_h, card)

    if "rocket" in selected and "RetroRocket" in outputs:
        rocket = outputs["RetroRocket"]
        add("08_rocket_atlas", 512, 512, resize_nearest(rocket["buffer"], 1024, 1024, 512, 512))
        add("09_rocket_pbr_ball", 256, 256, render_pbr_sphere(rocket["buffer"], outputs["RetroRocket_Normal"]["buffer"], outputs["RetroRocket_MetallicSmoothness"]["buffer"], outputs["RetroRocket_Occlusion"]["buffer"], 1024, 1024, 256, 0.71))
        add("10_rocket_emission", 512, 512, resize_nearest(outputs["RetroRocket_Emission"]["buffer"], 1024, 1024, 512, 512))
    for index, (family_id, family) in enumerate((("weapon-metal", "RetroWeaponMetal"), ("weapon-dark", "RetroWeaponDark"), ("weapon-accent", "RetroWeaponAccent")), 11):
        if family_id not in selected or family not in outputs:
            continue
        add(f"{index:02d}_{family.lower()}_tile", 256, 256, resize_nearest(outputs[family]["buffer"], 2048, 2048, 256, 256))
        add(f"{index:02d}_{family.lower()}_pbr_ball", 256, 256, render_pbr_sphere(outputs[family]["buffer"], outputs[family + "_Normal"]["buffer"], outputs[family + "_MetallicSmoothness"]["buffer"], outputs[family + "_Occlusion"]["buffer"], 2048, 2048, 256, index * 0.22))

    if "rocket-glow" in selected and "RetroRocketGlow" in outputs:
        glow_w, glow_h, glow_preview = alpha_overlay(outputs["RetroRocketGlow"]["buffer"], 128, 128, 3)
        add("17_rocket_glow_alpha", glow_w, glow_h, glow_preview)
    if "explosion" in selected and "RetroExplosion" in outputs:
        exp_w, exp_h, exp_preview = alpha_overlay(outputs["RetroExplosion"]["buffer"], 128, 128, 2)
        add("18_explosion_sheet_alpha", exp_w, exp_h, exp_preview)
    if "smoke" in selected and "RetroSmoke" in outputs:
        smoke_w, smoke_h, smoke_preview = alpha_overlay(outputs["RetroSmoke"]["buffer"], 128, 128, 2)
        add("19_smoke_sheet_alpha", smoke_w, smoke_h, smoke_preview)
    if "shield" in selected and "RetroShield" in outputs:
        add("20_shield_alpha", 256, 256, alpha_overlay(outputs["RetroShield"]["buffer"], 128, 128, 2)[2])
    if "sky" in selected and "RetroSunnySky" in outputs:
        add("21_sunny_sky_panorama", 512, 256, resize_nearest(outputs["RetroSunnySky"]["buffer"], 2048, 1024, 512, 256))
    return previews


FAMILY_REGISTRY = {
    "grass": {"outputs": ("RetroGrass", "RetroGrass_Normal", "RetroGrass_MetallicSmoothness", "RetroGrass_Occlusion"), "previews": ("01_retrograss_tile.png", "01_retrograss_pbr_ball.png"), "expected_outputs": 4, "expected_previews": 2},
    "wall": {"outputs": ("RetroWall", "RetroWall_Normal", "RetroWall_MetallicSmoothness", "RetroWall_Occlusion"), "previews": ("02_retrowall_tile.png", "02_retrowall_pbr_ball.png"), "expected_outputs": 4, "expected_previews": 2},
    "trim": {"outputs": ("RetroTrim", "RetroTrim_Normal", "RetroTrim_MetallicSmoothness", "RetroTrim_Occlusion"), "previews": ("03_retrotrim_tile.png", "03_retrotrim_pbr_ball.png"), "expected_outputs": 4, "expected_previews": 2},
    "hazard": {"outputs": ("RetroHazard", "RetroHazard_Normal", "RetroHazard_MetallicSmoothness", "RetroHazard_Occlusion"), "previews": ("04_retrohazard_tile.png", "04_retrohazard_pbr_ball.png"), "expected_outputs": 4, "expected_previews": 2},
    "detail-normal": {"outputs": ("RetroDetailNormal",), "previews": ("05_detail_normal.png",), "expected_outputs": 1, "expected_previews": 1},
    "weapon-metal": {"outputs": ("RetroWeaponMetal", "RetroWeaponMetal_Normal", "RetroWeaponMetal_MetallicSmoothness", "RetroWeaponMetal_Occlusion"), "previews": ("11_retroweaponmetal_tile.png", "11_retroweaponmetal_pbr_ball.png"), "expected_outputs": 4, "expected_previews": 2},
    "weapon-dark": {"outputs": ("RetroWeaponDark", "RetroWeaponDark_Normal", "RetroWeaponDark_MetallicSmoothness", "RetroWeaponDark_Occlusion"), "previews": ("12_retroweapondark_tile.png", "12_retroweapondark_pbr_ball.png"), "expected_outputs": 4, "expected_previews": 2},
    "weapon-accent": {"outputs": ("RetroWeaponAccent", "RetroWeaponAccent_Normal", "RetroWeaponAccent_MetallicSmoothness", "RetroWeaponAccent_Occlusion", "RetroWeaponAccent_Emission"), "previews": ("13_retroweaponaccent_tile.png", "13_retroweaponaccent_pbr_ball.png"), "expected_outputs": 5, "expected_previews": 2},
    "ball": {"outputs": ("RetroBall", "RetroBall_Normal", "RetroBall_MetallicSmoothness", "RetroBall_Occlusion"), "previews": ("06_ball_pbr.png", "07_ball_cardinals.png"), "expected_outputs": 4, "expected_previews": 2},
    "rocket": {"outputs": ("RetroRocket", "RetroRocket_Normal", "RetroRocket_MetallicSmoothness", "RetroRocket_Occlusion", "RetroRocket_Emission"), "previews": ("08_rocket_atlas.png", "09_rocket_pbr_ball.png", "10_rocket_emission.png"), "expected_outputs": 5, "expected_previews": 3},
    "rocket-glow": {"outputs": ("RetroRocketGlow",), "previews": ("17_rocket_glow_alpha.png",), "expected_outputs": 1, "expected_previews": 1},
    "smoke": {"outputs": ("RetroSmoke",), "previews": ("19_smoke_sheet_alpha.png",), "expected_outputs": 1, "expected_previews": 1},
    "explosion": {"outputs": ("RetroExplosion",), "previews": ("18_explosion_sheet_alpha.png",), "expected_outputs": 1, "expected_previews": 1},
    "shield": {"outputs": ("RetroShield",), "previews": ("20_shield_alpha.png",), "expected_outputs": 1, "expected_previews": 1},
    "sky": {"outputs": ("RetroSunnySky",), "previews": ("21_sunny_sky_panorama.png",), "expected_outputs": 1, "expected_previews": 1},
}
for _family_entry in FAMILY_REGISTRY.values():
    _family_entry["generator"] = "_family_buffers"
    _family_entry["preview"] = "make_previews"
    _family_entry["semantic_audit"] = "run_semantic_audits"
FAMILY_IDS = tuple(FAMILY_REGISTRY)
FULL_OUTPUT_COUNT = sum(entry["expected_outputs"] for entry in FAMILY_REGISTRY.values())
FULL_PREVIEW_COUNT = sum(entry["expected_previews"] for entry in FAMILY_REGISTRY.values())


def resolve_families(values=None):
    """Resolve repeatable ``--family`` values to a stable registry order."""
    if not values or "all" in values:
        if values and any(value != "all" for value in values):
            raise ValueError("--family all cannot be combined with another family")
        return FAMILY_IDS
    unknown = [value for value in values if value not in FAMILY_REGISTRY]
    if unknown:
        raise ValueError(f"Unknown texture family: {', '.join(unknown)}")
    selected = set(values)
    return tuple(family_id for family_id in FAMILY_IDS if family_id in selected)


def _family_buffers(family_id):
    """Generate one registry family as ``(name,width,height,buffer,kind,flags...)`` records."""
    records = []
    frames = None

    def record(name, width, height, buffer, map_type="base", seam_u=False, seam_v=False, poles=False, alpha_range=None):
        records.append((name, width, height, buffer, map_type, seam_u, seam_v, poles, alpha_range))

    if family_id in ("grass", "wall", "trim", "hazard"):
        kind = family_id
        base, normal, metallic, ao = generate_surface_maps(kind, 1024, 1024)
        prefix = "Retro" + family_id.capitalize()
        record(prefix, 1024, 1024, base, "base", True, True)
        record(prefix + "_Normal", 1024, 1024, normal, "normal", True, True)
        record(prefix + "_MetallicSmoothness", 1024, 1024, metallic, "metallic", True, True)
        record(prefix + "_Occlusion", 1024, 1024, ao, "ao", True, True)
    elif family_id == "detail-normal":
        record("RetroDetailNormal", 512, 512, generate_detail_normal(), "normal", True, True)
    elif family_id in ("weapon-metal", "weapon-dark", "weapon-accent"):
        kind = family_id.removeprefix("weapon-")
        prefix = "RetroWeapon" + kind.capitalize()
        base, normal, metallic, ao = generate_surface_maps(kind, 2048, 2048)
        record(prefix, 2048, 2048, base, "base", True, True)
        record(prefix + "_Normal", 2048, 2048, normal, "normal", True, True)
        record(prefix + "_MetallicSmoothness", 2048, 2048, metallic, "metallic", True, True)
        record(prefix + "_Occlusion", 2048, 2048, ao, "ao", True, True)
        if family_id == "weapon-accent":
            base_array = _rgba_view(base, 2048, 2048)
            emission = _rgba_array(2048, 2048, (0, 0, 0, 255))
            bright = ((np.arange(2048, dtype=np.int64) % 96) >= 48) & ((np.arange(2048, dtype=np.int64) % 96) < 56)
            emission[:, :, :3] = np.where(bright[None, :, None], base_array[:, :, :3], 0)
            close_repeat_edges(emission, 2048, 2048)
            record(prefix + "_Emission", 2048, 2048, emission, "emission", True, True)
    elif family_id == "ball":
        ball_base, ball_normal, ball_metallic, ball_ao, frames = generate_ball_maps()
        record("RetroBall", 1024, 512, ball_base, "base", True, False, True)
        record("RetroBall_Normal", 1024, 512, ball_normal, "normal", True, False, True)
        record("RetroBall_MetallicSmoothness", 1024, 512, ball_metallic, "metallic", True, False, True)
        record("RetroBall_Occlusion", 1024, 512, ball_ao, "ao", True, False, True)
    elif family_id == "rocket":
        rocket_maps = generate_rocket_maps()
        record("RetroRocket", 1024, 1024, rocket_maps["base"])
        record("RetroRocket_Normal", 1024, 1024, rocket_maps["normal"], "normal")
        record("RetroRocket_MetallicSmoothness", 1024, 1024, rocket_maps["metallic"], "metallic")
        record("RetroRocket_Occlusion", 1024, 1024, rocket_maps["occlusion"], "ao")
        record("RetroRocket_Emission", 1024, 1024, rocket_maps["emission"], "emission")
    elif family_id == "rocket-glow":
        record("RetroRocketGlow", 128, 128, generate_glow(), "vfx", alpha_range=(0, 128))
    elif family_id == "smoke":
        record("RetroSmoke", 128, 128, generate_sprite_sheet("smoke"), "vfx", alpha_range=(0, 128))
    elif family_id == "explosion":
        record("RetroExplosion", 128, 128, generate_sprite_sheet("explosion"), "vfx", alpha_range=(0, 128))
    elif family_id == "shield":
        record("RetroShield", 128, 128, generate_shield(), "vfx", alpha_range=(16, 220))
    elif family_id == "sky":
        record("RetroSunnySky", 2048, 1024, generate_sky(), "sky", True, False)
    else:
        raise ValueError(f"Unknown texture family: {family_id}")
    return records, frames


def _relative_path(path):
    return os.path.relpath(path, REPOSITORY_ROOT).replace(os.sep, "/")


def _source_sha256():
    with open(__file__, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def _manifest_versions():
    system_zlib = _load_system_zlib()
    return {
        "python": platform.python_version(),
        "numpy": np.__version__,
        "zlib": getattr(zlib, "ZLIB_VERSION", "unknown"),
        "zlib_runtime": getattr(zlib, "ZLIB_RUNTIME_VERSION", getattr(zlib, "ZLIB_VERSION", "unknown")),
        "png_compressor": system_zlib[1] if system_zlib else getattr(zlib, "ZLIB_VERSION", "unknown"),
    }


def _canonical_manifest_bytes(manifest):
    """Serialize manifest data with the exact bytes persisted by the generator."""
    return json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=True).encode("utf-8") + b"\n"


def _first_manifest_difference(left, right, path=""):
    """Return the first deterministic field/path whose canonical values differ."""
    if isinstance(left, dict) and isinstance(right, dict):
        for key in sorted(set(left) | set(right), key=str):
            child_path = f"{path}.{key}" if path else str(key)
            if key not in left:
                return child_path, None, right[key]
            if key not in right:
                return child_path, left[key], None
            difference = _first_manifest_difference(left[key], right[key], child_path)
            if difference is not None:
                return difference
        return None
    if isinstance(left, list) and isinstance(right, list):
        for index in range(max(len(left), len(right))):
            child_path = f"{path}[{index}]"
            if index >= len(left):
                return child_path, None, right[index]
            if index >= len(right):
                return child_path, left[index], None
            difference = _first_manifest_difference(left[index], right[index], child_path)
            if difference is not None:
                return difference
        return None
    if left != right or type(left) is not type(right):
        return path or "<root>", left, right
    return None


def compare_manifest_content(first, second):
    """Compare canonical manifest bytes and report the first differing field/path."""
    first_bytes = _canonical_manifest_bytes(first)
    second_bytes = _canonical_manifest_bytes(second)
    if first_bytes == second_bytes:
        return True
    # Normalize through JSON so the reported path matches the canonical bytes
    # even when a caller supplied equivalent tuple/list values in memory.
    first_value = json.loads(first_bytes.decode("utf-8"))
    second_value = json.loads(second_bytes.decode("utf-8"))
    difference = _first_manifest_difference(first_value, second_value)
    if difference is None:  # pragma: no cover - defensive; bytes differ implies a value difference.
        raise RuntimeError("manifest mismatch: canonical bytes differ")
    path, left, right = difference
    raise RuntimeError(f"manifest mismatch: {path} ({left!r} != {right!r})")


def _write_json_if_changed(path, value):
    encoded = _canonical_manifest_bytes(value)
    if os.path.isfile(path):
        with open(path, "rb") as handle:
            if handle.read() == encoded:
                return False
    with open(path, "wb") as handle:
        handle.write(encoded)
    return True


def _build_generated(selected_families):
    generated = {}
    frames = None
    for family_id in selected_families:
        records, family_frames = _family_buffers(family_id)
        for name, width, height, buffer, map_type, seam_u, seam_v, poles, alpha_range in records:
            audit = audit_texture(name, width, height, buffer, (width, height), seam_u, seam_v, poles, alpha_range, map_type)
            encoded = encode_png(width, height, buffer)
            path = save_png(name, width, height, buffer, encoded)
            generated[name] = {
                "path": path,
                "dimensions": [width, height],
                "channels": "RGBA8",
                "buffer": buffer,
                "map_type": map_type,
                "sha256": hashlib.sha256(encoded).hexdigest(),
                "audit": audit,
            }
        if family_id == "ball":
            frames = family_frames
    # Avoid a second ball generation while retaining the frame set used by its audit.
    if "ball" in selected_families and frames is None:
        frames = ball_centers()
    return generated, frames


def _serializable_outputs(generated):
    outputs = {}
    for name, entry in generated.items():
        outputs[name] = {key: value for key, value in entry.items() if key != "buffer"}
        outputs[name]["path"] = _relative_path(outputs[name]["path"])
    return outputs


def _serializable_previews(previews):
    serializable = {}
    for name, entry in previews.items():
        serializable[name] = dict(entry)
        serializable[name]["path"] = _relative_path(serializable[name]["path"])
    return serializable


def _run_once(selected_families, write_manifest=True):
    os.makedirs(TEXTURE_DIRECTORY, exist_ok=True)
    os.makedirs(PREVIEW_DIRECTORY, exist_ok=True)
    started = time.perf_counter()
    generated, frames = _build_generated(selected_families)
    previews = make_previews(generated, selected_families)
    outputs = _serializable_outputs(generated)
    previews_manifest = _serializable_previews(previews)
    memory_mib = estimate_compressed_memory(outputs)
    semantic_audit = run_semantic_audits(generated, frames, selected_families)
    full = tuple(selected_families) == FAMILY_IDS
    expected_outputs = sum(FAMILY_REGISTRY[family_id]["expected_outputs"] for family_id in selected_families)
    expected_previews = sum(FAMILY_REGISTRY[family_id]["expected_previews"] for family_id in selected_families)
    memory_pass = memory_mib <= 96.0
    contract_pass = (len(generated) == FULL_OUTPUT_COUNT and len(previews) == FULL_PREVIEW_COUNT and memory_pass) if full else True
    overall_pass = semantic_audit["pass"] and len(generated) == expected_outputs and len(previews) == expected_previews and contract_pass
    manifest = {
        "generator": "Tools/Blender/generate_retro_textures.py",
        "seed": SEED,
        "source_sha256": _source_sha256(),
        "versions": _manifest_versions(),
        "selected_families": list(selected_families),
        "family_registry": {family_id: {key: value for key, value in FAMILY_REGISTRY[family_id].items()} for family_id in FAMILY_IDS},
        "outputs": outputs,
        "previews": previews_manifest,
        "counts": {"outputs": len(outputs), "previews": len(previews)},
        "semantic_audit": semantic_audit,
        "memory_forecast": {
            "format": "BC1/BC4-like 8-byte blocks for opaque base/AO/emission/sky; BC5/BC7/BC3-like 16-byte blocks for normal/metallic/VFX; full mips ~= 4/3",
            "compressed_mib": round(memory_mib, 3),
            "budget_mib": 96.0,
            "within_budget": memory_pass,
            "asserted": full,
        },
        "contract": {
            "full_run": full,
            "full_output_count": FULL_OUTPUT_COUNT if full else None,
            "full_preview_count": FULL_PREVIEW_COUNT if full else None,
            "selected_expected_outputs": expected_outputs,
            "selected_expected_previews": expected_previews,
            "canonical_manifest": full,
        },
        "ball": {"centers": [list(frame[0]) for frame in frames], "center_count": len(frames), "unit_tolerance": 1e-6, "u_wrap": True, "v_wrap": False} if frames else None,
        "status": "PASS" if overall_pass else "FAIL",
    }
    elapsed_seconds = round(time.perf_counter() - started, 6)
    if write_manifest:
        manifest_name = "retro_texture_manifest.json" if full else "retro_texture_manifest_targeted.json"
        _write_json_if_changed(os.path.join(PREVIEW_DIRECTORY, manifest_name), manifest)
    if not overall_pass:
        raise RuntimeError(f"RetroTextures semantic/audit contract failed: {semantic_audit['failed']}")
    # Timing is useful evidence for callers, but intentionally remains outside
    # the persisted/canonical manifest so proof bytes stay stable across runs.
    return {"generated": generated, "previews": previews, "manifest": manifest, "elapsed_seconds": elapsed_seconds}


def _hash_map(entries):
    if isinstance(entries, dict):
        return {name: (value.get("sha256") if isinstance(value, dict) else value) for name, value in entries.items()}
    return dict(entries)


def compare_hash_maps(first, second, category="texture"):
    """Compare sorted named hashes and fail on the first deterministic mismatch."""
    left, right = _hash_map(first), _hash_map(second)
    for name in sorted(set(left) | set(right)):
        if left.get(name) != right.get(name):
            raise RuntimeError(f"{category} hash mismatch: {name} ({left.get(name)} != {right.get(name)})")
    return True


def compare_generation_runs(first, second):
    compare_hash_maps(first["manifest"]["outputs"], second["manifest"]["outputs"], "output")
    compare_hash_maps(first["manifest"]["previews"], second["manifest"]["previews"], "preview")
    compare_manifest_content(first["manifest"], second["manifest"])
    return True


def build_argument_parser():
    parser = argparse.ArgumentParser(description="Generate deterministic Rocket Fooxball PBR textures.")
    parser.add_argument("--family", action="append", choices=("all",) + FAMILY_IDS, help="Generate one family; repeatable. Default/all selects the full registry.")
    parser.add_argument("--proof-two-run", action="store_true", help="Run the full selection twice and compare all named hashes.")
    return parser


def main(argv=None):
    args = build_argument_parser().parse_args(argv)
    selected = resolve_families(args.family)
    if args.proof_two_run and tuple(selected) != FAMILY_IDS:
        raise SystemExit("--proof-two-run requires full selection (omit --family or use --family all)")
    result = _run_once(selected, write_manifest=not args.proof_two_run)
    if args.proof_two_run:
        second = _run_once(selected, write_manifest=False)
        compare_generation_runs(result, second)
        _write_json_if_changed(os.path.join(PREVIEW_DIRECTORY, "retro_texture_manifest.json"), second["manifest"])
        result = second
        print(f"PROOF RetroTextures: PASS ({FULL_OUTPUT_COUNT} output hashes, {FULL_PREVIEW_COUNT} preview hashes)")
    print(f"MEMORY RetroTextures: compressed forecast {result['manifest']['memory_forecast']['compressed_mib']:.3f} MiB / 96.000 MiB")
    print(f"AUDIT RetroTextures: PASS ({result['manifest']['counts']['outputs']} outputs, {result['manifest']['counts']['previews']} previews, families={','.join(selected)}, seed {SEED})")
    return result


if __name__ == "__main__":
    main()
