# S — 一键验证入口
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts\check.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\check.ps1 -Task tests
#   powershell -ExecutionPolicy Bypass -File scripts\check.ps1 -Task shots
#
# 任务：tests（结算断言） / setup（场景+设置+着色器） / assets（模型朝向契约+对照图）
#       / build（打包） / smoke（正常启动路径：菜单/光标/遥测） / shots（实机自检+截图） / all
#
# 判据：每个任务都必须打印 "RESULT ..." 行；任何 RESULT 含 FAIL 或退出码非 0 都视为失败。

param(
    [ValidateSet("all", "tests", "setup", "assets", "ui", "build", "smoke", "shots")]
    [string]$Task = "all"
)

$ErrorActionPreference = "Stop"

$Proj     = Split-Path -Parent $PSScriptRoot

# 编辑器路径从枢纽 profile 取，不写死盘符。
# 查找顺序：$env:UNITY_EXE → $env:HUB_ROOT → 与工程同级的 Unity Development。
$Unity = $env:UNITY_EXE
if (-not $Unity) {
    $candidates = @()
    if ($env:HUB_ROOT) { $candidates += (Join-Path $env:HUB_ROOT "machine\profile.ps1") }
    $candidates += (Join-Path (Split-Path $Proj -Parent) "Unity Development\machine\profile.ps1")
    foreach ($p in $candidates) {
        if (Test-Path $p) {
            . $p
            try { $Unity = Get-UnityEditor -Version "6000.6.4f1"; break } catch { }
        }
    }
}
if (-not $Unity -or -not (Test-Path $Unity)) {
    Write-Host "找不到 Unity 编辑器。设 `$env:UNITY_EXE 或 `$env:HUB_ROOT，或在 machine/profile.local.json 填 tools.unityEditorRoot。" -ForegroundColor Red
    Write-Host "  尝试过：$Unity" -ForegroundColor DarkGray
    exit 3
}

$Exe      = Join-Path $Proj "Build\Windows\S.exe"
$LogDir   = Join-Path $Proj "Logs"
$TestProj = Join-Path $Proj "tools\loadout-tests"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$script:Failed = @()

function Write-Section($t) {
    Write-Host ""
    Write-Host ("=" * 62) -ForegroundColor DarkCyan
    Write-Host "  $t" -ForegroundColor Cyan
    Write-Host ("=" * 62) -ForegroundColor DarkCyan
}

function Invoke-UnityMethod($method, $logName) {
    $log = Join-Path $LogDir $logName
    if (Test-Path $log) { Remove-Item $log -Force }

    # 工程被编辑器占用时，batchmode 拿不到锁会「启动即退出」或长时间干等，
    # 日志里只有一句 Aborting、没有 error CS，表现为莫名其妙的超时。
    # 先快速判掉，给出可操作的提示，而不是让人等 30 分钟。
    $lock = Join-Path $Proj "Temp\UnityLockfile"
    if (Test-Path $lock) {
        $editor = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
                  Where-Object { $_.CommandLine -like "*$Proj*" -and $_.CommandLine -notlike "*-batchMode*" }
        Write-Host "工程锁被占用：Temp\UnityLockfile 存在" -ForegroundColor Red
        if ($editor) {
            Write-Host "  占用进程：PID $($editor.ProcessId -join ',')（Unity 编辑器正打开本工程）" -ForegroundColor Yellow
            Write-Host "  请先关闭该编辑器窗口再重跑，否则 batchmode 无法编译。" -ForegroundColor Yellow
        }
        throw "工程被占用，无法执行 $method"
    }

    Write-Host "unity -executeMethod $method" -ForegroundColor DarkGray
    # 参数必须逐个带引号：Start-Process 的 -ArgumentList 是把数组拼成一条命令行，
    # 路径含空格（如 "F:\Unity Development\Project\S\unity"）时，
    # -projectPath 会只吃到空格前那一段，Unity 报
    # "Couldn't set project path to: ...\unity/F:/Unity" 并立刻退出（踩过）。
    $args = @(
        "-batchmode", "-quit",
        "-projectPath", "`"$Proj`"",
        "-executeMethod", $method,
        "-logFile", "`"$log`""
    )
    $p = Start-Process -FilePath $Unity -PassThru -NoNewWindow -ArgumentList $args
    $p.WaitForExit(1800000) | Out-Null
    if (-not $p.HasExited) { $p.Kill(); throw "Unity 超时: $method" }

    $errors = @()
    if (Test-Path $log) {
        $errors = Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue |
                  ForEach-Object { $_.Line.Trim() } | Select-Object -Unique
    }
    if ($errors.Count -gt 0) {
        Write-Host "编译错误：" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "编译失败"
    }
    return @{ Exit = $p.ExitCode; Log = $log }
}

function Task-Ui {
    Write-Section "ui — 素材前期处理 / 合成 / 边距审计 / 重导"

    # 顺序不能颠倒，每一步都是下一步的前提：
    #   1. 前期处理：抠图 + 尺寸规范化 + 质量门。不过就让后面全部作废。
    #   2. 合成：把材质和精确几何拼成最终 UI 图。
    #   3. 审计：按控件真实像素核对 9-slice 边距。
    #   4. 重导：让 BorderFor 的新边距真正写进 .meta。
    #      漏了这步会出现"代码改了、测试也过、画面还是坏的"（踩过）。

    Write-Host ">>> 1/4 前期处理（抠图 / 尺寸 / 质量门）" -ForegroundColor Cyan
    python tools\preprocess_ui_assets.py
    if ($LASTEXITCODE -ne 0) {
        $script:Failed += "ui-preprocess"
        Write-Host "RESULT ui FAIL（前期处理未通过，已拦下，不再合成）" -ForegroundColor Red
        return
    }

    Write-Host ">>> 2/4 合成（AI 材质 + 精确几何）" -ForegroundColor Cyan
    python tools\gen_ui_assets_ai.py
    if ($LASTEXITCODE -ne 0) {
        $script:Failed += "ui-compose"
        Write-Host "RESULT ui FAIL（合成失败）" -ForegroundColor Red
        return
    }

    Write-Host ">>> 3/4 边距审计（按控件真实像素）" -ForegroundColor Cyan
    python tools\audit_ui_slices.py
    if ($LASTEXITCODE -ne 0) {
        $script:Failed += "ui-audit"
        Write-Host "RESULT ui FAIL（9-slice 边距与控件尺寸不匹配）" -ForegroundColor Red
        return
    }

    Write-Host ">>> 4/4 重新导入贴图（让新边距写进 .meta）" -ForegroundColor Cyan
    $r = Invoke-UnityMethod "S.EditorTools.SUiTextureReimport.ReimportAll" "uireimport.log"
    if (Select-String -Path $r.Log -Pattern "RESULT UI 贴图重导" -Quiet) {
        Write-Host "RESULT ui OK" -ForegroundColor Green
    } else {
        $script:Failed += "ui-reimport"
        Write-Host "RESULT ui FAIL（贴图重导失败）" -ForegroundColor Red
    }

    # 统一字体：预制体里 72 处 m_Font 是硬 GUID 引用，字体导入失败时 Unity **不报错**，
    # 只是所有文字变成 Missing (Font)。必须单独断言，否则只能靠肉眼发现。
    # 空 fontNames / 手写 m_Font 还会出现「GUID 对得上但动态贴图为空、字全没」——
    # 所以这里跑 Rebind（重导 + 重赋 Text.font + Verify），不能只跑 Verify。
    Write-Host ">>> 5/5 统一字体（重导 + 重绑 + 校验）" -ForegroundColor Cyan
    $rf = Invoke-UnityMethod "S.EditorTools.SUiFontSetup.RebindBatch" "fontverify.log"
    if ((Select-String -Path $rf.Log -Pattern "RESULT UI 字体重绑 完成" -Quiet) -and
        (Select-String -Path $rf.Log -Pattern "RESULT UI 字体 全部通过" -Quiet)) {
        Write-Host "RESULT ui-font OK" -ForegroundColor Green
    } else {
        $script:Failed += "ui-font"
        Write-Host "RESULT ui FAIL（统一字体未通过）" -ForegroundColor Red
        Select-String -Path $rf.Log -Pattern "FAIL |RESULT UI 字体" | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
    }
}

function Task-Tests {
    Write-Section "tests — 配件结算断言（dotnet，零 Unity 依赖）"
    Push-Location $TestProj
    try {
        $out = & dotnet run -c Release 2>&1
        $code = $LASTEXITCODE
    } finally { Pop-Location }

    $out | ForEach-Object { Write-Host $_ }
    $line = $out | Select-String -Pattern "^RESULT (.*)$" | Select-Object -Last 1
    if ($code -ne 0 -or -not $line) {
        $script:Failed += "tests"
        Write-Host "RESULT FAIL (dotnet exit=$code)" -ForegroundColor Red
    } else {
        Write-Host "RESULT $($line.Matches[0].Groups[1].Value)" -ForegroundColor Green
    }

    # UI 素材分类一致性（纯 Python，秒级）。
    # 为什么放在 tests 里：分类表存在 C# + 两个 Python 共三份，
    # 漂移的症状是**静默的**（Resources.Load 返回 null，界面退化成纯色块，不报错）。
    # 不接进自动化，就只能在看图时偶然发现。详见 docs/ui-asset-taxonomy.md。
    Write-Section "tests — UI 素材分类一致性"
    $tax = & python (Join-Path $Proj "tools\audit_ui_taxonomy.py") 2>&1
    $taxCode = $LASTEXITCODE
    $tax | ForEach-Object { Write-Host $_ }
    if ($taxCode -ne 0) {
        $script:Failed += "taxonomy"
        Write-Host "RESULT UI 素材分类 DRIFT" -ForegroundColor Red
    } else {
        Write-Host "RESULT UI 素材分类 OK" -ForegroundColor Green
    }
}

function Task-Setup {
    Write-Section "setup — 生成主场景 / 工程设置 / Always Included Shaders"
    $r = Invoke-UnityMethod "S.EditorTools.SceneBuilder.BatchSetup" "setup.log"
    $ok = Select-String -Path $r.Log -Pattern "BatchSetup 完成" -Quiet
    if ($ok) { Write-Host "RESULT setup OK" -ForegroundColor Green }
    else { $script:Failed += "setup"; Write-Host "RESULT setup FAIL" -ForegroundColor Red }
}

function Task-Assets {
    Write-Section "assets — 模型朝向契约（上=+Y / 前=+Z）+ 全模型对照图"
    $r = Invoke-UnityMethod "S.EditorTools.SModelContract.VerifyBatch" "contract.log"
    $line = Select-String -Path $r.Log -Pattern "RESULT 模型朝向 (\d+)/(\d+) passed" | Select-Object -Last 1
    $hasFail = Select-String -Path $r.Log -Pattern "^FAIL Modle" -Quiet
    if ($line -and -not $hasFail) {
        Write-Host "RESULT $($line.Matches[0].Value -replace '^RESULT ','')" -ForegroundColor Green
    } else {
        $script:Failed += "assets"
        Write-Host "RESULT 模型朝向 FAIL" -ForegroundColor Red
        Select-String -Path $r.Log -Pattern "^FAIL Modle" | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
    }

    # 对照图：把全部模型摆进同一场景渲一张，便于人工核对朝向与比例
    Invoke-UnityMethod "S.EditorTools.SModelSheet.RenderBatch" "modelsheet.log" | Out-Null
    if (Test-Path (Join-Path $Proj "Shots\models_all.png")) {
        Write-Host "SHEET Shots\models_all.png" -ForegroundColor DarkGray
    }

    # 端到端：直接量 SAssets.SpawnFit（运行时真正用的那条路径）的产物是否直立落地。
    # 契约那步只证明 FBX 导入正确，而本次 bug 就出在 SpawnFit 把导入旋转清零，
    # 所以必须有这一步，否则同类回归会静默通过。
    $r2 = Invoke-UnityMethod "S.EditorTools.SModelContract.VerifySpawnBatch" "spawn.log"
    $line2 = Select-String -Path $r2.Log -Pattern "RESULT 运行时生成 (\d+)/(\d+) passed" | Select-Object -Last 1
    $hasFail2 = Select-String -Path $r2.Log -Pattern "^FAIL Modle" -Quiet
    if ($line2 -and -not $hasFail2) {
        Write-Host "RESULT $($line2.Matches[0].Value -replace '^RESULT ','')" -ForegroundColor Green
    } else {
        $script:Failed += "spawn"
        Write-Host "RESULT 运行时生成 FAIL" -ForegroundColor Red
        Select-String -Path $r2.Log -Pattern "^FAIL Modle" | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
    }

    # 编辑器输入路由：必须让鼠标在 Play 时进游戏。
    # 默认值 PointersAndKeyboardsRespectGameViewFocus 会在 Game 视图失焦时
    # 把鼠标输入路由给编辑器，表现就是「编辑器里鼠标完全没反应，但打包版正常」。
    # SInputFix 是幂等的：已是目标值就什么都不做，所以可以每次都跑。
    $r3 = Invoke-UnityMethod "S.EditorTools.SInputFix.ApplyBatch" "inputfix.log"
    $ok3 = Select-String -Path $r3.Log -Pattern "落盘校验.*结果=OK" -Quiet
    if ($ok3) {
        Write-Host "RESULT 编辑器输入路由 OK" -ForegroundColor Green
    } else {
        $script:Failed += "input"
        Write-Host "RESULT 编辑器输入路由 FAIL" -ForegroundColor Red
        Select-String -Path $r3.Log -Pattern "\[S\] " | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
    }
}

function Task-Smoke {
    Write-Section "smoke — 正常启动路径（菜单/光标/遥测）"
    if (-not (Test-Path $Exe)) {
        $script:Failed += "smoke"
        Write-Host "RESULT smoke FAIL（没有 exe，先跑 build）" -ForegroundColor Red
        return
    }

    # 同 shots：残留实例会让新进程静默退出、不写日志（Force Single Instance）。
    $stale = Get-Process -Name "S" -ErrorAction SilentlyContinue
    if ($stale) { $stale | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800 }

    $log = Join-Path $Proj "smoke.log"
    if (Test-Path $log) { Remove-Item $log -Force }

    # 这条跑的是**正常启动**（不跳过菜单），覆盖 -scheck 刻意关掉的那段：
    # 菜单能否显示、光标是否可点、遥测是否开启、点开始后世界是否真的运转。
    $p = Start-Process -FilePath $Exe -PassThru -ArgumentList @(
        "-smoke", "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0"
    )
    $p.WaitForExit(180000) | Out-Null
    if (-not $p.HasExited) { $p.Kill(); $script:Failed += "smoke"; Write-Host "RESULT smoke FAIL（超时）" -ForegroundColor Red; return }

    if (Test-Path $log) {
        Get-Content $log | ForEach-Object { Write-Host $_ }
        $line = Select-String -Path $log -Pattern "SMOKE RESULT (.*)$" | Select-Object -Last 1
        $fails = Select-String -Path $log -Pattern "  FAIL " -Quiet
        if ($line -and -not $fails -and $p.ExitCode -eq 0) {
            Write-Host "RESULT smoke $($line.Matches[0].Groups[1].Value)" -ForegroundColor Green
        } else {
            $script:Failed += "smoke"
            Write-Host "RESULT smoke FAIL (exit=$($p.ExitCode))" -ForegroundColor Red
        }
    } else {
        $script:Failed += "smoke"
        Write-Host "RESULT smoke FAIL（未生成 smoke.log，常见原因：残留 S.exe）" -ForegroundColor Red
    }
}

function Task-Build {
    Write-Section "build — 打包 Windows x64"
    $r = Invoke-UnityMethod "S.EditorTools.BuildPlayer.BuildBatch" "build.log"
    $res = Select-String -Path $r.Log -Pattern "BUILD RESULT (\w+)" | Select-Object -Last 1
    $err = Select-String -Path $r.Log -Pattern "BUILD ERRORS (\d+)"    | Select-Object -Last 1
    if ($res -and $res.Matches[0].Groups[1].Value -eq "Succeeded" -and (Test-Path $Exe)) {
        Write-Host "RESULT build Succeeded ($($err.Matches[0].Groups[1].Value) errors)" -ForegroundColor Green
    } else {
        $script:Failed += "build"
        Write-Host "RESULT build FAIL" -ForegroundColor Red
        return
    }

    # 配置表必须真的进包。`.tsv` 不在 Unity 默认文本扩展名清单里，
    # 少了 STsvImporter 时文件会被**静默丢弃** —— 编译 0 错误、build Succeeded，
    # 只有运行起来才发现「缺表」。所以这里直接查构建产物里有没有这些文件。
    $cfgSrc = Join-Path $Proj "Assets\Resources\Data\Cfg"
    $assets = Join-Path $Proj "Build\Windows\S_Data\resources.assets"
    $missing = @()
    if (-not (Test-Path $assets)) {
        $script:Failed += "build-cfg"
        Write-Host "RESULT build FAIL（找不到 resources.assets，构建产物不完整）" -ForegroundColor Red
        return
    }
    Get-ChildItem $cfgSrc -Filter *.tsv | ForEach-Object {
        # TextAsset 的正文会以原文形式落在 resources.assets 里，
        # 表名与首个字段名都会出现；两者都查不到才算没进包。
        if (-not (Select-String -Path $assets -Pattern $_.BaseName -Quiet -ErrorAction SilentlyContinue)) {
            $missing += $_.BaseName
        }
    }
    if ($missing.Count -gt 0) {
        $script:Failed += "build-cfg"
        Write-Host "RESULT build FAIL（配置表未进包：$($missing -join ', ')）" -ForegroundColor Red
        Write-Host "  检查 Assets/_Game/Editor/STsvImporter.cs 是否生效，或改为 .txt 后缀" -ForegroundColor Yellow
    } else {
        Write-Host "RESULT 配置表进包 $((Get-ChildItem $cfgSrc -Filter *.tsv).Count) 张" -ForegroundColor Green
    }
}

function Task-Shots {
    Write-Section "shots — 实机自检 + 截图"
    if (-not (Test-Path $Exe)) {
        $script:Failed += "shots"
        Write-Host "RESULT shots FAIL（没有 exe，先跑 build）" -ForegroundColor Red
        return
    }

    # 必须清掉残留实例：Unity 播放器默认开启 Force Single Instance，
    # 上一个 S.exe 还在时新进程会「立刻静默退出、不写日志」，表现为
    # RESULT shots FAIL（未生成 selfcheck.log），极难定位（踩过）。
    $stale = Get-Process -Name "S" -ErrorAction SilentlyContinue
    if ($stale) {
        Write-Host "清理残留 S.exe: $($stale.Count) 个" -ForegroundColor Yellow
        $stale | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
    }

    $sc = Join-Path $Proj "selfcheck.log"
    if (Test-Path $sc) { Remove-Item $sc -Force }

    $p = Start-Process -FilePath $Exe -PassThru -ArgumentList @(
        "-scheck", "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0"
    )
    $p.WaitForExit(180000) | Out-Null
    if (-not $p.HasExited) { $p.Kill(); $script:Failed += "shots"; Write-Host "RESULT shots FAIL（超时）" -ForegroundColor Red; return }

    if (Test-Path $sc) {
        Get-Content $sc | ForEach-Object { Write-Host $_ }
        $line = Select-String -Path $sc -Pattern "SELFCHECK RESULT (.*)$" | Select-Object -Last 1
        $fails = Select-String -Path $sc -Pattern "  FAIL " -Quiet
        if ($line -and -not $fails -and $p.ExitCode -eq 0) {
            Write-Host "RESULT shots $($line.Matches[0].Groups[1].Value)" -ForegroundColor Green
        } else {
            $script:Failed += "shots"
            Write-Host "RESULT shots FAIL (exit=$($p.ExitCode))" -ForegroundColor Red
        }
    } else {
        $script:Failed += "shots"
        Write-Host "RESULT shots FAIL（未生成 selfcheck.log）" -ForegroundColor Red
        Write-Host "  常见原因：残留 S.exe 占用（Force Single Instance 会让新进程静默退出）" -ForegroundColor Yellow
        $alive = Get-Process -Name "S" -ErrorAction SilentlyContinue
        if ($alive) { Write-Host "  当前仍有 $($alive.Count) 个 S.exe 在运行" -ForegroundColor Yellow }
        $plog = Join-Path $env:USERPROFILE "AppData\LocalLow\S Project\ProjectS\Player.log"
        if (Test-Path $plog) { Write-Host "--- Player.log 尾部 ---" -ForegroundColor Yellow; Get-Content $plog -Tail 30 }
    }

    $shots = Join-Path $Proj "Shots"
    if (Test-Path $shots) {
        $n = (Get-ChildItem $shots -Filter *.png).Count
        Write-Host "SHOTS $n 张 -> $shots" -ForegroundColor DarkGray
    }
}

switch ($Task) {
    "tests"  { Task-Tests }
    "setup"  { Task-Setup }
    "assets" { Task-Assets }
    # ui 单独一条，不进 all：只在改素材时需要，且要重导 32 张图，拖慢全量。
    "ui"     { Task-Ui }
    "build"  { Task-Build }
    "shots"  { Task-Shots }
    "smoke"  { Task-Smoke }
    "all"    { Task-Tests; Task-Setup; Task-Assets; Task-Build; Task-Smoke; Task-Shots }
}

Write-Host ""
if ($script:Failed.Count -eq 0) {
    Write-Host "RESULT ALL PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT FAILED: $($script:Failed -join ', ')" -ForegroundColor Red
    exit 1
}
