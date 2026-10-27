# 一次性注册脚本：把 AbpAdmin 基础镜像每周自动更新挂到 Windows 计划任务。
# 由主流程以 UAC 提权调用；删除任务：schtasks /Delete /TN AbpAdminStackAutoUpdate /F
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument '-NoProfile -ExecutionPolicy Bypass -File E:\code\abp_admin\scripts\auto-update-stack.ps1'
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At 07:00
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries
Register-ScheduledTask -TaskName 'AbpAdminStackAutoUpdate' `
    -Action $action -Trigger $trigger -Settings $settings `
    -Description 'AbpAdmin 基础镜像每周自动更新（--pull 重建 web/host/migrator，健康检查失败自动回滚）' -Force | Out-Null
Write-Host 'TASK-REGISTERED'
