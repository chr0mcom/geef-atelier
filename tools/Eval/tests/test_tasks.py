import json

import pytest

from geef_eval.tasks import TaskError, load_tasks


def test_examples_load_as_three_synthetic_tasks(examples_dir):
    tasks = load_tasks(examples_dir)

    assert [t.id for t in tasks] == ["01-sommerfest-einladung", "02-produkttext-thermoskanne", "03-absage-bewerbung"]
    assert all(t.original_brief and t.reference_text for t in tasks)
    assert tasks[0].attachments[0].name == "eckdaten.md"
    assert tasks[0].attachments[0].content_type == "text/markdown"
    assert tasks[2].length.max_words == 100


def test_markdown_task_with_attachment_section(tmp_path):
    (tmp_path / "aufgabe-a.md").write_text(
        "# Kurzauftrag\n\nSchreib etwas.\n\n# Anhang: notiz.txt\n\nInhalt der Notiz\n\n# Original-Briefing\n\nAusführlich.\n",
        encoding="utf-8")

    task = load_tasks(tmp_path)[0]

    assert task.id == "aufgabe-a"
    assert task.brief == "Schreib etwas."
    assert task.attachments[0].text == "Inhalt der Notiz"
    assert task.attachments[0].content_type == "text/plain"
    assert task.judge_brief == "Ausführlich."


def test_task_without_original_brief_is_judged_against_the_short_brief(tmp_path):
    (tmp_path / "x.json").write_text(json.dumps({"kurzauftrag": "Nur kurz."}), encoding="utf-8")

    task = load_tasks(tmp_path)[0]

    assert task.id == "x"
    assert task.judge_brief == "Nur kurz."


def test_pdf_attachment_by_path_is_read_as_bytes(tmp_path):
    (tmp_path / "dateien").mkdir()
    (tmp_path / "dateien" / "a.pdf").write_bytes(b"%PDF-1.4 fake")
    (tmp_path / "t.json").write_text(json.dumps({
        "kurzauftrag": "Fasse zusammen.",
        "anhaenge": [{"name": "a.pdf", "pfad": "dateien/a.pdf"}],
    }), encoding="utf-8")

    attachment = load_tasks(tmp_path)[0].attachments[0]

    assert attachment.content_type == "application/pdf"
    assert attachment.data == b"%PDF-1.4 fake"
    assert attachment.text is None


@pytest.mark.parametrize("payload, message", [
    ({"id": "a"}, "kurzauftrag"),
    ({"kurzauftrag": "x", "id": "../böse"}, "ungültige id"),
    ({"kurzauftrag": "x", "anhaenge": [{"name": "a.docx", "text": "y"}]}, "nur .md"),
    ({"kurzauftrag": "x", "anhaenge": [{"name": "a.md"}]}, "'text' oder 'pfad'"),
    ({"kurzauftrag": "x", "laenge": {"max": -3}}, "positive"),
])
def test_malformed_tasks_are_rejected(tmp_path, payload, message):
    (tmp_path / "t.json").write_text(json.dumps(payload), encoding="utf-8")

    with pytest.raises(TaskError, match=message):
        load_tasks(tmp_path)


def test_duplicate_ids_are_rejected(tmp_path):
    (tmp_path / "a.json").write_text(json.dumps({"id": "same", "kurzauftrag": "x"}), encoding="utf-8")
    (tmp_path / "b.json").write_text(json.dumps({"id": "same", "kurzauftrag": "y"}), encoding="utf-8")

    with pytest.raises(TaskError, match="Doppelte"):
        load_tasks(tmp_path)


def test_readme_and_hidden_files_are_ignored(tmp_path):
    (tmp_path / "README.md").write_text("# Hinweis\n", encoding="utf-8")
    (tmp_path / ".x.json").write_text("{}", encoding="utf-8")
    (tmp_path / "ok.json").write_text(json.dumps({"kurzauftrag": "x"}), encoding="utf-8")

    assert [t.id for t in load_tasks(tmp_path)] == ["ok"]
