"""
Unit tests for _heal_review() — the self-healing core logic.
Covers all error types: missing_text, empty_text, wrong_type,
special_characters_only, too_long, and the happy path.
"""
import pytest
from dags.agentic_pipeline_dag import _heal_review, Config


# ── helpers ───────────────────────────────────────────────────────────────────

def make_review(**kwargs) -> dict:
    base = {
        "review_id": "r1",
        "business_id": "b1",
        "user_id": "u1",
        "stars": 4,
        "text": "Great place!",
        "date": "2024-01-01",
        "useful": 0,
        "funny": 0,
        "cool": 0,
    }
    base.update(kwargs)
    return base


# ── happy path ────────────────────────────────────────────────────────────────

class TestHealReviewHappyPath:
    def test_normal_text_not_healed(self):
        result = _heal_review(make_review(text="Great food and service!"))
        assert result["was_healed"] is False
        assert result["healed_text"] == "Great food and service!"
        assert result["error_type"] is None
        assert result["action_taken"] == "none"

    def test_strips_leading_trailing_whitespace(self):
        result = _heal_review(make_review(text="  Nice place  "))
        assert result["was_healed"] is False
        assert result["healed_text"] == "Nice place"

    def test_preserves_metadata(self):
        review = make_review(stars=5, useful=3, funny=1, cool=2)
        result = _heal_review(review)
        assert result["metadata"]["useful"] == 3
        assert result["metadata"]["funny"] == 1
        assert result["metadata"]["cool"] == 2
        assert result["stars"] == 5


# ── missing text ──────────────────────────────────────────────────────────────

class TestHealReviewMissingText:
    def test_none_text(self):
        result = _heal_review(make_review(text=None))
        assert result["was_healed"] is True
        assert result["error_type"] == "missing_text"
        assert result["action_taken"] == "filled_with_placeholder"
        assert result["healed_text"] == "No review text provided."

    def test_missing_key_defaults_to_empty(self):
        review = make_review()
        del review["text"]
        result = _heal_review(review)
        # text defaults to '' (not None), so it's empty_text
        assert result["was_healed"] is True
        assert result["error_type"] in ("missing_text", "empty_text")


# ── empty text ────────────────────────────────────────────────────────────────

class TestHealReviewEmptyText:
    def test_empty_string(self):
        result = _heal_review(make_review(text=""))
        assert result["was_healed"] is True
        assert result["error_type"] == "empty_text"
        assert result["healed_text"] == "No review text provided."

    def test_whitespace_only(self):
        result = _heal_review(make_review(text="   "))
        assert result["was_healed"] is True
        assert result["error_type"] == "empty_text"

    def test_tabs_and_newlines(self):
        result = _heal_review(make_review(text="\t\n  \t"))
        assert result["was_healed"] is True
        assert result["error_type"] == "empty_text"


# ── wrong type ────────────────────────────────────────────────────────────────

class TestHealReviewWrongType:
    def test_integer_text(self):
        result = _heal_review(make_review(text=42))
        assert result["was_healed"] is True
        assert result["error_type"] == "wrong_type"
        assert result["action_taken"] == "type_conversion"
        assert result["healed_text"] == "42"

    def test_float_text(self):
        result = _heal_review(make_review(text=3.14))
        assert result["was_healed"] is True
        assert result["error_type"] == "wrong_type"
        assert "3.14" in result["healed_text"]

    def test_bool_text(self):
        result = _heal_review(make_review(text=True))
        assert result["was_healed"] is True
        assert result["error_type"] == "wrong_type"

    def test_list_text(self):
        result = _heal_review(make_review(text=["some", "list"]))
        assert result["was_healed"] is True
        assert result["error_type"] == "wrong_type"


# ── special characters only ───────────────────────────────────────────────────

class TestHealReviewSpecialChars:
    def test_only_punctuation(self):
        result = _heal_review(make_review(text="!!!???..."))
        assert result["was_healed"] is True
        assert result["error_type"] == "special_characters_only"
        assert result["healed_text"] == "[Non-text content]"
        assert result["action_taken"] == "replaced_special_characters"

    def test_only_emoji(self):
        result = _heal_review(make_review(text="😀🎉🔥"))
        assert result["was_healed"] is True
        assert result["error_type"] == "special_characters_only"

    def test_mixed_has_alphanumeric_not_healed(self):
        result = _heal_review(make_review(text="!!!A!!!"))
        assert result["was_healed"] is False


# ── too long ──────────────────────────────────────────────────────────────────

class TestHealReviewTooLong:
    def test_text_over_max_length(self):
        long_text = "A" * (Config.MAX_TEXT_LENGTH + 10)
        result = _heal_review(make_review(text=long_text))
        assert result["was_healed"] is True
        assert result["error_type"] == "too_long"
        assert result["action_taken"] == "truncated_text"
        assert len(result["healed_text"]) == Config.MAX_TEXT_LENGTH
        assert result["healed_text"].endswith("...")

    def test_text_exactly_at_max_length(self):
        exact_text = "B" * Config.MAX_TEXT_LENGTH
        result = _heal_review(make_review(text=exact_text))
        assert result["was_healed"] is False

    def test_text_one_over_max_length(self):
        over_text = "C" * (Config.MAX_TEXT_LENGTH + 1)
        result = _heal_review(make_review(text=over_text))
        assert result["was_healed"] is True
        assert result["error_type"] == "too_long"


# ── original_text preservation ────────────────────────────────────────────────

class TestOriginalTextPreservation:
    def test_original_text_saved_on_heal(self):
        original = "   "
        result = _heal_review(make_review(text=original))
        assert result["original_text"] == original

    def test_original_text_saved_for_none(self):
        result = _heal_review(make_review(text=None))
        assert result["original_text"] is None

    def test_original_text_saved_for_int(self):
        result = _heal_review(make_review(text=99))
        assert result["original_text"] == 99
