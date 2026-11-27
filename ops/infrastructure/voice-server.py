#!/usr/bin/env python3
import json
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, HTTPServer
import tempfile
from faster_whisper import WhisperModel

MODEL = WhisperModel("base", device="cpu", compute_type="int8", cpu_threads=2, num_workers=1)

class Handler(BaseHTTPRequestHandler):
    def setup(self):
        super().setup()
        self.connection.settimeout(15)

    def reply(self, code, body):
        payload = json.dumps(body, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        self.reply(200, {"status": "ready"}) if self.path == "/health" else self.reply(404, {})

    def do_POST(self):
        if self.path != "/v1/audio/transcriptions":
            return self.reply(404, {})
        try:
            length = int(self.headers.get("Content-Length", "0"))
            kind = self.headers.get("Content-Type", "")
            if not 0 < length <= 6 * 1024 * 1024 or not kind.startswith("multipart/form-data;"):
                return self.reply(400, {"error": "invalid request"})
            message = BytesParser(policy=policy.default).parsebytes(
                ("Content-Type: " + kind + "\r\nMIME-Version: 1.0\r\n\r\n").encode() + self.rfile.read(length))
            parts = [part for part in message.iter_parts() if part.get_param("name", header="content-disposition") == "file"]
            if len(parts) != 1:
                return self.reply(400, {"error": "file required"})
            audio = parts[0].get_payload(decode=True)
            if not audio or len(audio) > 5 * 1024 * 1024:
                return self.reply(413, {"error": "audio too large"})
            with tempfile.NamedTemporaryFile() as file:
                file.write(audio)
                file.flush()
                segments, _ = MODEL.transcribe(file.name, beam_size=1, vad_filter=True, clip_timestamps="0,120")
                text = " ".join(segment.text.strip() for segment in segments)[:6000]
            self.reply(200, {"text": text})
        except Exception:
            self.reply(422, {"error": "transcription failed"})

if __name__ == "__main__":
    HTTPServer(("127.0.0.1", 1237), Handler).serve_forever()
