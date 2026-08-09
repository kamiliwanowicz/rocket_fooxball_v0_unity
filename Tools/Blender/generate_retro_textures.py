"""Generate deterministic bright retro textures and diagnostic previews."""

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
    name: os.path.join(TEXTURE_DIRECTORY, name + ".png")
    for name in (
        "RetroGrass",
        "RetroBall",
        "RetroExplosion",
        "RetroSmoke",
        "RetroWall",
        "RetroTrim",
        "RetroHazard",
        "RetroShield",
    )
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
    value = 0.0
    weight = 0.0
    for frequency_x, frequency_y, phase, amplitude in phases:
        value += math.sin(math.tau * (frequency_x * x + frequency_y * y) + phase) * amplitude
        weight += amplitude
    return value / weight


def periodic_phases(rng, count, max_frequency=13):
    return [
        (
            rng.randint(1, max_frequency),
            rng.randint(1, max_frequency),
            rng.random() * math.tau,
            1.0 / (index + 1),
        )
        for index in range(count)
    ]


def close_repeat_edges(pixels, width, height):
    """Copy opposite borders so filtering never reveals a tile seam."""
    for y in range(height):
        pixels[y * width + width - 1] = pixels[y * width]
    for x in range(width):
        pixels[(height - 1) * width + x] = pixels[x]


def generate_grass():
    width = height = 128
    rng = random.Random(SEED ^ 0x11)
    phases = periodic_phases(rng, 18)
    pixels = make_buffer(width, height)
    deep = (0.018, 0.23, 0.25, 1.0)
    mid = (0.035, 0.47, 0.43, 1.0)
    light = (0.16, 0.70, 0.56, 1.0)

    for y in range(height):
        for x in range(width):
            noise = periodic_noise(x / width, y / height, phases)
            quantized = round(clamp01(0.52 + noise * 0.34) * 5.0) / 5.0
            color = rgba_mix(deep, mid, min(1.0, quantized * 1.40))
            if quantized > 0.70:
                color = rgba_mix(color, light, (quantized - 0.70) / 0.30)
            pixels[y * width + x] = color

    # Pale flecks read as turf blades while wrapped placement preserves tiling.
    pale = (0.63, 0.94, 0.76, 1.0)
    pale_shadow = (0.30, 0.72, 0.60, 1.0)
    for _ in range(330):
        x = rng.randrange(width)
        y = rng.randrange(height)
        length = rng.choice((1, 2, 2, 3, 4))
        lean = rng.choice((-1, 0, 0, 1))
        color = pale if rng.random() > 0.30 else pale_shadow
        for step in range(length):
            set_pixel(pixels, width, height, x + lean * (step // 2), y + step, color, wrap=True)

    close_repeat_edges(pixels, width, height)
    return width, height, pixels


def generate_wall():
    width = height = 128
    rng = random.Random(SEED ^ 0x22)
    phases = periodic_phases(rng, 14)
    pixels = make_buffer(width, height)
    ivory_dark = (0.52, 0.66, 0.65, 1.0)
    ivory = (0.80, 0.88, 0.82, 1.0)
    ivory_light = (0.96, 0.97, 0.86, 1.0)
    cyan_seam = (0.13, 0.82, 0.84, 1.0)
    cyan_shadow = (0.03, 0.42, 0.49, 1.0)

    for y in range(height):
        for x in range(width):
            noise = periodic_noise(x / width, y / height, phases)
            value = clamp01(0.52 + noise * 0.20)
            color = rgba_mix(ivory_dark, ivory, value)
            if value > 0.70:
                color = rgba_mix(color, ivory_light, (value - 0.70) / 0.30)

            # Four-panel cadence gives readable seams without unique UVs.
            seam_x = x % 32
            seam_y = y % 32
            if seam_x <= 1 or seam_y <= 1:
                color = cyan_seam
            elif seam_x == 2 or seam_y == 2:
                color = cyan_shadow
            elif (x // 32 + y // 32) % 2 == 0 and (x % 16 == 7 or y % 16 == 9):
                color = rgba_mix(color, ivory_light, 0.20)
            pixels[y * width + x] = color

    close_repeat_edges(pixels, width, height)
    return width, height, pixels


def generate_trim():
    width = height = 128
    pixels = make_buffer(width, height)
    cyan = (0.06, 0.78, 0.84, 1.0)
    cyan_dark = (0.02, 0.34, 0.44, 1.0)
    white = (0.90, 1.0, 0.95, 1.0)
    blue = (0.22, 0.56, 0.78, 1.0)
    period = 32

    for y in range(height):
        for x in range(width):
            diagonal = (x + y) % period
            if diagonal < 5:
                color = white
            elif diagonal < 9:
                color = cyan
            elif diagonal < 12:
                color = cyan_dark
            else:
                color = blue
            # Periodic rivets break broad bands into authored low-poly modules.
            if x % period in (3, 4) and y % period in (3, 4):
                color = white
            pixels[y * width + x] = color

    close_repeat_edges(pixels, width, height)
    return width, height, pixels


def generate_hazard():
    width = height = 128
    pixels = make_buffer(width, height)
    yellow = (1.0, 0.82, 0.10, 1.0)
    yellow_light = (1.0, 0.96, 0.45, 1.0)
    white = (0.94, 0.98, 0.90, 1.0)
    navy = (0.035, 0.15, 0.22, 1.0)
    period = 32

    for y in range(height):
        for x in range(width):
            diagonal = (x + y) % period
            if diagonal < 2:
                color = navy
            elif diagonal < 12:
                color = yellow_light if diagonal < 6 else yellow
            elif diagonal < 14:
                color = navy
            elif diagonal < 26:
                color = white
            else:
                color = navy
            pixels[y * width + x] = color

    close_repeat_edges(pixels, width, height)
    return width, height, pixels


def generate_shield():
    width = height = 128
    pixels = make_buffer(width, height)
    for y in range(height):
        for x in range(width):
            # Three wrapped line families form a clamp-safe hex/energy mask.
            u = (x + 0.5) / 16.0
            v = (y + 0.5) / 16.0
            fu = u - math.floor(u)
            fv_a = (v + u * 0.58) - math.floor(v + u * 0.58)
            fv_b = (v - u * 0.58) - math.floor(v - u * 0.58)
            line_distance = min(min(fu, 1.0 - fu), min(fv_a, 1.0 - fv_a), min(fv_b, 1.0 - fv_b))
            line = 1.0 - smoothstep(0.015, 0.105, line_distance)
            pulse = 0.5 + 0.5 * math.sin(math.tau * (u * 0.13 + v * 0.17))
            alpha = clamp01(0.035 + line * (0.76 + 0.18 * pulse))
            color = (
                clamp01(0.44 + 0.18 * pulse + 0.22 * line),
                clamp01(0.82 + 0.12 * pulse),
                clamp01(0.92 + 0.08 * line),
                alpha,
            )
            pixels[y * width + x] = color
    return width, height, pixels


def angular_distance(a, b):
    return abs((a - b + math.pi) % math.tau - math.pi)


def generate_ball():
    width, height = 256, 128
    pixels = make_buffer(width, height)
    orange = (1.0, 0.26, 0.025, 1.0)
    orange_dark = (0.50, 0.035, 0.015, 1.0)
    cream = (1.0, 0.88, 0.50, 1.0)
    ink = (0.015, 0.012, 0.018, 1.0)

    for y in range(height):
        latitude = -math.pi / 2.0 + math.pi * y / (height - 1)
        pole_fade = math.sin(latitude + math.pi / 2.0) ** 2
        for x in range(width):
            longitude = -math.pi + math.tau * x / (width - 1)
            wave = math.sin(longitude * 3.0 + 0.4) * math.cos(latitude * 2.0)
            base = rgba_mix(orange_dark, orange, 0.72 + 0.16 * wave)

            panel_distance = math.sqrt(
                (angular_distance(longitude, 0.42) / 0.92) ** 2
                + ((latitude + 0.03) / 0.66) ** 2
            )
            panel = (1.0 - smoothstep(0.72, 1.0, panel_distance)) * pole_fade
            color = rgba_mix(base, cream, panel)

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

    for x in range(width):
        pixels[x] = orange_dark
        pixels[(height - 1) * width + x] = orange_dark
    for y in range(height):
        pixels[y * width + width - 1] = pixels[y * width]
    return width, height, pixels


def generate_radial_sprite(kind, variant):
    width = height = 32
    pixels = make_buffer(width, height)
    phase = (0.61 if kind == "explosion" else 1.73) + variant * 0.79
    edge_base = (0.88 if kind == "explosion" else 0.82) + 0.035 * math.sin(variant * 1.9)
    for y in range(height):
        for x in range(width):
            nx = (x + 0.5 - width / 2.0) / (width / 2.0)
            ny = (y + 0.5 - height / 2.0) / (height / 2.0)
            radius = math.sqrt(nx * nx + ny * ny)
            angle = math.atan2(ny, nx)
            irregular = (
                0.07 * math.sin(5.0 * angle + phase)
                + 0.045 * math.sin(9.0 * angle - phase * 1.7)
                + 0.025 * math.sin(13.0 * angle + 0.8 + variant)
            )
            edge = edge_base + irregular
            alpha = 1.0 - smoothstep(edge - 0.24, edge, radius)
            if kind == "explosion":
                hot = (1.0, 0.94, 0.36, alpha)
                orange = (1.0, 0.18 + 0.05 * variant, 0.018, alpha)
                ember = (0.20, 0.010, 0.008, alpha)
                color = rgba_mix(hot, orange, smoothstep(0.10, 0.52, radius))
                color = rgba_mix(color, ember, smoothstep(0.52, edge, radius))
                cavity = math.sqrt((nx + 0.22 * math.cos(variant)) ** 2 + (ny - 0.16) ** 2)
                if cavity < 0.13:
                    color = (0.14, 0.012, 0.009, alpha * 0.82)
            else:
                core = (0.39, 0.37, 0.35, alpha * 0.90)
                rim = (0.07, 0.075, 0.09, alpha * 0.60)
                color = rgba_mix(core, rim, smoothstep(0.12, edge, radius))
                puff = 0.12 * math.sin(4.0 * angle + 2.0 * radius + phase)
                color = (color[0] + puff * alpha, color[1] + puff * alpha, color[2] + puff * alpha, color[3])
                color = tuple(clamp01(component) for component in color)
            pixels[y * width + x] = color
    return width, height, pixels


def generate_sprite_sheet(kind):
    width = height = 128
    sheet = make_buffer(width, height)
    for cell_y in range(4):
        for cell_x in range(4):
            variant = cell_y * 4 + cell_x
            sprite_width, sprite_height, sprite = generate_radial_sprite(kind, variant)
            blit(sheet, width, height, sprite, sprite_width, sprite_height, cell_x * 32, cell_y * 32)
    return width, height, sheet


def audit_texture(
    name,
    width,
    height,
    pixels,
    expected_size,
    seam_u=False,
    seam_v=False,
    poles=False,
    alpha_range=None,
    sheet_cells=False,
):
    if (width, height) != expected_size or len(pixels) != width * height:
        raise RuntimeError(f"{name}: dimensions/buffer mismatch")
    if not all(math.isfinite(component) and 0.0 <= component <= 1.0 for pixel in pixels for component in pixel):
        raise RuntimeError(f"{name}: non-finite or out-of-range pixel")

    values = [0.2126 * pixel[0] + 0.7152 * pixel[1] + 0.0722 * pixel[2] for pixel in pixels]
    value_min, value_max = min(values), max(values)
    if value_max - value_min < 0.025:
        raise RuntimeError(f"{name}: flat value range {value_min:.4f}..{value_max:.4f}")

    seam_u_error = 0.0
    seam_v_error = 0.0
    pole_error = 0.0
    if seam_u:
        seam_u_error = max(
            abs(pixels[y * width][channel] - pixels[y * width + width - 1][channel])
            for y in range(height)
            for channel in range(4)
        )
        if seam_u_error > 1.0e-7:
            raise RuntimeError(f"{name}: U seam error {seam_u_error}")
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
    alpha_min, alpha_max = min(alphas), max(alphas)
    transparent_count = sum(alpha < 0.98 for alpha in alphas)
    if alpha_range is not None:
        minimum, maximum = alpha_range
        if alpha_min > minimum or alpha_max < maximum:
            raise RuntimeError(
                f"{name}: controlled alpha range {alpha_min:.4f}..{alpha_max:.4f}, expected <= {minimum:.4f} and >= {maximum:.4f}"
            )
    if sheet_cells:
        for cell_y in range(4):
            for cell_x in range(4):
                cell_alphas = [
                    pixels[(cell_y * 32 + y) * width + cell_x * 32 + x][3]
                    for y in range(32)
                    for x in range(32)
                ]
                if max(cell_alphas) < 0.50:
                    raise RuntimeError(f"{name}: empty sprite cell {cell_x},{cell_y}")

    print(
        f"AUDIT {name}: {width}x{height}, finite={len(pixels)}, "
        f"value={value_min:.4f}..{value_max:.4f}, "
        f"seamU={seam_u_error:.8f}, seamV={seam_v_error:.8f}, "
        f"poles={pole_error:.8f}, alpha={alpha_min:.4f}..{alpha_max:.4f}, "
        f"transparent={transparent_count}"
    )


def checker(width, height, cell=12):
    return [
        ((0.10, 0.13, 0.18, 1.0) if ((x // cell + y // cell) % 2) else (0.42, 0.48, 0.54, 1.0))
        for y in range(height)
        for x in range(width)
    ]


def blit(
    destination,
    destination_width,
    destination_height,
    source,
    source_width,
    source_height,
    left,
    bottom,
    scale=1,
    alpha_blend=False,
):
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


def tiled_preview(name, texture, scale=4):
    width, height, pixels = texture
    canvas = make_buffer(width * scale, height * scale)
    for tile_y in range(scale):
        for tile_x in range(scale):
            blit(canvas, width * scale, height * scale, pixels, width, height, tile_x * width, tile_y * height)
    return name, width * scale, height * scale, canvas


def alpha_preview(name, texture, scale=4):
    width, height, pixels = texture
    canvas = checker(width * scale, height * scale, max(8, 6 * scale))
    blit(canvas, width * scale, height * scale, pixels, width, height, 0, 0, scale=scale, alpha_blend=True)
    return name, width * scale, height * scale, canvas


def make_previews(textures):
    previews = [
        tiled_preview("01_grass_4x4_tile.png", textures["RetroGrass"]),
        tiled_preview("02_wall_4x4_tile.png", textures["RetroWall"]),
        tiled_preview("03_trim_4x4_tile.png", textures["RetroTrim"]),
        tiled_preview("04_hazard_4x4_tile.png", textures["RetroHazard"]),
        alpha_preview("05_shield_alpha.png", textures["RetroShield"]),
    ]

    ball = textures["RetroBall"]
    ball_canvas = make_buffer(512, 256, (0.06, 0.07, 0.10, 1.0))
    blit(ball_canvas, 512, 256, ball[2], 256, 128, 0, 0)
    blit(ball_canvas, 512, 256, ball[2], 256, 128, 256, 0)
    blit(ball_canvas, 512, 256, ball[2], 256, 128, 0, 128)
    blit(ball_canvas, 512, 256, ball[2], 256, 128, 256, 128)
    previews.append(("06_ball_layout_repeat.png", 512, 256, ball_canvas))

    previews.extend(
        [
            alpha_preview("07_explosion_sheet_alpha.png", textures["RetroExplosion"], scale=3),
            alpha_preview("08_smoke_sheet_alpha.png", textures["RetroSmoke"], scale=3),
        ]
    )

    atlas = checker(768, 512, 16)
    blit(atlas, 768, 512, textures["RetroGrass"][2], 128, 128, 0, 256, scale=2)
    blit(atlas, 768, 512, textures["RetroWall"][2], 128, 128, 256, 256, scale=2)
    blit(atlas, 768, 512, textures["RetroTrim"][2], 128, 128, 512, 256, scale=2)
    blit(atlas, 768, 512, textures["RetroHazard"][2], 128, 128, 0, 0, scale=2)
    blit(atlas, 768, 512, textures["RetroShield"][2], 128, 128, 256, 0, scale=2, alpha_blend=True)
    blit(atlas, 768, 512, textures["RetroExplosion"][2], 128, 128, 512, 0, scale=2, alpha_blend=True)
    previews.append(("09_texture_atlas_overview.png", 768, 512, atlas))

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
        "RetroWall": generate_wall(),
        "RetroTrim": generate_trim(),
        "RetroHazard": generate_hazard(),
        "RetroShield": generate_shield(),
        "RetroBall": generate_ball(),
        "RetroExplosion": generate_sprite_sheet("explosion"),
        "RetroSmoke": generate_sprite_sheet("smoke"),
    }
    contracts = {
        "RetroGrass": ((128, 128), True, True, False, None, False),
        "RetroWall": ((128, 128), True, True, False, None, False),
        "RetroTrim": ((128, 128), True, True, False, None, False),
        "RetroHazard": ((128, 128), True, True, False, None, False),
        "RetroShield": ((128, 128), False, False, False, (0.05, 0.70), False),
        "RetroBall": ((256, 128), True, False, True, None, False),
        "RetroExplosion": ((128, 128), False, False, False, (0.01, 0.50), True),
        "RetroSmoke": ((128, 128), False, False, False, (0.01, 0.50), True),
    }
    for name, texture in textures.items():
        width, height, pixels = texture
        expected_size, seam_u, seam_v, poles, alpha_range, sheet_cells = contracts[name]
        audit_texture(
            name,
            width,
            height,
            pixels,
            expected_size,
            seam_u,
            seam_v,
            poles,
            alpha_range,
            sheet_cells,
        )
        save_image(name, width, height, pixels, TEXTURE_PATHS[name])
        print(f"OUTPUT {TEXTURE_PATHS[name]}: {os.path.getsize(TEXTURE_PATHS[name])} bytes")

    make_previews(textures)
    print("AUDIT RetroTextures: PASS (8 textures, 9 previews, deterministic seed)")


if __name__ == "__main__":
    main()
