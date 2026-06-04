"""Convert a Markdown file (with local images) to PDF via Playwright."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import markdown
from playwright.sync_api import sync_playwright


def build_html(md_text: str, base_dir: Path) -> str:
    body = markdown.markdown(
        md_text,
        extensions=["tables", "fenced_code", "sane_lists"],
    )
    base_uri = base_dir.resolve().as_uri() + "/"
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<base href="{base_uri}">
<style>
@page {{ margin: 18mm 15mm; }}
body {{
  font-family: "Microsoft YaHei", "PingFang SC", "Segoe UI", sans-serif;
  font-size: 11pt;
  line-height: 1.55;
  color: #1a1a1a;
  max-width: 180mm;
  margin: 0 auto;
}}
h1 {{ font-size: 20pt; border-bottom: 1px solid #ddd; padding-bottom: 0.3em; }}
h2 {{ font-size: 15pt; margin-top: 1.4em; }}
h3 {{ font-size: 12.5pt; }}
img {{ max-width: 100%; height: auto; display: block; margin: 0.8em auto; page-break-inside: avoid; }}
table {{ border-collapse: collapse; width: 100%; margin: 0.8em 0; font-size: 10pt; }}
th, td {{ border: 1px solid #ccc; padding: 6px 8px; text-align: left; vertical-align: top; }}
th {{ background: #f0f0f0; }}
code {{ background: #f4f4f4; padding: 1px 4px; font-size: 0.92em; }}
pre {{ background: #f4f4f4; padding: 10px; overflow-x: auto; font-size: 9pt; }}
pre code {{ background: none; padding: 0; }}
blockquote {{ border-left: 4px solid #ccc; margin: 0.8em 0; padding-left: 1em; color: #444; }}
hr {{ border: none; border-top: 1px solid #ddd; margin: 1.5em 0; }}
em {{ color: #555; }}
a {{ color: #0e639c; }}
h1, h2, h3 {{ page-break-after: avoid; }}
</style>
</head>
<body>
{body}
</body>
</html>
"""


def convert(md_path: Path, pdf_path: Path | None = None) -> Path:
    md_path = md_path.resolve()
    if not md_path.is_file():
        raise FileNotFoundError(md_path)

    pdf_path = (pdf_path or md_path.with_suffix(".pdf")).resolve()
    base_dir = md_path.parent
    md_text = md_path.read_text(encoding="utf-8")
    html = build_html(md_text, base_dir)

    preview_html = base_dir / f"_{md_path.stem}_preview.html"
    preview_html.write_text(html, encoding="utf-8")

    try:
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch()
            page = browser.new_page()
            page.goto(preview_html.as_uri(), wait_until="networkidle")
            page.pdf(
                path=str(pdf_path),
                format="A4",
                print_background=True,
                margin={"top": "18mm", "bottom": "18mm", "left": "15mm", "right": "15mm"},
            )
            browser.close()
    finally:
        if preview_html.exists():
            preview_html.unlink()

    return pdf_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert Markdown to PDF")
    parser.add_argument("markdown", type=Path, help="Input .md file")
    parser.add_argument("-o", "--output", type=Path, default=None, help="Output .pdf path")
    args = parser.parse_args()

    try:
        out = convert(args.markdown, args.output)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1

    print(f"Wrote {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
