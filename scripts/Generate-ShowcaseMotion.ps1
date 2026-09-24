#requires -Version 5.1
<#
.SYNOPSIS
  生成 10 分钟复杂动作编排示例 .motion (Simulator 动感平台)。

.DESCRIPTION
  为什么用脚本而不是手写 JSON: 10 分钟编排有几十个预设实例、上百个 clip 与关键帧，
  手写既易错又不可复核。脚本把"预设定义 / 播放节拍 / 环境门控 / 章节标记"分开表达，
  并内置同轨 clip 重叠检查 (重叠会被求值器静默遮蔽)。

  产物: IOStudio/Config/Motion/showcase_10min.motion
  用法: powershell -ExecutionPolicy Bypass -File scripts/Generate-ShowcaseMotion.ps1 [-OutFile <path>]
#>
param(
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $root "IOStudio\Config\Motion\showcase_10min.motion"
}

$Device = "Simulator-1"
$DurationMs = 600000.0   # 10 分钟

function Kf([double]$t, [double]$v, [string]$i) {
    [ordered]@{ time_ms = $t; value = [double][Math]::Round($v, 4); interpolation = $i }
}

# 归一化值按强度缩放: 运动轴绕中性 0.5, 特效通道绕 0
function ScaleVal([double]$v, [double]$neutral, [double]$intensity) {
    $r = $neutral + ($v - $neutral) * $intensity
    if ($r -lt 0) { $r = 0 } elseif ($r -gt 1) { $r = 1 }
    return [Math]::Round($r, 4)
}

# ── 轨道定义 ────────────────────────────────────────────────
# neutral: 运动轴 0.5, 特效/状态 0; range: 工程量程 (工程值 = (归一化-neutral)*range)
$tracks = @(
    [ordered]@{ id="trk_pitch"; ch="OAxis_01"; label="俯仰";   role="pitch";  color="#A78BFA"; neutral=0.5; range=60.0;  group="主运动"; idle=@(3200,   0,   @(@(0,0.5),@(800,0.515),@(1600,0.5),@(2400,0.485),@(3200,0.5))) },
    [ordered]@{ id="trk_roll";  ch="OAxis_03"; label="横滚";   role="roll";   color="#34D399"; neutral=0.5; range=60.0;  group="主运动"; idle=@(4000, 800,  @(@(0,0.5),@(1000,0.52),@(2000,0.5),@(3000,0.48),@(4000,0.5))) },
    [ordered]@{ id="trk_yaw";   ch="OAxis_02"; label="偏航";   role="yaw";    color="#60A5FA"; neutral=0.5; range=90.0;  group="主运动"; idle=@(8000,1500, @(@(0,0.5),@(2000,0.53),@(4000,0.5),@(6000,0.47),@(8000,0.5))) },
    [ordered]@{ id="trk_heave"; ch="OAxis_06"; label="升降";   role="heave";  color="#4FC3F7"; neutral=0.5; range=600.0; group="主运动"; idle=@(5200,   0,  @(@(0,0.5),@(1300,0.54),@(2600,0.5),@(3900,0.46),@(5200,0.5))) },
    [ordered]@{ id="trk_surge"; ch="OAxis_04"; label="前后";   role="surge";  color="#FBBF24"; neutral=0.5; range=600.0; group="主运动"; idle=@(6400,1200, @(@(0,0.5),@(1600,0.52),@(3200,0.5),@(4800,0.48),@(6400,0.5))) },
    [ordered]@{ id="trk_sway";  ch="OAxis_05"; label="左右";   role="sway";   color="#F472B6"; neutral=0.5; range=400.0; group="主运动"; idle=@(7200,2400, @(@(0,0.5),@(1800,0.48),@(3600,0.5),@(5400,0.52),@(7200,0.5))) },
    [ordered]@{ id="trk_angle"; ch="OAxis_07"; label="转台";   role="angle";  color="#C084FC"; neutral=0.5; range=720.0; group="主运动"; idle=@(10000, 0,  @(@(0,0.5),@(2500,0.52),@(5000,0.5),@(7500,0.48),@(10000,0.5))) },

    [ordered]@{ id="trk_wind";    ch="OAxis_18"; label="风效";   role="wind";    color="#7DD3FC"; neutral=0.0; range=$null; group="环境特效" },
    [ordered]@{ id="trk_fog";     ch="OAxis_19"; label="烟雾";   role="fog";     color="#94A3B8"; neutral=0.0; range=$null; group="环境特效" },
    [ordered]@{ id="trk_water";   ch="OAxis_16"; label="水效";   role="water";   color="#38BDF8"; neutral=0.0; range=$null; group="环境特效" },
    [ordered]@{ id="trk_snow";    ch="OAxis_17"; label="飘雪";   role="snow";    color="#E2E8F0"; neutral=0.0; range=$null; group="环境特效" },
    [ordered]@{ id="trk_hot";     ch="OAxis_20"; label="热浪";   role="hot";     color="#FB923C"; neutral=0.0; range=$null; group="环境特效" },
    [ordered]@{ id="trk_light";   ch="OAxis_21"; label="频闪";   role="light";   color="#FDE047"; neutral=0.0; range=$null; group="环境特效" },
    [ordered]@{ id="trk_vibrate"; ch="OAxis_22"; label="振动";   role="vibrate"; color="#FB7185"; neutral=0.0; range=$null; group="环境特效" },

    [ordered]@{ id="trk_speed";     ch="OAxis_08"; label="速度级别"; role="speed";          color="#22D3EE"; neutral=0.0; range=100.0; group="安全与状态" },
    [ordered]@{ id="trk_enable";    ch="OAxis_11"; label="运动使能"; role="motion_enable";  color="#4ADE80"; neutral=0.0; range=$null; group="安全与状态" },
    [ordered]@{ id="trk_reset";     ch="OAxis_09"; label="复位";     role="reset";          color="#FACC15"; neutral=0.0; range=$null; group="安全与状态" },
    [ordered]@{ id="trk_emergency"; ch="OAxis_10"; label="急停";     role="emergency_stop"; color="#EF4444"; neutral=0.0; range=$null; group="安全与状态" }
)

$roleTrack = @{}
foreach ($t in $tracks) { $roleTrack[$t.role] = $t.id }
$roleTrack["impact"] = "trk_vibrate"
$roleTrack["stop"]   = "trk_surge"

# ── 预设定义 (内嵌快照; 同时作为 action instance 的重建来源) ──
function Preset($id, $name, $category, $desc, $dur, $channels) {
    [ordered]@{ id=$id; name=$name; category=$category; desc=$desc; duration=[double]$dur; channels=$channels }
}
function Chan($role, $kfs) { [ordered]@{ role=$role; kfs=$kfs } }

$presets = @(
    (Preset "fx_weightless_rise" "失重上升" "失重效果" "迅速上冲失重感: 升降快速上冲→短暂悬浮回落→稳定回中; 俯仰后仰 + 横滚微摆联动" 3000 @(
        (Chan "heave" @(@(0,0.5,"bezier"),@(600,0.88,"bezier"),@(1800,0.58,"bezier"),@(3000,0.5,"ease_in_out")))
        (Chan "pitch" @(@(0,0.5,"bezier"),@(600,0.62,"bezier"),@(2400,0.55,"bezier"),@(3000,0.5,"ease_in_out")))
        (Chan "roll"  @(@(0,0.5,"bezier"),@(600,0.58,"bezier"),@(2400,0.45,"bezier"),@(3000,0.5,"ease_in_out")))
    )),
    (Preset "fx_weightless_fall" "失重下降" "失重效果" "快速下坠失重感: 升降急速下冲→回弹→稳定回中; 俯仰/横滚联动错峰" 2000 @(
        (Chan "heave" @(@(0,0.5,"bezier"),@(500,0.12,"bezier"),@(1800,0.42,"bezier"),@(2000,0.5,"ease_in_out")))
        (Chan "pitch" @(@(0,0.5,"bezier"),@(500,0.38,"bezier"),@(1800,0.6,"bezier"),@(2000,0.5,"ease_in_out")))
        (Chan "roll"  @(@(0,0.5,"bezier"),@(500,0.42,"bezier"),@(1800,0.58,"bezier"),@(2000,0.5,"ease_in_out")))
    )),
    (Preset "fx_dive" "俯冲" "过山车" "前冲俯冲: 升降前冲下降→俯仰前倾→稳定回中" 2000 @(
        (Chan "heave" @(@(0,0.5,"bezier"),@(800,0.1,"bezier"),@(1600,0.9,"ease_in_out"),@(2000,0.5,"bezier")))
        (Chan "pitch" @(@(0,0.5,"bezier"),@(800,0.15,"bezier"),@(1600,0.85,"ease_in_out"),@(2000,0.5,"bezier")))
    )),
    (Preset "fx_impact_vibration" "撞击震动" "冲击震动" "撞击冲击波: 瞬间冲击→高频衰减震荡→稳定回中" 1000 @(
        (Chan "impact" @(@(0,0,"step"),@(50,0.8,"bezier"),@(300,0.3,"bezier"),@(600,0.6,"bezier"),@(900,0.4,"bezier"),@(1000,0,"ease_in_out")))
    )),
    (Preset "fx_emergency_stop" "急停" "冲击震动" "快速急停: 瞬间减速→过冲修正→稳定回中" 500 @(
        (Chan "stop" @(@(0,0,"step"),@(50,0.3,"bezier"),@(150,0.6,"bezier"),@(300,0.45,"bezier"),@(500,0,"ease_in_out")))
    )),
    (Preset "fx_gentle_cruise" "平稳巡航" "巡航" "长周期平缓摆动: 升降/横滚/偏航低幅错相, 营造巡航漂浮感" 6000 @(
        (Chan "heave" @(@(0,0.5,"ease_in_out"),@(1500,0.56,"ease_in_out"),@(3000,0.5,"ease_in_out"),@(4500,0.44,"ease_in_out"),@(6000,0.5,"ease_in_out")))
        (Chan "roll"  @(@(0,0.5,"ease_in_out"),@(1500,0.54,"ease_in_out"),@(3000,0.5,"ease_in_out"),@(4500,0.46,"ease_in_out"),@(6000,0.5,"ease_in_out")))
        (Chan "yaw"   @(@(0,0.5,"ease_in_out"),@(2000,0.52,"ease_in_out"),@(4000,0.48,"ease_in_out"),@(6000,0.5,"ease_in_out")))
    )),
    (Preset "fx_turbulence" "气流颠簸" "过山车" "高频颠簸: 横滚/俯仰快速抖动 + 升降起伏 + 冲击振动" 5000 @(
        (Chan "roll"   @(@(0,0.5,"bezier"),@(400,0.62,"bezier"),@(900,0.4,"bezier"),@(1500,0.6,"bezier"),@(2100,0.42,"bezier"),@(2800,0.58,"bezier"),@(3500,0.45,"bezier"),@(4300,0.55,"bezier"),@(5000,0.5,"ease_in_out")))
        (Chan "pitch"  @(@(0,0.5,"bezier"),@(500,0.58,"bezier"),@(1100,0.44,"bezier"),@(1800,0.56,"bezier"),@(2600,0.46,"bezier"),@(3400,0.55,"bezier"),@(4200,0.47,"bezier"),@(5000,0.5,"ease_in_out")))
        (Chan "heave"  @(@(0,0.5,"ease_in_out"),@(1200,0.6,"bezier"),@(2500,0.4,"bezier"),@(3800,0.58,"bezier"),@(5000,0.5,"ease_in_out")))
        (Chan "impact" @(@(0,0,"step"),@(100,0.6,"bezier"),@(500,0.2,"bezier"),@(1200,0.5,"bezier"),@(2200,0.15,"bezier"),@(3200,0.4,"bezier"),@(4200,0.1,"bezier"),@(5000,0,"ease_in_out")))
    )),
    (Preset "fx_canyon_sweep" "峡谷穿越" "过山车" "大幅偏航扫掠 + 横滚压弯 + 左右平移, 营造贴地穿越感" 6000 @(
        (Chan "yaw"  @(@(0,0.5,"ease_in_out"),@(1500,0.85,"bezier"),@(3000,0.5,"ease_in_out"),@(4500,0.15,"bezier"),@(6000,0.5,"ease_in_out")))
        (Chan "roll" @(@(0,0.5,"ease_in_out"),@(1500,0.66,"bezier"),@(3000,0.5,"ease_in_out"),@(4500,0.34,"bezier"),@(6000,0.5,"ease_in_out")))
        (Chan "sway" @(@(0,0.5,"ease_in_out"),@(1500,0.68,"bezier"),@(3000,0.5,"ease_in_out"),@(4500,0.32,"bezier"),@(6000,0.5,"ease_in_out")))
    )),
    (Preset "fx_strobe" "频闪" "特效" "座舱顶灯短促频闪" 1500 @(
        (Chan "light" @(@(0,0,"step"),@(60,1,"step"),@(180,0,"step"),@(320,1,"step"),@(460,0,"step"),@(640,1,"step"),@(820,0,"step"),@(1100,1,"step"),@(1280,0,"step"),@(1500,0,"step")))
    )),
    (Preset "fx_fog_push" "迷雾推进" "特效" "烟雾弥漫 + 升降缓慢起伏" 6000 @(
        (Chan "fog"   @(@(0,0,"step"),@(300,0.85,"ease_in_out"),@(4800,0.85,"ease_in_out"),@(6000,0,"ease_in_out")))
        (Chan "heave" @(@(0,0.5,"ease_in_out"),@(2000,0.58,"ease_in_out"),@(4000,0.46,"ease_in_out"),@(6000,0.5,"ease_in_out")))
    )),
    (Preset "fx_water_blast" "水花冲击" "特效" "水效爆发 + 冲击振动" 2000 @(
        (Chan "water"  @(@(0,0,"step"),@(200,1,"step"),@(1200,1,"step"),@(2000,0,"ease_in_out")))
        (Chan "impact" @(@(0,0,"step"),@(80,0.7,"bezier"),@(500,0.25,"bezier"),@(1200,0.5,"bezier"),@(2000,0,"ease_in_out")))
    )),
    (Preset "fx_snow_drift" "飘雪漂移" "特效" "飘雪 + 横滚缓漂" 8000 @(
        (Chan "snow" @(@(0,0,"step"),@(500,0.9,"ease_in_out"),@(6500,0.9,"ease_in_out"),@(8000,0,"ease_in_out")))
        (Chan "roll" @(@(0,0.5,"ease_in_out"),@(2500,0.56,"ease_in_out"),@(5000,0.44,"ease_in_out"),@(8000,0.5,"ease_in_out")))
    )),
    (Preset "fx_heat_wave" "热浪涌起" "特效" "热浪 + 横滚轻摆" 4000 @(
        (Chan "hot"  @(@(0,0,"step"),@(400,0.9,"ease_in_out"),@(3200,0.9,"ease_in_out"),@(4000,0,"ease_in_out")))
        (Chan "roll" @(@(0,0.5,"ease_in_out"),@(1500,0.55,"ease_in_out"),@(3000,0.45,"ease_in_out"),@(4000,0.5,"ease_in_out")))
    ))
)
$presetById = @{}
foreach ($p in $presets) { $presetById[$p.id] = $p }

# ── 播放节拍 (cue): 时间 + 预设 + 强度 ──────────────────────
$cues = @(
    @(32000,"fx_weightless_rise",0.9), @(44000,"fx_gentle_cruise",0.8), @(62000,"fx_weightless_rise",0.8), @(74000,"fx_dive",0.9),
    @(96000,"fx_gentle_cruise",0.7), @(106000,"fx_fog_push",0.7), @(114000,"fx_gentle_cruise",0.75), @(132000,"fx_canyon_sweep",0.8),
    @(156000,"fx_turbulence",0.8), @(170000,"fx_impact_vibration",1.0), @(182000,"fx_turbulence",0.7), @(196000,"fx_weightless_fall",0.8),
    @(216000,"fx_dive",1.0), @(224000,"fx_heat_wave",0.7), @(232000,"fx_weightless_fall",0.9), @(246000,"fx_water_blast",0.9), @(258000,"fx_dive",0.8),
    @(276000,"fx_canyon_sweep",0.9), @(286000,"fx_fog_push",0.6), @(294000,"fx_gentle_cruise",0.7), @(312000,"fx_canyon_sweep",0.8),
    @(322000,"fx_snow_drift",0.7), @(336000,"fx_weightless_rise",1.0), @(350000,"fx_weightless_rise",0.8), @(366000,"fx_gentle_cruise",0.75), @(380000,"fx_weightless_fall",0.9),
    @(396000,"fx_dive",1.0), @(400000,"fx_heat_wave",0.6), @(406000,"fx_strobe",1.0), @(408000,"fx_weightless_rise",0.9), @(418000,"fx_strobe",0.9),
    @(420000,"fx_turbulence",0.9), @(430000,"fx_strobe",1.0), @(432000,"fx_impact_vibration",1.0), @(440000,"fx_canyon_sweep",0.8), @(444000,"fx_strobe",0.8),
    @(456000,"fx_emergency_stop",1.0), @(466000,"fx_turbulence",0.7), @(480000,"fx_gentle_cruise",0.7), @(496000,"fx_weightless_fall",0.8),
    @(516000,"fx_gentle_cruise",0.7), @(534000,"fx_canyon_sweep",0.8), @(542000,"fx_snow_drift",0.6), @(552000,"fx_gentle_cruise",0.7)
)

# ── 环境门控 (0/1, 直接落在特效轨) ──────────────────────────
$gates = @(
    @("wind", 30000, 90000), @("wind", 210000, 270000), @("wind", 330000, 450000),
    @("fog", 118000, 150000), @("fog", 270000, 284000), @("fog", 420000, 450000)
)

# ── 章节 ────────────────────────────────────────────────────
$chapters = @(
    @(0,"开场·待机迎宾"), @(30000,"第一幕·启动升空"), @(90000,"第二幕·平稳巡航"), @(150000,"第三幕·气流颠簸"),
    @(210000,"第四幕·俯冲"), @(270000,"第五幕·峡谷穿越"), @(330000,"第六幕·失重攀升"), @(390000,"第七幕·高潮过山车"),
    @(450000,"第八幕·紧急制动"), @(510000,"第九幕·返场巡航"), @(570000,"尾声·回中收束")
)

# ── 构建 ────────────────────────────────────────────────────
$trackClips = @{}
foreach ($t in $tracks) { $trackClips[$t.id] = New-Object System.Collections.ArrayList }
$actionInstances = New-Object System.Collections.ArrayList
$events = New-Object System.Collections.ArrayList
$markers = New-Object System.Collections.ArrayList

# 章节标记 + 场景事件
foreach ($c in $chapters) {
    $markers.Add([ordered]@{ time_ms=[double]$c[0]; name=$c[1]; color="#94A3B8"; note="章节"; lane="chapters" }) | Out-Null
    $events.Add([ordered]@{ time_ms=[double]$c[0]; event_name="scene.chapter"; event_data=("{`"chapter`":`"$($c[1])`"}"); color="#38BDF8"; data_type="json"; lane="scene" }) | Out-Null
}

$cueIndex = 0
foreach ($cue in $cues) {
    $cueIndex++
    $time = [double]$cue[0]
    $preset = $presetById[$cue[1]]
    $intensity = [double]$cue[2]
    $actId = "act_{0:D2}_{1}" -f $cueIndex, ($preset.id -replace '^fx_','')
    $dur = [double]$preset.duration

    $roleTrackIds = [ordered]@{}
    $rolesUsed = @()
    foreach ($chan in $preset.channels) {
        $role = $chan.role
        $tid = $roleTrack[$role]
        if (-not $tid) { throw "角色 '$role' 未绑定轨道" }
        $rolesUsed += $role
        if (-not $roleTrackIds.Contains($tid)) { $roleTrackIds[$role] = $tid }

        $neutral = 0.0
        foreach ($t in $tracks) { if ($t.id -eq $tid) { $neutral = [double]$t.neutral } }

        $kfs = New-Object System.Collections.ArrayList
        foreach ($k in $chan.kfs) {
            [void]$kfs.Add((Kf ([double]$k[0]) (ScaleVal ([double]$k[1]) $neutral $intensity) $k[2]))
        }
        $clipId = "clip_{0:D2}_{1}" -f $cueIndex, ($role)
        $clip = [ordered]@{
            id = $clipId
            start_ms = $time
            end_ms = ($time + $dur)
            keyframes = @($kfs)
            action_instance_id = $actId
            source_definition_id = $preset.id
            source_role = $role
            source_action_name = $preset.name
        }
        $trackClips[$tid].Add([pscustomobject]@{ start=$time; end=($time+$dur); clip=$clip }) | Out-Null
    }

    $actionInstances.Add([ordered]@{
        id=$actId; definition_id=$preset.id; definition_revision=1; name=$preset.name
        start_ms=$time; duration_ms=$dur; intensity=$intensity; playback_rate=1.0; loop_count=1; enabled=$true
        role_track_ids=$roleTrackIds
    }) | Out-Null

    $markers.Add([ordered]@{ time_ms=$time; name=$preset.name; color="#C084FC"; note=("预设 · 强度 {0:N2}" -f $intensity); lane="cues" }) | Out-Null
    $events.Add([ordered]@{ time_ms=$time; event_name=("fx." + ($preset.id -replace '^fx_','')); event_data=("{`"intensity`":$intensity}"); color="#FB7185"; data_type="json"; lane="effects" }) | Out-Null
}

# 环境门控 clip
$gateIndex = 0
foreach ($g in $gates) {
    $gateIndex++
    $role = $g[0]; $t0 = [double]$g[1]; $t1 = [double]$g[2]
    $tid = $roleTrack[$role]
    $clip = [ordered]@{
        id = "clip_gate_{0:D2}_{1}" -f $gateIndex, $role
        start_ms = $t0; end_ms = $t1
        keyframes = @((Kf 0 0 "step"), (Kf 300 1 "ease_in_out"), (Kf ($t1-$t0-300) 1 "ease_in_out"), (Kf ($t1-$t0) 0 "ease_in_out"))
    }
    $trackClips[$tid].Add([pscustomobject]@{ start=$t0; end=$t1; clip=$clip }) | Out-Null
    $events.Add([ordered]@{ time_ms=$t0; event_name=("env." + $role + ".on"); event_data=("{`"until_ms`":$t1}"); color="#38BDF8"; data_type="json"; lane="effects" }) | Out-Null
    $markers.Add([ordered]@{ time_ms=$t0; name=("环境 · " + $role); color="#7DD3FC"; note="门控开启"; lane="cues" }) | Out-Null
}

# 安全与状态轨 (整段)
$trackClips["trk_enable"].Add([pscustomobject]@{ start=0.0; end=$DurationMs; clip=[ordered]@{ id="clip_enable_on"; start_ms=0.0; end_ms=$DurationMs; keyframes=@((Kf 0 0 "step"),(Kf 100 1 "step"),(Kf $DurationMs 1 "step")) } }) | Out-Null
$trackClips["trk_reset"].Add([pscustomobject]@{ start=0.0; end=600.0; clip=[ordered]@{ id="clip_reset_boot"; start_ms=0.0; end_ms=600.0; keyframes=@((Kf 0 0 "step"),(Kf 100 1 "step"),(Kf 400 1 "step"),(Kf 600 0 "step")) } }) | Out-Null
$trackClips["trk_reset"].Add([pscustomobject]@{ start=458000.0; end=458600.0; clip=[ordered]@{ id="clip_reset_recover"; start_ms=458000.0; end_ms=458600.0; keyframes=@((Kf 0 0 "step"),(Kf 100 1 "step"),(Kf 400 1 "step"),(Kf 600 0 "step")) } }) | Out-Null
$trackClips["trk_emergency"].Add([pscustomobject]@{ start=456000.0; end=458000.0; clip=[ordered]@{ id="clip_estop"; start_ms=456000.0; end_ms=458000.0; keyframes=@((Kf 0 0 "step"),(Kf 200 1 "step"),(Kf 1800 1 "step"),(Kf 2000 0 "step")) } }) | Out-Null
$trackClips["trk_speed"].Add([pscustomobject]@{ start=0.0; end=$DurationMs; clip=[ordered]@{ id="clip_speed_profile"; start_ms=0.0; end_ms=$DurationMs; keyframes=@(
    (Kf 0 0.2 "ease_in_out"),(Kf 30000 0.5 "ease_in_out"),(Kf 90000 0.6 "ease_in_out"),(Kf 210000 0.75 "ease_in_out"),
    (Kf 330000 0.9 "ease_in_out"),(Kf 450000 0.35 "ease_in_out"),(Kf 510000 0.3 "ease_in_out"),(Kf 570000 0.15 "ease_in_out"),(Kf 600000 0.0 "ease_in_out")) } }) | Out-Null

# 同轨重叠检查 (重叠会被求值器静默遮蔽)
$overlaps = New-Object System.Collections.ArrayList
foreach ($tid in $trackClips.Keys) {
    $list = @($trackClips[$tid] | Sort-Object start)
    for ($i=0; $i -lt $list.Count-1; $i++) {
        if ($list[$i+1].start -lt $list[$i].end - 1.0) {
            $overlaps.Add(("轨道 {0}: [{1}..{2}] 与 [{3}..{4}] 重叠" -f $tid,$list[$i].start,$list[$i].end,$list[$i+1].start,$list[$i+1].end)) | Out-Null
        }
    }
}
if ($overlaps.Count -gt 0) {
    Write-Host "发现同轨重叠:" -ForegroundColor Red
    $overlaps | ForEach-Object { Write-Host "  $_" }
    throw "存在同轨 clip 重叠, 生成中止"
}

# 组装轨道 JSON
$trackJson = New-Object System.Collections.ArrayList
foreach ($t in $tracks) {
    $o = [ordered]@{
        id=$t.id; device=$Device; oaction=""; output_type="oaxis"; oaxis_channel=$t.ch
        label=$t.label; color=$t.color; value_type="float"; muted=$false; enabled=$true; locked=$false
        group=$t.group; role=$t.role; show_in_curve=$true; neutral_value=[double]$t.neutral
    }
    if ($null -ne $t.range) { $o["engineering_range"] = [double]$t.range }
    if ($t.Contains("idle") -and $null -ne $t.idle) {
        $idle = $t.idle
        $ikfs = New-Object System.Collections.ArrayList
        foreach ($k in $idle[2]) { [void]$ikfs.Add((Kf ([double]$k[0]) ([double]$k[1]) "bezier")) }
        $o["idle"] = [ordered]@{
            enabled=$true; period_ms=[double]$idle[0]; blend_ms=400.0; phase_mode="continuous"
            phase_offset_ms=[double]$idle[1]; group_id="idle_main"; keyframes=@($ikfs)
        }
    }
    $o["clips"] = @($trackClips[$t.id] | Sort-Object start | ForEach-Object { $_.clip })
    $trackJson.Add($o) | Out-Null
}

$timeline = [ordered]@{
    version="3.0"
    name="10 分钟动感影院完整演示 - Simulator 联调"
    duration_ms=$DurationMs
    fps=60
    tracks=@($trackJson)
    action_instances=@($actionInstances)
    role_track_bindings=$roleTrack
    embedded_presets=@($presets | ForEach-Object {
        [ordered]@{
            id=$_.id; revision=1; name=$_.name; category=$_.category; description=$_.desc
            duration_ms=[double]$_.duration; default_intensity=0.8; min_intensity=0.2; max_intensity=1.2
            channels=@($_.channels | ForEach-Object {
                [ordered]@{ role=$_.role; scale=1.0; keyframes=@($_.kfs | ForEach-Object { Kf ([double]$_[0]) ([double]$_[1]) $_[2] }) }
            })
        }
    })
    groups=@(
        [ordered]@{ name="主运动"; color="#64748B"; collapsed=$false },
        [ordered]@{ name="环境特效"; color="#F97316"; collapsed=$false },
        [ordered]@{ name="安全与状态"; color="#22C55E"; collapsed=$false }
    )
    events=@($events)
    markers=@($markers)
    event_lanes=@(
        [ordered]@{ id="scene"; name="场景节点"; color="#38BDF8" },
        [ordered]@{ id="effects"; name="特效触发"; color="#FB7185" }
    )
    marker_lanes=@(
        [ordered]@{ id="chapters"; name="章节"; color="#94A3B8" },
        [ordered]@{ id="cues"; name="预设点"; color="#C084FC" }
    )
}

$json = $timeline | ConvertTo-Json -Depth 40
$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $json, (New-Object System.Text.UTF8Encoding($false)))

$clipCount = 0; foreach ($t in $tracks) { $clipCount += $trackClips[$t.id].Count }
Write-Host ("已生成: {0}" -f $OutFile) -ForegroundColor Green
Write-Host ("  轨道 {0} / clip {1} / 动作实例 {2} / 预设 {3} / 事件 {4} / 标记 {5}" -f $tracks.Count,$clipCount,$actionInstances.Count,$presets.Count,$events.Count,$markers.Count)
