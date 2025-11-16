# ---- Stage 1: Build dependencies ----
# Use the official NVIDIA CUDA 12.1 runtime image. Ubuntu 22.04 comes with Python 3.10.
FROM nvidia/cuda:12.1.1-runtime-ubuntu22.04 AS builder

# Set environment variables to non-interactive
ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1

# Install Python 3.10 (which is default python3), pip, and curl
RUN apt-get update && apt-get install -y \
    python3.10 \
    python3-pip \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Install Poetry
RUN curl -sSL https://install.python-poetry.org | python3 -
ENV PATH="/root/.local/bin:$PATH"

# Set up the working directory
WORKDIR /app

# Configure poetry to create the virtual environment in the project's root
RUN poetry config virtualenvs.in-project true

# Copy only the dependency files to leverage Docker cache
COPY pyproject.toml poetry.lock ./

# Install dependencies, skipping dev dependencies and not installing the project itself (as we'll copy code later)
# The --no-root is important for this pattern
RUN poetry install --no-root

FROM nvidia/cuda:12.1.1-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1

WORKDIR /app

RUN apt-get update && apt-get install -y \
    python3.10 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/.venv ./.venv

COPY src/ ./src/

ENV PATH="/app/.venv/bin:$PATH"

CMD ["mlagents-learn", "--help"]