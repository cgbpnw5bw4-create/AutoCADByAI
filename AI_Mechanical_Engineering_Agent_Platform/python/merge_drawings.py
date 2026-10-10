"""全套图纸合订为一份 PDF: 目录页 + 装配图 + 零件图 (按目录分组), 带书签。

用法: python python/merge_drawings.py <运行目录>
输入: <运行目录>/run.json (标题) 与 drawings/index.json (内核出图时写入)
输出: <运行目录>/exports/<标题>_全套图纸.pdf
来源: CFC-650 铜箔清洗机 scripts/merge_pdf.py (108 页合订本), 改为按运行目录调用。
"""
import datetime
import json
import os
import sys

import pymupdf

sys.stdout.reconfigure(encoding="utf-8")
W, H = 1190.55, 841.89                    # A3 横向 (pt)
MM = 72 / 25.4
FONT = "hei"                              # 黑体: 内置 china-s 的西文为全角宽度, 图号会挤进名称列
FONTFILE = r"C:\Windows\Fonts\simhei.ttf"
PER_COL, COLS = 42, 2


def main(run_dir):
    with open(os.path.join(run_dir, "run.json"), encoding="utf-8") as f:
        title = json.load(f).get("title") or os.path.basename(run_dir)
    drw = os.path.join(run_dir, "drawings")
    with open(os.path.join(drw, "index.json"), encoding="utf-8") as f:
        rows = json.load(f)
    for r in rows:
        r["pdf_path"] = os.path.join(drw, r["pdf"])
        r["dir"] = os.path.dirname(r["file"]) or "零件图"
    missing = [r["pdf"] for r in rows if not os.path.exists(r["pdf_path"])]
    if missing:
        sys.exit("缺少 PDF: " + ", ".join(missing))
    asm = sorted([r for r in rows if r["kind"] == "assembly"], key=lambda r: r["code"])
    parts = [r for r in rows if r["kind"] != "assembly"]
    groups = sorted(set(r["dir"] for r in parts))
    ordered = asm + [r for g in groups for r in sorted(parts, key=lambda r: r["code"]) if r["dir"] == g]
    if not ordered:
        sys.exit("没有图纸")

    n_toc = -(-len(ordered) // (PER_COL * COLS))
    out = pymupdf.open()
    for _ in range(n_toc):
        out.new_page(width=W, height=H)
    page_no = n_toc + 1
    for r in ordered:
        r["page"] = page_no
        page_no += 1
    font_ok = os.path.exists(FONTFILE)
    for p in range(n_toc):
        pg = out[p]
        kw = {"fontname": FONT}
        if font_ok:
            pg.insert_font(fontname=FONT, fontfile=FONTFILE)
        else:
            kw = {"fontname": "china-s"}
        pg.draw_rect(pymupdf.Rect(25 * MM, 10 * MM, W - 10 * MM, H - 10 * MM), width=1.0)
        pg.insert_text((32 * MM, 24 * MM), title + "  全套图纸", fontsize=20, **kw)
        pg.insert_text((32 * MM, 32 * MM), "共 %d 张: 装配图 %d, 零件图 %d    第一角投影, GB 图框    %s    目录第 %d/%d 页"
                       % (len(ordered), len(asm), len(parts), datetime.date.today().isoformat(), p + 1, n_toc), fontsize=10, **kw)
        colw = (W - 45 * MM) / COLS
        for c in range(COLS):
            x0 = 32 * MM + c * colw
            y = 42 * MM
            chunk = ordered[(p * COLS + c) * PER_COL:(p * COLS + c + 1) * PER_COL]
            if not chunk:
                continue
            for t, dx in [("页码", 0), ("图号", 14), ("名称", 50), ("图幅", 118), ("比例", 132)]:
                pg.insert_text((x0 + dx * MM, y), t, fontsize=9, **kw)
            pg.draw_line((x0, y + 1.5 * MM), (x0 + 150 * MM, y + 1.5 * MM), width=0.6)
            prev = None
            for r in chunk:
                y += 5.5 * MM
                grp = "装配图" if r["kind"] == "assembly" else r["dir"]
                if grp != prev and prev is not None:
                    pg.draw_line((x0, y - 4.2 * MM), (x0 + 150 * MM, y - 4.2 * MM), width=0.3, color=(0.6, 0.6, 0.6))
                prev = grp
                name = r["name"] if len(r["name"]) <= 22 else r["name"][:21] + "…"
                for t, dx in [(str(r["page"]), 0), (r["code"], 14), (name, 50), (r["size"], 118), (r["scale"], 132)]:
                    pg.insert_text((x0 + dx * MM, y), t, fontsize=8.5, **kw)

    for r in ordered:
        src = pymupdf.open(r["pdf_path"])
        out.insert_pdf(src)
        src.close()

    toc = [[1, "图纸目录", 1]]
    if asm:
        toc.append([1, "装配图", asm[0]["page"]])
        for r in asm:
            toc.append([2, "%s %s" % (r["code"], r["name"]), r["page"]])
    if parts:
        first = next(r for r in ordered if r["kind"] != "assembly")
        toc.append([1, "零件图", first["page"]])
        for g in groups:
            gr = [r for r in ordered if r["kind"] != "assembly" and r["dir"] == g]
            toc.append([2, g.replace("_", " "), gr[0]["page"]])
            for r in gr:
                toc.append([3, "%s %s" % (r["code"], r["name"]), r["page"]])
    out.set_toc(toc)
    out.set_metadata({"title": title + " 全套图纸", "creator": "AutoCADByAI (SolidWorks + PyMuPDF)"})
    os.makedirs(os.path.join(run_dir, "exports"), exist_ok=True)
    safe = "".join(ch if ch not in '\\/:*?"<>|' else "_" for ch in title).replace(" ", "_")
    dst = os.path.join(run_dir, "exports", safe + "_全套图纸.pdf")
    out.subset_fonts()
    out.save(dst, garbage=3, deflate=True)
    print("页数 %d (目录 %d + 图纸 %d)  %.1f MB -> %s" % (len(out), n_toc, len(ordered), os.path.getsize(dst) / 1048576, dst))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("用法: python python/merge_drawings.py <运行目录>")
    main(sys.argv[1])
