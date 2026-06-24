# Gateway Standard

AgentGatewayHost is the external access boundary for OpenClaw and other entry points.

External systems can use:

- `GET /agents`
- `POST /agents/{agentId}/message`

External systems must not:

- read project folders to discover agents
- call internal agents directly
- call workers directly
- call validators or gatekeepers directly

Every accepted external Agent message must pass through the Gateway QualityGate chain:

1. Execute the Public Agent.
2. Convert `AgentOutput` into `ReviewReport`.
3. Evaluate through Gatekeeper.
4. Return the Agent output only when the GateDecision is `Passed`.
5. Return `rejected`, `failed`, or `needs_human_approval` with gate details otherwise.

First version exposure:

```json
[
  {
    "id": "chief-engineer",
    "display_name": "机械总工程师",
    "role": "总调度、任务拆解、内部 Agent 协作、质量裁决",
    "mention": "@机械总工程师",
    "visibility": "Public"
  }
]
```

The chief engineer agent can internally coordinate mechanical-designer, cad-modeler, drawing-engineer, drawing-reviewer and error-diagnosis agents through AgentRegistry.
