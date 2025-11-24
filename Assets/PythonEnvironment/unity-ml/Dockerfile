# ---- Stage 1: Build dependencies ----
FROM nvidia/cuda:12.1.1-runtime-ubuntu22.04 AS builder

ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1

RUN apt-get update && apt-get install -y \
    python3.10 \
    python3-pip \
    curl \
    && rm -rf /var/lib/apt/lists/*

RUN curl -sSL https://install.python-poetry.org | python3 -
ENV PATH="/root/.local/bin:$PATH"

WORKDIR /app
RUN poetry config virtualenvs.in-project true

COPY pyproject.toml poetry.lock ./
RUN poetry install --no-root

# ---- Stage 2: Runtime Image ----
FROM nvidia/cuda:12.1.1-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1

WORKDIR /app

# Ensure bash is installed for the complex entrypoint command used by the backend
RUN apt-get update && apt-get install -y \
    python3.10 \
    bash \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/.venv ./.venv

# Add venv to path so we can call 'mlagents-learn' and 'tensorboard' directly
ENV PATH="/app/.venv/bin:$PATH"

# Expose the standard ports for documentation
# 5004 = Unity-Python Bridge
# 6006 = TensorBoard
EXPOSE 5004 6006

CMD ["mlagents-learn", "--help"]