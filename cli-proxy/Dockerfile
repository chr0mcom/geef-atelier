FROM python:3.12-slim

# Install Node.js (for claude and codex CLIs which are npm packages).
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl \
        ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Install claude and codex CLIs globally.
RUN npm install -g @anthropic-ai/claude-code @openai/codex

# Install Python dependencies.
WORKDIR /app
COPY pyproject.toml .
RUN pip install --no-cache-dir ".[dev]"

# Copy source.
COPY src/ ./src/

# Auth directories — expected to be volume-mounted in production.
RUN mkdir -p /auth/claude /auth/codex

EXPOSE 8090

ENV PYTHONPATH=/app/src
ENV CLAUDE_HOME=/auth/claude
ENV CODEX_HOME=/auth/codex

HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=30s \
    CMD curl -f http://localhost:8090/health || exit 1

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8090"]
