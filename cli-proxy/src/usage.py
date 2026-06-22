"""Shared token-usage carrier for CLI adapters.

The CLIs report real token counts (claude via --output-format json, codex via the
exec --json event stream). This dataclass carries those counts from the adapters up
to the OpenAI response builders so the proxy can emit a faithful `usage` block
instead of zeros.
"""
from __future__ import annotations

from dataclasses import dataclass


@dataclass
class UsageParts:
    """Provider-agnostic token usage, mapped to OpenAI accounting.

    input_tokens     — total prompt tokens (including cached), == OpenAI prompt_tokens
    output_tokens    — total completion tokens (including reasoning), == OpenAI completion_tokens
    cached_tokens    — subset of input_tokens served from cache (prompt_tokens_details.cached_tokens)
    reasoning_tokens — subset of output_tokens spent on reasoning (completion_tokens_details.reasoning_tokens)
    cost_usd         — provider-reported cost when available (claude total_cost_usd); not part of OpenAI schema
    """

    input_tokens: int = 0
    output_tokens: int = 0
    cached_tokens: int = 0
    reasoning_tokens: int = 0
    cost_usd: float | None = None

    @property
    def total_tokens(self) -> int:
        return self.input_tokens + self.output_tokens
