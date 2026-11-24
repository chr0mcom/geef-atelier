"""Secret redaction (port of the Atelier's SecretRedactionExtensions)."""
from __future__ import annotations

import os
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from redaction import PLACEHOLDER, redact_known_secrets, redact_secrets  # noqa: E402

# Synthetic values only; none of them is a real credential.
_FAKE = {
    "sk": "sk-ant-api03-AbCdEfGhIjKlMnOp1234",
    "tvly": "tvly-dev-AbCdEf1234567890",
    "aiza": "AIzaSyA1234567890abcdefghijKLMNOP",
    "gsk": "gsk_AbCdEf1234567890GhIjKl",
}


class TestKnownKeyPrefixes:
    @pytest.mark.parametrize("name", sorted(_FAKE))
    def test_prefixed_keys_are_removed(self, name: str) -> None:
        text = f"upstream said: invalid key {_FAKE[name]} (check settings)"
        out = redact_known_secrets(text)
        assert _FAKE[name] not in out
        assert PLACEHOLDER in out
        assert out.startswith("upstream said: invalid key ")
        assert out.endswith(" (check settings)")

    def test_short_prefix_lookalikes_stay(self) -> None:
        text = "risk-assessment and task-list and sk-short"
        assert redact_known_secrets(text) == text


class TestBearerAndHeaders:
    def test_bearer_token_is_removed(self) -> None:
        out = redact_known_secrets("request failed: Bearer abc123DEF456ghi789")
        assert out == f"request failed: Bearer {PLACEHOLDER}"

    def test_ordinary_word_after_basic_stays(self) -> None:
        text = "Basic research shows nothing"
        assert redact_known_secrets(text) == text

    def test_authorization_header_is_removed(self) -> None:
        out = redact_known_secrets("Authorization: Token 9f8e7d6c5b4a3210zyx")
        assert "9f8e7d6c5b4a3210zyx" not in out
        assert out.startswith("Authorization: ")

    def test_api_key_provided(self) -> None:
        out = redact_known_secrets("Incorrect API key provided: abcd1234efgh5678")
        assert "abcd1234efgh5678" not in out


class TestNamedSecrets:
    def test_query_parameter_key_is_removed_even_when_short(self) -> None:
        out = redact_known_secrets("GET https://example.test/v1/search?q=x&key=abcd9")
        assert "abcd9" not in out
        assert "?q=x&key=" in out

    def test_secret_looking_value_after_key_name(self) -> None:
        for text in ("api_key=Zx81kLm02Qw93Er4", "OPENAI_API_KEY: Zx81kLm02Qw93Er4",
                     '"apiKey": "Zx81kLm02Qw93Er4"', "x-api-key=Zx81kLm02Qw93Er4",
                     "password=hunter2hunter2X"):
            out = redact_known_secrets(text)
            assert "Zx81kLm02Qw93Er4" not in out and "hunter2hunter2X" not in out, text

    def test_ordinary_value_after_key_name_stays(self) -> None:
        text = "Key: Strategic goals, token: none"
        assert redact_known_secrets(text) == text

    def test_url_password(self) -> None:
        out = redact_known_secrets("postgres://user:pa55word@db:5432/x")
        assert "pa55word" not in out
        assert "postgres://user:" in out and "@db:5432/x" in out


class TestOpaqueTokens:
    def test_long_opaque_token_is_removed(self) -> None:
        token = "a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6q7"
        assert token not in redact_secrets(f"value {token} end")

    def test_uuid_session_id_stays(self) -> None:
        text = 'session_id "22947b5d-589f-4ee6-abba-553cf16e1422"'
        assert redact_secrets(text) == text

    def test_long_word_without_digits_stays(self) -> None:
        text = "Supercalifragilisticexpialidociousness"
        assert redact_secrets(text) == text

    def test_known_level_keeps_opaque_tokens(self) -> None:
        token = "a1B2c3D4e5F6g7H8i9J0k1L2m3N4o5P6q7"
        assert token in redact_known_secrets(f"value {token} end")


class TestEdges:
    @pytest.mark.parametrize("value", [None, ""])
    def test_empty_input(self, value) -> None:
        assert redact_secrets(value) == ""
        assert redact_known_secrets(value) == ""

    def test_plain_text_is_untouched(self) -> None:
        text = "You've hit your session limit · resets 9pm (UTC). Prüfen Sie Größe und Maß."
        assert redact_secrets(text) == text

    def test_runs_in_linear_time_on_pathological_input(self) -> None:
        import time
        text = "key=" + "a" * 200_000 + " Bearer " + "=" * 100_000
        start = time.monotonic()
        redact_secrets(text)
        assert time.monotonic() - start < 5
