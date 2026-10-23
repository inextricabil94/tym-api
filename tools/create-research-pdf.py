"""Render a research Markdown document; requires reportlab and Windows Arial fonts."""
import argparse
from html import escape
from pathlib import Path
import re
from urllib.parse import urljoin

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source", default="docs/research/TYM_Research_Improvement_Proposal.md")
parser.add_argument("--output", default="output/pdf/TYM_Research_Improvement_Proposal.pdf")
parser.add_argument("--date", default="19 October 2026")
parser.add_argument("--title", default="TYM: Research and Improvement Proposal")
parser.add_argument("--header", default="TYM  /  RESEARCH AND IMPROVEMENT PROPOSAL")
parser.add_argument("--footer", default="Evidence audit and proposed work")
args = parser.parse_args()
SOURCE = ROOT / args.source
OUTPUT = ROOT / args.output
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
for name, filename in [("Arial", "arial.ttf"), ("Arial-Bold", "arialbd.ttf"),
                       ("Arial-Italic", "ariali.ttf"), ("Arial-BoldItalic", "arialbi.ttf"), ("Consolas", "consola.ttf")]:
    pdfmetrics.registerFont(TTFont(name, str(Path("C:/Windows/Fonts") / filename)))
pdfmetrics.registerFontFamily("Arial", normal="Arial", bold="Arial-Bold", italic="Arial-Italic", boldItalic="Arial-BoldItalic")
BASE = dict(fontName="Arial", fontSize=10.2, leading=13.4, textColor=colors.HexColor("#202721"),
            spaceAfter=8, alignment=TA_LEFT, splitLongWords=True)
body = ParagraphStyle("Body", **BASE)
title = ParagraphStyle("Title", parent=body, fontName="Arial-Bold", fontSize=24, leading=29, spaceAfter=15)
h2 = ParagraphStyle("H2", parent=body, fontName="Arial-Bold", fontSize=18, leading=23, spaceAfter=13, keepWithNext=True)
h3 = ParagraphStyle("H3", parent=body, fontName="Arial-Bold", fontSize=12, leading=16, spaceBefore=8, spaceAfter=8, keepWithNext=True)
bullet = ParagraphStyle("Bullet", parent=body, leftIndent=13, firstLineIndent=-10, spaceAfter=6)
cell = ParagraphStyle("Cell", parent=body, fontSize=9.3, leading=12, spaceAfter=0)
header = ParagraphStyle("Header", parent=cell, fontName="Arial-Bold")
compact_cell = ParagraphStyle("CompactCell", parent=cell, fontSize=8.2, leading=10.4)
compact_header = ParagraphStyle("CompactHeader", parent=compact_cell, fontName="Arial-Bold")


def inline(text):
    saved = []
    def save(value):
        saved.append(value)
        return f"@@TOKEN{len(saved)-1}@@"
    base_url = "https://github.com/inextricabil94/tym-api/blob/main/" + SOURCE.relative_to(ROOT).as_posix()
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)",
                  lambda m: save(f'<a href="{escape(urljoin(base_url, m[2]), quote=True)}" color="#176245">{escape(m[1])}</a>'), text)
    text = re.sub(r"`([^`]+)`", lambda m: save(f'<font name="Consolas">{escape(m[1])}</font>'), text)
    text = escape(text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"\*([^*]+)\*", r"<i>\1</i>", text)
    for i, value in enumerate(saved):
        text = text.replace(f"@@TOKEN{i}@@", value)
    return text


def footer(canvas, doc):
    canvas.saveState()
    width, height = letter
    canvas.setFont("Arial", 8)
    canvas.setFillColor(colors.HexColor("#5a685e"))
    canvas.drawString(54, height - 31, args.header)
    canvas.setStrokeColor(colors.HexColor("#ccd4ce"))
    canvas.setLineWidth(0.5)
    canvas.line(54, 43, width - 54, 43)
    canvas.drawString(54, 29, args.date + "  |  " + args.footer)
    canvas.drawRightString(width - 54, 29, str(doc.page))
    canvas.restoreState()


lines = SOURCE.read_text(encoding="utf-8").splitlines()
story = []
i = 0
while i < len(lines):
    line = lines[i].strip()
    i += 1
    if not line:
        continue
    if line == "<!-- pagebreak -->":
        story.append(PageBreak())
    elif line.startswith("# "):
        story.append(Paragraph(inline(line[2:]), title))
    elif line.startswith("## "):
        story.append(Paragraph(inline(line[3:]), h2))
    elif line.startswith("### "):
        story.append(Paragraph(inline(line[4:]), h3))
    elif line.startswith("| "):
        rows = [line]
        while i < len(lines) and lines[i].strip().startswith("|"):
            rows.append(lines[i].strip())
            i += 1
        data = []
        for row in rows:
            cells = [value.strip() for value in row.strip("|").split("|")]
            if all(re.fullmatch(r":?-+:?", value) for value in cells):
                continue
            table_cell, table_header = (compact_cell, compact_header) if len(cells) > 4 else (cell, header)
            data.append([Paragraph(inline(value), table_header if not data else table_cell) for value in cells])
        n = len(data[0])
        width = letter[0] - 108
        tracks = [width * .31, width * .69] if n == 2 else [width / n] * n
        if n == 7:
            tracks = [54, 44, 60, 70, 70, 70, 136]
        table = Table(data, colWidths=tracks, repeatRows=1, hAlign="LEFT")
        table.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e9f0eb")),
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("LEFTPADDING", (0, 0), (-1, -1), 8), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
            ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ("LINEBELOW", (0, 0), (-1, 0), 0.6, colors.HexColor("#a0b3a6")),
            ("LINEBELOW", (0, 1), (-1, -1), 0.4, colors.HexColor("#d6dfd9")),
        ]))
        story.extend([table, Spacer(1, 12)])
    else:
        paragraph = [line]
        while i < len(lines) and lines[i].strip() and not lines[i].startswith(("#", "|", "- ", "<!--")) and not re.match(r"\d+\. ", lines[i]):
            paragraph.append(lines[i].strip())
            i += 1
        style = bullet if line.startswith("- ") or re.match(r"\d+\. ", line) else body
        story.append(Paragraph(inline(" ".join(paragraph)), style))

document = SimpleDocTemplate(str(OUTPUT), pagesize=letter, rightMargin=54, leftMargin=54,
                             topMargin=57, bottomMargin=57, title=args.title,
                             author="TYM research working document", pageCompression=1)
document.build(story, onFirstPage=footer, onLaterPages=footer)
print(OUTPUT)
