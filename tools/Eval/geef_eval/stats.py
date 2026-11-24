"""Exact two-sided sign test."""
from __future__ import annotations

import math


def sign_test_p(wins: int, losses: int) -> float:
    """Two-sided exact binomial test with p = 0.5 on wins versus losses (ties excluded)."""
    if wins < 0 or losses < 0:
        raise ValueError("wins and losses must be non-negative")
    n = wins + losses
    if n == 0:
        return 1.0
    observed = math.comb(n, wins)
    tail = sum(math.comb(n, i) for i in range(n + 1) if math.comb(n, i) <= observed)
    return min(1.0, tail / 2 ** n)
