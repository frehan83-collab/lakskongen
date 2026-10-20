#!/usr/bin/env python3
"""Figma webhook handler: extract tokens from payload, write to design/tokens.json"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

try:
    from .config import DESIGN_TOKENS, FIGMA_WEBHOOK_SECRET
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import DESIGN_TOKENS, FIGMA_WEBHOOK_SECRET


def parse_figma_payload(payload: dict) -> dict | None:
    """Extract tokens from Figma webhook payload."""
    # Handle different payload formats
    
    # Format 1: Direct tokens in client_payload
    if "client_payload" in payload:
        cp = payload["client_payload"]
        if "tokens" in cp:
            return cp["tokens"]
    
    # Format 2: Figma REST API response format
    if "meta" in payload and "tokens" in payload:
        return payload["tokens"]
    
    # Format 3: Webhook with file_key, need to fetch
    if "file_key" in payload.get("client_payload", {}):
        print("[design-token-sync] Figma file_key detected, but REST fetch not implemented", file=sys.stderr)
        print("[design-token-sync] Provide tokens directly in client_payload.tokens", file=sys.stderr)
        return None
    
    return None


def write_tokens(tokens: dict) -> int:
    """Write tokens to design/tokens.json with version bump."""
    DESIGN_TOKENS.parent.mkdir(parents=True, exist_ok=True)
    
    # Ensure required structure
    if "name" not in tokens:
        tokens["name"] = "lakskongen-design-tokens"
    if "version" not in tokens:
        # Try to increment from existing
        existing = {}
        if DESIGN_TOKENS.exists():
            try:
                existing = json.loads(DESIGN_TOKENS.read_text())
                old_version = existing.get("version", "1.0.0")
                parts = old_version.split(".")
                parts[-1] = str(int(parts[-1]) + 1)
                tokens["version"] = ".".join(parts)
            except Exception:
                tokens["version"] = "1.0.0"
    
    if "theme" not in tokens:
        tokens["theme"] = "kyst-og-stal"
    
    # Write with consistent formatting
    DESIGN_TOKENS.write_text(json.dumps(tokens, indent=2) + "\n")
    print(f"[design-token-sync] Wrote tokens to {DESIGN_TOKENS} (v{tokens.get('version', '?')})")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Figma webhook token extractor")
    parser.add_argument("--payload", help="Path to webhook payload JSON file")
    parser.add_argument("--write", action="store_true", help="Write tokens to design/tokens.json")
    args = parser.parse_args(argv)
    
    if args.payload:
        payload_path = Path(args.payload)
        if not payload_path.exists():
            print(f"[design-token-sync] ERROR: Payload file not found: {payload_path}", file=sys.stderr)
            return 1
        payload = json.loads(payload_path.read_text())
    else:
        # Read from stdin (GitHub Actions passes payload via stdin or env)
        raw = sys.stdin.read()
        if not raw.strip():
            # Check for GITHUB_EVENT_PATH
            event_path = os.environ.get("GITHUB_EVENT_PATH")
            if event_path and Path(event_path).exists():
                raw = Path(event_path).read_text()
            else:
                print("[design-token-sync] ERROR: No payload provided", file=sys.stderr)
                return 1
        payload = json.loads(raw)
    
    # Manual dispatch: tokens arrive as the optional `tokens_json` input; without
    # it the run re-syncs and verifies the committed design/tokens.json.
    inputs = payload.get("inputs") if isinstance(payload, dict) else None
    if isinstance(inputs, dict) and "client_payload" not in payload:
        raw_tokens = str(inputs.get("tokens_json") or "").strip()
        if not raw_tokens:
            print("[design-token-sync] No token payload: keeping design/tokens.json as committed")
            return 0
        payload = {"client_payload": {"tokens": json.loads(raw_tokens)}}

    tokens = parse_figma_payload(payload)
    if not tokens:
        print("[design-token-sync] ERROR: Could not extract tokens from payload", file=sys.stderr)
        return 1

    print(f"[design-token-sync] Extracted tokens: {len(tokens.get('surfaces', {}))} surfaces, "
          f"{len(tokens.get('contrast', {}).get('pairs', []))} contrast pairs, {len(tokens.get('spacing', {}))} spacing")
    
    if args.write:
        return write_tokens(tokens)
    
    # Dry run: just print
    print(json.dumps(tokens, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())