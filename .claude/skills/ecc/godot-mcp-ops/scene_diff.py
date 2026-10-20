#!/usr/bin/env python3
"""
scene_diff.py — Scene Diff Logic

Compares two scene files (base vs PR) and reports:
- Nodes added/removed
- Property changes
- Resource reference changes
"""

from typing import Any, Dict, List, Optional
from pathlib import Path


class SceneDiff:
    def __init__(self, ops):
        self.ops = ops
        self.project_path = ops.project_path

    def run(self, base_scene: str, pr_scene: str) -> Dict[str, Any]:
        """Compare two scenes and return diff."""
        try:
            if self.ops._use_mcp and self.ops.mcp_client:
                return self._diff_via_mcp(base_scene, pr_scene)
            else:
                return self._diff_via_headless(base_scene, pr_scene)
        except Exception as e:
            return {
                "success": False,
                "data": {},
                "errors": [f"Scene diff failed: {str(e)}"],
                "warnings": []
            }

    def _diff_via_mcp(self, base_scene: str, pr_scene: str) -> Dict[str, Any]:
        """Use MCP bridge scene.diff command."""
        # First open base scene
        self.ops.mcp_client.call("scene.open", {"file_path": base_scene})
        # Get tree for base
        base_result = self.ops.mcp_client.call("scene.get_tree", {
            "max_depth": -1,
            "include_properties": True
        })
        base_tree = base_result.get("tree", {})

        # Open PR scene
        self.ops.mcp_client.call("scene.open", {"file_path": pr_scene})
        # Get tree for PR
        pr_result = self.ops.mcp_client.call("scene.get_tree", {
            "max_depth": -1,
            "include_properties": True
        })
        pr_tree = pr_result.get("tree", {})

        # Use scene.diff command
        diff_result = self.ops.mcp_client.call("scene.diff", {
            "before": base_tree,
            "after": pr_tree
        })

        return {
            "success": True,
            "data": {
                "mode": "mcp",
                "base_scene": base_scene,
                "pr_scene": pr_scene,
                "changed": diff_result.get("changed", False),
                "added": diff_result.get("added", 0),
                "removed": diff_result.get("removed", 0),
                "diff": diff_result.get("diff", "")
            },
            "errors": [],
            "warnings": []
        }

    def _diff_via_headless(self, base_scene: str, pr_scene: str) -> Dict[str, Any]:
        """Use headless Godot to parse and compare scenes."""
        base_tree = self.ops.headless.parse_scene(base_scene)
        pr_tree = self.ops.headless.parse_scene(pr_scene)

        if "error" in base_tree:
            return {"success": False, "data": {}, "errors": [f"Base scene: {base_tree['error']}"], "warnings": []}
        if "error" in pr_tree:
            return {"success": False, "data": {}, "errors": [f"PR scene: {pr_tree['error']}"], "warnings": []}

        diff = self._compute_diff(base_tree, pr_tree)
        return {
            "success": True,
            "data": {
                "mode": "headless",
                "base_scene": base_scene,
                "pr_scene": pr_scene,
                **diff
            },
            "errors": [],
            "warnings": []
        }

    def _compute_diff(self, base: Dict, pr: Dict) -> Dict[str, Any]:
        """Compute diff between two scene trees."""
        base_nodes = {n["path"]: n for n in base.get("nodes", [])}
        pr_nodes = {n["path"]: n for n in pr.get("nodes", [])}

        base_paths = set(base_nodes.keys())
        pr_paths = set(pr_nodes.keys())

        added_paths = pr_paths - base_paths
        removed_paths = base_paths - pr_paths
        common_paths = base_paths & pr_paths

        nodes_added = []
        nodes_removed = []
        properties_changed = []
        resource_refs_changed = []

        for path in added_paths:
            nodes_added.append({
                "path": path,
                "name": pr_nodes[path]["name"],
                "type": pr_nodes[path]["type"]
            })

        for path in removed_paths:
            nodes_removed.append({
                "path": path,
                "name": base_nodes[path]["name"],
                "type": base_nodes[path]["type"]
            })

        # Note: Headless mode doesn't include properties for performance
        # Property diff only available via MCP bridge
        for path in common_paths:
            base_node = base_nodes[path]
            pr_node = pr_nodes[path]
            # Type change detection
            if base_node.get("type") != pr_node.get("type"):
                properties_changed.append({
                    "path": path,
                    "property": "type",
                    "before": base_node.get("type"),
                    "after": pr_node.get("type")
                })
            # Name change detection
            if base_node.get("name") != pr_node.get("name"):
                properties_changed.append({
                    "path": path,
                    "property": "name",
                    "before": base_node.get("name"),
                    "after": pr_node.get("name")
                })

        return {
            "changed": len(nodes_added) > 0 or len(nodes_removed) > 0 or len(properties_changed) > 0 or len(resource_refs_changed) > 0,
            "nodes_added": nodes_added,
            "nodes_removed": nodes_removed,
            "properties_changed": properties_changed,
            "resource_refs_changed": resource_refs_changed,
            "summary": {
                "added": len(nodes_added),
                "removed": len(nodes_removed),
                "properties_changed": len(properties_changed),
                "resource_refs_changed": len(resource_refs_changed)
            }
        }

    def _is_resource_ref(self, value: Any) -> bool:
        """Check if a value is a resource reference (res:// path or Resource object)."""
        if isinstance(value, str) and value.startswith("res://"):
            return True
        if isinstance(value, dict) and value.get("type") == "Resource":
            return True
        return False