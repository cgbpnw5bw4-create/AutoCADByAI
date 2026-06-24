# CADModeling Module

Turns accepted design intent into `BuildSpec` and plans CAD Worker execution.

Boundaries:

- Agents create modeling plans and Worker call plans.
- Skills build structured `BuildSpec`.
- Workers perform future SolidWorks, AutoCAD or other CAD execution.
- Agents never call CAD APIs, SDKs or COM directly.
