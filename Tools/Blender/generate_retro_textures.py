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
import inspect
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
PREVIEW_DIRECTORY = os.environ.get(
    "ROCKET_FOOXBALL_TEXTURE_EVIDENCE_ROOT",
    os.path.join(REPOSITORY_ROOT, "Temp", "BlenderPreviews", "RetroTextures"),
)
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


NATURAL_SURFACE_CONTRACT = {
    "grass": {
        "construction": "continuous-periodic-field",
        "palette_u8": ((36, 78, 6), (78, 138, 24)),
        "mean_rgb_u8": ((50.0, 100.0, 10.0), (70.0, 122.0, 24.0)),
        "green_dominance_fraction": 0.99,
        "yellow_green_fraction": 0.97,
        "maximum_blue_green_ratio": 0.34,
        "near_white_threshold_u8": 236,
        "maximum_directional_coherence": 0.38,
        "maximum_axis_band_fraction": 0.035,
        "maximum_low_frequency_rms_u8": 6.1,
        "maximum_luminance_span_u8": 50,
        "minimum_microstructure_energy_fraction": 0.30,
        "maximum_microstructure_orientation_fraction": 0.52,
        "metallic": (0.02, 0.04),
        "smoothness": (0.44, 0.52),
        "ao": (0.91, 0.99),
        "motif_inventory": (),
    },
    "wall": {
        "construction": "continuous-periodic-field",
        "palette_u8": ((82, 82, 82), (220, 220, 220)),
        "rgb_spread_max": 0.08,
        "metallic": (0.0, 0.0),
        "smoothness": (0.42, 0.58),
        "ao": (0.91, 0.99),
        "effective_tile": (13, 2),
        "maximum_diagonal_spectral_fraction": 0.40,
        "maximum_diagonal_autocorrelation": 0.72,
        "motif_inventory": (),
    },
}


def _periodic_ellipse_mask(u, v, centre_u, centre_v, radius_u, radius_v):
    du = np.abs(np.mod(u - centre_u + 0.5, 1.0) - 0.5) / radius_u
    dv = np.abs(np.mod(v - centre_v + 0.5, 1.0) - 0.5) / radius_v
    return du * du + dv * dv <= 1.0


def _segment_mask(u, v, segment):
    x0, y0, x1, y1, width = segment
    vx, vy = x1 - x0, y1 - y0
    denominator = vx * vx + vy * vy
    amount = np.clip(((u - x0) * vx + (v - y0) * vy) / denominator, 0.0, 1.0)
    dx = u - (x0 + amount * vx)
    dy = v - (y0 + amount * vy)
    return dx * dx + dy * dy <= width * width


def _balanced_toroidal_field(u, v, layers, u_frequency_scale=1.0):
    """Sum symmetric lattice directions so no layer owns one dominant ridge."""
    shape = np.broadcast_shapes(np.shape(u), np.shape(v))
    field = np.zeros(shape, dtype=np.float64)
    total_weight = 0.0
    phase_offsets = (0.0, 1.71, 3.83, 5.37)
    for frequency_u, frequency_v, phase, amplitude in layers:
        directions = (
            (u_frequency_scale * frequency_u, frequency_v),
            (u_frequency_scale * frequency_v, -frequency_u),
            (u_frequency_scale * frequency_u, -frequency_v),
            (u_frequency_scale * frequency_v, frequency_u),
        )
        symmetric_layer = np.zeros(shape, dtype=np.float64)
        for (direction_u, direction_v), phase_offset in zip(directions, phase_offsets):
            symmetric_layer += np.sin(
                np.float64(math.tau) * (direction_u * u + direction_v * v) + phase + phase_offset
            )
        field += amplitude * symmetric_layer * np.float64(0.25)
        total_weight += amplitude
    return field / np.float64(total_weight)


def _irregular_periodic_mottle(u, v, layers, warp_layers):
    """Build an irregular continuous torus by warping several periodic modes."""
    u = np.mod(np.asarray(u, dtype=np.float64), 1.0)
    v = np.mod(np.asarray(v, dtype=np.float64), 1.0)
    shape = np.broadcast_shapes(u.shape, v.shape)
    warp_u = np.zeros(shape, dtype=np.float64)
    warp_v = np.zeros(shape, dtype=np.float64)
    for frequency_u, frequency_v, phase, amplitude in warp_layers:
        angle = np.float64(math.tau) * (frequency_u * u + frequency_v * v) + phase
        warp_u += np.sin(angle) * np.float64(amplitude)
        warp_v += np.cos(angle) * np.float64(amplitude * 0.83)
    warped_u = u + warp_u
    warped_v = v + warp_v
    field = np.zeros(shape, dtype=np.float64)
    total_weight = 0.0
    for frequency_u, frequency_v, phase, amplitude in layers:
        angle = np.float64(math.tau) * (frequency_u * warped_u + frequency_v * warped_v) + phase
        field += np.sin(angle) * np.float64(amplitude)
        total_weight += amplitude
    return field / np.float64(total_weight)


def _grass_interwoven_fields(u, v):
    """Author seamless yellow-green turf variation with fine crossed blade ridges."""
    u = np.mod(np.asarray(u, dtype=np.float64), 1.0)
    v = np.mod(np.asarray(v, dtype=np.float64), 1.0)
    broad = (
        0.44 * np.sin(np.float64(math.tau) * (2.0 * u + 3.0 * v) + 0.61)
        + 0.31 * np.sin(np.float64(math.tau) * (5.0 * u - 2.0 * v) + 2.34)
        + 0.25 * np.sin(np.float64(math.tau) * (3.0 * u + 7.0 * v) + 4.87)
    )
    broad /= np.float64(1.0)
    patch_frequency = 47.0
    scaled_u = u * patch_frequency
    scaled_v = v * patch_frequency
    patch_u = np.floor(scaled_u)
    patch_v = np.floor(scaled_v)
    local_u = scaled_u - patch_u
    local_v = scaled_v - patch_v
    blade_sum = np.zeros(np.broadcast_shapes(u.shape, v.shape), dtype=np.float64)

    def hash01(cell_u, cell_v, salt):
        value = np.sin(cell_u * 127.1 + cell_v * 311.7 + salt * 74.7) * 43758.5453123
        return value - np.floor(value)

    for offset_v in (-1.0, 0.0, 1.0):
        for offset_u in (-1.0, 0.0, 1.0):
            cell_u = np.mod(patch_u + offset_u, patch_frequency)
            cell_v = np.mod(patch_v + offset_v, patch_frequency)
            for blade_index in (0.0, 1.0, 2.0):
                centre_u = offset_u + 0.10 + 0.80 * hash01(cell_u, cell_v, blade_index + 0.17)
                centre_v = offset_v + 0.10 + 0.80 * hash01(cell_u, cell_v, blade_index + 1.83)
                angle = math.tau * hash01(cell_u, cell_v, blade_index + 3.41)
                half_length = 0.22 + 0.30 * hash01(cell_u, cell_v, blade_index + 5.09)
                half_width = 0.040 + 0.022 * hash01(cell_u, cell_v, blade_index + 6.67)
                direction_u = np.cos(angle)
                direction_v = np.sin(angle)
                relative_u = local_u - centre_u
                relative_v = local_v - centre_v
                along = np.clip(relative_u * direction_u + relative_v * direction_v, -half_length, half_length)
                across_u = relative_u - along * direction_u
                across_v = relative_v - along * direction_v
                across_squared = across_u * across_u + across_v * across_v
                tip_fade = np.cos(math.pi * along / (2.0 * half_length))
                blade_sum += np.exp(-across_squared / (half_width * half_width)) * tip_fade * tip_fade
    blades = np.tanh((blade_sum - 0.14) * 4.2)
    return broad, np.clip(0.5 + broad * 0.5, 0.0, 1.0), blades


def _natural_surface_layers(kind: str, u, v):
    """Return deterministic multi-scale periodic values for a natural surface."""
    if kind not in NATURAL_SURFACE_CONTRACT:
        raise ValueError(f"Unknown natural surface kind: {kind}")
    u = np.mod(np.asarray(u, dtype=np.float64), 1.0)
    v = np.mod(np.asarray(v, dtype=np.float64), 1.0)
    if kind == "grass":
        return _grass_interwoven_fields(u, v)
    else:
        layers = (
            (3.0, 8.0, 1.17, 0.30),
            (8.0, 3.0, 3.09, 0.30),
            (5.0, -14.0, 5.11, 0.176),
            (-14.0, 5.0, 2.63, 0.176),
            (7.0, 19.0, 0.87, 0.13),
            (19.0, 7.0, 4.31, 0.13),
            (11.0, -23.0, 2.21, 0.094),
            (-23.0, 11.0, 5.07, 0.094),
        )
        detail_layers = (
            (17.0, 5.0, 0.41, 0.23), (5.0, 17.0, 2.59, 0.23),
            (13.0, -31.0, 4.27, 0.14), (-31.0, 13.0, 1.18, 0.14),
            (23.0, 47.0, 2.08, 0.10), (47.0, 23.0, 5.33, 0.10),
            (19.0, -61.0, 3.12, 0.06), (-61.0, 19.0, 0.72, 0.06),
        )

    warp_layers = (
        (2.0, 5.0, 0.73, 0.018), (5.0, 2.0, 3.41, 0.018),
        (3.0, -11.0, 5.19, 0.011), (-11.0, 3.0, 1.83, 0.011),
    )
    broad = _irregular_periodic_mottle(u, v, layers, warp_layers)
    detail = _irregular_periodic_mottle(u, v, detail_layers, warp_layers)
    return broad, np.clip(0.5 + broad * 0.5, 0.0, 1.0), detail


def _natural_surface_fields(kind: str, u, v, n=None, n01=None):
    """Derive natural PBR fields from continuous periodic noise without hard masks."""
    if kind not in NATURAL_SURFACE_CONTRACT:
        raise ValueError(f"Unknown natural surface kind: {kind}")
    u = np.asarray(u, dtype=np.float64)
    v = np.asarray(v, dtype=np.float64)
    if n is None or n01 is None:
        n, n01, detail = _natural_surface_layers(kind, u, v)
    else:
        _unused_n, _unused_n01, detail = _natural_surface_layers(kind, u, v)
        n = np.asarray(n, dtype=np.float64)
        n01 = np.asarray(n01, dtype=np.float64)
    tone = np.clip(n01, 0.0, 1.0)
    shape = np.broadcast_shapes(u.shape, v.shape)
    zero = np.zeros(shape, dtype=np.float64)
    if kind == "grass":
        broad_tone = np.clip(0.5 + 0.5 * n, 0.0, 1.0)
        fine_tone = np.clip(0.5 + 0.5 * detail, 0.0, 1.0)
        colour = np.stack(
            (
                np.clip(0.155 + 0.100 * broad_tone + 0.026 * fine_tone, 0.0, 1.0),
                np.clip(0.330 + 0.140 * broad_tone + 0.042 * fine_tone, 0.0, 1.0),
                np.clip(0.028 + 0.035 * broad_tone + 0.008 * fine_tone, 0.0, 1.0),
            ),
            axis=-1,
        )
        height = 0.50 + 0.003 * n + 0.007 * detail
        response = np.clip(0.5 + 0.22 * n + 0.10 * detail, 0.0, 1.0)
        metallic = np.clip(0.02 + 0.02 * response, 0.02, 0.04)
        smoothness = np.clip(0.44 + 0.08 * response, 0.44, 0.52)
        ao = np.clip(0.91 + 0.08 * (0.5 + 0.22 * n + 0.10 * detail), 0.91, 0.99)
    else:
        aggregate = np.clip(0.5 + n * 0.5, 0.0, 1.0)
        pore_tone = np.clip(0.5 + detail * 0.5, 0.0, 1.0)
        neutral = 0.56 + 0.11 * aggregate + 0.008 * (pore_tone - 0.5)
        tint = np.stack((-0.010 + 0.003 * detail, 0.001 * detail, 0.010 - 0.003 * detail), axis=-1)
        colour = np.clip(neutral[..., None] + tint, 0.0, 1.0)
        height = 0.50 + 0.018 * n + 0.006 * detail
        response = np.clip(0.5 + n * 0.22 + detail * 0.04, 0.0, 1.0)
        smoothness = np.clip(0.42 + 0.16 * response, 0.42, 0.58)
        ao = np.clip(0.91 + 0.08 * np.clip(0.5 + n * 0.25 + detail * 0.05, 0.0, 1.0), 0.91, 0.99)
    return colour, height, metallic if kind == "grass" else zero, smoothness, ao, {}


def _natural_surface_height(kind: str, u, v):
    """Return the authored height field before normal encoding."""
    n, _n01, detail = _natural_surface_layers(kind, u, v)
    if kind == "grass":
        return 0.50 + 0.003 * n + 0.007 * detail
    if kind == "wall":
        return 0.50 + 0.018 * n + 0.006 * detail
    raise ValueError(f"Unknown natural surface kind: {kind}")


# Weapon maps are authored from explicit readability targets so the scalar
# Blender path and the NumPy path have one source of truth.  The ranges are
# inclusive authoring bounds; PNG quantization may move a boundary by one u8.
WEAPON_READABILITY_TARGETS = {
    "metal": {
        "palette": ((0.26, 0.19, 0.12), (0.58, 0.44, 0.27), (0.78, 0.60, 0.36)),
        "metallic": 0.65,
        "smoothness": (0.52, 0.74),
        "ao": (0.86, 0.98),
        "luminance": (0.19, 0.36, 0.50),
    },
    "dark": {
        "palette": ((0.10, 0.12, 0.15), (0.22, 0.25, 0.30), (0.32, 0.35, 0.38)),
        "metallic": 0.05,
        "smoothness": (0.38, 0.58),
        "ao": (0.86, 0.98),
        "luminance": (0.11, 0.20, 0.28),
    },
}

WEAPON_ACCENT_CONTRACT = {
    "palette": ((0.18, 0.012, 0.018), (0.58, 0.035, 0.050), (0.96, 0.14, 0.12)),
    "metallic": (0.0, 0.02),
    "smoothness": (0.90, 0.98),
    "ao": (0.94, 0.99),
    "normal_xy_max": 0.16,
    "emission_coverage": (0.055, 0.12),
}

# Weapon clean surfaces use broad periodic lobes instead of the general-purpose
# multi-frequency noise. This keeps the material tileable without competing
# with the authored scratches, chips, polish, and grime.
WEAPON_TONAL_PHASES = {
    "metal": ((1, 0, 0.37, 0.34), (0, 1, 1.13, 0.30), (2, 0, 2.41, 0.19), (0, 2, 3.07, 0.17)),
    "dark": ((1, 0, 1.31, 0.32), (0, 1, 2.17, 0.34), (2, 0, 3.43, 0.16), (0, 2, 0.71, 0.18)),
    "accent": ((1, 0, 2.03, 0.52), (0, 1, 4.11, 0.48)),
}

# Explicit motifs keep weapon wear stable across Python/NumPy versions. Every
# mask below is consumed by base colour, height/normal, metallic, smoothness,
# and AO so the maps describe the same physical damage rather than unrelated
# procedural noise.
WEAPON_WEAR_MOTIFS = {
    "scratches": (
        (0.06, 0.13, 0.22, 0.19, 0.0028), (0.28, 0.31, 0.43, 0.25, 0.0032),
        (0.55, 0.10, 0.72, 0.16, 0.0026), (0.77, 0.34, 0.92, 0.29, 0.0030),
        (0.08, 0.67, 0.24, 0.61, 0.0032), (0.34, 0.88, 0.49, 0.80, 0.0028),
        (0.57, 0.58, 0.71, 0.67, 0.0034), (0.80, 0.84, 0.95, 0.78, 0.0028),
        (0.18, 0.46, 0.29, 0.51, 0.0026), (0.63, 0.42, 0.76, 0.39, 0.0026),
    ),
    "chips": (
        (0.12, 0.22, 0.018, 0.011), (0.31, 0.74, 0.014, 0.020),
        (0.47, 0.37, 0.022, 0.013), (0.66, 0.15, 0.015, 0.018),
        (0.82, 0.58, 0.023, 0.014), (0.91, 0.86, 0.014, 0.019),
        (0.22, 0.91, 0.020, 0.012), (0.71, 0.76, 0.017, 0.015),
    ),
    "polish": (
        (0.18, 0.55, 0.095, 0.065), (0.52, 0.82, 0.080, 0.055),
        (0.83, 0.25, 0.070, 0.105),
    ),
    "grime": (
        (0.07, 0.78, 0.075, 0.105), (0.38, 0.14, 0.090, 0.055),
        (0.58, 0.52, 0.060, 0.090), (0.76, 0.91, 0.105, 0.045),
        (0.94, 0.45, 0.050, 0.085),
    ),
}

WEAPON_FAMILY_OUTPUTS = {
    "weapon-metal": ("RetroWeaponMetal", "RetroWeaponMetal_Normal", "RetroWeaponMetal_MetallicSmoothness", "RetroWeaponMetal_Occlusion"),
    "weapon-dark": ("RetroWeaponDark", "RetroWeaponDark_Normal", "RetroWeaponDark_MetallicSmoothness", "RetroWeaponDark_Occlusion"),
}


def _weapon_wear_masks(u, v):
    shape = np.broadcast_shapes(np.shape(u), np.shape(v))
    masks = {}
    scratches = np.zeros(shape, dtype=bool)
    for motif in WEAPON_WEAR_MOTIFS["scratches"]:
        scratches |= _segment_mask(u, v, motif)
    masks["scratches"] = scratches
    for name in ("chips", "polish", "grime"):
        mask = np.zeros(shape, dtype=bool)
        for motif in WEAPON_WEAR_MOTIFS[name]:
            mask |= _periodic_ellipse_mask(u, v, *motif)
        masks[name] = mask
    return masks


def _weapon_tonal_field(kind, u, v):
    """Return deterministic low-frequency tileable variation in ``[-1, 1]``."""
    u = np.asarray(u, dtype=np.float64)
    v = np.asarray(v, dtype=np.float64)
    value = np.zeros(np.broadcast_shapes(u.shape, v.shape), dtype=np.float64)
    weight = 0.0
    for frequency_u, frequency_v, phase, amplitude in WEAPON_TONAL_PHASES[kind]:
        value += np.sin(np.float64(math.tau) * (frequency_u * u + frequency_v * v) + phase) * amplitude
        weight += amplitude
    return value / weight


def _weapon_surface_fields(kind, u, v):
    """Build one weapon material from shared deterministic physical masks."""
    tonal = _weapon_tonal_field(kind, u, v)
    tonal01 = np.clip(0.5 + tonal * 0.50, 0.0, 1.0)
    if kind == "accent":
        shadow, base_colour, highlight = (np.asarray(value, dtype=np.float64) for value in WEAPON_ACCENT_CONTRACT["palette"])
        value = 0.46 + tonal01 * 0.16
        low_amount = np.clip(value / 0.58, 0.0, 1.0)
        high_amount = np.clip((value - 0.58) / 0.42, 0.0, 1.0)
        low_base = shadow + (base_colour - shadow) * low_amount[..., None]
        high_base = base_colour + (highlight - base_colour) * high_amount[..., None]
        base = np.where((value <= 0.58)[..., None], low_base, high_base)
        pane = np.logical_or(np.mod(u * 8.0, 1.0) < 0.055, np.mod(v * 8.0, 1.0) < 0.055).astype(np.float64)
        base = np.clip(base + pane[..., None] * np.array((0.035, 0.010, 0.012)), 0.0, 1.0)
        height = 0.50 + tonal * 0.004 + pane * 0.006
        metallic = np.full_like(tonal, 0.008)
        smoothness = 0.935 + tonal01 * 0.025
        ao = 0.965 + tonal01 * 0.020
        return base, height, metallic, smoothness, ao, {"pane": pane > 0.0}

    targets = WEAPON_READABILITY_TARGETS[kind]
    shadow, base_colour, highlight = (np.asarray(value, dtype=np.float64) for value in targets["palette"])
    value = 0.30 + tonal01 * 0.44
    low_amount = np.clip(value / 0.58, 0.0, 1.0)
    high_amount = np.clip((value - 0.58) / 0.42, 0.0, 1.0)
    low_base = shadow + (base_colour - shadow) * low_amount[..., None]
    high_base = base_colour + (highlight - base_colour) * high_amount[..., None]
    base = np.where((value <= 0.58)[..., None], low_base, high_base)
    grid = np.logical_or(np.mod(u * 16.0, 1.0) < 0.025, np.mod(v * 16.0, 1.0) < 0.025)
    base = base * (1.0 - grid[..., None] * 0.30) + shadow * (grid[..., None] * 0.30)
    masks = _weapon_wear_masks(u, v)
    scratches, chips, polish, grime = (masks[name] for name in ("scratches", "chips", "polish", "grime"))

    if kind == "metal":
        base = np.where(scratches[..., None], np.array((0.84, 0.72, 0.55)), base)
        base = np.where(chips[..., None], np.array((0.66, 0.61, 0.52)), base)
        base = np.where(polish[..., None], np.minimum(1.0, base * 1.17 + 0.025), base)
        base = np.where(grime[..., None], base * np.array((0.50, 0.46, 0.40)), base)
        metallic = np.full_like(tonal, 0.65)
        metallic = np.where(scratches, 0.86, metallic)
        metallic = np.where(chips, 0.78, metallic)
        metallic = np.where(polish, 0.75, metallic)
        metallic = np.where(grime, 0.48, metallic)
        smoothness = 0.56 + tonal01 * 0.12
        smoothness = np.where(scratches, 0.36, smoothness)
        smoothness = np.where(chips, 0.33, smoothness)
        smoothness = np.where(polish, 0.88, smoothness)
        smoothness = np.where(grime, 0.26, smoothness)
    else:
        base = np.where(scratches[..., None], np.array((0.56, 0.54, 0.50)), base)
        base = np.where(chips[..., None], np.array((0.45, 0.43, 0.40)), base)
        base = np.where(polish[..., None], np.minimum(1.0, base * 1.24 + 0.018), base)
        base = np.where(grime[..., None], base * np.array((0.48, 0.50, 0.52)), base)
        metallic = np.full_like(tonal, 0.05)
        metallic = np.where(scratches, 0.68, metallic)
        metallic = np.where(chips, 0.62, metallic)
        metallic = np.where(polish, 0.12, metallic)
        metallic = np.where(grime, 0.02, metallic)
        smoothness = 0.42 + tonal01 * 0.10
        smoothness = np.where(scratches, 0.62, smoothness)
        smoothness = np.where(chips, 0.40, smoothness)
        smoothness = np.where(polish, 0.82, smoothness)
        smoothness = np.where(grime, 0.24, smoothness)

    height = 0.50 + tonal * 0.018 + grid * 0.012
    height = height - scratches * 0.040 - chips * 0.026 + polish * 0.010 - grime * 0.007
    ao = 0.92 + tonal01 * 0.05
    ao = np.where(scratches, 0.70, ao)
    ao = np.where(chips, 0.66, ao)
    ao = np.where(polish, 0.98, ao)
    ao = np.where(grime, 0.62, ao)
    return base, height, metallic, smoothness, ao, {**masks, "grid": grid}


def _surface_fields(kind: str, u: float, v: float):
    """Return base RGB, height, metallic, smoothness, AO for one tiled material."""
    u %= 1.0
    v %= 1.0
    if kind in NATURAL_SURFACE_CONTRACT:
        u_array = np.asarray(u, dtype=np.float64)
        v_array = np.asarray(v, dtype=np.float64)
        n, n01, _detail = _natural_surface_layers(kind, u_array, v_array)
        values = _natural_surface_fields(kind, u_array, v_array, n, n01)
        base, height, metallic, smoothness, ao, _masks = values
        return tuple(float(value) for value in base), float(height), float(metallic), float(smoothness), float(ao)
    phases = SURFACE_PHASES[kind]
    n = periodic_noise(u, v, phases)
    n01 = clamp01(0.5 + n * 0.50)
    if kind == "grass":
        stripe = 0.5 + 0.5 * math.sin(math.tau * (u * 8.0))
        panel = 1.0 if (u * 8.0) % 1.0 < 0.035 or (v * 8.0) % 1.0 < 0.035 else 0.0
        base = (mix(0.018, 0.055, n01), mix(0.20, 0.43, n01), mix(0.20, 0.36, n01))
        base = tuple(mix(value, value + 0.10, stripe * 0.16) for value in base)
        return base, 0.47 + n * 0.06 - panel * 0.08, 0.04 + panel * 0.18, 0.48 + n01 * 0.18, 0.72 - panel * 0.20
    if kind == "wall":
        seam = 1.0 if (u * 16.0) % 1.0 < 0.028 or (v * 16.0) % 1.0 < 0.028 else 0.0
        base = (mix(0.46, 0.76, n01), mix(0.53, 0.80, n01), mix(0.51, 0.72, n01))
        base = tuple(mix(value, (0.06, 0.40, 0.47)[i], seam * 0.62) for i, value in enumerate(base))
        return base, 0.50 + n * 0.07 - seam * 0.10, 0.0, 0.46 + n01 * 0.20, 0.77 - seam * 0.24
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
    if kind in WEAPON_READABILITY_TARGETS:
        shadow, base_colour, highlight = WEAPON_READABILITY_TARGETS[kind]["palette"]
    else:
        # Accent remains on its existing authored contract.
        shadow, base_colour, highlight = ((0.16, 0.012, 0.008), (0.52, 0.040, 0.018), (0.90, 0.17, 0.028))
    value = round(n01 * 8.0) / 8.0
    if kind in WEAPON_READABILITY_TARGETS:
        # Use all three palette stops.  The old weapon colours never reached
        # their highlight because the base stop was below the 0.58 threshold.
        if value <= 0.58:
            palette_amount = value / 0.58
            base = tuple(mix(shadow[i], base_colour[i], palette_amount) for i in range(3))
        else:
            palette_amount = (value - 0.58) / 0.42
            base = tuple(mix(base_colour[i], highlight[i], palette_amount) for i in range(3))
    else:
        base = tuple(mix(shadow[i], base_colour[i], value) for i in range(3))
        base = tuple(mix(value, highlight[i], max(0.0, value - 0.58) / 0.42) for i, value in enumerate(base))
    grid = 1.0 if (u * 16.0) % 1.0 < 0.025 or (v * 16.0) % 1.0 < 0.025 else 0.0
    base = tuple(mix(value, shadow[i], grid * 0.45) for i, value in enumerate(base))
    height = 0.50 + n * 0.09 + grid * 0.035
    if kind in WEAPON_READABILITY_TARGETS:
        targets = WEAPON_READABILITY_TARGETS[kind]
        smooth_min, smooth_max = targets["smoothness"]
        metallic = targets["metallic"]
        smoothness = smooth_min + n01 * (smooth_max - smooth_min)
        ao_min, ao_max = targets["ao"]
        # Keep seam relief while staying inside the declared AO range.
        ao_range = ao_max - ao_min
        ao = ao_min + ao_range * 0.25 + n01 * ao_range * 0.75 - grid * ao_range * 0.25
        return base, height, metallic, smoothness, ao
    return base, height, 0.52, 0.64 + n01 * 0.25, 0.88 - grid * 0.16


def _surface_normal(kind: str, u: float, v: float):
    if kind in NATURAL_SURFACE_CONTRACT:
        delta = 1.0 / 2048.0
        height_u0 = float(_natural_surface_height(kind, u - delta, v))
        height_u1 = float(_natural_surface_height(kind, u + delta, v))
        height_v0 = float(_natural_surface_height(kind, u, v - delta))
        height_v1 = float(_natural_surface_height(kind, u, v + delta))
        slope_u = (height_u1 - height_u0) / (2.0 * delta)
        slope_v = (height_v1 - height_v0) / (2.0 * delta)
        tangent = np.asarray((-0.075 * slope_u, -0.075 * slope_v, 1.0), dtype=np.float64)
        tangent /= np.linalg.norm(tangent)
        return tuple(float(value * 0.5 + 0.5) for value in tangent)
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
    if kind in NATURAL_SURFACE_CONTRACT:
        n, n01, _detail = _natural_surface_layers(kind, u, v)
        base, height, metallic, smoothness, ao, _masks = _natural_surface_fields(kind, u, v, n, n01)
        return base, height, metallic, smoothness, ao
    n = _periodic_noise_array(kind, u, v)
    n01 = np.clip(0.5 + n * 0.50, 0.0, 1.0)
    shape = np.broadcast_shapes(u.shape, v.shape)
    zero = np.zeros(shape, dtype=np.float64)
    if kind == "grass":
        stripe = 0.5 + 0.5 * np.sin(np.float64(math.tau) * (u * 8.0))
        panel = np.logical_or(np.mod(u * 8.0, 1.0) < 0.035, np.mod(v * 8.0, 1.0) < 0.035).astype(np.float64)
        base = np.stack((0.018 + (0.055 - 0.018) * n01, 0.20 + (0.43 - 0.20) * n01, 0.20 + (0.36 - 0.20) * n01), axis=-1)
        base = base + stripe[..., None] * 0.016
        return base, 0.47 + n * 0.06 - panel * 0.08, 0.04 + panel * 0.18, 0.48 + n01 * 0.18, 0.72 - panel * 0.20
    if kind == "wall":
        seam = np.logical_or(np.mod(u * 16.0, 1.0) < 0.028, np.mod(v * 16.0, 1.0) < 0.028).astype(np.float64)
        base = np.stack((0.46 + (0.76 - 0.46) * n01, 0.53 + (0.80 - 0.53) * n01, 0.51 + (0.72 - 0.51) * n01), axis=-1)
        seam_color = np.array((0.06, 0.40, 0.47), dtype=np.float64)
        base = base * (1.0 - seam[..., None] * 0.62) + seam_color * (seam[..., None] * 0.62)
        return base, 0.50 + n * 0.07 - seam * 0.10, np.zeros_like(n), 0.46 + n01 * 0.20, 0.77 - seam * 0.24
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
    if kind in WEAPON_READABILITY_TARGETS:
        palette = WEAPON_READABILITY_TARGETS[kind]["palette"]
    else:
        # Accent remains on its existing authored contract.
        palette = ((0.16, 0.012, 0.008), (0.52, 0.040, 0.018), (0.90, 0.17, 0.028))
    shadow, base_colour, highlight = (np.array(values, dtype=np.float64) for values in palette)
    value = np.rint(n01 * 8.0) / 8.0
    if kind in WEAPON_READABILITY_TARGETS:
        # Mirror the scalar two-segment palette interpolation above.
        low_amount = np.clip(value / 0.58, 0.0, 1.0)
        high_amount = np.clip((value - 0.58) / 0.42, 0.0, 1.0)
        low_base = shadow + (base_colour - shadow) * low_amount[..., None]
        high_base = base_colour + (highlight - base_colour) * high_amount[..., None]
        base = np.where((value <= 0.58)[..., None], low_base, high_base)
    else:
        base = shadow + (base_colour - shadow) * value[..., None]
        # Scalar code shadows ``value`` with each channel's interpolated value in
        # the highlight pass; retain that per-channel amount here.
        highlight_amount = np.clip((base - 0.58) / 0.42, 0.0, 1.0)
        base = base + (highlight - base) * highlight_amount
    grid = np.logical_or(np.mod(u * 16.0, 1.0) < 0.025, np.mod(v * 16.0, 1.0) < 0.025).astype(np.float64)
    base = base * (1.0 - grid[..., None] * 0.45) + shadow * (grid[..., None] * 0.45)
    height = 0.50 + n * 0.09 + grid * 0.035
    if kind in WEAPON_READABILITY_TARGETS:
        targets = WEAPON_READABILITY_TARGETS[kind]
        smooth_min, smooth_max = targets["smoothness"]
        metallic = np.full_like(n, targets["metallic"])
        smoothness = smooth_min + n01 * (smooth_max - smooth_min)
        ao_min, ao_max = targets["ao"]
        ao_range = ao_max - ao_min
        ao = ao_min + ao_range * 0.25 + n01 * ao_range * 0.75 - grid * ao_range * 0.25
        return base, height, metallic, smoothness, ao
    return base, height, np.full_like(n, 0.52), 0.64 + n01 * 0.25, 0.88 - grid * 0.16


def _wrapped_central_differences(field, spacing_u, spacing_v):
    """Calculate central differences on a two-dimensional torus."""
    field = np.asarray(field, dtype=np.float64)
    gradient_u = (np.roll(field, -1, axis=1) - np.roll(field, 1, axis=1)) / (2.0 * spacing_u)
    gradient_v = (np.roll(field, -1, axis=0) - np.roll(field, 1, axis=0)) / (2.0 * spacing_v)
    return gradient_u, gradient_v


def _generate_natural_surface_maps(kind: str, width: int, height: int):
    """Build four maps from one unique toroidal height field, then close edges."""
    if width < 3 or height < 3:
        raise ValueError("Natural surfaces require dimensions of at least 3x3")
    unique_width, unique_height = width - 1, height - 1
    u_values = np.arange(unique_width, dtype=np.float64) / float(unique_width)
    v_values = np.arange(unique_height, dtype=np.float64) / float(unique_height)
    u = u_values[None, :]
    authored_height = np.empty((unique_height, unique_width), dtype=np.float64)
    chunk_rows = max(1, min(unique_height, 64))

    # The height field is the source of truth for the normal map.  It is fully
    # authored on the unique torus before any output map receives its seam row.
    for start in range(0, unique_height, chunk_rows):
        stop = min(unique_height, start + chunk_rows)
        v = v_values[start:stop, None]
        authored_height[start:stop, :] = _natural_surface_height(kind, u, v)

    spacing_u, spacing_v = 1.0 / float(unique_width), 1.0 / float(unique_height)
    slope_u, slope_v = _wrapped_central_differences(authored_height, spacing_u, spacing_v)
    vector_u = -0.075 * slope_u
    vector_v = -0.075 * slope_v
    vector_w = np.ones_like(authored_height)
    lengths = np.sqrt(vector_u * vector_u + vector_v * vector_v + vector_w * vector_w)
    encoded_normal = np.stack(
        (vector_u / lengths * 0.5 + 0.5, vector_v / lengths * 0.5 + 0.5, vector_w / lengths * 0.5 + 0.5),
        axis=-1,
    )

    base = _rgba_array(width, height)
    normal = _rgba_array(width, height)
    metallic = _rgba_array(width, height)
    occlusion = _rgba_array(width, height)
    normal[:unique_height, :unique_width, :3] = _u8_array(encoded_normal)
    for start in range(0, unique_height, chunk_rows):
        stop = min(unique_height, start + chunk_rows)
        v = v_values[start:stop, None]
        colour, _height, metal, smooth, ao = _surface_fields_array(kind, u, v)
        base[start:stop, :unique_width, :3] = _u8_array(colour)
        metallic[start:stop, :unique_width, 0] = _u8_array(metal)
        metallic[start:stop, :unique_width, 3] = _u8_array(smooth)
        occlusion[start:stop, :unique_width, :3] = _u8_array(np.repeat(ao[..., None], 3, axis=-1))
    for buffer in (base, normal, metallic, occlusion):
        close_repeat_edges(buffer, width, height)
    return base, normal, metallic, occlusion


def generate_surface_maps(kind: str, width: int, height: int):
    if kind in NATURAL_SURFACE_CONTRACT:
        return _generate_natural_surface_maps(kind, width, height)
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


def generate_weapon_accent_emission(width=2048, height=2048):
    """Author a bounded red light-strip mask at the weapon source resolution."""
    source_width = min(width, 1024)
    source_height = min(height, 1024)
    u = np.arange(source_width, dtype=np.float64)[None, :] / float(source_width - 1)
    v = np.arange(source_height, dtype=np.float64)[:, None] / float(source_height - 1)
    noise = _weapon_tonal_field("accent", u, v)
    mask = np.mod(u * 8.0, 1.0) < 0.075
    intensity = np.clip(0.72 + noise * 0.12 + 0.05 * np.cos(np.float64(math.tau) * v * 4.0), 0.62, 0.90)
    emission = _rgba_array(source_width, source_height, (0, 0, 0, 255))
    colour = np.stack((intensity, intensity * 0.075, intensity * 0.055), axis=-1)
    emission[:, :, :3] = _u8_array(np.where(mask[..., None], colour, 0.0))
    close_repeat_edges(emission, source_width, source_height)
    if (source_width, source_height) != (width, height):
        return resize_nearest(emission, source_width, source_height, width, height)
    return emission


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
    u = (np.arange(width, dtype=np.float64) + 0.5) / float(width)
    v = (np.arange(height, dtype=np.float64) + 0.5)[:, None] / float(height)
    u = u[None, :]
    # Several soft, irregular periodic modes keep the mask continuous while
    # leaving the shader's animated scan line as the only deliberate scan cue.
    mottle_layers = (
        (2.0, 5.0, 0.37, 0.42),
        (5.0, 2.0, 2.11, 0.29),
        (3.0, -7.0, 4.03, 0.19),
        (-7.0, 3.0, 1.47, 0.10),
    )
    warp_layers = ((1.0, 3.0, 0.71, 0.028), (3.0, 1.0, 3.27, 0.017))
    mottle = _irregular_periodic_mottle(u, v, mottle_layers, warp_layers)
    pulse = 0.5 + 0.5 * np.sin(np.float64(math.tau) * (u * 1.0 + v * 4.0) + 0.59)
    density = np.clip(0.5 + 0.5 * (0.78 * mottle + 0.22 * (2.0 * pulse - 1.0)), 0.0, 1.0)
    density = (density - np.min(density)) / np.float64(np.max(density) - np.min(density))
    alpha = 0.063 + 0.80 * density
    colour = np.stack(
        (
            np.clip(0.40 + 0.12 * pulse + 0.08 * density, 0.0, 1.0),
            np.clip(0.76 + 0.12 * pulse + 0.06 * density, 0.0, 1.0),
            np.clip(0.88 + 0.08 * pulse + 0.06 * density, 0.0, 1.0),
            alpha,
        ),
        axis=-1,
    )
    buffer[:, :, :] = _u8_array(colour)
    # Keep the VFX source compatible with the generator's repeat-edge audit;
    # the runtime importer still uses Clamp wrapping for this mask.
    buffer[:, -1, :] = buffer[:, 0, :]
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


_SKY_CLOUD_BROAD_LAYERS = (
    (1.0, 2.0, 0.53, 0.55),
    (2.0, 3.0, 2.71, 0.30),
    (4.0, 5.0, 4.19, 0.15),
)
_SKY_CLOUD_DETAIL_LAYERS = (
    (6.0, 9.0, 1.07, 0.58),
    (11.0, 13.0, 3.83, 0.27),
    (19.0, 23.0, 5.10, 0.15),
)


def _sky_cloud_density(u, v):
    """Return scattered cloud masses on an exact U-periodic panorama."""
    u = np.mod(np.asarray(u, dtype=np.float64), 1.0)
    v = np.asarray(v, dtype=np.float64)
    # A 2:1 panorama needs twice as many U cycles for square-pixel symmetry.
    broad = _balanced_toroidal_field(u, v, _SKY_CLOUD_BROAD_LAYERS, u_frequency_scale=2.0)
    detail = _balanced_toroidal_field(u, v, _SKY_CLOUD_DETAIL_LAYERS, u_frequency_scale=2.0)
    density = _smoothstep_array(0.52, 0.68, 0.5 + broad * 0.95 + detail * 0.30)
    pole_fade = _smoothstep_array(0.0, 0.08, v) * _smoothstep_array(0.0, 0.08, 1.0 - v)
    return np.clip(density * pole_fade, 0.0, 1.0)


def generate_sky(width=2048, height=1024):
    if width < 3 or height < 2:
        raise ValueError("Sky panorama requires dimensions of at least 3x2")
    buffer = _rgba_array(width, height)
    unique_width = width - 1
    x = np.arange(unique_width, dtype=np.float64) / float(unique_width)
    horizon = np.array((185.0, 220.0, 242.0), dtype=np.float64) / 255.0
    zenith = np.array((76.0, 145.0, 216.0), dtype=np.float64) / 255.0
    cloud_colour = np.array((245.0, 243.0, 232.0), dtype=np.float64) / 255.0
    chunk_rows = max(1, min(height, 64))
    for start in range(0, height, chunk_rows):
        stop = min(height, start + chunk_rows)
        u = x[None, :]
        v = (np.arange(start, stop, dtype=np.float64) / float(height - 1))[:, None]
        sky_t = _smoothstep_array(0.0, 1.0, v)
        sky = horizon + (zenith - horizon) * sky_t[..., None]
        density = _sky_cloud_density(u, v)
        cloud_mix = np.clip(density * 0.68, 0.0, 0.72)
        colour = sky * (1.0 - cloud_mix[..., None]) + cloud_colour * cloud_mix[..., None]
        buffer[start:stop, :unique_width, :3] = _u8_array(colour)
        buffer[start:stop, :unique_width, 3] = _u8_array(density)
    # The final column is an exact copy, not a second floating-point sample.
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


def _wrapped_boundary_continuity(rgba, width, height):
    """Audit seam neighborhoods and first-derivative continuity on unique texels."""
    image = _rgba_view(rgba, width, height).astype(np.float64)
    unique = image[:-1, :-1, :]
    if unique.shape[0] < 4 or unique.shape[1] < 4:
        return {
            "pass": False,
            "finite": False,
            "axes": {},
            "reason": "wrapped continuity requires at least four unique samples per axis",
        }
    finite = bool(np.isfinite(unique).all())
    axes = {}
    for axis, label in ((1, "u"), (0, "v")):
        forward = np.roll(unique, -1, axis=axis) - unique
        seam_step = np.take(forward, -1, axis=axis)
        first_step = np.take(forward, 0, axis=axis)
        previous_step = np.take(forward, -2, axis=axis)
        interior_steps = np.delete(np.abs(forward), -1, axis=axis)
        interior_step_p999 = float(np.percentile(interior_steps, 99.9))
        seam_step_max = float(np.max(np.abs(seam_step)))
        seam_spike_limit = max(3.0, interior_step_p999 + 2.0)
        seam_spike = seam_step_max <= seam_spike_limit

        # A C1 torus has matching forward steps at both sides of the seam.
        seam_second_difference = np.maximum(np.abs(seam_step - first_step), np.abs(seam_step - previous_step))
        interior_second_difference = np.abs(np.diff(forward, axis=axis))
        interior_second_p999 = float(np.percentile(interior_second_difference, 99.9))
        seam_second_max = float(np.max(seam_second_difference))
        neighborhood_limit = max(3.0, interior_second_p999 + 2.0)
        neighborhood_continuous = seam_second_max <= neighborhood_limit

        central = (np.roll(unique, -1, axis=axis) - np.roll(unique, 1, axis=axis)) * 0.5
        boundary_derivative_error = np.take(central, 0, axis=axis) - np.take(central, -1, axis=axis)
        interior_derivative_difference = np.abs(np.diff(central, axis=axis))
        interior_derivative_p999 = float(np.percentile(interior_derivative_difference, 99.9))
        derivative_error_max = float(np.max(np.abs(boundary_derivative_error)))
        derivative_limit = max(3.0, interior_derivative_p999 + 2.0)
        derivative_continuous = derivative_error_max <= derivative_limit
        axes[label] = {
            "seam_step_max_u8": seam_step_max,
            "interior_step_p999_u8": interior_step_p999,
            "seam_spike_limit_u8": seam_spike_limit,
            "seam_spike": seam_spike,
            "seam_second_difference_max_u8": seam_second_max,
            "interior_second_difference_p999_u8": interior_second_p999,
            "neighborhood_limit_u8": neighborhood_limit,
            "neighborhood_continuous": neighborhood_continuous,
            "boundary_derivative_error_max_u8": derivative_error_max,
            "interior_derivative_difference_p999_u8": interior_derivative_p999,
            "derivative_limit_u8": derivative_limit,
            "derivative_continuous": derivative_continuous,
            "pass": seam_spike and neighborhood_continuous and derivative_continuous,
        }
    return {"pass": finite and all(axis["pass"] for axis in axes.values()), "finite": finite, "axes": axes}


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


def audit_shield_semantics(rgba, width=128, height=128):
    """Check the soft team mask and reject the former diagonal line lattice."""
    array = _rgba_view(rgba, width, height)
    alpha = array[:, :, 3].astype(np.float64)
    rgb = array[:, :, :3].astype(np.float64)
    luminance = rgb[:, :, 0] * 0.2126 + rgb[:, :, 1] * 0.7152 + rgb[:, :, 2] * 0.0722
    centred = alpha - float(np.mean(alpha))
    spectrum = np.abs(np.fft.fftshift(np.fft.fft2(centred))) ** 2
    frequency_v = np.fft.fftshift(np.fft.fftfreq(height)) * np.float64(height)
    frequency_u = np.fft.fftshift(np.fft.fftfreq(width)) * np.float64(width)
    v_grid, u_grid = np.meshgrid(frequency_v, frequency_u, indexing="ij")
    non_dc = (u_grid != 0.0) | (v_grid != 0.0)
    maximum_frequency = np.maximum(np.abs(u_grid), np.abs(v_grid))
    minimum_frequency = np.minimum(np.abs(u_grid), np.abs(v_grid))
    ratio = np.divide(minimum_frequency, maximum_frequency, out=np.zeros_like(maximum_frequency), where=maximum_frequency > 0.0)
    diagonal_line_band = non_dc & (ratio >= 0.50) & (ratio <= 2.0)
    total_energy = float(np.sum(spectrum[non_dc]))
    diagonal_energy = float(np.sum(spectrum[diagonal_line_band]))
    diagonal_fraction = diagonal_energy / total_energy if total_energy > np.finfo(np.float64).eps else 1.0
    alpha_range = [int(np.min(alpha)), int(np.max(alpha))]
    gates = {
        "dimensions": bool(array.shape == (height, width, 4)),
        "alpha_range": bool(alpha_range[0] >= 16 and alpha_range[1] <= 220),
        "soft_alpha_mask": bool(alpha_range[1] - alpha_range[0] >= 96 and np.count_nonzero(alpha > 24) < alpha.size),
        "team_tint": bool(float(np.mean(rgb[:, :, 1])) > float(np.mean(rgb[:, :, 0])) and float(np.mean(rgb[:, :, 2])) > float(np.mean(rgb[:, :, 1]))),
        "no_diagonal_line_lattice": bool(np.isfinite(diagonal_fraction) and diagonal_fraction <= 0.30),
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "alpha_range_u8": alpha_range,
        "mean_rgb_u8": [float(np.mean(rgb[:, :, channel])) for channel in range(3)],
        "luminance_range_u8": [float(np.min(luminance)), float(np.max(luminance))],
        "diagonal_lattice_spectral_fraction": float(diagonal_fraction),
        "diagonal_lattice_spectral_limit": 0.30,
        "spectral_energy": total_energy,
        "diagonal_lattice_spectral_energy": diagonal_energy,
    }


def _weapon_readability_family_audit(kind, generated):
    """Check palette, PBR channel ranges, dimensions, and base luminance."""
    family_id = "weapon-" + kind
    expected_names = WEAPON_FAMILY_OUTPUTS[family_id]
    targets = WEAPON_READABILITY_TARGETS[kind]
    entries = {name: generated.get(name) for name in expected_names}
    complete = all(entry is not None for entry in entries.values())
    dimensions = {name: (list(entry["dimensions"]) if entry is not None else None) for name, entry in entries.items()}
    dimensions_ok = complete and all(value == [2048, 2048] for value in dimensions.values())
    if not complete:
        return {
            "pass": False,
            "gates": {
                "dimensions": False,
                "base_palette": False,
                "base_luminance": False,
                "metallic": False,
                "smoothness": False,
                "ao": False,
            },
            "dimensions": dimensions,
            "base_luminance": None,
            "base_palette": None,
            "metallic": None,
            "smoothness": None,
            "ao": None,
        }

    width, height = entries[expected_names[0]]["dimensions"]
    base = _rgba_view(entries[expected_names[0]]["buffer"], width, height)
    base_rgb = base[:, :, :3].astype(np.float64) / 255.0
    luminance = base_rgb[:, :, 0] * 0.2126 + base_rgb[:, :, 1] * 0.7152 + base_rgb[:, :, 2] * 0.0722
    base_luminance = {
        "mean": float(np.mean(luminance)),
        "range": [float(np.min(luminance)), float(np.max(luminance))],
        "span": float(np.max(luminance) - np.min(luminance)),
    }
    luminance_floor, luminance_mean, luminance_peak = targets["luminance"]
    luminance_gates = {
        "floor": base_luminance["range"][0] >= luminance_floor - (1.0 / 255.0),
        "mean": base_luminance["mean"] >= luminance_mean,
        "peak": base_luminance["range"][1] >= luminance_peak,
        "contrast": base_luminance["span"] >= 0.20,
    }

    palette = targets["palette"]
    base_ranges = [[int(np.min(base[:, :, channel])), int(np.max(base[:, :, channel]))] for channel in range(3)]
    palette_bounds = {
        "shadow": [u8(value) for value in palette[0]],
        "highlight": [u8(value) for value in palette[2]],
        "ranges": base_ranges,
    }
    palette_gates = {
        "within_shadow_highlight": all(
            base_ranges[channel][0] >= palette_bounds["shadow"][channel] - 1
            and base_ranges[channel][1] <= palette_bounds["highlight"][channel] + 1
            for channel in range(3)
        ),
        "base_stop_reached": all(
            base_ranges[channel][1] >= u8(palette[1][channel]) - 1 for channel in range(3)
        ),
    }

    metallic_array = _rgba_view(entries[expected_names[2]]["buffer"], width, height)
    occlusion_array = _rgba_view(entries[expected_names[3]]["buffer"], width, height)
    metallic_range = [float(np.min(metallic_array[:, :, 0])) / 255.0, float(np.max(metallic_array[:, :, 0])) / 255.0]
    smoothness_range = [float(np.min(metallic_array[:, :, 3])) / 255.0, float(np.max(metallic_array[:, :, 3])) / 255.0]
    ao_values = occlusion_array[:, :, :3].astype(np.float64) / 255.0
    ao_range = [float(np.min(ao_values)), float(np.max(ao_values))]
    tolerance = (1.0 / 255.0) + 1e-6
    metallic_target = targets["metallic"]
    smoothness_target = targets["smoothness"]
    ao_target = targets["ao"]
    metallic_gate = metallic_range[0] >= metallic_target - tolerance and metallic_range[1] <= metallic_target + tolerance
    smoothness_gate = smoothness_range[0] >= smoothness_target[0] - tolerance and smoothness_range[1] <= smoothness_target[1] + tolerance
    ao_gate = ao_range[0] >= ao_target[0] - tolerance and ao_range[1] <= ao_target[1] + tolerance
    gates = {
        "dimensions": dimensions_ok,
        "base_palette": all(palette_gates.values()),
        "base_luminance": all(luminance_gates.values()),
        "metallic": metallic_gate,
        "smoothness": smoothness_gate,
        "ao": ao_gate,
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "dimensions": dimensions,
        "base_palette": {"target": [list(values) for values in palette], **palette_bounds, "gates": palette_gates},
        "base_luminance": {**base_luminance, "target": [luminance_floor, luminance_mean, luminance_peak], "gates": luminance_gates},
        "metallic": {"range": metallic_range, "target": metallic_target},
        "smoothness": {"range": smoothness_range, "target": list(smoothness_target)},
        "ao": {"range": ao_range, "target": list(ao_target)},
    }


def audit_weapon_readability(generated, selected_families=None):
    """Audit only the weapon families requested by the current generator run."""
    selected = set(selected_families or FAMILY_IDS)
    selected_weapon_families = tuple(family_id for family_id in ("weapon-metal", "weapon-dark") if family_id in selected)
    expected_names = {name for family_id in selected_weapon_families for name in WEAPON_FAMILY_OUTPUTS[family_id]}
    actual_names = {name for name in generated if name in {name for values in WEAPON_FAMILY_OUTPUTS.values() for name in values}}
    selected_family_execution = actual_names == expected_names
    families = {family_id.removeprefix("weapon-"): _weapon_readability_family_audit(family_id.removeprefix("weapon-"), generated) for family_id in selected_weapon_families}
    family_gates = {kind: result["pass"] for kind, result in families.items()}
    gates = {"selected_family_execution": selected_family_execution, **family_gates}
    return {
        "pass": bool(selected_weapon_families) and all(gates.values()),
        "gates": gates,
        "selected_families": list(selected_weapon_families),
        "selected_family_execution": {"expected": sorted(expected_names), "actual": sorted(actual_names), "pass": selected_family_execution},
        "families": families,
    }


_NATURAL_FORBIDDEN_SOURCE_TOKENS = (
    "_periodic_grid_distance",
    "_periodic_ellipse_mask",
    "_segment_mask",
    "panel",
    "stripe",
    "grid",
    "plate",
    "slab",
    "fastener",
    "rib",
    "stain",
    "scratch",
    "mowing",
)

_GRASS_FORBIDDEN_SOURCE_TOKENS = (
    "teal",
    "cyan",
    "water",
    "swirl",
    "isotropic",
)


def _directional_structure_audit(field, maximum_coherence):
    """Measure global ridge bias from the wrapped luminance/density tensor."""
    values = np.asarray(field, dtype=np.float64)
    gradient_x = (np.roll(values, -1, axis=1) - np.roll(values, 1, axis=1)) * np.float64(0.5)
    gradient_y = (np.roll(values, -1, axis=0) - np.roll(values, 1, axis=0)) * np.float64(0.5)
    tensor_xx = float(np.mean(gradient_x * gradient_x))
    tensor_yy = float(np.mean(gradient_y * gradient_y))
    tensor_xy = float(np.mean(gradient_x * gradient_y))
    energy = tensor_xx + tensor_yy
    coherence = 1.0 if energy <= np.finfo(np.float64).eps else math.sqrt(
        (tensor_xx - tensor_yy) ** 2 + 4.0 * tensor_xy * tensor_xy
    ) / energy
    return {
        "pass": bool(np.isfinite(coherence) and coherence <= maximum_coherence),
        "coherence": float(coherence),
        "maximum": float(maximum_coherence),
        "tensor": {"xx": tensor_xx, "yy": tensor_yy, "xy": tensor_xy},
    }


def _axis_periodic_band_audit(field, maximum_axis_fraction):
    """Reject horizontal/vertical periodic energy hidden inside a tiled field."""
    values = np.asarray(field, dtype=np.float64)
    centred = values - float(np.mean(values))
    total_variance = float(np.mean(centred * centred))

    def profile_metrics(profile):
        profile = np.asarray(profile, dtype=np.float64)
        variance_fraction = 0.0 if total_variance <= np.finfo(np.float64).eps else float(np.mean(profile * profile) / total_variance)
        spectrum = np.abs(np.fft.rfft(profile)) ** 2
        if spectrum.size:
            spectrum[0] = 0.0
        dominant_cycle = int(np.argmax(spectrum)) if spectrum.size else 0
        spectral_energy = float(np.sum(spectrum))
        peak_fraction = 0.0 if spectral_energy <= np.finfo(np.float64).eps else float(spectrum[dominant_cycle] / spectral_energy)
        return {
            "variance_fraction": variance_fraction,
            "dominant_cycle": dominant_cycle,
            "peak_fraction": peak_fraction,
        }

    horizontal = profile_metrics(np.mean(centred, axis=1))
    vertical = profile_metrics(np.mean(centred, axis=0))
    maximum_observed = max(horizontal["variance_fraction"], vertical["variance_fraction"])
    return {
        "pass": bool(np.isfinite(maximum_observed) and maximum_observed <= maximum_axis_fraction),
        "maximum_observed": maximum_observed,
        "maximum": float(maximum_axis_fraction),
        "horizontal": horizontal,
        "vertical": vertical,
    }


def _grass_microstructure_audit(field, minimum_energy_fraction, maximum_orientation_fraction):
    """Require high-frequency, multi-directional turf detail without a dominant weave."""
    values = np.asarray(field, dtype=np.float64)
    centred = values - float(np.mean(values))
    spectrum = np.abs(np.fft.fft2(centred)) ** 2
    height, width = values.shape
    frequency_v = np.fft.fftfreq(height) * np.float64(height)
    frequency_u = np.fft.fftfreq(width) * np.float64(width)
    v_grid, u_grid = np.meshgrid(frequency_v, frequency_u, indexing="ij")
    radius = np.sqrt(u_grid * u_grid + v_grid * v_grid)
    non_dc = radius > 0.0
    micro = (radius >= 18.0) & (radius <= 100.0)
    total_energy = float(np.sum(spectrum[non_dc]))
    micro_energy = float(np.sum(spectrum[micro]))
    energy_fraction = micro_energy / total_energy if total_energy > np.finfo(np.float64).eps else 0.0
    orientation = np.mod(np.arctan2(v_grid, u_grid), math.pi)
    sector_energy = []
    for index in range(6):
        lower = math.pi * index / 6.0
        upper = math.pi * (index + 1) / 6.0
        sector_energy.append(float(np.sum(spectrum[micro & (orientation >= lower) & (orientation < upper)])))
    orientation_fraction = max(sector_energy) / micro_energy if micro_energy > np.finfo(np.float64).eps else 1.0
    gates = {
        "microstructure_energy": bool(np.isfinite(energy_fraction) and energy_fraction >= minimum_energy_fraction),
        "orientation_balance": bool(np.isfinite(orientation_fraction) and orientation_fraction <= maximum_orientation_fraction),
    }
    return {
        "pass": all(gates.values()),
        "microstructure_energy_fraction": energy_fraction,
        "minimum_microstructure_energy_fraction": float(minimum_energy_fraction),
        "maximum_orientation_fraction": orientation_fraction,
        "orientation_fraction_limit": float(maximum_orientation_fraction),
        "sector_energy": sector_energy,
        "gates": gates,
    }


def _quantized_audit_field(field, normalized=False):
    """Convert authored or output fields to the same u8 domain used by the PNG."""
    values = np.asarray(field, dtype=np.float64)
    if values.ndim != 2:
        raise ValueError("spectral audit fields must be two-dimensional")
    return _u8_array(values) if normalized else np.rint(np.clip(values, 0.0, 255.0)).astype(np.uint8)


def _radial_low_frequency_audit(fields, radius=6, maximum_rms_u8=0.5):
    """Report non-DC FFT RMS in the low radial band of quantized fields."""
    reports = {}
    for name, (field, normalized) in fields.items():
        quantized = _quantized_audit_field(field, normalized)
        values = quantized.astype(np.float64)
        centred = values - float(np.mean(values))
        height, width = values.shape
        spectrum = np.fft.fft2(centred) / np.float64(values.size)
        frequency_v = np.fft.fftfreq(height) * np.float64(height)
        frequency_u = np.fft.fftfreq(width) * np.float64(width)
        v_grid, u_grid = np.meshgrid(frequency_v, frequency_u, indexing="ij")
        radial_squared = u_grid * u_grid + v_grid * v_grid
        low_mask = (radial_squared > 0.0) & (radial_squared <= np.float64(radius * radius))
        rms_u8 = float(np.sqrt(np.sum(np.abs(spectrum[low_mask]) ** 2)))
        full_rms_u8 = float(np.sqrt(np.sum(np.abs(spectrum) ** 2)))
        reports[name] = {
            "shape": [int(height), int(width)],
            "quantized_range_u8": [int(np.min(quantized)), int(np.max(quantized))],
            "low_frequency_radius": int(radius),
            "low_frequency_rms_u8": rms_u8,
            "full_rms_u8": full_rms_u8,
            "low_frequency_energy_fraction": float((rms_u8 / full_rms_u8) ** 2) if full_rms_u8 > np.finfo(np.float64).eps else 0.0,
            "pass": bool(np.isfinite(rms_u8) and rms_u8 <= maximum_rms_u8),
        }
    maximum_observed = max((report["low_frequency_rms_u8"] for report in reports.values()), default=float("inf"))
    return {
        "pass": bool(reports) and all(report["pass"] for report in reports.values()),
        "maximum_observed_rms_u8": float(maximum_observed),
        "maximum_rms_u8": float(maximum_rms_u8),
        "radius": int(radius),
        "fields": reports,
    }


def _effective_tiled_field(field, tile_u=13, tile_v=2, sample_size=127):
    """Downsample one unique torus, then apply the arena's effective 13x2 tiling."""
    values = np.asarray(field, dtype=np.float64)
    if values.ndim != 2 or min(values.shape) < 4:
        raise ValueError("wall tiling audit requires a two-dimensional field")
    count_u = min(int(sample_size), values.shape[1])
    count_v = min(int(sample_size), values.shape[0])
    indices_u = np.linspace(0, values.shape[1] - 1, count_u).round().astype(np.int64)
    indices_v = np.linspace(0, values.shape[0] - 1, count_v).round().astype(np.int64)
    sampled = values[np.ix_(indices_v, indices_u)]
    return np.tile(sampled, (int(tile_v), int(tile_u)))


def _wall_diagonal_pattern_audit(field, tile_u=13, tile_v=2, maximum_diagonal_fraction=0.40, maximum_autocorrelation_limit=0.60):
    """Reject texture-space diagonal lattice energy and long diagonal repeats."""
    effective = _effective_tiled_field(field, tile_u, tile_v)
    centred = effective - float(np.mean(effective))
    spectrum = np.abs(np.fft.fftshift(np.fft.fft2(centred))) ** 2
    height, width = effective.shape
    frequency_v = np.fft.fftshift(np.fft.fftfreq(height)) * np.float64(height) / np.float64(tile_v)
    frequency_u = np.fft.fftshift(np.fft.fftfreq(width)) * np.float64(width) / np.float64(tile_u)
    v_grid, u_grid = np.meshgrid(frequency_v, frequency_u, indexing="ij")
    non_dc = (u_grid != 0.0) | (v_grid != 0.0)
    maximum_frequency = np.maximum(np.abs(u_grid), np.abs(v_grid))
    minimum_frequency = np.minimum(np.abs(u_grid), np.abs(v_grid))
    ratio = np.divide(minimum_frequency, maximum_frequency, out=np.zeros_like(maximum_frequency), where=maximum_frequency > 0.0)
    diagonal_band = non_dc & (ratio >= 0.60) & (ratio <= 1.0 / 0.60)
    total_energy = float(np.sum(spectrum[non_dc]))
    diagonal_energy = float(np.sum(spectrum[diagonal_band]))
    diagonal_fraction = diagonal_energy / total_energy if total_energy > np.finfo(np.float64).eps else 1.0

    variance = float(np.sum(centred * centred))
    autocorrelations = []
    # Exclude the repeated tile period itself; the diagonal lag window stays
    # within one source tile while still covering non-local pattern repeats.
    maximum_lag = min(effective.shape[0] // int(tile_v), effective.shape[1] // int(tile_u)) // 2
    for lag in range(4, maximum_lag):
        correlation = float(np.sum(centred * np.roll(centred, (lag, lag), axis=(0, 1))) / variance) if variance > np.finfo(np.float64).eps else 1.0
        autocorrelations.append({"lag": int(lag), "correlation": correlation})
    maximum_autocorrelation = max((abs(item["correlation"]) for item in autocorrelations), default=float("inf"))
    gates = {
        "diagonal_spectrum": bool(np.isfinite(diagonal_fraction) and diagonal_fraction <= maximum_diagonal_fraction),
        "diagonal_autocorrelation": bool(np.isfinite(maximum_autocorrelation) and maximum_autocorrelation <= maximum_autocorrelation_limit),
    }
    return {
        "pass": all(gates.values()),
        "effective_tiling": {"u": int(tile_u), "v": int(tile_v), "sample_shape": [int(effective.shape[0] // tile_v), int(effective.shape[1] // tile_u)]},
        "diagonal_spectral_fraction": float(diagonal_fraction),
        "diagonal_spectral_limit": float(maximum_diagonal_fraction),
        "diagonal_autocorrelation_max": float(maximum_autocorrelation),
        "diagonal_autocorrelation_limit": float(maximum_autocorrelation_limit),
        "diagonal_autocorrelation": autocorrelations,
        "spectral_energy": total_energy,
        "diagonal_spectral_energy": diagonal_energy,
        "gates": gates,
    }


def _natural_surface_source_guard():
    """Prove grass and wall use only the continuous toroidal construction path."""
    path_functions = (
        _balanced_toroidal_field,
        _irregular_periodic_mottle,
        _grass_interwoven_fields,
        _natural_surface_layers,
        _natural_surface_fields,
        _natural_surface_height,
        _wrapped_central_differences,
        _generate_natural_surface_maps,
        generate_surface_maps,
        run_semantic_audits,
    )
    try:
        source = "\n".join(inspect.getsource(function) for function in path_functions).lower()
    except (OSError, TypeError):
        return {"pass": False, "forbidden_tokens": ["<source-unavailable>"], "path_functions": [function.__name__ for function in path_functions]}
    forbidden = sorted(token for token in _NATURAL_FORBIDDEN_SOURCE_TOKENS if token in source)
    grass_source = "\n".join(
        inspect.getsource(function) for function in (_grass_interwoven_fields, _natural_surface_layers, _natural_surface_fields)
    ).lower()
    grass_forbidden = sorted(token for token in _GRASS_FORBIDDEN_SOURCE_TOKENS if token in grass_source)
    route_requirements = {
        "map_generation": (
            inspect.getsource(generate_surface_maps).lower(),
            "if kind in natural_surface_contract:\n        return _generate_natural_surface_maps(kind, width, height)",
        ),
        "semantic_audit": (
            inspect.getsource(run_semantic_audits).lower(),
            "checks[kind] = audit_continuous_surface(kind, generated)",
        ),
        "grass_interwoven_route": (
            inspect.getsource(_natural_surface_layers).lower(),
            "return _grass_interwoven_fields(u, v)",
        ),
    }
    missing = [name for name, (route_source, required) in route_requirements.items() if required not in route_source]
    return {
        "pass": not forbidden and not grass_forbidden and not missing,
        "forbidden_tokens": forbidden,
        "grass_forbidden_tokens": grass_forbidden,
        "missing_continuous_route_tokens": missing,
        "path_functions": [function.__name__ for function in path_functions],
    }


def audit_continuous_surface(kind, generated):
    """Audit natural surface inventory, toroidal seams, fields, and PBR bounds."""
    contract = NATURAL_SURFACE_CONTRACT[kind]
    prefix = "Retro" + kind.capitalize()
    expected_names = tuple(FAMILY_REGISTRY[kind]["outputs"])
    entries = {name: generated.get(name) for name in expected_names}
    complete = all(entry is not None for entry in entries.values())
    dimensions_ok = complete and all(entry["dimensions"] == [1024, 1024] for entry in entries.values())
    family_names = {name for name in generated if name.startswith(prefix)}
    inventory_ok = tuple(entries) == expected_names and family_names == set(expected_names)
    if not complete:
        return {"pass": False, "gates": {"inventory": inventory_ok, "dimensions": dimensions_ok}, "expected": list(expected_names)}

    width, height = entries[prefix]["dimensions"]
    base = _rgba_view(entries[prefix]["buffer"], width, height)
    normal = _rgba_view(entries[prefix + "_Normal"]["buffer"], width, height)
    metallic = _rgba_view(entries[prefix + "_MetallicSmoothness"]["buffer"], width, height)
    occlusion = _rgba_view(entries[prefix + "_Occlusion"]["buffer"], width, height)
    maps = {"base": base, "normal": normal, "metallic_smoothness": metallic, "occlusion": occlusion}
    edge_errors = {}
    for map_name, image in maps.items():
        edge_errors[map_name] = {
            "u": int(np.count_nonzero(image[:, 0, :] != image[:, -1, :])),
            "v": int(np.count_nonzero(image[0, :, :] != image[-1, :, :])),
        }
    edges_ok = all(error["u"] == 0 and error["v"] == 0 for error in edge_errors.values())
    wrapped_continuity = {map_name: _wrapped_boundary_continuity(image, width, height) for map_name, image in maps.items()}
    wrapped_continuity_ok = all(result["pass"] for result in wrapped_continuity.values())

    rgb = base[:, :, :3].astype(np.float64) / 255.0
    luminance = rgb[:, :, 0] * 0.2126 + rgb[:, :, 1] * 0.7152 + rgb[:, :, 2] * 0.0722
    palette_ranges = [[int(np.min(base[:, :, channel])), int(np.max(base[:, :, channel]))] for channel in range(3)]
    palette_floor, palette_ceiling = contract["palette_u8"]
    palette_ok = all(palette_ranges[channel][0] >= palette_floor[channel] - 1 and palette_ranges[channel][1] <= palette_ceiling[channel] + 1 for channel in range(3))
    luminance_span = float(np.max(luminance) - np.min(luminance))

    metal_values = metallic[:, :, 0].astype(np.float64) / 255.0
    smooth_values = metallic[:, :, 3].astype(np.float64) / 255.0
    ao_values = occlusion[:, :, 0].astype(np.float64) / 255.0
    tolerance = (1.0 / 255.0) + 1e-6
    metallic_range = [float(np.min(metal_values)), float(np.max(metal_values))]
    smoothness_range = [float(np.min(smooth_values)), float(np.max(smooth_values))]
    ao_range = [float(np.min(ao_values)), float(np.max(ao_values))]
    metallic_ok = metallic_range[0] >= contract["metallic"][0] - tolerance and metallic_range[1] <= contract["metallic"][1] + tolerance
    smoothness_ok = smoothness_range[0] >= contract["smoothness"][0] - tolerance and smoothness_range[1] <= contract["smoothness"][1] + tolerance
    ao_ok = ao_range[0] >= contract["ao"][0] - tolerance and ao_range[1] <= contract["ao"][1] + tolerance

    normal_audit = _normal_map_audit(normal, width, height)
    decoded_normal = normal[:, :, :3].astype(np.float64) / 127.5 - 1.0
    normal_lengths = np.sqrt(np.sum(decoded_normal * decoded_normal, axis=-1))
    unit_error = float(np.max(np.abs(normal_lengths - 1.0)))
    normal_xy_ranges = [[int(np.min(normal[:, :, channel])), int(np.max(normal[:, :, channel]))] for channel in range(2)]
    normal_nonflat = normal_audit["valid"] and unit_error <= 0.02 and all(maximum - minimum >= 8 for minimum, maximum in normal_xy_ranges)

    unique_width, unique_height = width - 1, height - 1
    x_values = np.arange(unique_width, dtype=np.float64) / float(unique_width)
    y_values = np.arange(unique_height, dtype=np.float64) / float(unique_height)
    height_field = _natural_surface_height(kind, x_values[None, :], y_values[:, None])
    slope_u, slope_v = _wrapped_central_differences(height_field, 1.0 / unique_width, 1.0 / unique_height)
    finite_gradients = bool(np.isfinite(height_field).all() and np.isfinite(slope_u).all() and np.isfinite(slope_v).all())
    directional = _directional_structure_audit(height_field, contract.get("maximum_directional_coherence", 0.12))
    radial_low_frequency = None
    luminance_span_u8 = None
    wall_pattern = None

    if kind == "grass":
        green_dominant = (base[:, :, 1] > base[:, :, 0]) & (base[:, :, 1] > base[:, :, 2])
        green_fraction = float(np.mean(green_dominant))
        yellow_green = green_dominant & (base[:, :, 0].astype(np.float64) >= base[:, :, 2].astype(np.float64) * 1.5)
        yellow_green_fraction = float(np.mean(yellow_green))
        blue_green_ratio = float(np.max(base[:, :, 2].astype(np.float64) / np.maximum(base[:, :, 1], 1)))
        near_white = int(np.count_nonzero(np.all(base[:, :, :3] >= contract["near_white_threshold_u8"], axis=-1)))
        palette_semantics = (
            green_fraction >= contract["green_dominance_fraction"]
            and yellow_green_fraction >= contract["yellow_green_fraction"]
            and blue_green_ratio <= contract["maximum_blue_green_ratio"]
            and near_white == 0
        )
        palette_mean = [float(np.mean(base[:, :, channel])) for channel in range(3)]
        mean_floor, mean_ceiling = contract["mean_rgb_u8"]
        palette_mean_ok = all(mean_floor[channel] <= palette_mean[channel] <= mean_ceiling[channel] for channel in range(3))
        directional_fields = {
            "albedo_luminance": _directional_structure_audit(luminance[:-1, :-1], contract["maximum_directional_coherence"]),
            "height": directional,
            "smoothness": _directional_structure_audit(smooth_values[:-1, :-1], contract["maximum_directional_coherence"]),
        }
        axis_bands = {
            "albedo_luminance": _axis_periodic_band_audit(luminance[:-1, :-1], contract["maximum_axis_band_fraction"]),
            "height": _axis_periodic_band_audit(height_field, contract["maximum_axis_band_fraction"]),
            "smoothness": _axis_periodic_band_audit(smooth_values[:-1, :-1], contract["maximum_axis_band_fraction"]),
        }
        directional_ok = all(result["pass"] for result in directional_fields.values())
        axis_bands_ok = all(result["pass"] for result in axis_bands.values())
        radial_low_frequency = _radial_low_frequency_audit(
            {
                "albedo_luminance": (luminance[:-1, :-1], True),
                "height": (height_field, True),
                "smoothness": (smooth_values[:-1, :-1], True),
                "ao": (ao_values[:-1, :-1], True),
            },
            radius=6,
            maximum_rms_u8=contract["maximum_low_frequency_rms_u8"],
        )
        microstructure = _grass_microstructure_audit(
            height_field,
            contract["minimum_microstructure_energy_fraction"],
            contract["maximum_microstructure_orientation_fraction"],
        )
        luminance_u8 = _quantized_audit_field(luminance[:-1, :-1], normalized=True)
        luminance_span_u8 = int(np.max(luminance_u8) - np.min(luminance_u8))
        surface_spread = None
    else:
        surface_spread = np.max(rgb, axis=-1) - np.min(rgb, axis=-1)
        palette_semantics = bool(float(np.max(surface_spread)) <= contract["rgb_spread_max"] + tolerance)
        directional_ok = directional["pass"]
        wall_pattern = _wall_diagonal_pattern_audit(
            luminance[:-1, :-1],
            tile_u=contract["effective_tile"][0],
            tile_v=contract["effective_tile"][1],
            maximum_diagonal_fraction=contract["maximum_diagonal_spectral_fraction"],
            maximum_autocorrelation_limit=contract["maximum_diagonal_autocorrelation"],
        )
        green_fraction = None
        yellow_green_fraction = None
        blue_green_ratio = None
        near_white = None
        microstructure = None
    source_guard = _natural_surface_source_guard()
    motif_inventory = list(contract["motif_inventory"])
    gates = {
        "inventory": inventory_ok,
        "dimensions": dimensions_ok,
        "edge_bytes": edges_ok,
        "wrapped_continuity": wrapped_continuity_ok,
        "palette_bounds": palette_ok,
        "palette_semantics": palette_semantics,
        "normal_nonflat": normal_nonflat,
        "metallic_range": metallic_ok,
        "smoothness_range": smoothness_ok,
        "ao_range": ao_ok,
        "finite_gradients": finite_gradients,
        "directional_balance": directional_ok,
        "construction": contract["construction"] == "continuous-periodic-field",
        "motif_inventory": not motif_inventory,
        "source_guard": source_guard["pass"],
        "wall_nonmetallic": kind != "wall" or metallic_range[1] <= tolerance,
    }
    if kind == "grass":
        gates["low_frequency_detail"] = radial_low_frequency["pass"]
        gates["interwoven_microstructure"] = microstructure["pass"]
        gates["luminance_span"] = luminance_span_u8 <= contract["maximum_luminance_span_u8"]
    else:
        gates["wall_diagonal_pattern"] = wall_pattern["pass"]
    result = {
        "pass": all(gates.values()),
        "gates": gates,
        "contract": contract,
        "construction": contract["construction"],
        "motifs": {"inventory": motif_inventory, "counts": {}},
        "edges": edge_errors,
        "wrapped_continuity": wrapped_continuity,
        "palette": {"ranges_u8": palette_ranges, "bounds_u8": [list(palette_floor), list(palette_ceiling)], "luminance_span": luminance_span, "green_dominant_fraction": green_fraction, "yellow_green_fraction": yellow_green_fraction, "maximum_blue_green_ratio": blue_green_ratio, "near_white_pixels": near_white, "max_rgb_spread": None if surface_spread is None else float(np.max(surface_spread))},
        "normal": {**normal_audit, "xy_ranges_u8": normal_xy_ranges, "unit_error": unit_error},
        "metallic": {"range": metallic_range, "target": list(contract["metallic"])},
        "smoothness": {"range": smoothness_range, "target": list(contract["smoothness"])},
        "ao": {"range": ao_range, "target": list(contract["ao"])},
        "gradients": {"finite": finite_gradients, "u_range": [float(np.min(slope_u)), float(np.max(slope_u))], "v_range": [float(np.min(slope_v)), float(np.max(slope_v))]},
        "directional_balance": directional,
        "source_guard": source_guard,
    }
    if kind == "grass":
        gates["palette_mean"] = palette_mean_ok
        gates["periodic_bands"] = axis_bands_ok
        result["palette"].update({"mean_rgb_u8": palette_mean, "mean_bounds_u8": [list(bound) for bound in contract["mean_rgb_u8"]]})
        result["directional_balance"] = {"pass": directional_ok, "fields": directional_fields}
        result["periodic_bands"] = {"pass": axis_bands_ok, "fields": axis_bands}
        result["radial_low_frequency"] = radial_low_frequency
        result["interwoven_microstructure"] = microstructure
        result["ao_spectral"] = radial_low_frequency["fields"]["ao"]
        result["palette"].update({"luminance_span_u8": luminance_span_u8, "maximum_luminance_span_u8": contract["maximum_luminance_span_u8"]})
        result["pass"] = all(gates.values())
    else:
        result["wall_diagonal_pattern"] = wall_pattern
        result["pass"] = all(gates.values())
    return result


def audit_sky_clouds(generated):
    """Audit the panorama seam, pole fade, scattered coverage, and ridge bias."""
    entry = generated.get("RetroSunnySky")
    if entry is None:
        return {"pass": False, "gates": {"inventory": False}}
    width, height = entry["dimensions"]
    image = _rgba_view(entry["buffer"], width, height)
    density = image[:, :-1, 3].astype(np.float64) / 255.0
    directional = _directional_structure_audit(density, 0.12)
    coverage = float(np.mean(density >= 0.20))
    gates = {
        "inventory": width == 2048 and height == 1024,
        "exact_u_seam": bool(np.array_equal(image[:, 0, :], image[:, -1, :])),
        "pole_fade": bool(np.count_nonzero(image[0, :, 3]) == 0 and np.count_nonzero(image[-1, :, 3]) == 0),
        "scattered_coverage": 0.10 <= coverage <= 0.60,
        "directional_balance": directional["pass"],
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "coverage_at_0_20": coverage,
        "density_range": [float(np.min(density)), float(np.max(density))],
        "directional_balance": directional,
    }


def audit_selected_inventory(generated, selected_families):
    expected = {name for family_id in selected_families for name in FAMILY_REGISTRY[family_id]["outputs"]}
    actual = set(generated)
    gates = {"exact_outputs": actual == expected, "count": len(actual) == len(expected)}
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "expected": sorted(expected),
        "actual": sorted(actual),
        "missing": sorted(expected - actual),
        "unexpected": sorted(actual - expected),
    }


def audit_tile_edges(generated):
    outputs = {}
    for name, entry in generated.items():
        audit = entry["audit"]
        outputs[name] = {
            "seam_u_error": audit["seam_u_error"],
            "seam_v_error": audit["seam_v_error"],
            "pass": audit["seam_u_error"] == 0 and audit["seam_v_error"] == 0,
        }
    return {"pass": bool(outputs) and all(value["pass"] for value in outputs.values()), "outputs": outputs}


def _mask_boundary(mask):
    interior = mask & np.roll(mask, 1, axis=0) & np.roll(mask, -1, axis=0) & np.roll(mask, 1, axis=1) & np.roll(mask, -1, axis=1)
    return mask & ~interior


def audit_cross_channel_weapon_wear(generated, selected_families):
    """Prove each physical wear mask is visible in every authored PBR map."""
    selected = [kind for kind in ("metal", "dark") if "weapon-" + kind in selected_families]
    families = {}
    for kind in selected:
        prefix = "RetroWeapon" + kind.capitalize()
        base = _rgba_view(generated[prefix]["buffer"], 2048, 2048)[::2, ::2]
        normal = _rgba_view(generated[prefix + "_Normal"]["buffer"], 2048, 2048)[::2, ::2]
        metallic = _rgba_view(generated[prefix + "_MetallicSmoothness"]["buffer"], 2048, 2048)[::2, ::2]
        occlusion = _rgba_view(generated[prefix + "_Occlusion"]["buffer"], 2048, 2048)[::2, ::2]
        size = base.shape[0]
        u = np.arange(size, dtype=np.float64)[None, :] / float(size - 1)
        v = np.arange(size, dtype=np.float64)[:, None] / float(size - 1)
        masks = _weapon_wear_masks(u, v)
        combined = np.zeros((size, size), dtype=bool)
        for mask in masks.values():
            combined |= mask
        clean = ~combined
        rgb = base[:, :, :3].astype(np.float64) / 255.0
        luminance = rgb[:, :, 0] * 0.2126 + rgb[:, :, 1] * 0.7152 + rgb[:, :, 2] * 0.0722
        normal_xy = normal[:, :, :2].astype(np.float64) / 127.5 - 1.0
        normal_relief = np.sqrt(np.sum(normal_xy * normal_xy, axis=-1))
        metallic_values = metallic[:, :, 0].astype(np.float64) / 255.0
        smoothness_values = metallic[:, :, 3].astype(np.float64) / 255.0
        ao_values = occlusion[:, :, 0].astype(np.float64) / 255.0
        clean_means = {
            "base_luminance": float(np.mean(luminance[clean])),
            "normal_relief": float(np.mean(normal_relief[clean])),
            "metallic": float(np.mean(metallic_values[clean])),
            "smoothness": float(np.mean(smoothness_values[clean])),
            "ao": float(np.mean(ao_values[clean])),
        }
        features = {}
        for name, mask in masks.items():
            boundary = _mask_boundary(mask)
            values = {
                "coverage": float(np.mean(mask)),
                "base_luminance_delta": abs(float(np.mean(luminance[mask])) - clean_means["base_luminance"]),
                "normal_relief_delta": float(np.mean(normal_relief[boundary])) - clean_means["normal_relief"],
                "metallic_delta": abs(float(np.mean(metallic_values[mask])) - clean_means["metallic"]),
                "smoothness_delta": abs(float(np.mean(smoothness_values[mask])) - clean_means["smoothness"]),
                "ao_delta": abs(float(np.mean(ao_values[mask])) - clean_means["ao"]),
            }
            gates = {
                "bounded_coverage": 0.0005 <= values["coverage"] <= 0.16,
                "base": values["base_luminance_delta"] >= 0.010,
                "normal": values["normal_relief_delta"] >= 0.002,
                "metallic": values["metallic_delta"] >= 0.025,
                "smoothness": values["smoothness_delta"] >= 0.025,
                "ao": values["ao_delta"] >= 0.025,
            }
            features[name] = {"pass": all(gates.values()), "gates": gates, **values}
        families[kind] = {
            "pass": all(feature["pass"] for feature in features.values()),
            "clean_means": clean_means,
            "features": features,
        }
    return {"pass": bool(families) and all(value["pass"] for value in families.values()), "families": families}


def audit_weapon_accent_glass(generated, selected_families):
    expected = tuple(FAMILY_REGISTRY["weapon-accent"]["outputs"])
    if "weapon-accent" not in selected_families:
        return {"pass": False, "gates": {"selected": False}}
    complete = all(name in generated for name in expected)
    if not complete:
        return {"pass": False, "gates": {"inventory": False}, "expected": list(expected)}
    base = _rgba_view(generated["RetroWeaponAccent"]["buffer"], 2048, 2048)
    normal = _rgba_view(generated["RetroWeaponAccent_Normal"]["buffer"], 2048, 2048)
    metallic = _rgba_view(generated["RetroWeaponAccent_MetallicSmoothness"]["buffer"], 2048, 2048)
    occlusion = _rgba_view(generated["RetroWeaponAccent_Occlusion"]["buffer"], 2048, 2048)
    emission = _rgba_view(generated["RetroWeaponAccent_Emission"]["buffer"], 2048, 2048)
    base_rgb = base[:, :, :3].astype(np.float64) / 255.0
    metallic_range = [float(np.min(metallic[:, :, 0])) / 255.0, float(np.max(metallic[:, :, 0])) / 255.0]
    smoothness_range = [float(np.min(metallic[:, :, 3])) / 255.0, float(np.max(metallic[:, :, 3])) / 255.0]
    ao_range = [float(np.min(occlusion[:, :, 0])) / 255.0, float(np.max(occlusion[:, :, 0])) / 255.0]
    decoded_xy = normal[:, :, :2].astype(np.float64) / 127.5 - 1.0
    normal_xy_max = float(np.max(np.abs(decoded_xy)))
    normal_xy_span = [int(np.max(normal[:, :, channel])) - int(np.min(normal[:, :, channel])) for channel in range(2)]
    emitted = np.any(emission[:, :, :3] > 0, axis=-1)
    emission_coverage = float(np.mean(emitted))
    emitted_rgb = emission[:, :, :3].astype(np.float64)[emitted]
    tolerance = (1.0 / 255.0) + 1e-6
    contract = WEAPON_ACCENT_CONTRACT
    base_ranges = [[float(np.min(base_rgb[:, :, channel])), float(np.max(base_rgb[:, :, channel]))] for channel in range(3)]
    gates = {
        "inventory": set(expected) == {name for name in generated if name.startswith("RetroWeaponAccent")},
        "dimensions": all(generated[name]["dimensions"] == [2048, 2048] for name in expected),
        "authored_red_palette": base_ranges[0][0] >= contract["palette"][0][0] - tolerance and base_ranges[0][1] >= contract["palette"][1][0] and float(np.mean(base_rgb[:, :, 0])) >= 5.0 * float(np.mean(base_rgb[:, :, 1])) and float(np.mean(base_rgb[:, :, 0])) >= 5.0 * float(np.mean(base_rgb[:, :, 2])),
        "nonmetallic": metallic_range[0] >= contract["metallic"][0] - tolerance and metallic_range[1] <= contract["metallic"][1] + tolerance,
        "glass_smoothness": smoothness_range[0] >= contract["smoothness"][0] - tolerance and smoothness_range[1] <= contract["smoothness"][1] + tolerance,
        "glass_ao": ao_range[0] >= contract["ao"][0] - tolerance and ao_range[1] <= contract["ao"][1] + tolerance,
        "shallow_nonflat_normal": normal_xy_max <= contract["normal_xy_max"] and min(normal_xy_span) >= 2 and _normal_map_audit(normal, 2048, 2048)["valid"],
        "bounded_emission": contract["emission_coverage"][0] <= emission_coverage <= contract["emission_coverage"][1],
        "red_emission": emitted_rgb.size > 0 and float(np.mean(emitted_rgb[:, 0])) >= 8.0 * float(np.mean(emitted_rgb[:, 1])) and float(np.mean(emitted_rgb[:, 0])) >= 10.0 * float(np.mean(emitted_rgb[:, 2])),
    }
    return {
        "pass": all(gates.values()),
        "gates": gates,
        "base_ranges": base_ranges,
        "metallic_range": metallic_range,
        "smoothness_range": smoothness_range,
        "ao_range": ao_range,
        "normal_xy_max": normal_xy_max,
        "normal_xy_span_u8": normal_xy_span,
        "emission_coverage": emission_coverage,
    }


def run_semantic_audits(generated, frames=None, selected_families=None):
    """Run only semantic checks whose family buffers were selected/generated."""
    selected_ordered = tuple(selected_families or FAMILY_IDS)
    selected = set(selected_ordered)
    checks = {
        "selected_inventory": audit_selected_inventory(generated, selected_ordered),
        "tile_edges": audit_tile_edges(generated),
    }
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
    if "shield" in selected and "RetroShield" in generated:
        checks["shield"] = audit_shield_semantics(generated["RetroShield"]["buffer"])
    for kind in ("grass", "wall"):
        if kind in selected:
            checks[kind] = audit_continuous_surface(kind, generated)
    if "sky" in selected:
        checks["sky"] = audit_sky_clouds(generated)
    if any(family_id in selected for family_id in ("weapon-metal", "weapon-dark")):
        checks["weapon_readability"] = audit_weapon_readability(generated, selected)
    normal_maps = {}
    for name, entry in generated.items():
        if entry["map_type"] == "normal":
            width, height = entry["dimensions"]
            normal_maps[name] = _normal_map_audit(entry["buffer"], width, height)
    normal_pass = all(result["valid"] for result in normal_maps.values())
    ball_nonmetallic = checks.get("ball", {}).get("gates", {}).get("nonmetallic", True)
    wall_nonmetallic = checks.get("wall", {}).get("gates", {}).get("wall_nonmetallic", True)
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
        record("RetroSunnySky", 2048, 1024, generate_sky(), "sky", True, False, True)
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
    contract_pass = (len(generated) == FULL_OUTPUT_COUNT and len(previews) == FULL_PREVIEW_COUNT) if full else True
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
    parser.add_argument("--proof-two-run", action="store_true", help="Run the selected families twice and compare every selected output and preview hash.")
    return parser


def main(argv=None):
    args = build_argument_parser().parse_args(argv)
    selected = resolve_families(args.family)
    result = _run_once(selected, write_manifest=not args.proof_two_run)
    if args.proof_two_run:
        second = _run_once(selected, write_manifest=False)
        compare_generation_runs(result, second)
        manifest_name = "retro_texture_manifest.json" if tuple(selected) == FAMILY_IDS else "retro_texture_manifest_targeted.json"
        _write_json_if_changed(os.path.join(PREVIEW_DIRECTORY, manifest_name), second["manifest"])
        result = second
        print(f"PROOF RetroTextures: PASS ({result['manifest']['counts']['outputs']} output hashes, {result['manifest']['counts']['previews']} preview hashes)")
    print(f"MEMORY RetroTextures: compressed forecast {result['manifest']['memory_forecast']['compressed_mib']:.3f} MiB / 96.000 MiB")
    print(f"AUDIT RetroTextures: PASS ({result['manifest']['counts']['outputs']} outputs, {result['manifest']['counts']['previews']} previews, families={','.join(selected)}, seed {SEED})")
    return result


if __name__ == "__main__":
    main()
