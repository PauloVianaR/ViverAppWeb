"""Render a scan plan inside an ephemeral ZAP container, never on the host."""

import os
from pathlib import Path

cookie = os.environ["QA_COOKIE"]
if not cookie.startswith("ViverApp.Session.Local="):
    raise RuntimeError("Synthetic session cookie is missing")

template = Path("/zap/wrk/plan.template.yaml").read_text(encoding="utf-8")
if template.count("__COOKIE__") != 1:
    raise RuntimeError("Unexpected scan plan template")

Path("/tmp/plan.yaml").write_text(template.replace("__COOKIE__", cookie), encoding="utf-8")
