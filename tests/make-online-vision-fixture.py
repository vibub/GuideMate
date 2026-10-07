"""Generate a six-second synthetic arrow clip for WebView2 integration checks."""
from pathlib import Path
import subprocess
import sys
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
arrow = Image.new('RGB', (160, 160))
ImageDraw.Draw(arrow).polygon([(80, 34), (116, 115), (80, 95), (44, 115)], fill=(20, 220, 255))
frames = []
for angle in (0, 90, None):
    image = Image.new('RGB', (1280, 720), (24, 32, 28))
    if angle is not None:
        marker = arrow.rotate(-angle).resize((94, 94))
        image.paste(marker, (140, 136))
    frames.append(image.tobytes())
command = [sys.argv[1], '-nostdin', '-hide_banner', '-loglevel', 'error', '-y',
           '-f', 'rawvideo', '-pixel_format', 'rgb24', '-video_size', '1280x720', '-framerate', '10',
           '-i', 'pipe:0', '-an', '-c:v', 'libvpx-vp9', '-crf', '12', '-b:v', '0',
           '-pix_fmt', 'yuv420p', str(root / 'assets' / 'online-vision-test.webm')]
with subprocess.Popen(command, stdin=subprocess.PIPE) as process:
    for pixels in frames:
        for _ in range(20):
            process.stdin.write(pixels)
    process.stdin.close()
    if process.wait() != 0:
        raise SystemExit('Fixture encoding failed')
print('Generated assets/online-vision-test.webm')
subprocess.run([sys.argv[1], '-nostdin', '-hide_banner', '-loglevel', 'error', '-y',
                '-i', str(root / 'assets' / 'online-vision-test.webm'), '-vf', 'scale=640:360',
                '-c:v', 'libvpx-vp9', '-crf', '12', '-b:v', '0',
                str(root / 'assets' / 'online-vision-small.webm')], check=True)
print('Generated assets/online-vision-small.webm')
