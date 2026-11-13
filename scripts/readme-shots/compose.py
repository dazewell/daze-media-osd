import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

SP, OUT = sys.argv[1], sys.argv[2]
FONT = r'C:\Windows\Fonts\SegUIVar.ttf'


def font(size, weight, opsz=36):
    f = ImageFont.truetype(FONT, size)
    axes = f.get_variation_axes()
    vals = []
    for a in axes:
        name = a['name'] if isinstance(a['name'], str) else a['name'].decode()
        if 'eight' in name: vals.append(weight)
        else: vals.append(max(a['minimum'], min(a['maximum'], opsz)))
    f.set_variation_by_axes(vals)
    return f


def rounded(img, radius):
    img = img.convert('RGBA')
    s = 4
    m = Image.new('L', (img.width * s, img.height * s), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, m.width - 1, m.height - 1], radius * s, fill=255)
    img.putalpha(m.resize(img.size, Image.LANCZOS))
    return img


def with_shadow(img, pad, blur, dy, alpha):
    canvas = Image.new('RGBA', (img.width + pad * 2, img.height + pad * 2), (0, 0, 0, 0))
    sh = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    a = img.getchannel('A').point(lambda v: v * alpha // 255)
    sh.paste(Image.new('RGBA', img.size, (0, 0, 0, 255)), (pad, pad + dy), a)
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    canvas.alpha_composite(sh)
    canvas.alpha_composite(img, (pad, pad))
    return canvas


def fit(img, width):
    return img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)


# Window frame box: PrintWindow leaves pure black where the invisible resize border is.
ref = Image.open(f'{SP}/light-pop-up.png').convert('RGB')
box = ImageChops.difference(ref, Image.new('RGB', ref.size, (0, 0, 0))).getbbox()
print('settings box', box)


def settings(theme, page):
    return rounded(Image.open(f'{SP}/{theme}-{page}.png').convert('RGB').crop(box), 16)


def osd(theme):
    im = Image.open(f'{SP}/{theme}-osd.png').convert('RGB')
    return rounded(im.crop((13, 1, 694, 308)), 16)


THEMES = {
    'light': dict(bg=((244, 240, 231), (226, 236, 236)), title=(18, 30, 30), sub=(66, 80, 78), accent=(8, 104, 116), shadow=70),
    'dark': dict(bg=((13, 22, 24), (22, 42, 46)), title=(244, 247, 246), sub=(170, 186, 184), accent=(92, 214, 230), shadow=170),
}

for theme, c in THEMES.items():
    # framed README shots, transparent so they sit on either GitHub theme
    with_shadow(fit(osd(theme), 600), 40, 14, 8, c['shadow']).save(f'{OUT}/popup-media-{theme}.png', optimize=True)
    for page, name in (('pop-up', 'settings-popup'), ('shortcuts', 'settings-shortcuts')):
        with_shadow(fit(settings(theme, page), 1000), 40, 16, 10, c['shadow']).save(f'{OUT}/{name}-{theme}.png', optimize=True)

    # hero
    W, H = 2400, 1100
    hero = Image.new('RGB', (W, H))
    (r1, g1, b1), (r2, g2, b2) = c['bg']
    grad = Image.linear_gradient('L').rotate(90, expand=True).resize((W, H))  # left -> right
    hero = Image.composite(Image.new('RGB', (W, H), (r2, g2, b2)), Image.new('RGB', (W, H), (r1, g1, b1)), grad)
    noise = Image.effect_noise((W, H), 6).convert('RGB')
    hero = Image.blend(hero, ImageChops.add(hero, noise, 1, -128), 0.5).convert('RGBA')

    win = with_shadow(fit(settings(theme, 'pop-up'), 1020), 120, 44, 28, c['shadow'])
    hero.alpha_composite(win, (1270 - 120, 150 - 120))
    pop = with_shadow(fit(osd(theme), 700), 120, 40, 24, min(255, c['shadow'] + 40))
    hero.alpha_composite(pop, (1080 - 120, 660 - 120))

    d = ImageDraw.Draw(hero)
    x = 150
    d.text((x - 6, 250), 'Surtitle', font=font(176, 650), fill=c['title'])
    d.text((x, 500), 'The Windows volume pop-up,', font=font(52, 400), fill=c['sub'])
    d.text((x, 570), 'with the things you wanted to know.', font=font(52, 400), fill=c['sub'])
    d.text((x, 700), 'Speakers   ·   Microphone   ·   Now playing', font=font(36, 600, 20), fill=c['accent'])
    d.rectangle([x, 222, x + 96, 230], fill=c['accent'])
    rounded(hero.convert('RGB'), 36).resize((1800, 825), Image.LANCZOS).save(f'{OUT}/hero-{theme}.png', optimize=True)
    print(theme, 'done')
