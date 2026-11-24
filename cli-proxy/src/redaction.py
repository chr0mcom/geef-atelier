"""Removes credential-like fragments from texts before they leave the proxy.

A port of Geef.Atelier's `SecretRedactionExtensions` (Core/Domain/Warnings), so error texts that
travel from the gateway into the Atelier, into logs or into `/health` follow the same rules. It is
a deny list and can only catch common forms. Two levels:

* `redact_known_secrets`: unambiguous key forms only -- known key prefixes (`sk-`, `tvly-`,
  `AIza`, `gsk_`, ...), passwords in URLs, bearer/basic tokens, `Authorization:` headers,
  `name=value` / `name: value` pairs whose name ends in a secret-sounding word (`key`, `token`,
  `secret`, `password`, ...) and `API key provided: value`. Apart from URL query parameters
  (`?key=value`) a value is only removed when it looks like a secret: at least 12 characters
  without whitespace with a letter and a digit (or base64 padding).
* `redact_secrets`: additionally every long opaque token (32+ characters of letters, digits,
  `_` and `-` with a letter and a digit, or base64 with mixed case and digits); UUIDs are kept,
  because session ids are what an operator needs to find a run.

Every pattern is a single character class or a fixed word sequence, so the run time stays linear.
"""
from __future__ import annotations

import re
import uuid

PLACEHOLDER = "[redacted]"

_MIN_SECRET_LENGTH = 12

_SECRET_NAMES = frozenset({
    "key", "apikey", "token", "secret", "password", "passwd", "pwd", "auth", "authorization",
    "credential", "credentials",
})

_KNOWN_KEY_FORMAT = re.compile(
    r"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{10,}|\btvly-[A-Za-z0-9_\-]{10,}|\bAIza[0-9A-Za-z_\-]{20,}"
    r"|\bgsk_[A-Za-z0-9]{20,}|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}"
    r"|\bxox[abprs]-[A-Za-z0-9\-]{10,}|\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}"
)
_URL_PASSWORD = re.compile(r"(?P<lead>://[^\s/:@]{1,256}:)[^\s/@]{1,256}(?=@)")
_AUTHORIZATION_HEADER = re.compile(
    r"(?P<name>(?<![A-Za-z0-9_\-])(?:proxy-)?authorization[\"']?(?>[ \t]*)[:=](?>[ \t]*))"
    r"(?P<value>[^\s\"'][^\r\n\"']*)",
    re.IGNORECASE,
)
_BEARER_TOKEN = re.compile(
    r"(?P<scheme>\b(?:Bearer|Basic|Token))(?>[ \t]+)(?P<value>(?>[A-Za-z0-9._~+/=\-]+))",
    re.IGNORECASE,
)
_KEY_PROVIDED = re.compile(
    r"(?P<lead>\bkey(?>[ \t]+)provided(?>[ \t]*):?(?>[ \t]*))(?P<value>(?>[^\s\"',;]+))",
    re.IGNORECASE,
)
_NAMED_SECRET = re.compile(
    r"(?P<name>(?<![A-Za-z0-9_\-.])(?P<id>(?>[A-Za-z][A-Za-z0-9_\-.]*))[\"']?)"
    r"(?P<sep>(?>[ \t]*)[:=](?>[ \t]*)[\"']?)"
    r"(?P<value>(?>[^\s\"',;&)=:]+)(?>={1,2}(?![^\s\"',;&)]))?)"
)
_BASE64_TOKEN = re.compile(r"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{32,}={0,2}")
_OPAQUE_TOKEN = re.compile(r"[A-Za-z0-9_\-]{32,}")


def redact_secrets(text: str | None) -> str:
    """`text` with every credential-like fragment replaced, including long opaque tokens.

    For exception messages, CLI error output and log texts."""
    result = redact_known_secrets(text)
    if not result:
        return result
    result = _BASE64_TOKEN.sub(
        lambda m: m.group(0) if _is_uuid(m.group(0)) or not _has_mixed_case_and_digit(m.group(0))
        else PLACEHOLDER, result)
    return _OPAQUE_TOKEN.sub(
        lambda m: m.group(0) if _is_uuid(m.group(0)) or not _has_letter_and_digit(m.group(0))
        else PLACEHOLDER, result)


def redact_known_secrets(text: str | None) -> str:
    """`text` with unambiguous key forms replaced; long words without such a form stay."""
    if not text:
        return ""
    result = _KNOWN_KEY_FORMAT.sub(PLACEHOLDER, text)
    result = _URL_PASSWORD.sub(lambda m: m.group("lead") + PLACEHOLDER, result)
    result = _AUTHORIZATION_HEADER.sub(
        lambda m: m.group("name") + PLACEHOLDER
        if any(_looks_like_secret(part) for part in m.group("value").split()) else m.group(0),
        result)
    result = _BEARER_TOKEN.sub(
        lambda m: f"{m.group('scheme')} {PLACEHOLDER}"
        if _looks_like_secret(m.group("value")) else m.group(0),
        result)
    result = _KEY_PROVIDED.sub(
        lambda m: m.group("lead") + PLACEHOLDER
        if _looks_like_secret(m.group("value")) else m.group(0),
        result)
    return _redact_named_secrets(result)


def _redact_named_secrets(text: str) -> str:
    pieces: list[str] = []
    copied = 0
    match = _NAMED_SECRET.search(text)
    while match:
        start, end = match.span("value")
        if _is_secret_name(match.group("id")) and _is_secret_value(
                match.group("value"), _is_query_parameter(text, match)):
            pieces.append(text[copied:start])
            pieces.append(PLACEHOLDER)
            copied = end
            match = _NAMED_SECRET.search(text, end)
        else:
            match = _NAMED_SECRET.search(text, start)
    if not pieces:
        return text
    pieces.append(text[copied:])
    return "".join(pieces)


def _is_uuid(value: str) -> bool:
    try:
        uuid.UUID(value)
    except ValueError:
        return False
    return True


def _looks_like_secret(value: str) -> bool:
    return (
        len(value) >= _MIN_SECRET_LENGTH
        and any(c.isalpha() for c in value)
        and (any(c.isdigit() for c in value) or value.endswith("="))
    )


def _is_secret_value(value: str, is_query_parameter: bool) -> bool:
    return len(value) >= 4 if is_query_parameter else _looks_like_secret(value)


def _is_query_parameter(text: str, match: re.Match[str]) -> bool:
    start = match.start()
    return start > 0 and text[start - 1] in "?&" and match.group("sep") == "="


def _is_secret_name(identifier: str) -> bool:
    cut = max(identifier.rfind("_"), identifier.rfind("-"), identifier.rfind("."))
    segment = identifier[cut + 1:]
    for i in range(len(segment) - 1, 0, -1):
        if segment[i].isupper() and segment[i - 1].islower():
            segment = segment[i:]
            break
    return segment.lower() in _SECRET_NAMES


def _has_letter_and_digit(value: str) -> bool:
    return any(c.isalpha() for c in value) and any(c.isdigit() for c in value)


def _has_mixed_case_and_digit(value: str) -> bool:
    return (any(c.isupper() for c in value) and any(c.islower() for c in value)
            and any(c.isdigit() for c in value))
