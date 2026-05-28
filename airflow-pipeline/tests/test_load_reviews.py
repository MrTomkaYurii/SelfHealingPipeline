"""
Unit tests for _load_reviews() and _load_from_file() — file loading logic.
Uses tmp_path to avoid touching real dataset files.
"""
import json
import pytest
from dags.business_pipeline_dag import _load_reviews
from dags.agentic_pipeline_dag import _load_from_file


def write_ndjson(path, records: list[dict]) -> None:
    with open(path, "w", encoding="utf-8") as f:
        for rec in records:
            f.write(json.dumps(rec) + "\n")


SAMPLE_REVIEWS = [
    {"review_id": f"r{i}", "business_id": "b1", "user_id": "u1",
     "stars": i % 5 + 1, "text": f"Review number {i}",
     "date": "2024-01-01", "useful": 0, "funny": 0, "cool": 0}
    for i in range(10)
]


# ── file not found ────────────────────────────────────────────────────────────

class TestLoadReviewsFileNotFound:
    def test_raises_file_not_found(self, tmp_path):
        with pytest.raises(FileNotFoundError):
            _load_reviews(str(tmp_path / "nonexistent.json"))


# ── basic loading ─────────────────────────────────────────────────────────────

class TestLoadReviewsBasic:
    def test_loads_all_records(self, tmp_path):
        f = tmp_path / "reviews.json"
        write_ndjson(f, SAMPLE_REVIEWS)
        result = _load_reviews(str(f))
        assert len(result) == 10

    def test_record_fields_mapped_correctly(self, tmp_path):
        f = tmp_path / "reviews.json"
        write_ndjson(f, [SAMPLE_REVIEWS[0]])
        result = _load_reviews(str(f))
        r = result[0]
        assert r["review_id"] == "r0"
        assert r["business_id"] == "b1"
        assert r["stars"] == 1
        assert r["text"] == "Review number 0"

    def test_empty_file_returns_empty_list(self, tmp_path):
        f = tmp_path / "empty.json"
        f.write_text("")
        result = _load_reviews(str(f))
        assert result == []

    def test_blank_lines_skipped(self, tmp_path):
        f = tmp_path / "reviews.json"
        content = json.dumps(SAMPLE_REVIEWS[0]) + "\n\n" + json.dumps(SAMPLE_REVIEWS[1]) + "\n"
        f.write_text(content)
        result = _load_reviews(str(f))
        assert len(result) == 2


# ── batch & offset ────────────────────────────────────────────────────────────

class TestLoadReviewsBatchOffset:
    def test_batch_size_limits_records(self, tmp_path):
        f = tmp_path / "reviews.json"
        write_ndjson(f, SAMPLE_REVIEWS)
        result = _load_reviews(str(f), batch_size=3)
        assert len(result) == 3

    def test_offset_skips_records(self, tmp_path):
        f = tmp_path / "reviews.json"
        write_ndjson(f, SAMPLE_REVIEWS)
        result = _load_reviews(str(f), batch_size=3, offset=5)
        assert len(result) == 3
        assert result[0]["review_id"] == "r5"

    def test_offset_beyond_file_returns_empty(self, tmp_path):
        f = tmp_path / "reviews.json"
        write_ndjson(f, SAMPLE_REVIEWS)
        result = _load_reviews(str(f), batch_size=5, offset=100)
        assert result == []

    def test_batch_size_zero_loads_all(self, tmp_path):
        f = tmp_path / "reviews.json"
        write_ndjson(f, SAMPLE_REVIEWS)
        result = _load_reviews(str(f), batch_size=0)
        assert len(result) == 10


# ── invalid JSON lines ────────────────────────────────────────────────────────

class TestLoadReviewsInvalidLines:
    def test_invalid_json_line_skipped(self, tmp_path):
        f = tmp_path / "reviews.json"
        content = json.dumps(SAMPLE_REVIEWS[0]) + "\nnot valid json\n" + json.dumps(SAMPLE_REVIEWS[1]) + "\n"
        f.write_text(content)
        result = _load_reviews(str(f))
        assert len(result) == 2

    def test_all_invalid_returns_empty(self, tmp_path):
        f = tmp_path / "reviews.json"
        f.write_text("bad\nbad\nbad\n")
        result = _load_reviews(str(f))
        assert result == []


# ── _load_from_file (agentic_pipeline_dag) ───────────────────────────────────

class TestLoadFromFile:
    def test_loads_with_params(self, tmp_path):
        f = tmp_path / "yelp.json"
        write_ndjson(f, SAMPLE_REVIEWS)
        params = {"input_file": str(f)}
        result = _load_from_file(params, batch_size=5, offset=0)
        assert len(result) == 5

    def test_raises_when_file_missing(self, tmp_path):
        params = {"input_file": str(tmp_path / "missing.json")}
        with pytest.raises(FileNotFoundError):
            _load_from_file(params, batch_size=10, offset=0)
