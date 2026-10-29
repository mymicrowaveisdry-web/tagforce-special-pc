FROM pspdev/pspdev@sha256:54895e6f5afb71b8f4f6915ee6d5023e1e087ca7afdf2dcb1d7a694a5233e45f
RUN apk add --no-cache python3 py3-pip
COPY python-requirements.txt /tmp/psp-python-requirements.txt
RUN python3 -m venv /opt/python \
    && /opt/python/bin/python -m pip install --no-cache-dir -r /tmp/psp-python-requirements.txt
ENV PATH="/opt/python/bin:${PATH}"
ENV PYTHONDONTWRITEBYTECODE=1
WORKDIR /source
