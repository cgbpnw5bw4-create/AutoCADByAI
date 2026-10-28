"""真实临时 PDF 的版面审核回归; 不读取项目输入或既有运行产物。

从平台根目录运行: python -B -m unittest discover -s tests/python -v
"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

import pymupdf


SOURCE = Path(__file__).resolve().parents[2] / "python" / "audit_drawings.py"
SPEC = importlib.util.spec_from_file_location("audit_drawings_under_test", SOURCE)
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)
MM = 72.0 / 25.4


def frame(page):
    """明确空白模板中的图框、标题栏与文字, 与每张图纸使用相同元素。"""
    page.draw_rect(pymupdf.Rect(25 * MM, 5 * MM, 415 * MM, 292 * MM))
    page.draw_rect(pymupdf.Rect(235 * MM, 236 * MM, 415 * MM, 292 * MM))
    page.draw_line((235 * MM, 250 * MM), (415 * MM, 250 * MM))
    page.insert_text((245 * MM, 260 * MM), "DRAWING", fontsize=10)


def pdf(path, draw=None, with_frame=True):
    path.parent.mkdir(parents=True, exist_ok=True)
    with pymupdf.open() as doc:
        page = doc.new_page(width=420 * MM, height=297 * MM)
        if with_frame:
            frame(page)
        if draw:
            draw(page)
        doc.save(str(path))


class DrawingAuditTests(unittest.TestCase):
    def audit_case(self, count, draw=None, template=True, with_frame=True):
        with tempfile.TemporaryDirectory(prefix="cad_pdf_audit_") as tmp:
            directory = Path(tmp)
            if template:
                pdf(directory / "_formats" / "format_A3.pdf")
            for i in range(count):
                pdf(directory / ("drawing_%d.pdf" % i), draw, with_frame)
            return AUDIT.audit(str(directory))

    def test_repeated_out_of_frame_lines_are_never_format_elements(self):
        for count in (1, 2):
            with self.subTest(drawings=count):
                result = self.audit_case(count, lambda p: p.draw_line((10 * MM, 40 * MM), (80 * MM, 40 * MM)))
                self.assertEqual(count, result["problem_count"])
                self.assertTrue(all(r["out_of_frame"] > 0 for r in result["drawings"]))

    def test_repeated_lines_over_title_block_are_never_format_elements(self):
        for count in (1, 2):
            with self.subTest(drawings=count):
                result = self.audit_case(count, lambda p: p.draw_line((260 * MM, 270 * MM), (350 * MM, 270 * MM)))
                self.assertEqual(count, result["problem_count"])
                self.assertTrue(all(r["over_title_block"] > 0 for r in result["drawings"]))

    def test_repeated_overlapping_text_is_never_format_elements(self):
        def overlap(page):
            page.insert_text((50 * MM, 100 * MM), "AAAAAA", fontsize=12)
            page.insert_text((50 * MM, 100 * MM), "BBBBBB", fontsize=12, color=(1, 0, 0))

        for count in (1, 2):
            with self.subTest(drawings=count):
                result = self.audit_case(count, overlap)
                self.assertEqual(count, result["problem_count"])
                self.assertTrue(all(r["text_overlap_count"] > 0 for r in result["drawings"]))

    def test_explicit_blank_template_is_exempt(self):
        result = self.audit_case(2)
        self.assertEqual(0, result["problem_count"])
        self.assertEqual(2, result["total"])
        self.assertEqual(["format_A3.pdf"], result["format_reference"])
        for row in result["drawings"]:
            self.assertFalse(row["format_missing"])
            self.assertEqual(0, row["out_of_frame"])
            self.assertEqual(0, row["over_title_block"])
            self.assertEqual(0, row["text_overlap_count"])

    def test_missing_template_is_an_explicit_problem_even_on_blank_page(self):
        result = self.audit_case(1, template=False, with_frame=False)
        self.assertEqual(1, result["problem_count"])
        self.assertTrue(result["problems"][0]["format_missing"])
        self.assertEqual(0, result["problems"][0]["out_of_frame"])
        self.assertEqual(0, result["problems"][0]["over_title_block"])

    def test_missing_template_never_hides_repeated_out_of_frame_lines(self):
        result = self.audit_case(2, lambda p: p.draw_line((10 * MM, 40 * MM), (80 * MM, 40 * MM)), template=False)
        self.assertEqual(2, result["problem_count"])
        self.assertTrue(all(r["format_missing"] and r["out_of_frame"] > 0 for r in result["drawings"]))

    def test_extent_readings_are_preserved(self):
        with tempfile.TemporaryDirectory(prefix="cad_pdf_audit_") as tmp:
            directory = Path(tmp)
            pdf(directory / "_formats" / "format_A3.pdf")
            drawing = directory / "dimension.pdf"
            pdf(drawing, lambda p: p.insert_text((60 * MM, 80 * MM), "123.45", fontsize=12))
            with pymupdf.open(str(drawing)) as doc:
                rect = doc[0].search_for("123.45")[0]
                x = (rect.x0 + rect.x1) / (2 * MM)
                y = 297 - (rect.y0 + rect.y1) / (2 * MM)
            (directory / "index.json").write_text(json.dumps([
                {"pdf": drawing.name, "extent_marks": [[x, y, 123.45]]}
            ]), encoding="utf-8")
            result = AUDIT.audit(str(directory))
            self.assertEqual(0, result["problem_count"])
            mark = result["extents"][0]["marks"][0]
            self.assertEqual(123.45, mark["geo"])
            self.assertEqual(123.45, mark["read"])
            self.assertEqual("123.45", mark["text"])
            self.assertAlmostEqual(0.025, mark["tol"])


if __name__ == "__main__":
    unittest.main()
