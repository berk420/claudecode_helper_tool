#!/usr/bin/env python3
"""Daily AI news: uses Claude web_search, sends summary in Turkish to Telegram."""
import json
import os
import urllib.error
import urllib.request
from datetime import datetime, timezone

import anthropic


def today_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%d")


def turkish_date(iso: str) -> str:
    months = [
        "", "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
    ]
    y, m, d = iso.split("-")
    return f"{int(d)} {months[int(m)]} {y}"


def fetch_news() -> str:
    client = anthropic.Anthropic(api_key=os.environ["ANTHROPIC_API_KEY"])
    today = today_iso()
    tr_date = turkish_date(today)

    prompt = f"""\
Today is {today}. Search the web and find:
1. The 3 most important artificial intelligence news stories published in the last 24 hours.
2. The most important news about Anthropic, Claude Code, or Claude API from the last 24 hours.

Write a 2-3 sentence summary in Turkish for each story.

Output ONLY the final Telegram message in MarkdownV2 format — no extra commentary before or after.
Use this exact structure (replace the placeholders):

*🤖 Günün AI Haberleri — {tr_date}*

*1\\. BAŞLIK*
Özet cümlesi\\.
[Kaynak](https://ornek-url.com)

*2\\. BAŞLIK*
Özet cümlesi\\.
[Kaynak](https://ornek-url.com)

*3\\. BAŞLIK*
Özet cümlesi\\.
[Kaynak](https://ornek-url.com)

*4\\. Claude / Anthropic — BAŞLIK*
Özet cümlesi\\.
[Kaynak](https://ornek-url.com)

MarkdownV2 escaping rules you MUST follow inside plain text (NOT inside URLs or link labels):
- Characters that must be escaped with \\: . ! ( ) - = + # | {{ }} > ~
- Characters that must NOT be escaped: * _ [ ] `
- Never place a trailing space before a newline.
- Dates and numbers with dots (e.g. "27.05.2026") must escape each dot: "27\\.05\\.2026"
"""

    messages = [{"role": "user", "content": prompt}]
    tools = [{"type": "web_search_20250305", "name": "web_search", "max_uses": 12}]

    # Tool-use loop: Claude may call web_search multiple times before final answer
    for _ in range(20):
        response = client.messages.create(
            model="claude-sonnet-4-6",
            max_tokens=4096,
            tools=tools,
            messages=messages,
        )

        if response.stop_reason == "end_turn":
            for block in response.content:
                if hasattr(block, "text") and block.text.strip():
                    return block.text.strip()
            raise RuntimeError("stop_reason=end_turn but no text block found")

        if response.stop_reason == "tool_use":
            # Append assistant turn, then supply tool results
            messages.append({"role": "assistant", "content": response.content})
            tool_results = []
            for block in response.content:
                if block.type == "tool_use":
                    # For built-in server-side tools the result arrives inside the block
                    content = getattr(block, "output", None) or getattr(block, "content", "")
                    tool_results.append({
                        "type": "tool_result",
                        "tool_use_id": block.id,
                        "content": content if isinstance(content, str) else json.dumps(content),
                    })
            messages.append({"role": "user", "content": tool_results})
            continue

        break  # unexpected stop_reason

    raise RuntimeError(f"Loop ended without a final answer. Last stop_reason={response.stop_reason}")


def send_telegram(text: str) -> None:
    token = os.environ["TELEGRAM_BOT_TOKEN"]
    chat_id = os.environ["TELEGRAM_CHAT_ID"]

    def post(payload: dict) -> dict:
        raw = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            f"https://api.telegram.org/bot{token}/sendMessage",
            data=raw,
            headers={"Content-Type": "application/json; charset=utf-8"},
        )
        try:
            with urllib.request.urlopen(req) as resp:
                return json.loads(resp.read().decode())
        except urllib.error.HTTPError as exc:
            body = exc.read().decode()
            raise RuntimeError(f"HTTP {exc.code}: {body}") from exc

    # Split into <=4096-char chunks at paragraph boundaries
    chunks: list[str] = []
    current = ""
    for para in text.split("\n\n"):
        candidate = (current + "\n\n" + para).lstrip("\n")
        if len(candidate) <= 4096:
            current = candidate
        else:
            if current:
                chunks.append(current)
            current = para
    if current:
        chunks.append(current)

    for i, chunk in enumerate(chunks, 1):
        try:
            result = post({
                "chat_id": chat_id,
                "text": chunk,
                "parse_mode": "MarkdownV2",
                "disable_web_page_preview": True,
            })
            mid = result["result"]["message_id"]
            print(f"✅ Parça {i}/{len(chunks)} gönderildi — message_id={mid}")
        except RuntimeError as exc:
            print(f"⚠️  MarkdownV2 hatası ({exc}). Düz metin olarak tekrar deneniyor…")
            result = post({
                "chat_id": chat_id,
                "text": chunk,
                "disable_web_page_preview": True,
            })
            mid = result["result"]["message_id"]
            print(f"✅ Düz metin olarak gönderildi — message_id={mid}")


if __name__ == "__main__":
    print("📰 Haberler aranıyor…")
    msg = fetch_news()
    print(f"\n📝 Oluşturulan mesaj ({len(msg)} karakter):\n{'─'*60}")
    print(msg)
    print("─" * 60)
    send_telegram(msg)
    print("🎉 Tamamlandı.")
