"""Generate deterministic original retro textures and diagnostic previews."""

import math
import os
import random

import bpy


REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEXTURE_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "_Game", "Textures")
PREVIEW_DIRECTORY = os.path.join(
    REPOSITORY_ROOT, "Temp", "BlenderPreviews", "RetroTextures"
)
SEED = 0xF00B411

TEXTURE_PATHS = {
    "RetroGrass": os.path.join(TEXTURE_DIRECTORY, "RetroGrass.png"),
    "RetroBall": os.path.join(TEXTURE_DIRECTORY, "RetroBall.png"),
    "RetroExplosion": os.path.join(TEXTURE_DIRECTORY, "RetroExplosion.png"),
    "RetroSmoke": os.path.join(TEXTURE_DIRECTORY, "RetroSmoke.png"),
}


def clamp01(value):
    return max(0.0, min(1.0, value))


def mix(a, b, amount):
    return a + (b - a) * amount


def smoothstep(edge0, edge1, value):
    if edge0 == edge1:
        return float(value >= edge1)
    t = clamp01((value - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


def rgba_mix(a, b, amount):
    return tuple(mix(a[index], b[index], amount) for index in range(4))


def make_buffer(width, height, fill=(0.0, 0.0, 0.0, 0.0)):
    return [fill for _ in range(width * height)]


def set_pixel(buffer, width, height, x, y, color, wrap=False):
    if wrap:
        x %= width
        y %= height
    if 0 <= x < width and 0 <= y < height:
        buffer[y * width + x] = color


def save_image(name, width, height, pixels, path):
    image = bpy.data.images.new(name=name, width=width, height=height, alpha=True)
    image.colorspace_settings.name = "sRGB"
    image.pixels.foreach_set([component for pixel in pixels for component in pixel])
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    if not os.path.isfile(path) or os.path.getsize(path) <= 0:
        raise RuntimeError(f"PNG write failed or empty: {path}")


def periodic_noise(x, y, phases):
    tau = math.tau
    value = 0.0
    weight = 0.0
    for frequency_x, frequency_y, phase, amplitude in phases:
        value += math.sin(tau * (frequency_x * x + frequency_y * y) + phase) * amplitude
        weight += amplitude
    return value / weight


def generate_grass():
    width = height = 128
    rng = random.Random(SEED)
    phases = [
        (rng.randint(1, 13), rng.randint(1, 13), rng.random() * math.tau, 1.0 / (i + 1))
        for i in range(18)
    ]
    pixels = make_buffer(width, height)
    dark = (0.055, 0.16, 0.075, 1.0)
    mid = (0.13, 0.34, 0.12, 1.0)
    light = (0.31, 0.52, 0.16, 1.0)

    for y in range(height):
        for x in range(width):
            u = x / width
            v = y / height
            noise = periodic_noise(u, v, phases)
            quantized = round(clamp01(0.48 + noise * 0.30) * 5.0) / 5.0
            color = rgba_mix(dark, mid, min(1.0, quantized * 1.45))
            if quantized > 0.72:
                color = rgba_mix(color, light, (quantized - 0.72) / 0.28)
            pixels[y * width + x] = color

    # Toroidally wrapped blade flecks keep the tile seamless.
    for _ in range(260):
        x = rng.randrange(width)
        y = rng.randrange(height)
        length = rng.choice((2, 3, 4, 5))
        lean = rng.choice((-1, 0, 0, 1))
        color = light if rng.random() > 0.37 else dark
        for step in range(length):
            set_pixel(pixels, width, height, x + lean * step // 2, y + step, color, wrap=True)

    # Equal opposing borders provide exact repeat continuity under bilinear sampling.
    for y in range(height):
        pixels[y * width + width - 1] = pixels[y * width]
    for x in range(width):
        pixels[(height - 1) * width + x] = pixels[x]
    return width, height, pixels


def angular_distance(a, b):
    return abs((a - b + math.pi) % math.tau - math.pi)


def generate_ball():
    width, height = 256, 128
    pixels = make_buffer(width, height)
    orange = (0.90, 0.22, 0.035, 1.0)
    orange_dark = (0.47, 0.055, 0.025, 1.0)
    cream = (0.93, 0.78, 0.48, 1.0)
    ink = (0.025, 0.018, 0.022, 1.0)

    for y in range(height):
        latitude = -math.pi / 2.0 + math.pi * y / (height - 1)
        pole_fade = math.sin(latitude + math.pi / 2.0) ** 2
        for x in range(width):
            longitude = -math.pi + math.tau * x / (width - 1)
            wave = math.sin(longitude * 3.0 + 0.4) * math.cos(latitude * 2.0)
            base = rgba_mix(orange_dark, orange, 0.72 + 0.16 * wave)

            # Broad offset cream panel, safely faded before either pole.
            panel_distance = math.sqrt(
                (angular_distance(longitude, 0.42) / 0.92) ** 2
                + ((latitude + 0.03) / 0.66) ** 2
            )
            panel = (1.0 - smoothstep(0.72, 1.0, panel_distance)) * pole_fade
            color = rgba_mix(base, cream, panel)

            # One black eye and a displaced slash make spin direction unambiguous.
            eye_distance = math.sqrt(
                (angular_distance(longitude, 0.22) / 0.22) ** 2
                + ((latitude - 0.04) / 0.18) ** 2
            )
            pupil_distance = math.sqrt(
                (angular_distance(longitude, 0.17) / 0.075) ** 2
                + ((latitude - 0.055) / 0.085) ** 2
            )
            slash = abs(latitude + 0.34 - 0.20 * math.sin(longitude - 1.25)) < 0.035
            slash = slash and angular_distance(longitude, 1.36) < 0.62
            if pole_fade > 0.08 and eye_distance < 1.0:
                color = ink
                if pupil_distance < 1.0:
                    color = orange
            if pole_fade > 0.12 and slash:
                color = ink
            pixels[y * width + x] = color

    # Pole-safe rows are uniform. U endpoints are byte-identical in float space.
    for x in range(width):
        pixels[x] = orange_dark
        pixels[(height - 1) * width + x] = orange_dark
    for y in range(height):
        pixels[y * width + width - 1] = pixels[y * width]
    return width, height, pixels


def generate_radial_sprite(kind):
    width = height = 32
    pixels = make_buffer(width, height)
    phase = 0.61 if kind == "explosion" else 1.73
    for y in range(height):
        for x in range(width):
            nx = (x + 0.5 - width / 2.0) / (width / 2.0)
            ny = (y + 0.5 - height / 2.0) / (height / 2.0)
            radius = math.sqrt(nx * nx + ny * ny)
            angle = math.atan2(ny, nx)
            irregular = (
                0.07 * math.sin(5.0 * angle + phase)
                + 0.045 * math.sin(9.0 * angle - phase * 1.7)
                + 0.025 * math.sin(13.0 * angle + 0.8)
            )
            edge = (0.88 if kind == "explosion" else 0.82) + irregular
            alpha = 1.0 - smoothstep(edge - 0.24, edge, radius)
            if kind == "explosion":
                hot = (1.0, 0.91, 0.42, alpha)
                orange = (1.0, 0.20, 0.025, alpha)
                ember = (0.22, 0.015, 0.012, alpha)
                color = rgba_mix(hot, orange, smoothstep(0.10, 0.52, radius))
                color = rgba_mix(color, ember, smoothstep(0.52, edge, radius))
                # Uneven dark cavities prevent a generic perfect fireball.
                cavity = math.sqrt((nx + 0.22) ** 2 + (ny - 0.16) ** 2)
                if cavity < 0.13:
                    color = (0.14, 0.012, 0.009, alpha * 0.82)
            else:
                core = (0.33, 0.30, 0.29, alpha * 0.88)
                rim = (0.075, 0.070, 0.075, alpha * 0.62)
                color = rgba_mix(core, rim, smoothstep(0.12, edge, radius))
                puff = 0.12 * math.sin(4.0 * angle + 2.0 * radius + phase)
                color = (color[0] + puff * alpha, color[1] + puff * alpha, color[2] + puff * alpha, color[3])
                color = tuple(clamp01(component) for component in color)
            pixels[y * width + x] = color
    return width, height, pixels


def audit_texture(name, width, height, pixels, expected_size, seam_u=False, seam_v=False, poles=False):
    if (width, height) != expected_size or len(pixels) != width * height:
        raise RuntimeError(f"{name}: dimensions/buffer mismatch")
    if not all(math.isfinite(component) and 0.0 <= component <= 1.0 for pixel in pixels for component in pixel):
        raise RuntimeError(f"{name}: non-finite or out-of-range pixel")
    seam_error = 0.0
    seam_v_error = 0.0
    pole_error = 0.0
    if seam_u:
        seam_error = max(
            abs(pixels[y * width][channel] - pixels[y * width + width - 1][channel])
            for y in range(height)
            for channel in range(4)
        )
        if seam_error > 1.0e-7:
            raise RuntimeError(f"{name}: U seam error {seam_error}")
    if seam_v:
        seam_v_error = max(
            abs(pixels[x][channel] - pixels[(height - 1) * width + x][channel])
            for x in range(width)
            for channel in range(4)
        )
        if seam_v_error > 1.0e-7:
            raise RuntimeError(f"{name}: V seam error {seam_v_error}")
    if poles:
        for y in (0, height - 1):
            reference = pixels[y * width]
            pole_error = max(
                pole_error,
                max(abs(pixels[y * width + x][c] - reference[c]) for x in range(width) for c in range(4)),
            )
        if pole_error > 1.0e-7:
            raise RuntimeError(f"{name}: pole discontinuity {pole_error}")
    alphas = [pixel[3] for pixel in pixels]
    if name in ("RetroExplosion", "RetroSmoke") and (min(alphas) > 0.01 or max(alphas) < 0.50):
        raise RuntimeError(f"{name}: insufficient alpha range {min(alphas)}..{max(alphas)}")
    print(
        f"AUDIT {name}: {width}x{height}, finite={len(pixels)}, "
        f"seamU={seam_error:.8f}, seamV={seam_v_error:.8f}, "
        f"poles={pole_error:.8f}, alpha={min(alphas):.4f}..{max(alphas):.4f}"
    )


def checker(width, height, cell=12):
    return [
        ((0.16, 0.16, 0.18, 1.0) if ((x // cell + y // cell) % 2) else (0.42, 0.42, 0.45, 1.0))
        for y in range(height)
        for x in range(width)
    ]


def blit(destination, destination_width, destination_height, source, source_width, source_height, left, bottom, scale=1, alpha_blend=False):
    for sy in range(source_height):
        for sx in range(source_width):
            source_pixel = source[sy * source_width + sx]
            for oy in range(scale):
                for ox in range(scale):
                    dx = left + sx * scale + ox
                    dy = bottom + sy * scale + oy
                    if not (0 <= dx < destination_width and 0 <= dy < destination_height):
                        continue
                    index = dy * destination_width + dx
                    if alpha_blend:
                        alpha = source_pixel[3]
                        under = destination[index]
                        destination[index] = (
                            mix(under[0], source_pixel[0], alpha),
                            mix(under[1], source_pixel[1], alpha),
                            mix(under[2], source_pixel[2], alpha),
                            1.0,
                        )
                    else:
                        destination[index] = source_pixel


def make_previews(textures):
    previews = []

    grass = textures["RetroGrass"]
    canvas = make_buffer(512, 512)
    for tile_y in range(4):
        for tile_x in range(4):
            blit(canvas, 512, 512, grass[2], 128, 128, tile_x * 128, tile_y * 128)
    previews.append(("01_grass_4x4_tile.png", 512, 512, canvas))

    ball = textures["RetroBall"]
    canvas = make_buffer(512, 256, (0.09, 0.07, 0.06, 1.0))
    blit(canvas, 512, 256, ball[2], 256, 128, 0, 0)
    blit(canvas, 512, 256, ball[2], 256, 128, 256, 0)
    blit(canvas, 512, 256, ball[2], 256, 128, 128, 128)
    previews.append(("02_ball_layout_repeat.png", 512, 256, canvas))

    canvas = make_buffer(512, 256, (0.08, 0.08, 0.08, 1.0))
    seam_strip = []
    for y in range(128):
        seam_strip.extend(ball[2][y * 256 + 240:y * 256 + 256])
        seam_strip.extend(ball[2][y * 256:y * 256 + 16])
    blit(canvas, 512, 256, seam_strip, 32, 128, 0, 0, scale=2)
    top = ball[2][127 * 256:128 * 256]
    bottom = ball[2][0:256]
    blit(canvas, 512, 256, top, 256, 1, 0, 212, scale=2)
    blit(canvas, 512, 256, bottom, 256, 1, 0, 40, scale=2)
    previews.append(("03_ball_seam_and_poles.png", 512, 256, canvas))

    for index, name in enumerate(("RetroExplosion", "RetroSmoke"), start=4):
        sprite = textures[name]
        canvas = checker(384, 384, 24)
        blit(canvas, 384, 384, sprite[2], 32, 32, 64, 64, scale=8, alpha_blend=True)
        previews.append((f"{index:02d}_{name[5:].lower()}_alpha.png", 384, 384, canvas))

    canvas = checker(768, 384, 16)
    blit(canvas, 768, 384, grass[2], 128, 128, 0, 128, scale=2)
    blit(canvas, 768, 384, ball[2], 256, 128, 256, 128, scale=2)
    blit(canvas, 768, 384, textures["RetroExplosion"][2], 32, 32, 256, 0, scale=4, alpha_blend=True)
    blit(canvas, 768, 384, textures["RetroSmoke"][2], 32, 32, 448, 0, scale=4, alpha_blend=True)
    previews.append(("06_texture_atlas_overview.png", 768, 384, canvas))

    for filename, width, height, pixels in previews:
        path = os.path.join(PREVIEW_DIRECTORY, filename)
        save_image(os.path.splitext(filename)[0], width, height, pixels, path)
        print(f"PREVIEW {path}: {width}x{height}, {os.path.getsize(path)} bytes")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(TEXTURE_DIRECTORY, exist_ok=True)
    os.makedirs(PREVIEW_DIRECTORY, exist_ok=True)

    textures = {
        "RetroGrass": generate_grass(),
        "RetroBall": generate_ball(),
        "RetroExplosion": generate_radial_sprite("explosion"),
        "RetroSmoke": generate_radial_sprite("smoke"),
    }
    contracts = {
        "RetroGrass": ((128, 128), True, True, False),
        "RetroBall": ((256, 128), True, False, True),
        "RetroExplosion": ((32, 32), False, False, False),
        "RetroSmoke": ((32, 32), False, False, False),
    }
    for name, (width, height, pixels) in textures.items():
        expected_size, seam_u, seam_v, poles = contracts[name]
        audit_texture(name, width, height, pixels, expected_size, seam_u, seam_v, poles)
        save_image(name, width, height, pixels, TEXTURE_PATHS[name])
        print(f"OUTPUT {TEXTURE_PATHS[name]}: {os.path.getsize(TEXTURE_PATHS[name])} bytes")

    make_previews(textures)
    print("AUDIT RetroTextures: PASS (4 textures, 6 previews, deterministic seed)")


if __name__ == "__main__":
    main()
