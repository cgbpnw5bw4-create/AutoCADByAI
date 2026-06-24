# SolidWorks Worker

This worker layer is reserved for future SolidWorks automation through API, SDK, COM, or MCP bridges.

Current scope:

- provide `ISolidWorksWorker`
- provide `FakeSolidWorksWorker`
- avoid any real SolidWorks process, COM object, license, or file operation

Future responsibilities:

- SolidWorks modeling
- drawing generation
- STEP/PDF export
- model metadata reading
