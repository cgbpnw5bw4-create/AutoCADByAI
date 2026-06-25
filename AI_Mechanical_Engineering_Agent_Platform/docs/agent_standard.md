# Agent Standard

An Agent is not just a chat persona.

An Agent is an execution role with:

- stable `Id`
- display `Name`
- `Role`
- `Description`
- `Visibility`
- structured `AgentInput`
- structured `AgentOutput`
- audit logs
- explicit next-agent recommendation when needed

Visibility rules:

- Public: can be exposed by AgentGatewayHost
- Internal: can be called only inside the platform
- Protected: can be called only by system workflows such as gatekeepers or validators

Agents decide, coordinate and produce structured outputs. They do not directly operate CAD software. CAD execution belongs to Workers.

## Runtime-backed Agents

V0.7 allows only `chief-engineer` to use real Microsoft runtime / LLM integration.

Runtime-backed output must still be converted into platform `AgentOutput`. The model can provide task understanding and internal collaboration advice, but it cannot:

- change Agent visibility
- expose Internal Agents through Gateway
- call Workers directly
- operate SolidWorks or AutoCAD
- modify files
- bypass WorkflowEngine
- bypass QualityGate

Internal Agents remain Module Agent or Mock Agent implementations until a later version explicitly promotes them.
