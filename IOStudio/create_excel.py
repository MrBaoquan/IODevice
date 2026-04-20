#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import csv
from pathlib import Path

try:
    import openpyxl
    from openpyxl.styles import Font, PatternFill, Alignment
    HAS_OPENPYXL = True
except ImportError:
    HAS_OPENPYXL = False
    print("openpyxl 未安装，使用 CSV 格式")

csv_path = r'o:\DevModules\IODevice\IODevice\IOTester\动感平台_实施计划_周期表.csv'
xlsx_path = r'o:\DevModules\IODevice\IODevice\IOTester\动感平台_实施计划_周期表.xlsx'

# 读取 CSV
data = []
with open(csv_path, 'r', encoding='utf-8') as f:
    reader = csv.DictReader(f)
    data = list(reader)

if not HAS_OPENPYXL:
    print("已创建 CSV 文件，可用 Excel 直接打开")
    exit(0)

# 创建 Excel 工作簿
wb = openpyxl.Workbook()
ws = wb.active
ws.title = "实施计划周期表"

# 写入标题
headers = ['阶段', 'Sprint', '周次', '起始日期', '结束日期', '工作日', '任务编号', '任务名称', '工时(h)', '优先级', '交付物', '备注']
ws.append(headers)

# 格式化标题行
header_fill = PatternFill(start_color="4472C4", end_color="4472C4", fill_type="solid")
header_font = Font(bold=True, color="FFFFFF")
for cell in ws[1]:
    cell.fill = header_fill
    cell.font = header_font
    cell.alignment = Alignment(horizontal="center", vertical="center")

# 写入数据
for row_data in data:
    values = [
        row_data.get('阶段', ''),
        row_data.get('Sprint', ''),
        row_data.get('周次', ''),
        row_data.get('起始日期', ''),
        row_data.get('结束日期', ''),
        row_data.get('工作日', ''),
        row_data.get('任务编号', ''),
        row_data.get('任务名称', ''),
        row_data.get('工时(h)', ''),
        row_data.get('优先级', ''),
        row_data.get('交付物', ''),
        row_data.get('备注', '')
    ]
    ws.append(values)

# 调整列宽
column_widths = {
    'A': 12,  # 阶段
    'B': 12,  # Sprint
    'C': 12,  # 周次
    'D': 12,  # 起始日期
    'E': 12,  # 结束日期
    'F': 8,   # 工作日
    'G': 10,  # 任务编号
    'H': 25,  # 任务名称
    'I': 8,   # 工时
    'J': 8,   # 优先级
    'K': 20,  # 交付物
    'L': 15   # 备注
}

for col, width in column_widths.items():
    ws.column_dimensions[col].width = width

# 冻结首行和首列
ws.freeze_panes = "B2"

# 交替行颜色
light_fill = PatternFill(start_color="E7E6E6", end_color="E7E6E6", fill_type="solid")
for row_idx, row in enumerate(ws.iter_rows(min_row=2, max_row=ws.max_row), start=2):
    if row_idx % 2 == 0:
        for cell in row:
            cell.fill = light_fill

# 保存
wb.save(xlsx_path)
print(f"✓ Excel 文件已创建: {xlsx_path}")
print(f"  总行数: {ws.max_row - 1} (含标题)")
