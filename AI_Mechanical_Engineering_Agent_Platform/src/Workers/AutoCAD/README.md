# AutoCAD Worker

本目录是未来 AutoCAD 执行层的预留边界。当前只保留接口和假实现，不连接真实 AutoCAD，不调用 COM，不消耗许可证，也不读取真实 DWG / DXF 文件。

当前范围：

- 提供 `IAutoCADWorker`。
- 提供 `FakeAutoCADWorker`。
- 避免任何真实 AutoCAD 进程、COM 对象、许可证或文件操作。

未来职责：

- 读取 DWG / DXF 输入。
- 提取图层、线段、圆和尺寸标注。
- 将图纸数据转换为 `CADModelSpec`。
