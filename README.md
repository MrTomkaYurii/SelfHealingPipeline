# Self-Healing Data Pipeline

AI-powered data pipeline that automatically detects and heals data quality issues during sentiment analysis of Yelp reviews using Apache Airflow and Ollama LLM.

![Python](https://img.shields.io/badge/Python-3.12+-blue?logo=python)
![Apache Airflow](https://img.shields.io/badge/Apache%20Airflow-3.0+-017CEE?logo=apache-airflow)
![Ollama](https://img.shields.io/badge/Ollama-LLaMA%203.2-orange)
![.NET](https://img.shields.io/badge/.NET-10.0-purple?logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-green)

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Features](#features)
- [Self-Healing Capabilities](#self-healing-capabilities)
- [Quick Start — Docker (recommended)](#quick-start--docker-recommended)
- [Alternative — Native Linux](#alternative--native-linux)
- [Configuration](#configuration)
- [Usage](#usage)
- [Pipeline Tasks](#pipeline-tasks)
- [Health Monitoring](#health-monitoring)
- [Blazor Dashboard](#blazor-dashboard)
- [Output](#output)

## Overview

Traditional data pipelines fail when they encounter malformed or unexpected data. This project demonstrates an **agentic, self-healing approach** where the pipeline:

1. **Diagnoses** data quality issues in real-time
2. **Heals** problematic records automatically
3. **Processes** sentiment analysis using a local LLM (Ollama)
4. **Reports** detailed health metrics and healing statistics

Two DAGs are included:

| DAG | Description |
|-----|-------------|
| `self_healing_pipeline` | Batch sentiment analysis of Yelp reviews |
| `business_analysis_pipeline` | Per-business aggregated analysis |

## Architecture

![Self healing pipeline](airflow-pipeline/assets/Self%20healing%20pipeline.jpeg)

## Project Structure

```
SelfHealingPipeline/
├── airflow-pipeline/
│   ├── dags/
│   │   ├── agentic_pipeline_dag.py      # Reviews sentiment DAG
│   │   └── business_pipeline_dag.py     # Business analysis DAG
│   ├── input/                           # Yelp dataset JSON files
│   ├── output/                          # Generated results
│   ├── docker-compose.yml               # Docker setup (recommended)
│   ├── Dockerfile
│   └── requirements.txt
├── blazor-dashboard/                    # .NET 10 Blazor monitoring dashboard
└── materials-of-diploma/                # Diploma materials and docs
```

## Features

| Feature | Description |
|---------|-------------|
| **Self-Healing** | Automatically fixes missing, malformed, or invalid data |
| **Local LLM** | Uses Ollama with LLaMA 3.2 — no external API calls |
| **Batch Processing** | Configurable batch sizes with offset support |
| **Health Monitoring** | Real-time pipeline health status reporting |
| **Graceful Degradation** | Falls back to neutral predictions on failures |
| **Detailed Metrics** | Sentiment distribution, confidence scores, healing stats |
| **Blazor Dashboard** | .NET 10 web dashboard for visualizing results |

## Self-Healing Capabilities

| Error Type | Detection | Healing Action |
|------------|-----------|----------------|
| `missing_text` | Text field is `None` | Fill with placeholder |
| `empty_text` | Text is empty or whitespace | Fill with placeholder |
| `wrong_type` | Text is not a string | Type conversion |
| `special_characters_only` | No alphanumeric characters | Replace with marker |
| `too_long` | Exceeds max length (2000 chars) | Truncate with ellipsis |

---

## Quick Start — Docker (recommended)

This is the primary way to run the pipeline. Works on **Windows, macOS, and Linux** without any Python environment setup.

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Ollama](https://ollama.ai/) installed and running on the **host** machine

### 1. Start Ollama and pull the model

```bash
ollama serve
ollama pull llama3.2
```

### 2. Build and start the container

```bash
cd airflow-pipeline
docker compose up --build
```

Airflow will be available at **http://localhost:8080**.

Default credentials: `admin` / `admin`

### 3. Trigger a DAG

Open the Airflow UI, navigate to `self_healing_pipeline`, and click **Trigger DAG**.

The container connects to Ollama on the host via `host.docker.internal:11434` — this is pre-configured in `docker-compose.yml` and works on all platforms.

### Stop

```bash
docker compose down
```

---

## Alternative — Native (Linux / macOS)

Works natively on Linux and macOS. On Windows Airflow was not reliable without a container — use Docker instead.

### Prerequisites

- Python 3.12+
- [Ollama](https://ollama.ai/) installed and running
- Apache Airflow 3.0+
- 8GB+ RAM recommended for LLM inference

### 1. Clone the repository

```bash
git clone <repo-url>
cd SelfHealingPipeline/airflow-pipeline
```

### 2. Create virtual environment

```bash
python -m venv .venv
source .venv/bin/activate
```

### 3. Install dependencies

```bash
pip install -r requirements.txt
```

### 4. Install and start Ollama

```bash
# Install Ollama (macOS)
brew install ollama

# Start Ollama service
ollama serve

# Pull the model (in a new terminal)
ollama pull llama3.2
```

### 5. Initialize Airflow

```bash
export AIRFLOW_HOME=$(pwd)
airflow db migrate
```

### 6. Start Airflow services

```bash
airflow standalone
```

Or run components separately:

```bash
# Terminal 1: Start webserver
airflow webserver --port 8080

# Terminal 2: Start scheduler
airflow scheduler
```

---

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `PIPELINE_BASE_DIR` | Project root | Base directory for the pipeline |
| `PIPELINE_INPUT_FILE` | `input/yelp_academic_dataset_review.json` | Input data file |
| `PIPELINE_OUTPUT_DIR` | `output/` | Output directory |
| `PIPELINE_MAX_TEXT_LENGTH` | `2000` | Max characters per review |
| `OLLAMA_HOST` | `http://localhost:11434` | Ollama server URL (Docker uses `host.docker.internal`) |
| `OLLAMA_MODEL` | `llama3.2` | Model for sentiment analysis |
| `OLLAMA_TIMEOUT` | `120` | Request timeout (seconds) |
| `OLLAMA_RETRIES` | `3` | Retry attempts on failure |

### DAG Parameters (Airflow UI or CLI)

```json
{
  "input_file": "/path/to/reviews.json",
  "batch_size": 100,
  "offset": 0,
  "ollama_model": "llama3.2"
}
```

---

## Usage

### Trigger via Airflow UI

1. Open **http://localhost:8080**
2. Navigate to `self_healing_pipeline` or `business_analysis_pipeline`
3. Click **Trigger DAG** and configure parameters
4. Monitor execution in the Graph view

### Trigger via CLI

```bash
airflow dags trigger self_healing_pipeline \
    --conf '{"batch_size": 100, "offset": 0}'
```

### Batch Processing (large datasets)

```bash
# Process 5M records sequentially
python scripts/batch_runner.py --total 5000000 --batch-size 1000

# Process with 5 parallel DAG runs
python scripts/batch_runner.py --total 5000000 --batch-size 5000 --parallel 5

# Resume from offset
python scripts/batch_runner.py --total 5000000 --batch-size 1000 --start 100000

# Dry run
python scripts/batch_runner.py --total 5000000 --batch-size 10000 --dry-run
```

---

## Pipeline Tasks

| Task | Description | Output |
|------|-------------|--------|
| `load_model` | Initialize Ollama model, pull if needed | Model config |
| `load_reviews` | Read reviews from JSON file with offset | List of reviews |
| `diagnose_and_heal_batch` | Detect and fix data quality issues | Healed reviews |
| `batch_analyze_sentiment` | LLM sentiment classification | Analyzed reviews |
| `aggregate_results` | Compute statistics, write output | Summary JSON |
| `generate_health_report` | Assess pipeline health status | Health report |

---

## Health Monitoring

| Status | Condition |
|--------|-----------|
| `HEALTHY` | <50% reviews needed healing, no degradation |
| `WARNING` | >50% reviews needed healing |
| `DEGRADED` | Some inference failures occurred |
| `CRITICAL` | >10% records in degraded state |

### Sample Health Report

```json
{
  "pipeline": "self_healing_pipeline",
  "health_status": "HEALTHY",
  "metrics": {
    "total_processed": 100,
    "success_rate": 0.85,
    "healing_rate": 0.15,
    "degradation_rate": 0.0
  },
  "sentiment_distribution": {
    "POSITIVE": 45,
    "NEGATIVE": 30,
    "NEUTRAL": 25
  }
}
```

---

## Blazor Dashboard

A .NET 10 Blazor web application for visualizing pipeline results, located in `blazor-dashboard/`.

### Run

```bash
cd blazor-dashboard
dotnet run --project Dashboard
```

The dashboard reads output files from the `airflow-pipeline/output/` directory and provides charts and tables for sentiment results, health metrics, and healing statistics.

---

## Output

Results are saved to `airflow-pipeline/output/` with timestamped filenames:

```
output/
└── sentiment_analysis_summary_2026-04-22_11-34-58_Offset0.json
```

### Output Schema

```json
{
  "run_info": {
    "timestamp": "2026-04-22T11:34:58",
    "batch_size": 100,
    "offset": 0
  },
  "totals": {
    "processed": 100,
    "success": 85,
    "healed": 15,
    "degraded": 0
  },
  "rates": {
    "success_rate": 0.85,
    "healing_rate": 0.15,
    "degradation_rate": 0.0
  },
  "sentiment_distribution": {},
  "healing_statistics": {},
  "results": []
}
```
