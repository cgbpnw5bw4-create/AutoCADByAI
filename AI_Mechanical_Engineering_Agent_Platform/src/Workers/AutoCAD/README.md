# AutoCAD Worker

This worker layer is reserved for future AutoCAD automation through API, SDK, COM, DXF/DWG parsers, or MCP bridges.

Current scope:

- provide `IAutoCADWorker`
- provide `FakeAutoCADWorker`
- avoid any real AutoCAD process, COM object, license, or file operation

Future responsibilities:

- read DWG/DXF inputs
- extract layers, lines, circles and dimensions
- convert drawing data into `CADModelSpec`
