"""
Unit tests for _parse_ollama_response() — Ollama JSON response parser.
Covers valid JSON, markdown code blocks, invalid JSON fallbacks,
confidence clamping, and unknown sentiments.
"""
import pytest
from dags.agentic_pipeline_dag import _parse_ollama_response


class TestParseOllamaResponseValidJson:
    def test_positive_sentiment(self):
        result = _parse_ollama_response('{"sentiment": "POSITIVE", "confidence": 0.95}')
        assert result["label"] == "POSITIVE"
        assert result["score"] == 0.95

    def test_negative_sentiment(self):
        result = _parse_ollama_response('{"sentiment": "NEGATIVE", "confidence": 0.80}')
        assert result["label"] == "NEGATIVE"
        assert result["score"] == 0.80

    def test_neutral_sentiment(self):
        result = _parse_ollama_response('{"sentiment": "NEUTRAL", "confidence": 0.60}')
        assert result["label"] == "NEUTRAL"
        assert result["score"] == 0.60

    def test_lowercase_sentiment_uppercased(self):
        result = _parse_ollama_response('{"sentiment": "positive", "confidence": 0.9}')
        assert result["label"] == "POSITIVE"

    def test_mixed_case_sentiment(self):
        result = _parse_ollama_response('{"sentiment": "Negative", "confidence": 0.7}')
        assert result["label"] == "NEGATIVE"


class TestParseOllamaResponseConfidenceClamping:
    def test_confidence_above_one_clamped(self):
        result = _parse_ollama_response('{"sentiment": "POSITIVE", "confidence": 1.5}')
        assert result["score"] == 1.0

    def test_confidence_below_zero_clamped(self):
        result = _parse_ollama_response('{"sentiment": "NEGATIVE", "confidence": -0.3}')
        assert result["score"] == 0.0

    def test_confidence_exactly_zero(self):
        result = _parse_ollama_response('{"sentiment": "NEUTRAL", "confidence": 0.0}')
        assert result["score"] == 0.0

    def test_confidence_exactly_one(self):
        result = _parse_ollama_response('{"sentiment": "POSITIVE", "confidence": 1.0}')
        assert result["score"] == 1.0


class TestParseOllamaResponseMarkdownBlock:
    def test_json_in_markdown_block(self):
        text = '```json\n{"sentiment": "POSITIVE", "confidence": 0.9}\n```'
        result = _parse_ollama_response(text)
        assert result["label"] == "POSITIVE"
        assert result["score"] == 0.9

    def test_json_in_plain_code_block(self):
        text = '```\n{"sentiment": "NEGATIVE", "confidence": 0.85}\n```'
        result = _parse_ollama_response(text)
        assert result["label"] == "NEGATIVE"


class TestParseOllamaResponseUnknownSentiment:
    def test_unknown_sentiment_defaults_to_neutral(self):
        result = _parse_ollama_response('{"sentiment": "MIXED", "confidence": 0.5}')
        assert result["label"] == "NEUTRAL"

    def test_empty_sentiment_defaults_to_neutral(self):
        result = _parse_ollama_response('{"sentiment": "", "confidence": 0.5}')
        assert result["label"] == "NEUTRAL"

    def test_missing_sentiment_key_defaults_to_neutral(self):
        result = _parse_ollama_response('{"confidence": 0.5}')
        assert result["label"] == "NEUTRAL"


class TestParseOllamaResponseFallbackText:
    def test_plain_positive_text(self):
        result = _parse_ollama_response("The sentiment is POSITIVE based on the review.")
        assert result["label"] == "POSITIVE"
        assert result["score"] == 0.75

    def test_plain_negative_text(self):
        result = _parse_ollama_response("This review has a NEGATIVE tone.")
        assert result["label"] == "NEGATIVE"
        assert result["score"] == 0.75

    def test_plain_text_no_sentiment(self):
        result = _parse_ollama_response("I cannot determine the sentiment.")
        assert result["label"] == "NEUTRAL"
        assert result["score"] == 0.5

    def test_empty_string(self):
        result = _parse_ollama_response("")
        assert result["label"] == "NEUTRAL"

    def test_garbage_json(self):
        result = _parse_ollama_response("{not valid json!!}")
        assert result["label"] in ("POSITIVE", "NEGATIVE", "NEUTRAL")

    def test_positive_takes_priority_over_negative(self):
        # POSITIVE appears first in the text
        result = _parse_ollama_response("POSITIVE but also NEGATIVE")
        assert result["label"] == "POSITIVE"
