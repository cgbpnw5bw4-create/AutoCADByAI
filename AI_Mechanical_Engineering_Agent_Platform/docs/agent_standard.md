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
