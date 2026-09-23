"""Send a daily-news Markdown attachment with Gmail OAuth; no SMTP password."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import mimetypes
import os
import secrets
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from email.message import EmailMessage
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SECRET_DIR = ROOT / ".codex" / "secrets"
CLIENT_FILE = SECRET_DIR / "gmail-oauth-client.json"
TOKEN_FILE = SECRET_DIR / "gmail-oauth-token.json"
SENDER = "yangli.graphic.dev@gmail.com"
SCOPE = "openid email https://www.googleapis.com/auth/gmail.send"
TOKEN_URL = "https://oauth2.googleapis.com/token"


def request_json(url: str, data: dict[str, str] | None = None, token: str | None = None) -> dict:
    body = urllib.parse.urlencode(data).encode() if data is not None else None
    headers = {"Accept": "application/json"}
    if body is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded"
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(url, data=body, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        detail = error.read(1000).decode("utf-8", errors="replace")
        raise RuntimeError(f"Google API HTTP {error.code}: {detail}") from error


def read_client() -> dict:
    if not CLIENT_FILE.is_file():
        raise RuntimeError(
            f"Missing OAuth desktop client JSON: {CLIENT_FILE}. "
            "See scripts/README-daily-news-email.md."
        )
    client = json.loads(CLIENT_FILE.read_text(encoding="utf-8")).get("installed")
    if not client or not client.get("client_id") or not client.get("client_secret"):
        raise RuntimeError("OAuth client JSON must be a Google Desktop app credential.")
    return client


def save_token(token: dict) -> None:
    SECRET_DIR.mkdir(parents=True, exist_ok=True)
    temporary = TOKEN_FILE.with_suffix(".tmp")
    temporary.write_text(json.dumps(token, indent=2), encoding="utf-8")
    os.replace(temporary, TOKEN_FILE)


def verify_sender(access_token: str) -> None:
    profile = request_json("https://openidconnect.googleapis.com/v1/userinfo", token=access_token)
    actual = profile.get("email", "")
    if actual.lower() != SENDER.lower() or profile.get("email_verified") is False:
        raise RuntimeError(f"Authorized Google account is {actual or 'unknown'}, expected {SENDER}.")


def authorize(client: dict) -> None:
    state = secrets.token_urlsafe(32)
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()

    class Callback(BaseHTTPRequestHandler):
        result: dict[str, str] = {}

        def do_GET(self) -> None:
            query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
            Callback.result = {key: values[0] for key, values in query.items() if values}
            ok = Callback.result.get("state") == state and "code" in Callback.result
            self.send_response(200 if ok else 400)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.end_headers()
            self.wfile.write(("Authorization received. You may close this tab." if ok else
                              "Authorization failed. Return to the terminal.").encode())

        def log_message(self, *_args: object) -> None:
            pass

    with HTTPServer(("127.0.0.1", 0), Callback) as server:
        server.timeout = 180
        redirect = f"http://127.0.0.1:{server.server_port}/"
        params = {
            "client_id": client["client_id"], "redirect_uri": redirect,
            "response_type": "code", "scope": SCOPE, "access_type": "offline",
            "prompt": "consent select_account", "login_hint": SENDER,
            "state": state, "code_challenge": challenge, "code_challenge_method": "S256",
        }
        url = "https://accounts.google.com/o/oauth2/v2/auth?" + urllib.parse.urlencode(params)
        print("Opening Google authorization in your browser. Choose " + SENDER)
        print("If the browser does not open, visit: " + url)
        webbrowser.open(url)
        server.handle_request()
    result = Callback.result
    if result.get("state") != state or "code" not in result:
        raise RuntimeError("OAuth authorization timed out, was rejected, or returned an invalid state.")
    response = request_json(TOKEN_URL, {
        "client_id": client["client_id"], "client_secret": client["client_secret"],
        "code": result["code"], "code_verifier": verifier,
        "grant_type": "authorization_code", "redirect_uri": redirect,
    })
    if not response.get("refresh_token"):
        raise RuntimeError("Google did not return a refresh token; authorization was not saved.")
    granted = set(response.get("scope", "").split())
    if "https://www.googleapis.com/auth/gmail.send" not in granted:
        raise RuntimeError("Gmail send permission was not granted. Re-authorize and select the email sending permission.")
    verify_sender(response["access_token"])
    save_token({"refresh_token": response["refresh_token"],
                "client_id": client["client_id"], "sender": SENDER})
    print(f"OAuth authorization saved for {SENDER}.")


def access_token(client: dict) -> str:
    if not TOKEN_FILE.is_file():
        raise RuntimeError("OAuth token missing. Run scripts/email_daily_news.py --authorize first.")
    saved = json.loads(TOKEN_FILE.read_text(encoding="utf-8"))
    if saved.get("client_id") != client["client_id"] or saved.get("sender") != SENDER:
        raise RuntimeError("OAuth token belongs to a different client or sender. Re-authorize.")
    response = request_json(TOKEN_URL, {
        "client_id": client["client_id"], "client_secret": client["client_secret"],
        "refresh_token": saved["refresh_token"], "grant_type": "refresh_token",
    })
    granted = set(response.get("scope", "").split())
    if granted and "https://www.googleapis.com/auth/gmail.send" not in granted:
        raise RuntimeError("Saved OAuth token lacks gmail.send. Run --authorize and select the sending permission.")
    token = response["access_token"]
    verify_sender(token)
    return token


def build_message(report: Path, recipient: str) -> EmailMessage:
    if not report.is_file():
        raise RuntimeError(f"Report does not exist: {report}")
    content = report.read_bytes()
    mime, _ = mimetypes.guess_type(report.name)
    main_type, sub_type = (mime or "text/markdown").split("/", 1)
    message = EmailMessage()
    message["From"] = SENDER
    message["To"] = recipient
    message["Subject"] = f"每日新闻与因子投资报告 | {report.stem[:10]}"
    message.set_content("今日新闻与因子投资报告见附件。")
    message.add_attachment(content, maintype=main_type, subtype=sub_type, filename=report.name)
    return message


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", nargs="?", type=Path)
    parser.add_argument("--to", default="q1093677764@gmail.com")
    parser.add_argument("--authorize", action="store_true", help="Open browser for one-time Google consent")
    parser.add_argument("--check", action="store_true", help="Refresh token and verify sender; do not send")
    args = parser.parse_args()
    client = read_client()
    if args.authorize:
        authorize(client)
        return 0
    token = access_token(client)
    if args.check:
        print(f"OAuth ready for {SENDER}; no email sent.")
        return 0
    if args.report is None:
        parser.error("report is required unless --authorize or --check is used")
    message = build_message(args.report, args.to)
    raw = base64.urlsafe_b64encode(message.as_bytes()).decode("ascii")
    payload = json.dumps({"raw": raw}).encode("utf-8")
    request = urllib.request.Request(
        "https://gmail.googleapis.com/gmail/v1/users/me/messages/send",
        data=payload,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            sent = json.load(response)
    except urllib.error.HTTPError as error:
        detail = error.read(1000).decode("utf-8", errors="replace")
        raise RuntimeError(f"Gmail send HTTP {error.code}: {detail}") from error
    print(f"Sent {args.report.name} to {args.to}; Gmail message ID: {sent['id']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
