#!/usr/bin/env python3
"""Translate changelog messages while preserving their YAML structure."""

from __future__ import annotations

import json
import re
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Callable, Iterable
from typing import Any


GOOGLE_TRANSLATE_URL = "https://translate.googleapis.com/translate_a/single"
CYRILLIC_RE = re.compile(r"[А-Яа-яЁё]")
BATCH_SEPARATOR = "\n\ue000\n"
MAX_BATCH_CHARACTERS = 4_000


class TranslationError(RuntimeError):
    """Raised when changelog messages cannot be translated safely."""


def needs_translation(message: str) -> bool:
    """Return whether a non-empty changelog message still needs Russian text."""
    return bool(message.strip()) and CYRILLIC_RE.search(message) is None


def _batches(messages: Iterable[str]) -> Iterable[list[str]]:
    batch: list[str] = []
    batch_length = 0

    for message in messages:
        added_length = len(message) + (len(BATCH_SEPARATOR) if batch else 0)
        if batch and batch_length + added_length > MAX_BATCH_CHARACTERS:
            yield batch
            batch = []
            batch_length = 0

        batch.append(message)
        batch_length += len(message) + (len(BATCH_SEPARATOR) if len(batch) > 1 else 0)

    if batch:
        yield batch


def _request_translation(text: str, timeout: float) -> str:
    query = urllib.parse.urlencode(
        {
            "client": "gtx",
            "sl": "auto",
            "tl": "ru",
            "dt": "t",
            "q": text,
        }
    )
    request = urllib.request.Request(
        f"{GOOGLE_TRANSLATE_URL}?{query}",
        headers={"User-Agent": "CMU-changelog-translator/1.0"},
    )

    with urllib.request.urlopen(request, timeout=timeout) as response:
        payload = json.load(response)

    try:
        return "".join(part[0] for part in payload[0] if part and part[0])
    except (IndexError, TypeError) as exc:
        raise TranslationError("Unexpected response from Google Translate") from exc


def translate_messages(
    messages: list[str], *, retries: int = 3, timeout: float = 20
) -> list[str]:
    """Translate messages in batches, retrying transient service failures."""
    translated: list[str] = []

    for batch in _batches(messages):
        joined = BATCH_SEPARATOR.join(batch)
        last_error: Exception | None = None

        for attempt in range(retries):
            try:
                result = _request_translation(joined, timeout)
                parts = result.split(BATCH_SEPARATOR)
                if len(parts) != len(batch):
                    raise TranslationError(
                        "Google Translate did not preserve the changelog batch separator"
                    )
                translated.extend(part.strip() for part in parts)
                break
            except (
                OSError,
                urllib.error.URLError,
                json.JSONDecodeError,
                TranslationError,
            ) as exc:
                last_error = exc
                if attempt + 1 < retries:
                    time.sleep(2**attempt)
        else:
            raise TranslationError(
                f"Unable to translate a batch after {retries} attempts: {last_error}"
            ) from last_error

    return translated


def translate_changelog(
    data: dict[str, Any],
    translator: Callable[[list[str]], list[str]] = translate_messages,
) -> int:
    """Translate every untranslated message in a parsed changelog in place."""
    pending: list[str] = []

    for entry in data.get("Entries", []):
        for change in entry.get("changes", []):
            message = change.get("message")
            if isinstance(message, str) and needs_translation(message):
                pending.append(message)

    unique_messages = list(dict.fromkeys(pending))
    if not unique_messages:
        return 0

    translated = translator(unique_messages)
    if len(translated) != len(unique_messages):
        raise TranslationError(
            f"Translator returned {len(translated)} messages for {len(unique_messages)} inputs"
        )

    replacements = dict(zip(unique_messages, translated, strict=True))
    if any(not value.strip() for value in replacements.values()):
        raise TranslationError("Translator returned an empty changelog message")

    for entry in data.get("Entries", []):
        for change in entry.get("changes", []):
            message = change.get("message")
            if message in replacements:
                change["message"] = replacements[message]

    return len(pending)
