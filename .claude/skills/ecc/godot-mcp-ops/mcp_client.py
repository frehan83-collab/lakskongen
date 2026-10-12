#!/usr/bin/env python3
"""
mcp_client.py — MCP WebSocket Client for Godot MCP Toolkit

Connects to the Godot editor's MCP server on ws://localhost:6550
and provides methods to call MCP commands.
"""

import asyncio
import json
import uuid
from pathlib import Path
from typing import Any, Dict, Optional
import websockets
from websockets.exceptions import WebSocketException


class MCPConnectionError(Exception):
    """Raised when MCP connection fails."""
    pass


class MCPClient:
    def __init__(self, uri: str = "ws://localhost:6550", timeout: float = 10.0):
        self.uri = uri
        self.timeout = timeout
        self.ws: Optional[websockets.WebSocketClientProtocol] = None
        self.token: Optional[str] = None
        self._pending: Dict[str, asyncio.Future] = {}
        self._receive_task: Optional[asyncio.Task] = None

    async def connect(self):
        """Connect to MCP server and authenticate."""
        try:
            self.ws = await websockets.connect(
                self.uri,
                open_timeout=self.timeout,
                close_timeout=self.timeout
            )
        except (WebSocketException, OSError) as e:
            raise MCPConnectionError(f"Failed to connect to {self.uri}: {e}")

        # Start receive loop
        self._receive_task = asyncio.create_task(self._receive_loop())

        # Read token from file (shared with editor)
        token_path = self._get_token_path()
        if token_path.exists():
            self.token = token_path.read_text().strip()

        # Send auth message
        await self._send_auth()

    def _get_token_path(self) -> Path:
        """Get the token file path (user://mcp_token.txt in Godot)."""
        import os
        # Godot user:// on macOS
        user_dir = Path.home() / "Library" / "Application Support" / "Godot" / "app_userdata"
        # Find the project folder
        for project_dir in user_dir.glob("*"):
            token_file = project_dir / "mcp_token.txt"
            if token_file.exists():
                return token_file
        # Fallback to project-local
        return Path.cwd() / "godot" / "mcp_token.txt"

    async def _send_auth(self):
        """Send authentication message."""
        if not self.token:
            # Try without token first (some configs allow it)
            auth_msg = {"jsonrpc": "2.0", "method": "auth", "params": {}, "id": str(uuid.uuid4())}
        else:
            auth_msg = {"jsonrpc": "2.0", "method": "auth", "params": {"token": self.token}, "id": str(uuid.uuid4())}
        await self.ws.send(json.dumps(auth_msg))

    async def _receive_loop(self):
        """Receive messages and resolve pending futures."""
        try:
            async for message in self.ws:
                data = json.loads(message)
                msg_id = data.get("id")
                if msg_id and msg_id in self._pending:
                    future = self._pending.pop(msg_id)
                    if not future.done():
                        future.set_result(data)
        except Exception:
            # Resolve all pending with error
            for future in self._pending.values():
                if not future.done():
                    future.set_exception(MCPConnectionError("Connection closed"))
            raise

    async def call(self, method: str, params: Dict[str, Any] = None) -> Dict[str, Any]:
        """Call an MCP method and return the result."""
        if not self.ws:
            raise MCPConnectionError("Not connected")

        msg_id = str(uuid.uuid4())
        request = {
            "jsonrpc": "2.0",
            "method": method,
            "params": params or {},
            "id": msg_id
        }

        future = asyncio.get_event_loop().create_future()
        self._pending[msg_id] = future

        await self.ws.send(json.dumps(request))

        try:
            response = await asyncio.wait_for(future, timeout=self.timeout)
        except asyncio.TimeoutError:
            self._pending.pop(msg_id, None)
            raise MCPConnectionError(f"Timeout calling {method}")

        if "error" in response:
            error = response["error"]
            raise MCPConnectionError(f"MCP error: {error.get('message', 'Unknown error')} (code: {error.get('code')})")

        return response.get("result", {})

    async def disconnect(self):
        """Disconnect from MCP server."""
        if self._receive_task:
            self._receive_task.cancel()
            try:
                await self._receive_task
            except asyncio.CancelledError:
                pass
            except Exception:
                # Ignore other exceptions during cleanup
                pass
        if self.ws:
            try:
                await self.ws.close()
            except Exception:
                # Ignore websocket close errors (e.g., loop mismatch)
                pass
            self.ws = None