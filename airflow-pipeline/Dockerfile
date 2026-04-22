FROM apache/airflow:3.0.6
RUN pip uninstall -y apache-airflow-providers-openlineage || true
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt