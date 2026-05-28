"""
Unit tests for health status logic extracted from generate_health_report()
and health_report() tasks.

The status rules:
  degraded > 10% of total  → CRITICAL
  degraded > 0             → DEGRADED
  healed   > 50% of total  → WARNING
  otherwise                → HEALTHY
"""
import pytest


def compute_health_status(total: int, healed: int, degraded: int) -> str:
    """Mirrors the logic from both DAG health report tasks."""
    if degraded > total * 0.1:
        return "CRITICAL"
    elif degraded > 0:
        return "DEGRADED"
    elif healed > total * 0.5:
        return "WARNING"
    return "HEALTHY"


class TestHealthStatusHealthy:
    def test_all_success(self):
        assert compute_health_status(100, 0, 0) == "HEALTHY"

    def test_few_healed_under_50_percent(self):
        assert compute_health_status(100, 49, 0) == "HEALTHY"

    def test_exactly_50_percent_healed(self):
        # 50 is NOT > 50%, so still HEALTHY
        assert compute_health_status(100, 50, 0) == "HEALTHY"

    def test_single_record_success(self):
        assert compute_health_status(1, 0, 0) == "HEALTHY"


class TestHealthStatusWarning:
    def test_more_than_half_healed(self):
        assert compute_health_status(100, 51, 0) == "WARNING"

    def test_all_healed_no_degraded(self):
        assert compute_health_status(100, 100, 0) == "WARNING"

    def test_just_over_50_percent(self):
        assert compute_health_status(10, 6, 0) == "WARNING"


class TestHealthStatusDegraded:
    def test_one_degraded(self):
        assert compute_health_status(100, 0, 1) == "DEGRADED"

    def test_degraded_under_10_percent(self):
        assert compute_health_status(100, 0, 9) == "DEGRADED"

    def test_exactly_10_percent_degraded(self):
        # 10 is NOT > 10%, so DEGRADED not CRITICAL
        assert compute_health_status(100, 0, 10) == "DEGRADED"


class TestHealthStatusCritical:
    def test_over_10_percent_degraded(self):
        assert compute_health_status(100, 0, 11) == "CRITICAL"

    def test_all_degraded(self):
        assert compute_health_status(100, 0, 100) == "CRITICAL"

    def test_majority_degraded(self):
        assert compute_health_status(10, 0, 2) == "CRITICAL"


class TestHealthStatusEdgeCases:
    def test_zero_total(self):
        # 0 total: no degraded, no healed → HEALTHY
        assert compute_health_status(0, 0, 0) == "HEALTHY"

    def test_critical_takes_priority_over_warning(self):
        # Many healed AND many degraded → CRITICAL wins
        assert compute_health_status(100, 80, 20) == "CRITICAL"

    def test_degraded_takes_priority_over_warning(self):
        # Many healed but some degraded → DEGRADED wins
        assert compute_health_status(100, 60, 5) == "DEGRADED"
