# Unity MCP Guide

> Agent-facing guide for the live Unity MCP bridge. Use this when deciding between Unity-side tools, file edits, and batch scripts.

## Quick Load

- Prefer Unity MCP when the task depends on live Unity editor state: console inspection, script recompiles, menu-item execution, scene or prefab changes, component wiring, material edits, or quick smoke tests inside the open editor.
- Prefer normal file edits for C# source, markdown, instructions, plans, and other deterministic text changes.
- **Prefer Unity MCP `run_tests` as the primary test runner** when Unity is open and MCP is connected. This keeps the editor live so other MCP tools (console logs, recompile, scene inspection) remain available throughout the edit-compile-test loop.
- Fall back to `Tools/Run-UnityTests.ps1` for CI-like runs when MCP is unavailable, when you need authoritative `TestResults/*.xml` artifacts, or when Unity is not open.
- Do not hand-edit `.unity`, `.prefab`, or `.mat` YAML when an MCP editor operation can make the change safely through Unity serialization.

## Transport

- Copilot talks to the MCP stdio server, while Unity listens separately on `ws://localhost:8090/McpUnity`.
- If port 8090 is open but Unity MCP tools do not appear in chat, the Unity bridge may be alive while the MCP server registration is missing for the current chat session.

## Tool Map

| Need | Prefer | Notes |
|---|---|---|
| Console inspection or lightweight diagnostics | `mcp_mcp-unity_get_console_logs`, `mcp_mcp-unity_send_console_log` | Use for quick evidence gathering. Keep stack traces off unless the trace is the thing you need. |
| Script compile or quick test feedback | `mcp_mcp-unity_recompile_scripts`, `mcp_mcp-unity_run_tests` | **Primary test runner** when Unity is open. Keeps the editor live for the full edit-compile-test-inspect loop. |
| Scene, prefab, or component authoring | `mcp_mcp-unity_execute_menu_item`, `mcp_mcp-unity_create_prefab`, `mcp_mcp-unity_add_asset_to_scene`, `mcp_mcp-unity_update_component` | Best when the task depends on Unity serialization, hierarchy state, or menu-driven builders. |
| Material inspection or edits | `mcp_mcp-unity_get_material_info`, `mcp_mcp-unity_modify_material` | Prefer over manual `.mat` edits. |
| Unity package additions | `mcp_mcp-unity_add_package` | Prefer over blind manifest edits when the package is a normal Unity Package Manager dependency. |
| Several Unity-side operations in one step | `mcp_mcp-unity_batch_execute` | Use when multiple editor mutations should happen together. |

## Decision Rules

1. If the task needs live editor state, prefer Unity MCP.
2. If the task is a pure source or documentation edit, use normal file editing tools.
3. **For test runs when Unity is open and MCP is connected, prefer `mcp_mcp-unity_run_tests`** — it avoids the close-Unity/reopen-Unity cycle and keeps MCP tools available.
4. Fall back to `Tools/Run-UnityTests.ps1` when MCP is unavailable, when you need CI-like XML artifacts, or when Unity is not open.
5. If you need scene, prefab, or material mutation, prefer Unity MCP over raw YAML edits.
6. After making C# edits, use `mcp_mcp-unity_recompile_scripts` to verify compilation before running tests, then `mcp_mcp-unity_get_console_logs` to check for errors.

## Current Available Tool Surface

- `mcp_mcp-unity_get_console_logs`
- `mcp_mcp-unity_send_console_log`
- `mcp_mcp-unity_recompile_scripts`
- `mcp_mcp-unity_run_tests`
- `mcp_mcp-unity_execute_menu_item`
- `mcp_mcp-unity_create_prefab`
- `mcp_mcp-unity_add_asset_to_scene`
- `mcp_mcp-unity_update_component`
- `mcp_mcp-unity_get_material_info`
- `mcp_mcp-unity_modify_material`
- `mcp_mcp-unity_add_package`
- `mcp_mcp-unity_batch_execute`

## Repo Guardrails

- Keep `AGENT_TEST_RUNNING.md` as the authority for unattended EditMode and PlayMode verification.
- Keep `TASK_ROUTING.md` as the entry point for choosing the right code, scene, and test surfaces.
- Keep `ARCHITECTURE.md` aligned when the Unity-side tooling changes how agents are expected to interact with scenes, prefabs, or builders.