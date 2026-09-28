"""从既有样例生成带符号方向的独立诊断输入，保留原文件。"""
import copy
import json
from pathlib import Path

root = Path(__file__).resolve().parents[3]
examples = root / "examples"
linear = json.loads((examples / "feature_pipeline_plate_linear_pattern.json").read_text(encoding="utf-8-sig"))
circular = json.loads((examples / "feature_pipeline_plate_circular_pattern.json").read_text(encoding="utf-8-sig"))

for name, source in [
    ("linear_opposite_edge", linear),
    ("linear_negative", linear),
    ("circular_positive_quarter", circular),
    ("circular_negative_quarter", circular),
]:
    data = copy.deepcopy(source)
    model = data["cad_model_spec"]
    model["model_id"] = "v2_2_c_pattern_" + name
    parameters = model["features"][-1]["parameters"]
    if name == "linear_opposite_edge":
        criteria = json.loads(parameters["direction_selection"])
        criteria["anchor_z_mm"] = -30
        parameters["direction_selection"] = json.dumps(criteria)
    elif name == "linear_negative":
        parameters.update(direction="-x", instance_count="3", spacing_mm="15")
        model["sketches"][-1]["entities"][0]["parameters"]["center_x_mm"] = "30"
    else:
        parameters.update(axis="+y" if "positive" in name else "-y", instance_count="3", angle_deg="90")
    path = examples / (model["model_id"] + ".json")
    if path.exists():
        raise RuntimeError(f"拒绝覆盖已有诊断输入：{path}")
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(path)
