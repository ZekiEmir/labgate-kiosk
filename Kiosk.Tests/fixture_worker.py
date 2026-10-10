"""TEST FIXTURE ONLY: deterministic protocol output, never a real face verifier."""
import json
import sys

request = json.loads(sys.stdin.read())
if sys.argv[1] == "enroll":
    print(json.dumps({"ok": True, "encoding": [0.0] * 128}))
elif sys.argv[1] == "verify":
    print(json.dumps({"ok": True, "match": True, "distance": 0.0}))
else:
    print(json.dumps({"ok": True, "image": "test-only"}))
