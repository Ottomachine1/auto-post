"""Private, offline machine translation. No cloud API or automatic downloads."""
import json
import os
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

os.environ.setdefault("ARGOS_DEVICE_TYPE", "cpu")
os.environ.setdefault("OMP_NUM_THREADS", "1")
import argostranslate.translate
import langid

LOCK = threading.Lock()
LIMIT = 100_000


def translate(text, source):
    if not text.strip():
        return ""
    languages = {language.code: language for language in argostranslate.translate.get_installed_languages()}
    if source not in languages or "zh" not in languages:
        raise ValueError("language_not_installed")
    translator = languages[source].get_translation(languages["zh"])
    # Preserve paragraphs; Argos splits sentences internally.
    return "\n".join(translator.translate(part) if part.strip() else part for part in text.split("\n"))


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass  # Never log news content or request bodies.

    def respond(self, code, value):
        body = json.dumps(value, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path != "/health":
            return self.respond(404, {"error": "not_found"})
        languages = [language.code for language in argostranslate.translate.get_installed_languages()]
        self.respond(200, {"engine": "argos-offline", "languages": languages, "ready": "en" in languages and "zh" in languages})

    def do_POST(self):
        if self.path != "/translate":
            return self.respond(404, {"error": "not_found"})
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= LIMIT:
                return self.respond(413, {"error": "invalid_size"})
            data = json.loads(self.rfile.read(length))
            title, body, source = data["title"], data["body"], data.get("source", "en")
            if not all(isinstance(value, str) for value in (title, body, source)) or len(title) > 1000 or len(body) > 25000:
                return self.respond(400, {"error": "invalid_input"})
            if source == "auto":
                source = langid.classify(body or title)[0]
            if source == "zh":
                return self.respond(200, {"title": title, "body": body, "engine": "argos-offline", "source": "zh"})
            if not LOCK.acquire(blocking=False):
                return self.respond(429, {"error": "busy"})
            try:
                translated_title, translated_body = translate(title, source), translate(body, source)
            finally:
                LOCK.release()
            if (title.strip() and not translated_title.strip()) or (body.strip() and not translated_body.strip()):
                raise ValueError("empty_translation")
            self.respond(200, {"title": translated_title, "body": translated_body, "engine": "argos-offline", "source": source})
        except (ValueError, KeyError, TypeError):
            self.respond(422, {"error": "invalid_input_or_missing_language"})
        except Exception:
            self.respond(503, {"error": "translation_unavailable"})


if __name__ == "__main__":
    ThreadingHTTPServer((os.environ.get("TRANSLATION_BIND", "127.0.0.1"), 8092), Handler).serve_forever()
