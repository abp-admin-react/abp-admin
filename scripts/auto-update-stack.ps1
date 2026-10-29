# 基础镜像自动更新：Windows 计划任务 AbpAdminStackAutoUpdate（周日 07:00）调用，也可手工
# 执行。Dockerfile 已是浮动 tag，本脚本补上"自动跟进"的另一半：显式 pull 底座 → digest
# 快照对比 → 有变化才构建 + up -d 滚动应用 → 健康不过自动回滚；无变化的周栈零动作。
# 机制要点（产物 ID 不可作信号、docker.io 故障降级、PS 5.1 坑）见各步骤行内注释。
# 日志：logs/auto-update.log。
# 手工回滚：docker tag abp-admin-host:prev abp-admin-host:latest && docker compose up -d
# 删除自动更新：schtasks /Delete /TN AbpAdminStackAutoUpdate /F

$ErrorActionPreference = 'Stop'
# 本脚本固定在 scripts/ 下，仓库根即其父目录；计划任务的工作目录不可靠，必须显式定位
$repo = Split-Path -Path $PSScriptRoot -Parent
Set-Location $repo

$logDir = Join-Path $repo 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'auto-update.log'

function Log([string]$msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Write-Host $line
    # 显式 UTF8：PS 5.1 的 Add-Content 缺省按系统 ANSI（本机 GBK）写，中文日志会变乱码
    Add-Content -Path $log -Value $line -Encoding UTF8
}

# 原生 docker 调用：进度行走 stderr，Windows PowerShell 5.1 在 ErrorActionPreference=Stop
# 下会把 2>&1 重定向来的 stderr 行变成终止错误（实测 compose 首条进度行即中断整个流程），
# 所以这里必须临时降为 Continue——成败只认 $LASTEXITCODE，不认 stderr 噪声。
# -Quiet：丢弃输出只取退出码（docker pull 的层进度没有日志价值）。
function Run-Docker([string[]]$dockerArgs, [switch]$Quiet) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Quiet) { $null = docker @dockerArgs 2>&1 }
        else { docker @dockerArgs 2>&1 | ForEach-Object { Log ("  " + $_) } }
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prev }
}

# 底座清单 = 两个后端 Dockerfile 与 web Dockerfile 的全部浮动 tag 基础镜像。
# ⚠ 新增/更换任何 FROM 都要同步这里，否则新底座不在监控内（漏更就是漏补丁）。
$baseRefs = @(
    'mcr.microsoft.com/dotnet/sdk:10.0',     # host/migrator 构建段
    'mcr.microsoft.com/dotnet/aspnet:10.0',  # host/migrator 运行段
    'node:22-alpine',                        # web 构建段（pnpm build + 取 node 二进制）
    'node:22-bookworm-slim',                 # host 构建段 COPY --from 取 node 二进制/npm（NV-12：此前不在监控内，build 不带 --pull 时长期吃本地旧 tag）
    'nginx:1.29-alpine'                      # web 运行段
)
$services = @('web', 'host', 'migrator')
$images = @{ web = 'abp-admin-web'; host = 'abp-admin-host'; migrator = 'abp-admin-migrator' }
$statePath = Join-Path $logDir 'base-digests.json'

Log "================ 自动更新开始 ================"

try {
    # 1) 回滚保险：先把当前 :latest 打成 :prev 底片（首次无镜像时容忍失败）
    foreach ($s in $services) {
        docker tag "$($images[$s]):latest" "$($images[$s]):prev" 2>$null
        if ($LASTEXITCODE -ne 0) { Log "（$s 无现存镜像，跳过 :prev 标记）" }
    }

    # 2) 底座变化检测：pull 到本地最新（宿主已配 1ms.run 镜像源加速；digest 不变时零层
    #    传输），按 RepoDigest 对比快照。⚠ 变化信号必须用底座 digest、不能用构建产物 ID：
    #    BuildKit 全缓存命中也会刷新 created 时间戳，产物 ID 每次必变，当信号每周必误报
    #    整栈重建（首版实测）。每个 ref 重试 3 次（间隔递增）；仍失败时本地有镜像 → 降级
    #    （沿用本地版本，记入 pullFailedRefs，下周重试远端），本地也没有 → 致命失败
    #    （没它构建必挂）。背景：docker.io auth 端点在本网络被间歇重置（TLS bad record
    #    MAC 实测）、MCR 可达——降级保证 node/nginx 故障不拖挂 MCR 补丁部署。
    $snapshot = @{}
    $pullFailedRefs = @()
    foreach ($ref in $baseRefs) {
        $rc = 1
        foreach ($attempt in 1..3) {
            $rc = Run-Docker @('pull', $ref) -Quiet
            if ($rc -eq 0) { break }
            if ($attempt -lt 3) { Start-Sleep -Seconds (10 * $attempt) }
        }
        if ($rc -ne 0) {
            $localId = docker images -q $ref
            if (-not $localId) { throw "docker pull $ref 失败且本地无此镜像，无法继续（下周自动重试）" }
            $pullFailedRefs += $ref
            Log "（$ref 远端拉取失败，按本地现有版本判定，下周重试远端）"
        }
    }
    foreach ($ref in $baseRefs) {
        # pull 成功后 RepoDigests 必有值；降级 ref 用本地既有 tag 的 digest 参与对比
        $digest = docker images --digests --format '{{.Digest}}' $ref
        if (-not $digest -or $digest -eq '<none>') { throw "取不到 $ref 的 RepoDigest，快照对比不可靠，保留现栈不动" }
        $snapshot[$ref] = $digest
    }
    # 快照读取（PS 5.1 无 ConvertFrom-Json -AsHashtable，手工转哈希表）
    $previous = @{}
    if (Test-Path $statePath) {
        (Get-Content $statePath -Raw | ConvertFrom-Json).PSObject.Properties |
            ForEach-Object { $previous[$_.Name] = $_.Value }
    }
    $changedRefs = @($baseRefs | Where-Object { $previous.ContainsKey($_) -and $previous[$_] -ne $snapshot[$_] })
    $baseChanged = ($previous.Count -eq 0) -or ($changedRefs.Count -gt 0)
    if ($pullFailedRefs) {
        Log "（本周未能确认远端版本的底座：$($pullFailedRefs -join ', ')——它们真有新版时会在下周拉取成功后被检出）"
    }

    if ($baseChanged) {
        if ($previous.Count -eq 0) {
            Log '底座快照缺失（首次运行），按有更新处理。'
        } else {
            Log "底座有新版本：$($changedRefs -join ', ')"
        }

        # 3) 构建不带 --pull：底座刷新已由步骤 2 完成（可达的已进本地 tag），构建吃本地
        #    即可——否则 docker.io 故障会让整个构建失败，连 MCR 补丁都部署不了
        Log '==> compose build web host migrator'
        $rc = Run-Docker (@('compose', 'build') + $services)
        if ($rc -ne 0) { throw "compose build 失败（exit $rc）" }

        # 4) trivy 门禁（未安装则跳过；装了之后无需改本脚本）。卡的是"新底座反而带来
        #    新漏洞"的小概率换血；与发布前 security-scan.sh 三路全量门禁职责不互替
        if (Get-Command trivy -ErrorAction SilentlyContinue) {
            foreach ($s in @('web', 'host')) {
                Log "==> trivy 扫描 $($images[$s]):latest"
                trivy image --severity CRITICAL,HIGH --exit-code 1 --quiet "$($images[$s]):latest"
                if ($LASTEXITCODE -ne 0) { throw "trivy 发现 $($images[$s]):latest 有 CRITICAL/HIGH 漏洞，保留旧栈不动" }
            }
        } elseif ($env:SKIP_TRIVY_GATE -eq '1') {
            # NV-13：门禁 fail-open（缺席即跳过）= 未扫描镜像无人工知晓地滚进生产。
            # 现默认 fail-closed；显式设 SKIP_TRIVY_GATE=1 才允许带跳过继续（风险自担，留痕）。
            Log "⚠ SKIP_TRIVY_GATE=1：本次跳过镜像漏洞门禁（trivy 未安装），带风险继续发布"
        } else {
            throw ("trivy 未安装，镜像漏洞门禁无法执行——按 NV-13 fail-closed 阻断发布。" +
                "安装 trivy（winget install Aquasecurity.Trivy）后自动恢复；" +
                "确要跳过请设 SKIP_TRIVY_GATE=1（风险自担）。")
        }
    } else {
        # 底座无新版：构建与 up -d 都不执行，彻底不动运行中的栈。
        # ⚠ up -d 不能无条件跑：migrator 是一次性服务，每次 up 都会重跑，还会经 host 的
        # depends_on 把 healthy 的 host 连带重启一遍（实测 Up 时长被清零）——编排配置
        # 变更（改 compose/Dockerfile）属于手工部署范畴，不归本周期的自动化管。
        Log '基础镜像无新版本，跳过构建与 up -d，栈未动。'
        Log '================ 自动更新结束（无变化） ================'
        exit 0
    }

    # 5) up -d 滚动应用：migrator 镜像变了会随 host 的
    #    depends_on(service_completed_successfully) 自动重跑迁移种子（幂等）。
    Log '==> compose up -d'
    $rc = Run-Docker @('compose', 'up', '-d')
    if ($rc -ne 0) { throw "compose up 失败（exit $rc）" }

    # 首次运行只是建基线（快照缺失→按有更新处理重建），没有"哪些底座变了"可报
    if ($changedRefs) { Log "镜像更新完成，涉及底座：$($changedRefs -join ', ')" }
    else { Log '底座快照已建立（首次运行基线），栈已按当前底座重建。' }

    # 6) 健康观察：host 容器 healthy 才算部署成功（healthcheck 15s 间隔，给足 5 分钟）。
    #    只在实际部署后执行——底座无新版的普通一周走上面的无变化分支，不会到这里。
    $health = ''
    $deadline = (Get-Date).AddMinutes(5)
    do {
        Start-Sleep -Seconds 15
        $health = docker inspect --format '{{.State.Health.Status}}' abp-admin-host-1 2>$null
        Log "host 健康：$health"
    } while ($health -ne 'healthy' -and (Get-Date) -lt $deadline)

    if ($health -ne 'healthy') { throw 'host 5 分钟内未达 healthy，执行回滚' }

    # 全部成功才落快照：中途失败时下周按"有更新"重试，不会把失败状态误记为已处理
    $snapshot | ConvertTo-Json | Set-Content -Path $statePath -Encoding UTF8

    Log '部署成功：'
    docker exec abp-admin-host-1 dotnet --list-runtimes 2>$null | ForEach-Object { Log "  $_" }
    Log '================ 自动更新结束（成功） ================'
    exit 0
}
catch {
    Log "!! 自动更新失败：$_"
    Log '==> 尝试回滚到 :prev 镜像'
    try {
        foreach ($s in $services) {
            docker tag "$($images[$s]):prev" "$($images[$s]):latest" 2>$null
            if ($LASTEXITCODE -ne 0) { Log "（$s 无 :prev 可回滚，跳过）" }
        }
        $rc = Run-Docker @('compose', 'up', '-d')
        if ($rc -ne 0) { throw "compose up 失败（exit $rc）" }
        Log '回滚完成（若 host 起不来，看 docker compose logs host）'
    }
    catch {
        Log "!! 回滚也失败：$_ —— 需要人工介入"
    }
    Log '================ 自动更新结束（失败） ================'
    exit 1
}
