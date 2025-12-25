# 修复 IODevice.h 文件中的 GetRawKeyValue 声明缩进问题

$filePath = "o:\DevModules\IODevice\IODevice\IODevice\Source\Public\IODevice.h"

Write-Host "正在修复 IODevice.h..."

# 读取文件内容（作为字节数组以处理二进制文件）
$content = [System.IO.File]::ReadAllText($filePath)

# 修复缩进问题 - 将不正确的缩进替换为正确的4空格缩进
$pattern = '(?m)^(\s*)float GetAxisKey\(const FKey& InKey\);\r?\n\s+float GetRawKeyValue\(const FKey& InKey\);'
$replacement = '    float GetAxisKey(const FKey& InKey);' + "`r`n" + '    float GetRawKeyValue(const FKey& InKey);'

if ($content -match $pattern) {
    $content = $content -replace $pattern, $replacement
    [System.IO.File]::WriteAllText($filePath, $content)
    Write-Host "✓ 已修复 GetRawKeyValue 缩进" -ForegroundColor Green
}
else {
    Write-Host "! 未找到需要修复的模式，可能已经修复或格式不同" -ForegroundColor Yellow
    
    # 显示相关行以便调试
    Write-Host "`n当前 GetAxisKey 附近的内容："
    $lines = $content -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "GetAxisKey") {
            for ($j = [Math]::Max(0, $i - 2); $j -le [Math]::Min($lines.Count - 1, $i + 3); $j++) {
                $prefix = if ($j -eq $i) { ">>> " } else { "    " }
                Write-Host "$prefix$($lines[$j])"
            }
            break
        }
    }
}

Write-Host "`n完成！"
