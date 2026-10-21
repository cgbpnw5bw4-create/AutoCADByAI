"""工程图 PDF 版面审核: 内容超出图框 / 图线压标题栏 / 文字互相重叠 / 图线压技术要求; 并读出外形尺寸的标注值。

用法: python python/audit_drawings.py <drawings 目录> [--json <输出.json>]
来源: CFC-650 铜箔清洗机 scripts/audit_pdf.py (106 张图 0 项), 改为按运行目录调用并输出 JSON。

格式元素 (图框、标题栏) 的识别:
  1. drawings/_formats/format_<图幅>.pdf (内核导出的空白图纸) 中出现的元素;
  2. 同图幅 PDF 不少于 2 份时, 出现于 ≥80% 份数中的元素。
其余为内容。

外形尺寸 (JSON 的 extents): 图纸目录 index.json 的 extent_marks 记有每个外形尺寸文字在图纸上的位置与应有值
(内核出图时记录; 出图中不向 SolidWorks 读回尺寸值, 读的是 PDF 上实际印出的数)。
在 PDF 上取离该位置最近 (8 mm 内) 的数字文字作为标注值; tol = 显示精度的一半 + 0.02 mm, 由内核 Drw.ApplyExtentReadings 核对。
"""
import argparse
import collections
import glob
import json
import os
import re
import sys

import pymupdf

sys.stdout.reconfigure(encoding="utf-8")
PT = 25.4 / 72.0
# GB/T 14689 留装订边: 左 a=25, 其余 c=5 (A3/A4) 或 10 (A0~A2)
FRAME = {"A3": (25, 5), "A2": (25, 10), "A1": (25, 10), "A0": (25, 10)}
# 图框左侧装订边内的登记栏文字属于图纸格式
REG = {"零 件 代 号", "借(通)用件登记", "描图", "描校", "旧底图总号", "底图总号", "签字", "日期"}


def size_of(w, h):
    w, h = round(w * PT), round(h * PT)
    return {(420, 297): "A3", (594, 420): "A2", (841, 594): "A1", (1189, 841): "A0"}.get((w, h), "%dx%d" % (w, h))


def items(page):
    out = []
    for d in page.get_drawings():
        for it in d["items"]:
            if it[0] == "l":
                xs = [it[1].x, it[2].x]; ys = [it[1].y, it[2].y]
            elif it[0] == "re":
                r = it[1]; xs = [r.x0, r.x1]; ys = [r.y0, r.y1]
            elif it[0] == "qu":
                q = it[1]; xs = [q.ul.x, q.ur.x, q.ll.x, q.lr.x]; ys = [q.ul.y, q.ur.y, q.ll.y, q.lr.y]
            elif it[0] == "c":
                xs = [p.x for p in it[1:5]]; ys = [p.y for p in it[1:5]]
            else:
                continue
            if max(xs) - min(xs) < 1e-3 and max(ys) - min(ys) < 1e-3:
                continue   # 导出器产生的零长度线 (不可见)
            out.append(("g", min(xs) * PT, min(ys) * PT, max(xs) * PT, max(ys) * PT, ""))
    for b in page.get_text("dict")["blocks"]:
        for l in b.get("lines", []):
            for s in l["spans"]:
                if s["text"].strip():
                    x0, y0, x1, y1 = s["bbox"]
                    out.append(("t", x0 * PT, y0 * PT, x1 * PT, y1 * PT, s["text"].strip()))
    return out


def key(it):
    if it[0] == "t":   # 文字: 按内容 + 3 mm 网格定位 (字形度量在各 PDF 间有 ~1 mm 抖动)
        return ("t", round(it[1] / 3), round(it[2] / 3), it[5])
    return (it[0], round(it[1]), round(it[2]), round(it[3]), round(it[4]), "")


def audit(drw_dir):
    pdfs = sorted(f for f in glob.glob(os.path.join(drw_dir, "**", "*.pdf"), recursive=True)
                  if os.sep + "_formats" + os.sep not in f)
    data = {}
    for f in pdfs:
        p = pymupdf.open(f)[0]
        data[f] = (size_of(p.rect.width, p.rect.height), p.rect.width * PT, p.rect.height * PT, items(p))

    by = collections.defaultdict(list)
    for f, (sz, W, H, its) in data.items():
        by[sz].append(f)
    fmt = {}
    for sz, fs in by.items():
        keys = set()
        ref = os.path.join(drw_dir, "_formats", "format_%s.pdf" % sz)
        if os.path.exists(ref):
            keys |= set(key(i) for i in items(pymupdf.open(ref)[0]))
        if len(fs) >= 2:
            cnt = collections.Counter()
            for f in fs:
                cnt.update(set(key(i) for i in data[f][3]))
            need = max(2, int(0.8 * len(fs)))
            keys |= {k for k, c in cnt.items() if c >= need}
        fmt[sz] = keys

    rows = []
    for f, (sz, W, H, its) in data.items():
        a, c = FRAME.get(sz, (25, 10))
        fx0, fy0, fx1, fy1 = a, c, W - c, H - c                       # 内框 (PDF 坐标 y 向下)
        tb = (fx1 - 180, fy1 - 56, fx1, fy1)                         # 标题栏 180×56
        content = [i for i in its if key(i) not in fmt[sz] and not (i[0] == "t" and i[5] in REG and i[1] < a)]
        out, tbx = [], []
        for i in content:
            _, x0, y0, x1, y1, t = i
            if x0 < fx0 - 0.5 or y0 < fy0 - 0.5 or x1 > fx1 + 0.5 or y1 > fy1 + 0.5:
                out.append(i)
            elif i[0] == "g" and x1 > tb[0] + 0.5 and y1 > tb[1] + 0.5:
                tbx.append(i)
        tx = [i for i in content if i[0] == "t"]
        ov = []
        for m in range(len(tx)):
            for n in range(m + 1, len(tx)):
                A, B = tx[m], tx[n]
                w = min(A[3], B[3]) - max(A[1], B[1]); h = min(A[4], B[4]) - max(A[2], B[2])
                if w > 0 and h > 0:
                    sm = min((A[3] - A[1]) * (A[4] - A[2]), (B[3] - B[1]) * (B[4] - B[2]))
                    if sm > 0 and w * h > 0.3 * sm:
                        ov.append(A[5] + "/" + B[5])
        nb = []
        hd = [i for i in tx if i[5] == "技术要求"]
        if hd:
            h0 = hd[0]; blk = [h0]
            for i in sorted(tx, key=lambda q: q[2]):
                if abs(i[1] - h0[1]) < 3 and i[2] > blk[-1][2] and i[2] - blk[-1][4] < 4:
                    blk.append(i)
            bx0, by0, bx1, by1 = min(i[1] for i in blk), min(i[2] for i in blk), max(i[3] for i in blk), max(i[4] for i in blk)
            nb = [i for i in content if i[0] == "g" and i[3] > bx0 and i[1] < bx1 and i[4] > by0 and i[2] < by1]
        rows.append({
            "file": os.path.relpath(f, drw_dir).replace("\\", "/"), "size": sz,
            "out_of_frame": len(out), "out_of_frame_text": [i[5] for i in out if i[0] == "t"][:6],
            "over_title_block": len(tbx), "text_overlaps": ov[:6], "text_overlap_count": len(ov),
            "lines_over_notes": len(nb),
        })
    probs = [r for r in rows if r["out_of_frame"] or r["over_title_block"] or r["text_overlap_count"] or r["lines_over_notes"]]
    return {"total": len(rows), "problem_count": len(probs), "by_size": {k: len(v) for k, v in by.items()},
            "format_reference": sorted(os.path.basename(p) for p in glob.glob(os.path.join(drw_dir, "_formats", "*.pdf"))),
            "problems": probs, "drawings": rows, "extents": read_extents(drw_dir, data)}


NUM = re.compile(r"\d+(?:\.\d+)?")
NEAR = 8.0   # mm: 标注文字中心离记录位置的最大距离


def read_extents(drw_dir, data):
    """按 index.json 的 extent_marks ([[x, y, 应有值], …], mm, 图纸坐标原点左下) 在各 PDF 上读出外形尺寸标注值。"""
    idx = os.path.join(drw_dir, "index.json")
    if not os.path.exists(idx):
        return []
    with open(idx, encoding="utf-8") as f:
        rows = json.load(f)
    pages = {os.path.normcase(os.path.abspath(k)): v for k, v in data.items()}
    out = []
    for r in rows:
        marks = r.get("extent_marks")
        if not marks:
            continue
        page = pages.get(os.path.normcase(os.path.abspath(os.path.join(drw_dir, r["pdf"]))))
        res = []
        for m in marks:
            x, y, geo = (list(m) + [None, None, None])[:3]
            hit = None
            if page is not None and x is not None and y is not None:
                H = page[2]
                mx, my = x, H - y                                   # PDF 坐标 y 向下
                for it in page[3]:
                    if it[0] != "t":
                        continue
                    num = NUM.search(it[5])
                    if not num:
                        continue
                    d = ((it[1] + it[3]) / 2 - mx) ** 2 + ((it[2] + it[4]) / 2 - my) ** 2
                    if d <= NEAR ** 2 and (hit is None or d < hit[0]):
                        hit = (d, num.group(0), it[5])
            if hit is None:
                res.append({"geo": geo, "read": None, "text": None, "dist": None, "tol": None})
            else:
                dec = len(hit[1].split(".")[1]) if "." in hit[1] else 0
                res.append({"geo": geo, "read": float(hit[1]), "text": hit[2], "dist": round(hit[0] ** 0.5, 2),
                            "tol": round(0.5 * 10 ** -dec + 0.02, 4)})
        out.append({"file": r["pdf"], "marks": res})
    return out


def main():
    ap = argparse.ArgumentParser(description="工程图 PDF 版面审核")
    ap.add_argument("drawings")
    ap.add_argument("--json")
    a = ap.parse_args()
    r = audit(a.drawings)
    print("PDF %d 张 %s, 有问题 %d 张" % (r["total"], r["by_size"], r["problem_count"]))
    for p in r["problems"]:
        print("  %s [%s] 出框 %d %s | 压标题栏 %d | 文字重叠 %d %s | 压技术要求 %d" % (
            p["file"], p["size"], p["out_of_frame"], p["out_of_frame_text"], p["over_title_block"],
            p["text_overlap_count"], p["text_overlaps"], p["lines_over_notes"]))
    ms = [m for e in r["extents"] for m in e["marks"]]
    print("外形尺寸 %d 个, 读不出 %d 个" % (len(ms), sum(1 for m in ms if m["read"] is None)))
    for e in r["extents"]:
        for m in e["marks"]:
            if m["read"] is None or abs(m["read"] - m["geo"]) > m["tol"]:
                print("  %s 外形尺寸 应为 %s, 读出 %s" % (e["file"], m["geo"], m["text"]))
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    main()
