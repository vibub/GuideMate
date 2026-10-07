"""Loopback fixture server with byte ranges and deliberately no CORS headers."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import re


class MediaHandler(SimpleHTTPRequestHandler):
    def serve_media(self, body):
        path = Path(self.translate_path(self.path))
        if path.suffix != '.webm' or not path.is_file():
            return super().do_GET() if body else super().do_HEAD()
        data = path.read_bytes()
        start, end = 0, len(data) - 1
        match = re.fullmatch(r'bytes=(\d+)-(\d*)', self.headers.get('Range', ''))
        if match:
            start = int(match[1])
            end = min(end, int(match[2])) if match[2] else end
            if start > end:
                self.send_error(416)
                return
        self.send_response(206 if match else 200)
        self.send_header('Content-Type', 'video/webm')
        self.send_header('Content-Length', str(end - start + 1))
        self.send_header('Accept-Ranges', 'bytes')
        if match:
            self.send_header('Content-Range', f'bytes {start}-{end}/{len(data)}')
        self.end_headers()
        if body:
            self.wfile.write(data[start:end + 1])

    def do_GET(self):
        self.serve_media(True)

    def do_HEAD(self):
        self.serve_media(False)


assets = Path(__file__).resolve().parents[1] / 'assets'
ThreadingHTTPServer(('127.0.0.1', 18768), partial(MediaHandler, directory=str(assets))).serve_forever()
