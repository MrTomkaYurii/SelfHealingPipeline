"""
Mocks airflow.sdk so DAG helper functions can be imported
without a running Airflow instance.
"""
import sys
from unittest.mock import MagicMock

# Patch before any DAG module is loaded
for mod in ("airflow", "airflow.sdk", "ollama"):
    sys.modules.setdefault(mod, MagicMock())
