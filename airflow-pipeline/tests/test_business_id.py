"""
Unit tests for _business_id_from_path() — extracts business_id from filename.
"""
import pytest
from dags.business_pipeline_dag import _business_id_from_path


class TestBusinessIdFromPath:
    def test_simple_filename(self):
        assert _business_id_from_path("business-analysis_abc123.json") == "abc123"

    def test_full_unix_path(self):
        result = _business_id_from_path(
            "/opt/airflow/input/business-analysis_PP3BBaVxZLcJU54uP_wL6Q.json"
        )
        assert result == "PP3BBaVxZLcJU54uP_wL6Q"

    def test_full_windows_path(self):
        result = _business_id_from_path(
            r"C:\airflow\input\business-analysis_XYZ-999.json"
        )
        assert result == "XYZ-999"

    def test_id_with_hyphens_and_underscores(self):
        result = _business_id_from_path("business-analysis_ab-cd_ef.json")
        assert result == "ab-cd_ef"

    def test_id_with_numbers_only(self):
        result = _business_id_from_path("business-analysis_123456.json")
        assert result == "123456"

    def test_unrecognised_filename_returns_stem(self):
        result = _business_id_from_path("some_other_file.json")
        assert result == "some_other_file"

    def test_no_extension(self):
        result = _business_id_from_path("business-analysis_myid")
        assert result == "myid"
