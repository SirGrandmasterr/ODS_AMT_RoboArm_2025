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

COPY ODS_AMT_RoboArm_2025/Assets/PythonEnvironment/unity-ml/pyproject.toml ODS_AMT_RoboArm_2025/Assets/PythonEnvironment/unity-ml/poetry.lock ./
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

# Copy the build server
COPY build_server /build_server

# Make the executable executable
RUN chmod +x /build_server/AgentTrainer.x86_64

# Copy the training config
COPY ODS_AMT_RoboArm_2025/Assets/trainingConfigServer.yaml .

# Expose the standard ports for documentation
# 5004 = Unity-Python Bridge
# 6006 = TensorBoard
EXPOSE 5004 6006

CMD ["mlagents-learn", "trainingConfigServer.yaml", "--env=/build_server/AgentTrainer.x86_64", "--run-id=RoboArm_Servered", "--num-envs=20", "--no-graphics"]