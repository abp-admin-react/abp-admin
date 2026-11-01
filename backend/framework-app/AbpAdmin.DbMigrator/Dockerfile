# DbMigrator 镜像：一次性迁移/种子进程（compose 里先于 Host 跑，跑完退出）。
# 构建上下文必须是 backend/（同 framework-app/AbpAdmin.HttpApi.Host/Dockerfile 的约定；
# 手工构建：cd backend && docker build -f framework-app/AbpAdmin.DbMigrator/Dockerfile -t abp-admin-migrator . ）。

# ---- 构建：与 Host Dockerfile 同款分层，restore 挂在 csproj 层上 ----
# ⚠ restore 层 COPY 链必须覆盖引用闭包的全部 csproj——framework/、framework-app/ 与 modules/ 三区都要 COPY，
#   漏拷一条 dotnet restore 在容器内直接断（工程挪区/新增业务模块时同步补链）。
# 基础镜像浮动 tag；底座新鲜度由 scripts/auto-update-stack.ps1 保证（显式 pull + digest
# 快照对比，有变化才构建），机制见 HttpApi.Host/Dockerfile 顶部。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config common.props Directory.Packages.props ./
COPY framework-app/AbpAdmin.Domain.Shared/AbpAdmin.Domain.Shared.csproj framework-app/AbpAdmin.Domain.Shared/
COPY framework-app/AbpAdmin.Domain/AbpAdmin.Domain.csproj framework-app/AbpAdmin.Domain/
COPY framework/AbpAdmin.DynamicQueryable/AbpAdmin.DynamicQueryable.csproj framework/AbpAdmin.DynamicQueryable/
COPY framework/AbpAdmin.Elasticsearch/AbpAdmin.Elasticsearch.csproj framework/AbpAdmin.Elasticsearch/
COPY framework-app/AbpAdmin.EntityFrameworkCore/AbpAdmin.EntityFrameworkCore.csproj framework-app/AbpAdmin.EntityFrameworkCore/
COPY framework-app/AbpAdmin.Application.Contracts/AbpAdmin.Application.Contracts.csproj framework-app/AbpAdmin.Application.Contracts/
COPY framework-app/AbpAdmin.Application/AbpAdmin.Application.csproj framework-app/AbpAdmin.Application/
COPY framework-app/AbpAdmin.HttpApi/AbpAdmin.HttpApi.csproj framework-app/AbpAdmin.HttpApi/
COPY framework-app/AbpAdmin.HttpApi.Client/AbpAdmin.HttpApi.Client.csproj framework-app/AbpAdmin.HttpApi.Client/
COPY framework-app/AbpAdmin.Webhooks/AbpAdmin.Webhooks.csproj framework-app/AbpAdmin.Webhooks/
COPY modules/AbpAdmin.Biz.Template/AbpAdmin.Biz.Template.csproj modules/AbpAdmin.Biz.Template/
COPY framework-app/modules/AbpAdmin.Collection.Contracts/AbpAdmin.Collection.Contracts.csproj framework-app/modules/AbpAdmin.Collection.Contracts/
COPY framework-app/modules/AbpAdmin.Collection/AbpAdmin.Collection.csproj framework-app/modules/AbpAdmin.Collection/
COPY framework-app/modules/AbpAdmin.Ordering/AbpAdmin.Ordering.csproj framework-app/modules/AbpAdmin.Ordering/
COPY framework-app/modules/AbpAdmin.Ledger.Integration.Contracts/AbpAdmin.Ledger.Integration.Contracts.csproj framework-app/modules/AbpAdmin.Ledger.Integration.Contracts/
COPY framework-app/modules/AbpAdmin.Ledger/AbpAdmin.Ledger.csproj framework-app/modules/AbpAdmin.Ledger/
COPY framework-app/modules/AbpAdmin.TokenGateway/AbpAdmin.TokenGateway.csproj framework-app/modules/AbpAdmin.TokenGateway/
COPY framework-app/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj framework-app/AbpAdmin.DbMigrator/
RUN dotnet restore framework-app/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj

COPY framework/ framework/
COPY framework-app/ framework-app/
COPY modules/ modules/
RUN dotnet publish framework-app/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj \
    -c Release -o /app/publish --no-restore

# ---- 运行时 ----
# 浮动 tag 策略见顶部与 HttpApi.Host/Dockerfile 说明。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 种子的配置真源：迁移器 Program.cs 按 ContentRootPath/../AbpAdmin.HttpApi.Host 分层读取
# （OpenIddict:Applications 客户端清单单一来源是 Configuration/OpenIddict.json，加载序见其 Program.cs）。容器里没有那个兄弟目录，这里把源文件摆到它要找的绝对路径上；
# 数据库连接串等敏感项不进镜像，由 compose 的环境变量（最高优先级）注入。
# appsettings.Development.json 同步带上：本地体验栈的 migrator 以 DOTNET_ENVIRONMENT=
# Development 运行，靠它种入开发专属客户端/回调（AbpAdmin_TestCli、demo 子域名）；
# 生产部署的迁移器不设该环境变量，这份文件在镜像里也不会被加载。

# Host Configuration/*.json = SelfUrl/Cors/OpenIddict/AuthServer 基线（Program.cs 先读 appsettings.json、再读本目录覆盖）。
# Real Database.json/Security.json are kept out of the build context by backend/.dockerignore
# (**/Configuration/*.json globs); only the non-secret baselines below are COPYed explicitly.
# Without these files AbpAdmin_App:ClientId is empty and the seed block is skipped entirely.
COPY framework-app/AbpAdmin.HttpApi.Host/Configuration/App.json /AbpAdmin.HttpApi.Host/Configuration/App.json
COPY framework-app/AbpAdmin.HttpApi.Host/Configuration/Cache.json /AbpAdmin.HttpApi.Host/Configuration/Cache.json
COPY framework-app/AbpAdmin.HttpApi.Host/Configuration/Cors.json /AbpAdmin.HttpApi.Host/Configuration/Cors.json
COPY framework-app/AbpAdmin.HttpApi.Host/Configuration/OpenIddict.json /AbpAdmin.HttpApi.Host/Configuration/OpenIddict.json
COPY framework-app/AbpAdmin.HttpApi.Host/Configuration/Quartz.json /AbpAdmin.HttpApi.Host/Configuration/Quartz.json

COPY framework-app/AbpAdmin.HttpApi.Host/appsettings.json /AbpAdmin.HttpApi.Host/appsettings.json
COPY framework-app/AbpAdmin.HttpApi.Host/appsettings.Development.json /AbpAdmin.HttpApi.Host/appsettings.Development.json

# Serilog 文件 sink 写相对路径 Logs/logs.txt；非 root（$APP_UID）建不了目录——预建并归属
# 运行用户，否则文件日志静默丢失（迁移是一次性进程，控制台日志才是主通道，兜底而已）。
RUN mkdir -p /app/Logs && chown $APP_UID /app/Logs

USER $APP_UID
ENTRYPOINT ["dotnet", "AbpAdmin.DbMigrator.dll"]
