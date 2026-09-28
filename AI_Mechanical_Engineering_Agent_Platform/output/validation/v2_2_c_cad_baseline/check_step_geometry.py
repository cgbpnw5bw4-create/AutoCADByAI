"""离线核对 STEP 比较器：只读真实探针，内存构造反例，不调用 CAD 或修改旧报告。

入口：python -X utf8 output/validation/v2_2_c_cad_baseline/check_step_geometry.py
结果输出到标准输出；退出 0 表示全部明确列出的预期行为通过。
"""

from __future__ import annotations

from copy import deepcopy
import json
from pathlib import Path
import runpy


def main() -> int:
    directory = Path(__file__).resolve().parent
    collector = runpy.run_path(str(directory / "collect_candidates.py"))
    paths = {
        "fillet_native": directory / "collection-20260914_092030-8b5acc4b/02-probe.json",
        "fillet_step": directory / "collection-20260914_092030-8b5acc4b/02-step-probe.json",
        "wrong_negative_native": directory / "collection-20260914_085332-1b6f5dda/01-probe.json",
        "wrong_negative_step": directory / "collection-20260914_085332-1b6f5dda/01-step-probe.json",
    }
    fingerprints = {name: collector["fingerprint"](path) for name, path in paths.items()}
    geometries = {name: collector["read_json"](path)["geometry"] for name, path in paths.items()}
    native, step = geometries["fillet_native"], geometries["fillet_step"]
    checks = []

    def check(name: str, expected: bool, left: dict, right: dict) -> None:
        try:
            result = collector["compare_geometry"](left, right)
            record = {"name": name, "expected": expected, "actual": result["passed"],
                      "categories": {key: value["matched"] for key, value in result.items() if isinstance(value, dict)},
                      "excluded_step_circle_count": len(result["circles"]["step_excluded_edges"]),
                      "unexpected_step_circle_indices": [value["edge"]["Index"] for value in result["circles"]["unexpected_in_step"]]}
        except (RuntimeError, ValueError, TypeError) as error:
            record = {"name": name, "expected": expected, "actual": False, "rejection": str(error)}
        record["check_passed"] = record["actual"] is expected
        checks.append(record)

    def modified_step(name: str, mutate) -> None:
        changed = deepcopy(step)
        mutate(changed)
        check(name, False, native, changed)

    def first_seam(geometry: dict) -> dict:
        return next(edge for edge in geometry["Edges"] if edge["Kind"] == "circle"
                    and edge["AdjacentSurfaceKinds"] == ["other", "other"])

    check("真实圆角与 STEP 环面分割缝一致", True, native, step)
    check("旧负 X 导出其他活动文档仍拒绝", False,
          geometries["wrong_negative_native"], geometries["wrong_negative_step"])
    modified_step("额外圆柱不得被忽略", lambda value: value["Cylinders"].append({
        "DiameterMm": 4, "OriginXmm": 33, "OriginYmm": 0, "OriginZmm": 0, "AxisX": 0, "AxisY": 1, "AxisZ": 0}))
    modified_step("漏掉一个实测孔圆柱仍拒绝", lambda value: value.update(
        Cylinders=[item for item in value["Cylinders"] if item["OriginXmm"] < 0]))
    modified_step("漏掉同一位置全部孔口圆仍拒绝", lambda value: value.update(
        Edges=[edge for edge in value["Edges"] if not (edge["Kind"] == "circle"
               and edge["AnchorXMm"] == 20 and edge["AnchorYMm"] == 0)]))
    modified_step("孔口错误法向仍拒绝", lambda value: next(edge for edge in value["Edges"]
                  if edge["Kind"] == "circle").update(Direction={"X": 1, "Y": 0, "Z": 0}))
    modified_step("圆柱错误轴向仍拒绝", lambda value: value["Cylinders"][0].update(AxisX=1, AxisY=0))
    modified_step("环面缝错误半径仍拒绝", lambda value: first_seam(value).update(RadiusMm=1.25))
    modified_step("环面缝错误轴向位置仍拒绝", lambda value: first_seam(value).update(AnchorYMm=9.1))
    modified_step("环面缝错误径向位置仍拒绝", lambda value: first_seam(value).update(AnchorZMm=-6.1))
    modified_step("环面缝非切向法向仍拒绝", lambda value: first_seam(value).update(Direction={"X": 0, "Y": 1, "Z": 0}))
    modified_step("未知额外圆边不得任意排除", lambda value: value["Edges"].append({
        "Index": 999, "Kind": "circle", "AnchorXMm": 33, "AnchorYMm": 9, "AnchorZMm": 6,
        "RadiusMm": 1, "Direction": {"X": 1, "Y": 0, "Z": 0}, "AdjacentSurfaceKinds": ["other", "other"]}))
    no_fillet = deepcopy(native)
    no_fillet["FeatureTypes"] = [value for value in native["FeatureTypes"] if value != "Fillet"]
    check("缺少原生 Fillet 不接受推导环面缝", False, no_fillet, step)
    changed_pair = deepcopy(native)
    for edge in changed_pair["Edges"]:
        if edge["Kind"] == "circle" and edge["RadiusMm"] == 6:
            edge["AnchorYMm"] = 10.5
    check("内外圈轴向差不等于小半径则拒绝", False, changed_pair, step)
    duplicates = deepcopy(step)
    duplicates["Cylinders"] += deepcopy(step["Cylinders"])
    duplicates["Edges"] += deepcopy(step["Edges"])
    check("STEP 合法拓扑重复可去重", True, native, duplicates)
    same_axis = deepcopy(step)
    for cylinder in same_axis["Cylinders"]:
        cylinder["OriginYmm"] += 7
        cylinder["AxisY"] *= -1
    check("同一无向轴线允许原点沿轴移动及反向", True, native, same_axis)
    unchanged = fingerprints == {name: collector["fingerprint"](path) for name, path in paths.items()}
    report = {"scope": "离线比较器行为检查，不替代真实 CAD 或生产验收", "sources": fingerprints,
              "source_bytes_unchanged": unchanged, "checks": checks,
              "passed": unchanged and all(value["check_passed"] for value in checks)}
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
