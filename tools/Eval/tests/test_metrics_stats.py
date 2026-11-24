import pytest

from geef_eval.metrics import complies, count_words, find_placeholders, length_rule_for, length_rule_from_text, measure
from geef_eval.stats import sign_test_p
from geef_eval.tasks import LengthRule, Task


def test_word_count_ignores_pure_punctuation():
    assert count_words("Hallo – Welt , vier Wörter.") == 4


def test_placeholders_in_square_brackets():
    assert find_placeholders("Am [Datum] in [Ort der Feier] um 15 Uhr.") == ["[Datum]", "[Ort der Feier]"]


@pytest.mark.parametrize("text, expected", [
    ("höchstens 80 Wörter", LengthRule(None, 80)),
    ("maximal 1.200 Wörtern bitte", LengthRule(None, 1200)),
    ("zwischen 200 und 300 Wörtern", LengthRule(200, 300)),
    ("200–300 words", LengthRule(200, 300)),
    ("mindestens 500 Wörter", LengthRule(500, None)),
    ("at most 150 words", LengthRule(None, 150)),
    ("ca. 100 Wörter", LengthRule(85, 115)),
    ("Schreib einen Text über Bäume.", None),
])
def test_length_rule_from_text(text, expected):
    assert length_rule_from_text(text) == expected


def test_explicit_task_rule_wins_over_brief_text():
    task = Task(id="t", brief="höchstens 80 Wörter", length=LengthRule(None, 50))
    assert length_rule_for(task) == LengthRule(None, 50)


def test_original_brief_is_used_when_short_brief_has_no_rule():
    task = Task(id="t", brief="Schreib was.", original_brief="Höchstens 120 Wörter.")
    assert length_rule_for(task) == LengthRule(None, 120)


def test_complies_and_measure():
    assert complies(10, None) is None
    assert complies(10, LengthRule(None, 9)) is False
    assert complies(10, LengthRule(10, 10)) is True

    metrics = measure("eins zwei [drei]", Task(id="t", brief="höchstens 2 Wörter"))
    assert (metrics.words, metrics.placeholders, metrics.length_ok) == (3, 1, False)


@pytest.mark.parametrize("wins, losses, expected", [
    (0, 0, 1.0),
    (5, 0, 0.0625),
    (0, 5, 0.0625),
    (6, 1, 0.125),
    (3, 3, 1.0),
])
def test_sign_test(wins, losses, expected):
    assert sign_test_p(wins, losses) == pytest.approx(expected)


def test_sign_test_rejects_negative_counts():
    with pytest.raises(ValueError):
        sign_test_p(-1, 2)
