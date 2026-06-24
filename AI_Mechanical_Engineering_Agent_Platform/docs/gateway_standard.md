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
2. Let chief-engineer route internally through InternalAgentRouter when needed.
3. Convert `AgentOutput` and any `InternalCollaborationReport` into `ReviewReport`.
4. Evaluate through Gatekeeper.
5. Return the Agent output only when the GateDecision is `Passed`.
6. Return `rejected`, `failed`, or `needs_human_approval` with gate details otherwise.

V0.2 exposes only `chief-engineer` externally. `mechanical-designer`, `cad-modeler`, `drawing-engineer`, `drawing-reviewer`, and `error-diagnosis` remain Internal and must not be invoked directly through Gateway.

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
