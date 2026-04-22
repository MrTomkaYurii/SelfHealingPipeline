import itertools
import json
import os
import re
from datetime import timedelta, datetime

from airflow.sdk import dag, task, Param, get_current_context
import logging

logger = logging.getLogger(__name__)


class Config:
    BASE_DIR   = os.getenv('PIPELINE_BASE_DIR',  '/opt/airflow')
    INPUT_DIR  = os.getenv('PIPELINE_INPUT_DIR',  f'{BASE_DIR}/input')
    OUTPUT_DIR = os.getenv('PIPELINE_OUTPUT_DIR', f'{BASE_DIR}/output')

    # Default input — можна перевизначити через Param при запуску
    INPUT_FILE = os.getenv(
        'PIPELINE_BUSINESS_INPUT_FILE',
        f'{INPUT_DIR}/business-analysis_CHANGE_ME.json'
    )

    MAX_TEXT_LENGTH    = int(os.getenv('PIPELINE_MAX_TEXT_LENGTH', 2000))
    DEFAULT_BATCH_SIZE = 0   # 0 = load all reviews
    DEFAULT_OFFSET     = 0

    OLLAMA_HOST    = os.getenv('OLLAMA_HOST',    'http://localhost:11434')
    OLLAMA_MODEL   = os.getenv('OLLAMA_MODEL',   'llama3.2')
    OLLAMA_TIMEOUT = int(os.getenv('OLLAMA_TIMEOUT', 120))
    OLLAMA_RETRIES = int(os.getenv('OLLAMA_RETRIES', 3))


default_args = {
    'owner': 'airflow',
    'depends_on_past': False,
    'retries': 1,
    'retry_delay': timedelta(minutes=1),
    'execution_timeout': timedelta(hours=2),
}


# ── helpers ──────────────────────────────────────────────────────────────────

def _business_id_from_path(input_file: str) -> str:
    """Extract business_id from filename: business-analysis_{id}.json → {id}"""
    stem = os.path.splitext(os.path.basename(input_file))[0]
    m = re.match(r'business-analysis_(.+)', stem)
    return m.group(1) if m else stem


def _load_ollama_model(model_name: str) -> dict:
    import ollama

    logger.info(f"Loading OLLAMA model: {model_name}")
    client = ollama.Client(host=Config.OLLAMA_HOST)

    try:
        client.show(model_name)
        logger.info(f'Model {model_name} is available.')
    except ollama.ResponseError:
        logger.info('Model not found locally — pulling...')
        client.pull(model_name)
        logger.info(f'Model {model_name} pulled successfully.')

    test = client.chat(
        model=model_name,
        messages=[{"role": "user", "content":
                   "Classify sentiment of 'Great place!' as positive/negative/neutral."}]
    )
    logger.info(f'Model validation: {test["message"]["content"].strip()[:80]}')

    return {
        'backend': 'ollama',
        'model_name': model_name,
        'ollama_host': Config.OLLAMA_HOST,
        'max_length': Config.MAX_TEXT_LENGTH,
        'status': 'loaded',
        'validated_at': datetime.now().isoformat(),
    }


def _load_reviews(input_file: str, batch_size: int = 0, offset: int = 0) -> list[dict]:
    """Read reviews from a business-analysis NDJSON file.
    batch_size=0 means read all; otherwise read [offset, offset+batch_size)."""
    if not os.path.exists(input_file):
        raise FileNotFoundError(f"Input file not found: {input_file}")

    reviews = []
    with open(input_file, 'r', encoding='utf-8') as f:
        lines = itertools.islice(f, offset, offset + batch_size) if batch_size > 0 else f
        for line in lines:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
                reviews.append({
                    'review_id':   r.get('review_id'),
                    'business_id': r.get('business_id'),
                    'user_id':     r.get('user_id'),
                    'stars':       r.get('stars', 0),
                    'text':        r.get('text'),
                    'date':        r.get('date'),
                    'useful':      r.get('useful', 0),
                    'funny':       r.get('funny', 0),
                    'cool':        r.get('cool', 0),
                })
            except json.JSONDecodeError as e:
                logger.warning(f"Skipping invalid JSON line: {e}")

    logger.info(f'Loaded {len(reviews)} reviews from {input_file} '
                f'(batch_size={batch_size or "all"}, offset={offset})')
    return reviews


def _parse_ollama_response(response_text: str) -> dict:
    try:
        clean = response_text.strip()
        if clean.startswith('```'):
            lines = clean.split('\n')
            clean = '\n'.join(lines[1:-1]) if lines[-1].strip() == '```' else '\n'.join(lines[1:])

        parsed = json.loads(clean)
        sentiment  = parsed.get('sentiment', 'NEUTRAL').upper()
        confidence = float(parsed.get('confidence', 0.0))

        if sentiment not in ('POSITIVE', 'NEGATIVE', 'NEUTRAL'):
            sentiment = 'NEUTRAL'

        return {'label': sentiment, 'score': min(max(confidence, 0.0), 1.0)}
    except Exception:
        upper = response_text.upper()
        if 'POSITIVE' in upper: return {'label': 'POSITIVE', 'score': 0.75}
        if 'NEGATIVE' in upper: return {'label': 'NEGATIVE', 'score': 0.75}
        return {'label': 'NEUTRAL', 'score': 0.5}


def _heal_review(review: dict) -> dict:
    text = review.get('text', '')
    result = {
        'review_id':   review.get('review_id'),
        'business_id': review.get('business_id'),
        'stars':       review.get('stars', 0),
        'original_text': None,
        'error_type':    None,
        'action_taken':  'none',
        'was_healed':    False,
        'metadata': {
            'user_id': review.get('user_id'),
            'date':    review.get('date'),
            'useful':  review.get('useful', 0),
            'funny':   review.get('funny', 0),
            'cool':    review.get('cool', 0),
        }
    }

    result['original_text'] = text if isinstance(text, (str, int, float, bool, type(None))) else str(text)

    if text is None:
        result.update(error_type='missing_text', action_taken='filled_with_placeholder',
                      healed_text='No review text provided.', was_healed=True)
    elif not isinstance(text, str):
        converted = str(text).strip() or 'No review text provided.'
        result.update(error_type='wrong_type', action_taken='type_conversion',
                      healed_text=converted, was_healed=True)
    elif not text.strip():
        result.update(error_type='empty_text', action_taken='filled_with_placeholder',
                      healed_text='No review text provided.', was_healed=True)
    elif not re.search(r'[a-zA-Z0-9]', text):
        result.update(error_type='special_characters_only', action_taken='replaced_special_characters',
                      healed_text='[Non-text content]', was_healed=True)
    elif len(text) > Config.MAX_TEXT_LENGTH:
        result.update(error_type='too_long', action_taken='truncated_text',
                      healed_text=text[:Config.MAX_TEXT_LENGTH - 3] + '...', was_healed=True)
    else:
        result['healed_text'] = text.strip()

    return result


def _analyze_with_ollama(healed_reviews: list[dict], model_info: dict) -> list[dict]:
    import ollama, time

    model_name  = model_info['model_name']
    ollama_host = model_info.get('ollama_host', Config.OLLAMA_HOST)

    try:
        client = ollama.Client(host=ollama_host)
    except Exception as e:
        logger.error(f'Cannot connect to OLLAMA: {e}')
        return _degraded_results(healed_reviews, str(e))

    results = []
    total   = len(healed_reviews)

    for idx, review in enumerate(healed_reviews):
        text       = review.get('healed_text', '')
        prediction = None

        for attempt in range(Config.OLLAMA_RETRIES):
            try:
                prompt = (
                    f'Analyze the sentiment of this review and classify it as '
                    f'POSITIVE, NEGATIVE, or NEUTRAL.\n'
                    f'Review: "{text}"\n'
                    f'Reply with ONLY a JSON object: {{"sentiment": "POSITIVE", "confidence": 0.95}}.'
                )
                response  = client.chat(model=model_name,
                                        messages=[{"role": "user", "content": prompt}],
                                        options={'temperature': 0.1})
                prediction = _parse_ollama_response(response['message']['content'].strip())
                break
            except Exception as e:
                if attempt < Config.OLLAMA_RETRIES - 1:
                    logger.warning(f'Attempt {attempt+1} failed for {review.get("review_id")}: {e}')
                    time.sleep(1)
                else:
                    logger.error(f'All attempts failed for {review.get("review_id")}: {e}')
                    prediction = {'label': 'NEUTRAL', 'score': 0.5, 'error': str(e)}

        if (idx + 1) % 10 == 0 or (idx + 1) == total:
            logger.info(f'Analyzed {idx + 1}/{total} reviews.')

        results.append({
            'review_id':          review.get('review_id'),
            'business_id':        review.get('business_id'),
            'stars':              review.get('stars', 0),
            'text':               review.get('healed_text', ''),
            'original_text':      review.get('original_text', ''),
            'predicted_sentiment':prediction.get('label'),
            'confidence':         round(prediction.get('score', 0), 4),
            'status':             'healed' if review.get('was_healed') else 'success',
            'healing_applied':    review.get('was_healed'),
            'healing_action':     review.get('action_taken') if review.get('was_healed') else None,
            'error_type':         review.get('error_type')   if review.get('was_healed') else None,
            'metadata':           review.get('metadata', {}),
        })

    return results


def _degraded_results(healed_reviews: list[dict], error_message: str) -> list[dict]:
    return [{**r, 'text': r.get('healed_text', ''),
             'predicted_sentiment': 'NEUTRAL', 'confidence': 0.5,
             'status': 'degraded', 'error_message': error_message}
            for r in healed_reviews]


# ── DAG ──────────────────────────────────────────────────────────────────────

@dag(
    dag_id='business_analysis_pipeline',
    default_args=default_args,
    description='Sentiment analysis for a single business (from business-analysis_*.json)',
    schedule=None,
    start_date=datetime(2025, 1, 1),
    catchup=False,
    tags=['business_analysis', 'sentiment', 'ollama', 'yelp'],
    params={
        'input_file': Param(
            default=Config.INPUT_FILE,
            type='string',
            description='Path to business-analysis_{business_id}.json inside the container '
                        '(e.g. /opt/airflow/input/business-analysis_PP3BBaVxZLcJU54uP_wL6Q.json)'
        ),
        'ollama_model': Param(
            default=Config.OLLAMA_MODEL,
            type='string',
            description='Ollama model name (e.g. llama3.2)'
        ),
        'batch_size': Param(
            default=Config.DEFAULT_BATCH_SIZE,
            type='integer',
            description='Number of reviews to process (0 = all).'
        ),
        'offset': Param(
            default=Config.DEFAULT_OFFSET,
            type='integer',
            description='Skip this many reviews from the start of the file.'
        ),
    },
    render_template_as_native_obj=True,
)
def business_analysis_pipeline():

    @task
    def load_model() -> dict:
        ctx        = get_current_context()
        model_name = ctx['params'].get('ollama_model', Config.OLLAMA_MODEL)
        logger.info(f'Model: {model_name}')
        return _load_ollama_model(model_name)

    @task
    def load_reviews() -> list[dict]:
        ctx        = get_current_context()
        params     = ctx['params']
        input_file = params.get('input_file',  Config.INPUT_FILE)
        batch_size = params.get('batch_size',  Config.DEFAULT_BATCH_SIZE)
        offset     = params.get('offset',      Config.DEFAULT_OFFSET)
        logger.info(f'Input file: {input_file}, batch_size={batch_size or "all"}, offset={offset}')
        return _load_reviews(input_file, batch_size, offset)

    @task
    def diagnose_and_heal(reviews: list[dict]) -> list[dict]:
        healed = [_heal_review(r) for r in reviews]
        healed_count = sum(1 for r in healed if r.get('was_healed'))
        logger.info(f'Healed {healed_count}/{len(reviews)} reviews.')
        return healed

    @task
    def analyze_sentiment(healed_reviews: list[dict], model_info: dict) -> list[dict]:
        if not healed_reviews:
            logger.warning('No reviews to analyze.')
            return []
        logger.info(f'Analyzing {len(healed_reviews)} reviews...')
        return _analyze_with_ollama(healed_reviews, model_info)

    @task
    def aggregate_and_save(results: list[dict]) -> dict:
        ctx        = get_current_context()
        input_file = ctx['params'].get('input_file', Config.INPUT_FILE)
        business_id = _business_id_from_path(input_file)

        total          = len(results)
        success_count  = sum(1 for r in results if r.get('status') == 'success')
        healed_count   = sum(1 for r in results if r.get('status') == 'healed')
        degraded_count = sum(1 for r in results if r.get('status') == 'degraded')

        sentiment_dist = {'POSITIVE': 0, 'NEGATIVE': 0, 'NEUTRAL': 0}
        for r in results:
            s = r.get('predicted_sentiment', 'NEUTRAL')
            sentiment_dist[s] = sentiment_dist.get(s, 0) + 1

        healing_stats = {}
        for r in results:
            if r.get('healing_applied'):
                a = r.get('healing_action', 'unknown')
                healing_stats[a] = healing_stats.get(a, 0) + 1

        star_sentiment = {}
        for r in results:
            stars     = r.get('stars', 0)
            sentiment = r.get('predicted_sentiment')
            if stars and sentiment:
                if stars not in star_sentiment:
                    star_sentiment[stars] = {'POSITIVE': 0, 'NEGATIVE': 0, 'NEUTRAL': 0}
                star_sentiment[stars][sentiment] += 1

        conf_by_status = {'success': [], 'healed': [], 'degraded': []}
        for r in results:
            st, c = r.get('status'), r.get('confidence', 0.0)
            if st in conf_by_status:
                conf_by_status[st].append(c)

        avg_confidence = {
            st: (sum(v) / len(v)) if v else 0
            for st, v in conf_by_status.items()
        }

        summary = {
            'run_info': {
                'timestamp':   datetime.now().isoformat(),
                'business_id': business_id,
                'input_file':  input_file,
                'total_reviews': total,
            },
            'totals': {
                'processed': total, 'success': success_count,
                'healed': healed_count, 'degraded': degraded_count,
            },
            'rates': {
                'success_rate':     round(success_count  / max(total, 1), 4),
                'healing_rate':     round(healed_count   / max(total, 1), 4),
                'degradation_rate': round(degraded_count / max(total, 1), 4),
            },
            'sentiment_distribution':  sentiment_dist,
            'healing_statistics':      healing_stats,
            'star_sentiment_correlation': star_sentiment,
            'average_confidence':      avg_confidence,
            'results':                 results,
        }

        os.makedirs(Config.OUTPUT_DIR, exist_ok=True)
        output_file = os.path.join(Config.OUTPUT_DIR,
                                   f'business-analysis_{business_id}-result.json')

        with open(output_file, 'w', encoding='utf-8') as f:
            json.dump(summary, f, indent=2, default=str, ensure_ascii=False)

        logger.info(f'Result saved → {output_file}')
        logger.info(f'Totals: {total} processed, {success_count} success, '
                    f'{healed_count} healed, {degraded_count} degraded.')

        return {k: v for k, v in summary.items() if k != 'results'}

    @task
    def health_report(summary: dict) -> dict:
        total    = summary['totals']['processed']
        healed   = summary['totals']['healed']
        degraded = summary['totals']['degraded']

        if   degraded > total * 0.1: status = 'CRITICAL'
        elif degraded > 0:           status = 'DEGRADED'
        elif healed   > total * 0.5: status = 'WARNING'
        else:                        status = 'HEALTHY'

        report = {
            'pipeline':     'business_analysis_pipeline',
            'business_id':  summary['run_info']['business_id'],
            'timestamp':    datetime.now().isoformat(),
            'health_status': status,
            'run_info':     summary['run_info'],
            'metrics': {
                'total_processed':  total,
                'success_rate':     summary['rates']['success_rate'],
                'healing_rate':     summary['rates']['healing_rate'],
                'degradation_rate': summary['rates']['degradation_rate'],
            },
            'sentiment_distribution': summary['sentiment_distribution'],
            'healing_summary':        summary['healing_statistics'],
            'average_confidence':     summary['average_confidence'],
        }

        logger.info(f'Health: {status} | sentiment: {summary["sentiment_distribution"]}')
        return report

    model_info     = load_model()
    reviews        = load_reviews()
    healed         = diagnose_and_heal(reviews)
    analyzed       = analyze_sentiment(healed, model_info)
    summary        = aggregate_and_save(analyzed)
    _              = health_report(summary)


business_analysis_pipeline_dag = business_analysis_pipeline()
