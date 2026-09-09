# -*- coding: utf-8 -*-
"""生成节点二汇报 PPT —— 集成式动感平台播控系统
汇报结构严格复用阶段一骨架：
封面 → 项目进度规划 → 本阶段计划目标 → 已完成工作 → 成果展示 → 执行过程
→ 难点与突破 → 下一步计划 → 人员分工与倒排计划 → 结束页
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.util import Inches, Pt


if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")


SCRIPT_DIR = Path(__file__).resolve().parent
OUTPUT_PATH = SCRIPT_DIR / "节点二_进度汇报.pptx"
THUMB_DIR = SCRIPT_DIR / "generated_thumbs"

VIDEO_DIR = SCRIPT_DIR / "成果" / "原型实现"
IMG_EDITOR_VIDEO = VIDEO_DIR / "动作编辑器-v1.0.mp4"
IMG_GUI_VIDEO = VIDEO_DIR / "播控GUI-v1.0.mp4"
IMG_PLAYER_VIDEO = VIDEO_DIR / "播放器联调-v1.0.mp4"

IMG_EDITOR = THUMB_DIR / "动作编辑器-v1.0.png"
IMG_GUI = THUMB_DIR / "播控GUI-v1.0.png"
IMG_PLAYER = THUMB_DIR / "播放器联调-v1.0.png"

IMG_PATENT = SCRIPT_DIR / "专利申请" / "专利申请.png"
IMG_PLATFORM_FEEDBACK = SCRIPT_DIR / "平台沟通结果反馈" / "平台沟通反馈到采购部.png"
IMG_FIELD_DEBUG = SCRIPT_DIR / "邯郸现场测试跑通" / "调试过程.jpg"
IMG_FIELD_STRUCTURE = SCRIPT_DIR / "邯郸现场测试跑通" / "平台结构梳理.jpg"


C_DARK = RGBColor(0x0A, 0x19, 0x29)
C_PRIMARY = RGBColor(0x14, 0x5C, 0xA8)
C_ACCENT = RGBColor(0xD4, 0xA0, 0x2E)
C_TEAL = RGBColor(0x00, 0x89, 0x7B)
C_ORANGE = RGBColor(0xD1, 0x6B, 0x1E)
C_WHITE = RGBColor(0xFF, 0xFF, 0xFF)
C_LIGHT_BG = RGBColor(0xEE, 0xF2, 0xF7)
C_GRAY = RGBColor(0x55, 0x5E, 0x68)
C_LGRAY = RGBColor(0x8C, 0x95, 0x9F)
C_TBL_HDR = RGBColor(0x0E, 0x3D, 0x6B)
C_TBL_R1 = RGBColor(0xE5, 0xEE, 0xF7)
C_TBL_R2 = RGBColor(0xFF, 0xFF, 0xFF)
C_OK_BG = RGBColor(0xE0, 0xF2, 0xED)
C_OK_TEXT = RGBColor(0x2E, 0x7D, 0x32)
C_HL_BG = RGBColor(0xFD, 0xF4, 0xE0)
C_CUR_BG = RGBColor(0xFF, 0xF3, 0xCD)

NODE_LABELS = {
    1: "节点一(架构原型)",
    2: "节点二(核心开发)",
    3: "节点三(接口整合)",
    4: "节点四(测试优化)",
    5: "节点五(产品验收)",
}

SLIDE_W = Inches(13.333)
SLIDE_H = Inches(7.5)


def ensure_thumbnail(video_path: Path, output_path: Path, timestamp: str = "00:00:01") -> Path | None:
    if output_path.exists():
        return output_path
    if not video_path.exists():
        return None

    output_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        "ffmpeg",
        "-y",
        "-ss",
        timestamp,
        "-i",
        str(video_path),
        "-frames:v",
        "1",
        str(output_path),
    ]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="ignore")
    if result.returncode != 0:
        print(f"thumbnail failed: {video_path.name}\n{result.stderr}")
        return None
    return output_path if output_path.exists() else None


def bg(slide, color):
    fill = slide.background.fill
    fill.solid()
    fill.fore_color.rgb = color


def rect(slide, l, t, w, h, color):
    shape = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, l, t, w, h)
    shape.fill.solid()
    shape.fill.fore_color.rgb = color
    shape.line.fill.background()
    return shape


def rrect(slide, l, t, w, h, color, border=None):
    shape = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, l, t, w, h)
    shape.fill.solid()
    shape.fill.fore_color.rgb = color
    if border:
        shape.line.color.rgb = border
        shape.line.width = Pt(1.5)
    else:
        shape.line.fill.background()
    return shape


def txt(slide, l, t, w, h, text, sz=18, color=C_DARK, bold=False, align=PP_ALIGN.LEFT, font="微软雅黑"):
    tb = slide.shapes.add_textbox(l, t, w, h)
    tf = tb.text_frame
    tf.word_wrap = True
    paragraph = tf.paragraphs[0]
    paragraph.text = text
    paragraph.font.size = Pt(sz)
    paragraph.font.color.rgb = color
    paragraph.font.bold = bold
    paragraph.font.name = font
    paragraph.alignment = align
    return tb


def bullet_box(slide, l, t, w, h, lines, sz=14, color=C_DARK):
    tb = slide.shapes.add_textbox(l, t, w, h)
    tf = tb.text_frame
    tf.word_wrap = True
    tf.clear()
    for index, line in enumerate(lines):
        paragraph = tf.paragraphs[0] if index == 0 else tf.add_paragraph()
        paragraph.text = f"• {line}"
        paragraph.font.size = Pt(sz)
        paragraph.font.color.rgb = color
        paragraph.font.name = "微软雅黑"
        paragraph.space_after = Pt(5)
    return tb


def badge(slide, l, t, w, h, text, bg_color, sz=14):
    shape = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, l, t, w, h)
    shape.fill.solid()
    shape.fill.fore_color.rgb = bg_color
    shape.line.fill.background()
    tf = shape.text_frame
    tf.word_wrap = False
    p = tf.paragraphs[0]
    p.text = text
    p.font.size = Pt(sz)
    p.font.color.rgb = C_WHITE
    p.font.bold = True
    p.font.name = "微软雅黑"
    p.alignment = PP_ALIGN.CENTER
    return shape


def title_bar(slide, text):
    rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(1.0), C_DARK)
    rect(slide, Inches(0), Inches(0.96), SLIDE_W, Inches(0.04), C_ACCENT)
    txt(slide, Inches(0.6), Inches(0.15), Inches(12), Inches(0.7), text, sz=30, color=C_WHITE, bold=True)


def add_img(slide, path: Path, l, t, w=None, h=None):
    if not path or not path.exists():
        return None
    if w and h:
        return slide.shapes.add_picture(str(path), l, t, w, h)
    if w:
        return slide.shapes.add_picture(str(path), l, t, width=w)
    return slide.shapes.add_picture(str(path), l, t)


def add_placeholder(slide, l, t, w, h, title, desc):
    rrect(slide, l, t, w, h, C_LIGHT_BG, border=C_LGRAY)
    txt(slide, l + Inches(0.2), t + Inches(0.25), w - Inches(0.4), Inches(0.35), title, sz=18, bold=True, color=C_DARK, align=PP_ALIGN.CENTER)
    txt(slide, l + Inches(0.3), t + Inches(0.9), w - Inches(0.6), Inches(1.2), desc, sz=12, color=C_GRAY, align=PP_ALIGN.CENTER)


def tbl_ex(slide, l, t, w, data, col_ws, highlight_row=None, row_colors=None, font_size=11):
    nr, nc = len(data), len(data[0])
    table_shape = slide.shapes.add_table(nr, nc, l, t, w, Inches(0.42) * nr)
    table = table_shape.table
    for i, cw in enumerate(col_ws):
        table.columns[i].width = cw
    for ri, row in enumerate(data):
        for ci, cell_text in enumerate(row):
            cell = table.cell(ri, ci)
            cell.text = ""
            p = cell.text_frame.paragraphs[0]
            p.text = str(cell_text)
            p.font.name = "微软雅黑"
            if ri == 0:
                p.font.size = Pt(font_size + 1)
                p.font.color.rgb = C_WHITE
                p.font.bold = True
                p.alignment = PP_ALIGN.CENTER
                cell.fill.solid()
                cell.fill.fore_color.rgb = C_TBL_HDR
            elif row_colors and ri in row_colors:
                p.font.size = Pt(font_size)
                p.font.bold = True
                p.font.color.rgb = C_OK_TEXT
                cell.fill.solid()
                cell.fill.fore_color.rgb = row_colors[ri]
            elif highlight_row is not None and ri == highlight_row:
                p.font.size = Pt(font_size)
                p.font.bold = True
                p.font.color.rgb = C_DARK
                cell.fill.solid()
                cell.fill.fore_color.rgb = C_CUR_BG
            else:
                p.font.size = Pt(font_size)
                p.font.color.rgb = C_DARK
                cell.fill.solid()
                cell.fill.fore_color.rgb = C_TBL_R1 if ri % 2 == 1 else C_TBL_R2
            cell.vertical_anchor = MSO_ANCHOR.MIDDLE
            cell.margin_left = Pt(4)
            cell.margin_right = Pt(4)
            cell.margin_top = Pt(3)
            cell.margin_bottom = Pt(3)
    return table_shape


def two_col_difficulty(slide, left_title, left_problem, left_solution, right_title, right_problem, right_solution):
    for index, (title, problem, solution) in enumerate(
        [
            (left_title, left_problem, left_solution),
            (right_title, right_problem, right_solution),
        ]
    ):
        x = Inches(0.6) + Inches(6.2) * index
        y = Inches(1.3)
        rrect(slide, x, y, Inches(5.9), Inches(5.5), C_LIGHT_BG)
        rect(slide, x, y, Inches(5.9), Inches(0.55), C_ORANGE)
        txt(slide, x + Inches(0.2), y + Inches(0.08), Inches(5.5), Inches(0.35), title, sz=16, color=C_WHITE, bold=True)
        txt(slide, x + Inches(0.2), y + Inches(0.7), Inches(5.5), Inches(0.3), "问题", sz=14, color=C_ORANGE, bold=True)
        txt(slide, x + Inches(0.2), y + Inches(1.0), Inches(5.45), Inches(1.3), problem, sz=13, color=C_GRAY)
        rect(slide, x + Inches(0.2), y + Inches(2.45), Inches(5.5), Inches(0.02), C_LGRAY)
        txt(slide, x + Inches(0.2), y + Inches(2.65), Inches(5.5), Inches(0.3), "解决方案", sz=14, color=C_TEAL, bold=True)
        txt(slide, x + Inches(0.2), y + Inches(2.95), Inches(5.45), Inches(2.2), solution, sz=13, color=C_DARK)


def build_ppt():
    ensure_thumbnail(IMG_EDITOR_VIDEO, IMG_EDITOR)
    ensure_thumbnail(IMG_GUI_VIDEO, IMG_GUI)
    ensure_thumbnail(IMG_PLAYER_VIDEO, IMG_PLAYER)

    prs = Presentation()
    prs.slide_width = SLIDE_W
    prs.slide_height = SLIDE_H

    # Slide 1
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.08), C_PRIMARY)
    txt(slide, Inches(0), Inches(2.2), SLIDE_W, Inches(1.0), "集成式动感平台播控系统", sz=48, color=C_PRIMARY, bold=True, align=PP_ALIGN.CENTER)
    txt(slide, Inches(0), Inches(3.3), SLIDE_W, Inches(0.6), "第二阶段 · 进度汇报", sz=28, color=C_DARK, bold=True, align=PP_ALIGN.CENTER)
    rect(slide, Inches(5.6), Inches(4.15), Inches(2.1), Inches(0.03), C_ACCENT)
    txt(slide, Inches(0), Inches(4.6), SLIDE_W, Inches(0.5), "软件技术中心", sz=22, color=C_GRAY, align=PP_ALIGN.CENTER)
    txt(slide, Inches(0), Inches(5.2), SLIDE_W, Inches(0.4), "2026 年 4 月", sz=18, color=C_LGRAY, align=PP_ALIGN.CENTER)
    rect(slide, Inches(0), Inches(7.38), SLIDE_W, Inches(0.08), C_PRIMARY)

    # Slide 2
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "项目进度规划（立项报告）")
    plan_data = [
        ["节点", "时间节点", "进度安排", "状态"],
        [NODE_LABELS[1], "2026 年 3 月 31 日前", "产品原型设计及系统架构方案完成", "已按计划完成"],
        [NODE_LABELS[2], "2026 年 4 月 30 日前", "核心播控系统开发与基础联调完成", "已完成"],
        [NODE_LABELS[3], "2026 年 5 月 15 日前", "完成设备接口整合及动作编排优化", ""],
        [NODE_LABELS[4], "2026 年 5 月 31 日前", "系统整体测试优化完成，达到上线标准", ""],
        [NODE_LABELS[5], "2026 年 6 月 30 日前", "产品验收完成", ""],
    ]
    tbl_ex(slide, Inches(0.6), Inches(1.3), Inches(12), plan_data, [Inches(2.0), Inches(2.8), Inches(5.4), Inches(1.8)], row_colors={1: C_OK_BG, 2: C_OK_BG})
    rrect(slide, Inches(0.6), Inches(3.95), Inches(12), Inches(0.5), C_OK_BG)
    txt(slide, Inches(0.6), Inches(3.97), Inches(12), Inches(0.5), f"当前汇报：{NODE_LABELS[2]} — 核心播控系统开发与基础联调完成", sz=15, color=C_DARK, bold=True, align=PP_ALIGN.CENTER)
    txt(slide, Inches(0.6), Inches(4.75), Inches(12), Inches(0.4), "项目需交付的主要产品", sz=18, color=C_DARK, bold=True)
    products = [
        ("影片播控平台", "完成核心播控界面与基础联调能力建设，支撑播放控制与现场联动", C_ACCENT),
        ("动作编辑平台", "完成核心编辑能力建设，支撑动作编排与时间轴编辑", C_PRIMARY),
    ]
    for idx, (title, desc, color) in enumerate(products):
        x = Inches(0.6) + Inches(6.0) * idx
        y = Inches(5.3)
        rrect(slide, x, y, Inches(5.7), Inches(1.8), C_LIGHT_BG)
        rect(slide, x, y + Inches(0.15), Inches(0.06), Inches(1.4), color)
        badge(slide, x + Inches(0.2), y + Inches(0.2), Inches(0.6), Inches(0.35), str(idx + 1), color)
        txt(slide, x + Inches(0.95), y + Inches(0.15), Inches(4.5), Inches(0.35), title, sz=17, color=C_DARK, bold=True)
        txt(slide, x + Inches(0.25), y + Inches(0.65), Inches(5.15), Inches(0.8), desc, sz=13, color=C_GRAY)

    # Slide 3
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "一、本阶段计划目标 — 核心播控系统开发与基础联调完成")
    txt(slide, Inches(0.6), Inches(1.15), Inches(4), Inches(0.42), "核心工作", sz=20, color=C_ACCENT, bold=True)
    txt(slide, Inches(0.6), Inches(1.48), Inches(4), Inches(0.3), "核心开发 + 基础联调", sz=12, color=C_LGRAY)
    core_items = [
        ("1", "核心播控能力开发", "完成动作编辑器、播控 GUI、播放器联调等核心能力建设\n形成完整的播控系统主体能力", C_PRIMARY),
        ("2", "基础联调验证", "完成核心链路联调、现场调试、问题闭环与运行验证\n支撑系统进入下一阶段接口整合", C_ACCENT),
    ]
    for idx, (num, title, desc, color) in enumerate(core_items):
        x = Inches(0.6) + Inches(6.2) * idx
        y = Inches(1.9)
        rrect(slide, x, y, Inches(5.9), Inches(2.1), C_HL_BG, border=C_ACCENT)
        badge(slide, x + Inches(0.2), y + Inches(0.2), Inches(0.5), Inches(0.35), num, color)
        txt(slide, x + Inches(0.85), y + Inches(0.15), Inches(4.4), Inches(0.4), title, sz=18, color=C_DARK, bold=True)
        txt(slide, x + Inches(0.25), y + Inches(0.75), Inches(5.3), Inches(1.1), desc, sz=14, color=C_GRAY)
    txt(slide, Inches(0.6), Inches(4.2), Inches(6), Inches(0.42), "其他任务", sz=18, color=C_PRIMARY, bold=True)
    other_items = [
        ("3", "动作编辑器 v1.0", "完成", C_PRIMARY),
        ("4", "播控 GUI v1.0", "完成", C_PRIMARY),
        ("5", "播放器联调样机", "完成", C_TEAL),
        ("6", "测试修复/平台沟通/专利沉淀", "同步完成", C_TEAL),
    ]
    for idx, (num, title, desc, color) in enumerate(other_items):
        col = idx % 2
        row = idx // 2
        x = Inches(0.6) + Inches(6.2) * col
        y = Inches(4.7) + Inches(1.05) * row
        rrect(slide, x, y, Inches(5.9), Inches(0.9), C_LIGHT_BG)
        badge(slide, x + Inches(0.2), y + Inches(0.25), Inches(0.5), Inches(0.35), num, color)
        txt(slide, x + Inches(0.85), y + Inches(0.12), Inches(3.6), Inches(0.3), title, sz=15, color=C_DARK, bold=True)
        txt(slide, x + Inches(0.85), y + Inches(0.45), Inches(4.7), Inches(0.25), desc, sz=12, color=C_GRAY)
    txt(slide, Inches(0.6), Inches(6.85), Inches(12), Inches(0.35), "共 6 项计划目标，其中核心播控能力开发与基础联调验证为本阶段核心工作", sz=14, color=C_PRIMARY)

    # Slide 4
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "二、已完成工作")
    completed = [
        ["序号", "类别", "计划任务", "完成情况", "状态"],
        ["1", "核心", "动作编辑器核心开发", "完成时间轴编辑、轨道管理、关键帧编辑、属性配置等核心能力，形成动作编辑器 v1.0", "已完成"],
        ["2", "核心", "播控 GUI 开发", "完成姿态控制、特效控制、播放控制、门控/压杆/车辆控制等核心界面能力，形成播控 GUI v1.0", "已完成"],
        ["3", "核心", "播放器联调", "完成播放器联调样机与基础联动验证，形成基础联调闭环", "已完成"],
        ["4", "质量", "测试与问题修复", "完成 165 项单元测试通过，累计修复 58 个关键问题", "已完成"],
        ["5", "协同", "平台沟通推进", "完成平台沟通反馈与采购侧支撑材料整理", "已完成"],
        ["6", "成果", "专利申请", "完成 3 项专利申请材料整理与提交", "已完成"],
    ]
    tbl_ex(slide, Inches(0.5), Inches(1.2), Inches(12.3), completed, [Inches(0.6), Inches(0.8), Inches(2.3), Inches(7.5), Inches(1.0)], row_colors={1: C_OK_BG, 2: C_OK_BG, 3: C_OK_BG, 4: C_OK_BG, 5: C_OK_BG, 6: C_OK_BG}, font_size=10)
    rrect(slide, Inches(0.6), Inches(4.7), Inches(5.2), Inches(0.55), C_OK_BG)
    txt(slide, Inches(0.85), Inches(4.8), Inches(4.7), Inches(0.3), "计划任务完成率：6 / 6（100%）", sz=18, color=C_TEAL, bold=True)
    txt(slide, Inches(0.6), Inches(5.5), Inches(12), Inches(0.35), "关键成果指标", sz=16, color=C_DARK, bold=True)
    metrics = [("动作编辑器", "v1.0"), ("播控 GUI", "v1.0"), ("联调样机", "已完成"), ("单元测试", "165 项"), ("专利申请", "3 项")]
    for idx, (label, value) in enumerate(metrics):
        x = Inches(0.6) + Inches(2.5) * idx
        rrect(slide, x, Inches(5.9), Inches(2.2), Inches(0.62), C_LIGHT_BG)
        txt(slide, x, Inches(5.93), Inches(2.2), Inches(0.22), label, sz=11, color=C_GRAY, align=PP_ALIGN.CENTER)
        txt(slide, x, Inches(6.18), Inches(2.2), Inches(0.24), value, sz=14, color=C_PRIMARY, bold=True, align=PP_ALIGN.CENTER)

    # Single result pages
    result_pages = [
        (IMG_EDITOR, "成果展示 · 动作编辑器 v1.0", "时间轴、轨道、关键帧、属性编辑形成完整工作区", "动作编辑平台核心编辑能力已完成，具备后续编排优化与效果扩展基础。"),
        (IMG_GUI, "成果展示 · 播控 GUI v1.0", "姿态、特效、播放、门控、压杆、车辆等控制分区清晰", "播控 GUI 已完成核心控制入口集成，具备现场播控操作基础。"),
        (IMG_PLAYER, "成果展示 · 播放器联调样机", "播放器与播控链路的基础联动能力已经形成", "播放器联调样机已完成，基础联调链路已经形成。"),
    ]
    for image_path, title, subtitle, footer in result_pages:
        slide = prs.slides.add_slide(prs.slide_layouts[6])
        bg(slide, C_DARK)
        txt(slide, Inches(0.6), Inches(0.18), Inches(12), Inches(0.5), title, sz=28, color=C_WHITE, bold=True)
        txt(slide, Inches(0.6), Inches(0.62), Inches(12), Inches(0.3), subtitle, sz=14, color=C_LGRAY)
        image = add_img(slide, image_path, Inches(1.05), Inches(1.0), w=Inches(11.2))
        if image is None:
            add_placeholder(slide, Inches(1.05), Inches(1.0), Inches(11.2), Inches(5.3), title, subtitle)
        txt(slide, Inches(0.7), Inches(6.75), Inches(12), Inches(0.35), footer, sz=13, color=C_LGRAY)

    # Combined IP + platform page
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "成果展示 · 知识产权成果沉淀与平台沟通推进")
    rrect(slide, Inches(0.6), Inches(1.35), Inches(5.9), Inches(4.95), C_LIGHT_BG)
    txt(slide, Inches(0.85), Inches(1.5), Inches(5.4), Inches(0.3), "知识产权成果沉淀", sz=18, color=C_DARK, bold=True, align=PP_ALIGN.CENTER)
    patent_image = add_img(slide, IMG_PATENT, Inches(0.85), Inches(1.9), w=Inches(5.4), h=Inches(2.95))
    if patent_image is None:
        rect(slide, Inches(0.85), Inches(1.9), Inches(5.4), Inches(2.95), C_TBL_R1)
    bullet_box(slide, Inches(0.95), Inches(4.95), Inches(5.1), Inches(1.0), ["3 项专利申请材料已整理提交", "覆盖多设备联动编排、异构 IO 通道抽象与控制策略协调"], sz=12, color=C_DARK)
    rrect(slide, Inches(6.8), Inches(1.35), Inches(5.9), Inches(4.95), C_LIGHT_BG)
    txt(slide, Inches(7.05), Inches(1.5), Inches(5.4), Inches(0.3), "平台沟通推进结果", sz=18, color=C_DARK, bold=True, align=PP_ALIGN.CENTER)
    platform_image = add_img(slide, IMG_PLATFORM_FEEDBACK, Inches(7.05), Inches(1.9), w=Inches(5.4), h=Inches(2.95))
    if platform_image is None:
        rect(slide, Inches(7.05), Inches(1.9), Inches(5.4), Inches(2.95), C_TBL_R1)
    bullet_box(slide, Inches(7.15), Inches(4.95), Inches(5.1), Inches(1.0), ["平台沟通结果已形成明确反馈路径", "为后续接口整合和项目推进提供组织协同基础"], sz=12, color=C_DARK)
    rrect(slide, Inches(0.6), Inches(6.45), Inches(12.1), Inches(0.55), C_OK_BG)
    txt(slide, Inches(0.8), Inches(6.56), Inches(11.7), Inches(0.22), "知识产权沉淀与平台沟通推进同步完成，节点二成果已形成研发与协同双重支撑。", sz=13, color=C_DARK, bold=True, align=PP_ALIGN.CENTER)

    # Execution process single page
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "三、执行过程")
    process_cards = [
        ("核心开发", ["完成动作编辑器核心工作区建设", "完成播控 GUI 主要控制分区建设", "完成核心交互链路组织"], C_PRIMARY),
        ("基础联调", ["完成播放器联调样机搭建", "完成基础联调链路验证", "形成节点二核心联调成果"], C_TEAL),
        ("同步推进", ["完成测试加固与问题修复", "完成平台沟通反馈整理", "完成专利申请材料提交"], C_ACCENT),
    ]
    for idx, (title, items, color) in enumerate(process_cards):
        x = Inches(0.6) + Inches(4.2) * idx
        y = Inches(1.55)
        rrect(slide, x, y, Inches(3.9), Inches(4.35), C_LIGHT_BG)
        rect(slide, x, y, Inches(3.9), Inches(0.65), color)
        txt(slide, x + Inches(0.18), y + Inches(0.16), Inches(3.5), Inches(0.22), title, sz=17, color=C_WHITE, bold=True, align=PP_ALIGN.CENTER)
        bullet_box(slide, x + Inches(0.22), y + Inches(0.95), Inches(3.35), Inches(3.0), items, sz=13, color=C_DARK)
    rrect(slide, Inches(0.6), Inches(6.3), Inches(12.0), Inches(0.6), C_OK_BG)
    txt(slide, Inches(0.8), Inches(6.45), Inches(11.6), Inches(0.25), "第二阶段围绕核心开发、基础联调与成果沉淀三条主线推进，形成了清晰的节点完成闭环。", sz=14, color=C_DARK, bold=True, align=PP_ALIGN.CENTER)

    # Slide 15
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "四、难点与突破")
    two_col_difficulty(
        slide,
        "难点一：核心开发内容多，系统能力需要并行成型",
        "节点二同时覆盖动作编辑、播控界面、播放器联调、测试修复等多项核心工作，工作面较宽，要求多个能力面同步成型。",
        "按“编辑器主线、播控主线、联调主线、质量主线”并行推进，先完成可运行、可展示、可联调的核心版本，再集中归并成果形成节点交付。",
        "难点二：研发成果需要形成更完整的交付表达",
        "节点二不仅要完成软件能力建设，还需要把联调成果、协同推进成果和技术沉淀成果整合成清晰的交付表达。",
        "通过联调样机展示、平台沟通反馈整理和专利成果沉淀，把核心开发成果转化为更完整、更易汇报的节点交付结果。",
    )

    # Slide 16
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "四、难点突破 · 成果实证")
    txt(slide, Inches(0.55), Inches(1.1), Inches(8), Inches(0.25), "联调与协同实证材料", sz=14, color=C_DARK, bold=True)
    evidence_images = [
        (IMG_PLAYER, "播放器联调样机"),
        (IMG_PATENT, "专利申请成果"),
        (IMG_PLATFORM_FEEDBACK, "平台沟通反馈"),
    ]
    for idx, (image_path, label) in enumerate(evidence_images):
        x = Inches(0.55) + Inches(4.05) * idx
        image = add_img(slide, image_path, x, Inches(1.5), w=Inches(3.7), h=Inches(3.9))
        if image is None:
            rect(slide, x, Inches(1.5), Inches(3.7), Inches(3.9), C_TBL_R1)
        txt(slide, x, Inches(5.55), Inches(3.7), Inches(0.35), label, sz=11, color=C_GRAY, align=PP_ALIGN.CENTER)
    rrect(slide, Inches(0.55), Inches(6.25), Inches(12.2), Inches(0.78), C_OK_BG)
    txt(slide, Inches(0.85), Inches(6.42), Inches(11.5), Inches(0.35), "通过联调样机、专利成果和平台沟通反馈，节点二成果完成了从核心开发到完整交付表达的延展。", sz=13, color=C_DARK, bold=True)

    # Slide 17
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "五、下一步计划")
    txt(slide, Inches(0.6), Inches(1.15), Inches(12), Inches(0.35), f"{NODE_LABELS[3]}（2026 年 5 月 15 日前）：完成设备接口整合及动作编排优化", sz=18, color=C_DARK, bold=True)
    next_items = [
        ("1", "设备接口整合", "推进平台接口、控制链路和设备适配整合工作", C_ACCENT),
        ("2", "动作编排优化", "围绕编排效率、效果表达和编辑体验，继续完善动作编辑器能力", C_PRIMARY),
        ("3", "系统联调深化", "在节点二基础联调成果上，继续推进接口整合后的系统联调与能力闭环", C_TEAL),
    ]
    for idx, (num, title, desc, color) in enumerate(next_items):
        x = Inches(0.6) + Inches(4.2) * idx
        y = Inches(1.7)
        rrect(slide, x, y, Inches(3.8), Inches(2.6), C_LIGHT_BG)
        badge(slide, x + Inches(0.2), y + Inches(0.2), Inches(0.5), Inches(0.35), num, color)
        txt(slide, x + Inches(0.85), y + Inches(0.18), Inches(2.7), Inches(0.35), title, sz=18, color=C_DARK, bold=True)
        txt(slide, x + Inches(0.3), y + Inches(0.85), Inches(3.15), Inches(1.35), desc, sz=14, color=C_GRAY)
    plan_full = [
        ["节点", "截止日期", "进度安排（立项报告）", "状态"],
        [NODE_LABELS[1], "3/31", "产品原型设计及系统架构方案完成", "已按计划完成"],
        [NODE_LABELS[2], "4/30", "核心播控系统开发与基础联调完成", "已完成"],
        [NODE_LABELS[3], "5/15", "完成设备接口整合及动作编排优化", "下一阶段"],
        [NODE_LABELS[4], "5/31", "系统整体测试优化完成，达到上线标准", ""],
        [NODE_LABELS[5], "6/30", "产品验收完成", ""],
    ]
    tbl_ex(slide, Inches(0.6), Inches(4.6), Inches(12), plan_full, [Inches(2.0), Inches(1.2), Inches(6.4), Inches(2.4)], row_colors={1: C_OK_BG, 2: C_OK_BG}, highlight_row=3, font_size=10)

    # Slide 18
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_WHITE)
    title_bar(slide, "六、人员分工与倒排计划")
    schedule_data = [
        ["节点", "截止", "马宝全", "居向前", "邵向阳", "郝晓阳", "穆大强"],
        [f"{NODE_LABELS[1]} ✅", "3/31", "架构+原型\n阶段一收口", "影片播控平台\n原型设计", "协议收集\n沟通推进", "协议收集", "厂家沟通"],
        [f"{NODE_LABELS[2]} ✅", "4/30", "编辑器核心开发\n播放器联调\n现场推进", "播控 GUI 开发\n界面完善", "平台沟通推进\n支撑材料整理", "测试协同\n问题跟踪", "现场支持\n结构梳理"],
        [NODE_LABELS[3], "5/15", "接口整合\n动作编排优化", "影片播控平台深化\n系统联调", "链路接口梳理\n现场协调", "测试验证\n问题闭环", "设备适配\n联调支持"],
        [NODE_LABELS[4], "5/31", "性能优化\n效果预设", "系统联调优化", "异常处理", "测试优化", "现场联调"],
        [NODE_LABELS[5], "6/30", "交付打包\n验收准备", "部署优化", "文档支持", "测试归档", "现场交付"],
    ]
    tbl_ex(slide, Inches(0.35), Inches(1.1), Inches(12.65), schedule_data, [Inches(2.0), Inches(0.7), Inches(2.0), Inches(1.8), Inches(1.8), Inches(1.6), Inches(1.8)], row_colors={1: C_OK_BG, 2: C_OK_BG}, font_size=9)
    txt(slide, Inches(0.6), Inches(5.1), Inches(12), Inches(0.25), "里程碑检查点", sz=14, color=C_DARK, bold=True)
    checkpoints = [
        ("5/05", "接口清单与适配方案明确"),
        ("5/10", "动作编排优化能力完成"),
        ("5/15", "设备接口整合与动作编排优化完成"),
        ("5/22", "全系统联调优化"),
        ("5/31", "系统测试优化完成"),
        ("6/30", "产品验收就绪"),
    ]
    for idx, (date, desc) in enumerate(checkpoints):
        col = idx % 3
        row = idx // 3
        x = Inches(0.6) + Inches(4.2) * col
        y = Inches(5.5) + Inches(0.82) * row
        rrect(slide, x, y, Inches(3.9), Inches(0.7), C_LIGHT_BG)
        badge(slide, x + Inches(0.1), y + Inches(0.17), Inches(0.7), Inches(0.3), date, C_ACCENT, sz=10)
        txt(slide, x + Inches(0.9), y + Inches(0.08), Inches(2.8), Inches(0.5), desc, sz=10, color=C_DARK)

    # Slide 19
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg(slide, C_DARK)
    rect(slide, Inches(0), Inches(0), SLIDE_W, Inches(0.06), C_ACCENT)
    txt(slide, Inches(0), Inches(2.0), SLIDE_W, Inches(1), "谢谢", sz=56, color=C_WHITE, bold=True, align=PP_ALIGN.CENTER)
    txt(slide, Inches(0), Inches(3.3), SLIDE_W, Inches(0.6), "集成式动感平台播控系统  |  第二阶段进度汇报", sz=22, color=C_LGRAY, align=PP_ALIGN.CENTER)
    txt(slide, Inches(0), Inches(4.0), SLIDE_W, Inches(0.4), "软件技术中心", sz=20, color=C_GRAY, align=PP_ALIGN.CENTER)
    txt(slide, Inches(0), Inches(4.5), SLIDE_W, Inches(0.5), "2026 年 4 月", sz=18, color=C_LGRAY, align=PP_ALIGN.CENTER)
    rect(slide, Inches(0), Inches(7.3), SLIDE_W, Inches(0.04), C_ACCENT)

    prs.save(str(OUTPUT_PATH))
    print(f"PPT generated: {OUTPUT_PATH}")


if __name__ == "__main__":
    build_ppt()