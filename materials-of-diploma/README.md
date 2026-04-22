# Self-Healing Data Pipeline

A production-ready, AI-powered data pipeline that automatically detects and heals data quality issues during sentiment analysis of Yelp reviews using Apache Airflow and Ollama LLM.

![Python](https://img.shields.io/badge/Python-3.12+-blue?logo=python)
![Apache Airflow](https://img.shields.io/badge/Apache%20Airflow-3.0+-017CEE?logo=apache-airflow)
![Ollama](https://img.shields.io/badge/Ollama-LLaMA%203.2-orange)
![License](https://img.shields.io/badge/License-MIT-green)

## Table of Contents
- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Self-Healing Capabilities](#self-healing-capabilities)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [Pipeline Tasks](#pipeline-tasks)
- [Health Monitoring](#health-monitoring)
- [Output](#output)
- [Contributing](#contributing)

## Overview

Traditional data pipelines fail when they encounter malformed or unexpected data. This project demonstrates an **agentic, self-healing approach** where the pipeline:

1. **Diagnoses** data quality issues in real-time
2. **Heals** problematic records automatically
3. **Processes** sentiment analysis using local LLM (Ollama)
4. **Reports** detailed health metrics and healing statistics

## Architecture
![Self healing pipeline.jpeg](assets/Self%20healing%20pipeline.jpeg)


## Features

| Feature | Description |
|---------|-------------|
| **Self-Healing** | Automatically fixes missing, malformed, or invalid data |
| **Local LLM** | Uses Ollama with LLaMA 3.2 - no external API calls |
| **Batch Processing** | Configurable batch sizes with offset support |
| **Parallel Execution** | Scale with multiple concurrent DAG runs |
| **Health Monitoring** | Real-time pipeline health status reporting |
| **Graceful Degradation** | Falls back to neutral predictions on failures |
| **Detailed Metrics** | Sentiment distribution, confidence scores, healing stats |

## Self-Healing Capabilities

The pipeline automatically detects and heals the following data quality issues:

| Error Type | Detection | Healing Action |
|------------|-----------|----------------|
| `missing_text` | Text field is `None` | Fill with placeholder |
| `empty_text` | Text is empty or whitespace | Fill with placeholder |
| `wrong_type` | Text is not a string | Type conversion |
| `special_characters_only` | No alphanumeric characters | Replace with marker |
| `too_long` | Exceeds max length (2000 chars) | Truncate with ellipsis |

## Prerequisites

- Python 3.12+
- [Ollama](https://ollama.ai/) installed and running
- Apache Airflow 3.0+
- 8GB+ RAM recommended for LLM inference

## Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/airscholar/SelfHealingPipeline.git
   cd SelfHealingPipeline
   ```

2. **Create virtual environment**
   ```bash
   python -m venv .venv
   source .venv/bin/activate  # On Windows: .venv\Scripts\activate
   ```

3. **Install dependencies**
   ```bash
   pip install -r requirements.txt
   ```

4. **Install and start Ollama**
   ```bash
   # Install Ollama (macOS)
   brew install ollama

   # Start Ollama service
   ollama serve

   # Pull the model (in a new terminal)
   ollama pull llama3.2
   ```

5. **Initialize Airflow**
   ```bash
   export AIRFLOW_HOME=$(pwd)
   airflow db migrate
   ```

6. **Start Airflow services**
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

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `PIPELINE_BASE_DIR` | Project root | Base directory for the pipeline |
| `PIPELINE_INPUT_FILE` | `input/yelp_academic_dataset_review.json` | Input data file |
| `PIPELINE_OUTPUT_DIR` | `output/` | Output directory |
| `PIPELINE_MAX_TEXT_LENGTH` | `2000` | Max characters per review |
| `OLLAMA_HOST` | `http://localhost:11434` | Ollama server URL |
| `OLLAMA_MODEL` | `llama3.2` | Model for sentiment analysis |
| `OLLAMA_TIMEOUT` | `120` | Request timeout (seconds) |
| `OLLAMA_RETRIES` | `3` | Retry attempts on failure |

### DAG Parameters

Configure via Airflow UI or CLI:

```json
{
  "input_file": "/path/to/reviews.json",
  "batch_size": 100,
  "offset": 0,
  "ollama_model": "llama3.2"
}
```

## Usage

### Single Run (Airflow UI)

1. Open Airflow UI at `http://localhost:8080`
2. Navigate to `self_healing_pipeline`
3. Click "Trigger DAG" and configure parameters
4. Monitor execution in the Graph view

### Single Run (CLI)

```bash
airflow dags trigger self_healing_pipeline \
    --conf '{"batch_size": 100, "offset": 0}'
```

### Batch Processing (Large Datasets)

Use the batch runner for processing millions of records:

```bash
# Process 5M records with batch size 1000 (sequential)
python scripts/batch_runner.py --total 5000000 --batch-size 1000

# Process with 5 parallel DAG runs
python scripts/batch_runner.py --total 5000000 --batch-size 5000 --parallel 5

# Resume from offset 100000
python scripts/batch_runner.py --total 5000000 --batch-size 1000 --start 100000

# Dry run to preview triggers
python scripts/batch_runner.py --total 5000000 --batch-size 10000 --dry-run
```

## Pipeline Tasks

| Task | Description | Output |
|------|-------------|--------|
| `load_model` | Initialize Ollama model, pull if needed | Model config |
| `load_reviews` | Read reviews from JSON file with offset | List of reviews |
| `diagnose_and_heal_batch` | Detect and fix data quality issues | Healed reviews |
| `batch_analyze_sentiment` | LLM sentiment classification | Analyzed reviews |
| `aggregate_results` | Compute statistics, write output | Summary JSON |
| `generate_health_report` | Assess pipeline health status | Health report |

## Health Monitoring

The pipeline generates health status based on processing outcomes:

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

## Output

Results are saved to `output/` with timestamped filenames:

```
output/
└── sentiment_analysis_summary_2025-12-08_14-30-00_Offset0.json
```

### Output Schema

```json
{
  "run_info": {
    "timestamp": "2025-12-08T14:30:00",
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
  "sentiment_distribution": {...},
  "healing_statistics": {...},
  "results": [...]
}
```

## Project Structure

```
SelfHealingPipeline/
├── dags/
│   └── agentic_pipeline_dag.py    # Main DAG definition
├── scripts/
│   └── batch_runner.py            # Batch processing script
├── input/                         # Input data directory
├── output/                        # Generated results
├── logs/                          # Airflow logs
├── requirements.txt               # Python dependencies
└── README.md
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## Author

**Yusuf Ganiyu** - [@airscholar](https://github.com/airscholar)

---

If you found this project helpful, please consider giving it a star!
