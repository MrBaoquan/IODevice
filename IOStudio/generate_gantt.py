#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成甘特图 Excel 文件
"""

from datetime import datetime, timedelta
from pathlib import Path

csv_path = r'o:\DevModules\IODevice\IODevice\IOTester\甘特图数据.csv'

# 定义所有 Phase 和 Sprint
phases_data = [
    {"phase": "Phase 1", "name": "播放引擎 + MotionServer", "start": "2026-03-09", "end": "2026-03-27", "color": "FF92D050"},  # 绿
    {"phase": "Phase 2", "name": "时间轴基础 UI", "start": "2026-04-06", "end": "2026-05-03", "color": "FF00B0F0"},  # 蓝
    {"phase": "Phase 3", "name": "导出 + 加密", "start": "2026-05-13", "end": "2026-06-10", "color": "FFFFC000"},  # 橙
    {"phase": "Phase 4", "name": "曲线编辑器", "start": "2026-06-17", "end": "2026-07-15", "color": "FFC5E0B4"},  # 浅绿
    {"phase": "Phase 5", "name": "媒体同步", "start": "2026-07-15", "end": "2026-08-12", "color": "FFE2EFDA"},  # 浅蓝
    {"phase": "Phase 6", "name": "效果预设", "start": "2026-08-12", "end": "2026-09-09", "color": "FFFFF2CC"},  # 浅黄
    {"phase": "Phase 7", "name": "LicHper 授权集成 + 打磨", "start": "2026-09-09", "end": "2026-10-07", "color": "FFFF0000"},  # 红（关键）
    {"phase": "Phase 8", "name": "Unity/UE SDK", "start": "2026-10-07", "end": "2026-10-28", "color": "FF9BC2E6"},  # 深蓝
]

# 转换为 CSV 格式用于 Excel
with open(csv_path, 'w', encoding='utf-8') as f:
    f.write('Phase,任务,起始日期,结束日期,周期,标记\n')
    for phase in phases_data:
        f.write(f'{phase["phase"]},{phase["name"]},{phase["start"]},{phase["end"]},{phase["phase"]},\n')

print(f"✓ 甘特图数据已导出: {csv_path}")
print("\n提示: 可在 Excel 中:")
print("  1. 导入上述 CSV")
print("  2. 使用 Excel 内置的 Gantt 图表模板")
print("  3. 或手动创建时间轴图表")
