"""Offline model acceptance: run beside server.py after explicit provisioning."""
import time
import resource
from server import translate

started = time.monotonic()
samples = [
    "Bitcoin markets react to Federal Reserve interest rate policy.",
    "OpenAI announces a new model. Investors remain uncertain about its market impact.",
]
for sample in samples:
    result = translate(sample, "en")
    assert result.strip() and any("\u4e00" <= character <= "\u9fff" for character in result)
    print(result)
print(f"elapsed_seconds={time.monotonic()-started:.2f} peak_kb={resource.getrusage(resource.RUSAGE_SELF).ru_maxrss}")
