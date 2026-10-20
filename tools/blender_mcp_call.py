#!/usr/bin/env python3
"""Call a Blender MCP tool and print the result.

Usage:
  python3 tools/blender_mcp_call.py <tool_name> [<json_args>]
  python3 tools/blender_mcp_call.py execute_blender_code --file blender/build_salmon.py

Requires: Blender running with the MCP addon connected, `uv` on PATH.
Part of the Blender -> Godot asset pipeline (see blender/).
"""

import asyncio
import json
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

SERVER_DIR = "/Users/fredrik.j.hansen/blender_mcp/mcp"


async def main() -> int:
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0
    tool = sys.argv[1]
    args: dict = {}
    rest = sys.argv[2:]
    if len(rest) >= 2 and rest[0] == "--file":
        with open(rest[1], encoding="utf-8") as f:
            args = {"code": f.read()}
    elif rest:
        args = json.loads(rest[0])

    params = StdioServerParameters(
        command="uv",
        args=["--directory", SERVER_DIR, "run", "blender-mcp"],
    )
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            result = await session.call_tool(tool, args)
            for c in result.content:
                print(getattr(c, "text", str(c)))
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
