# DbMigrator 镜像：一次性迁移/种子进程（compose 里先于 Host 跑，跑完退出）。
# 构建上下文必须是 backend/（同 framework/AbpAdmin.HttpApi.Host/Dockerfile 的约定；
# 手工构建：cd backend && docker build -f framework/AbpAdmin.DbMigrator/Dockerfile -t abp-admin-migrator . ）。

# ---- 构建：与 Host Dockerfile 同款分层，restore 挂在 csproj 层上 ----
# ⚠ restore 层 COPY 链必须覆盖引用闭包的全部 csproj——framework/ 与 modules/ 两区都要 COPY，
#   漏拷一条 dotnet restore 在容器内直接断（工程挪区/新增业务模块时同步补链）。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config common.props Directory.Packages.props ./
COPY framework/AbpAdmin.Domain.Shared/AbpAdmin.Domain.Shared.csproj framework/AbpAdmin.Domain.Shared/
COPY framework/AbpAdmin.Domain/AbpAdmin.Domain.csproj framework/AbpAdmin.Domain/
COPY framework/AbpAdmin.DynamicQueryable/AbpAdmin.DynamicQueryable.csproj framework/AbpAdmin.DynamicQueryable/
COPY framework/AbpAdmin.Elasticsearch/AbpAdmin.Elasticsearch.csproj framework/AbpAdmin.Elasticsearch/
COPY framework/AbpAdmin.EntityFrameworkCore/AbpAdmin.EntityFrameworkCore.csproj framework/AbpAdmin.EntityFrameworkCore/
COPY framework/AbpAdmin.Application.Contracts/AbpAdmin.Application.Contracts.csproj framework/AbpAdmin.Application.Contracts/
COPY framework/AbpAdmin.Application/AbpAdmin.Application.csproj framework/AbpAdmin.Application/
COPY framework/AbpAdmin.HttpApi/AbpAdmin.HttpApi.csproj framework/AbpAdmin.HttpApi/
COPY framework/AbpAdmin.HttpApi.Client/AbpAdmin.HttpApi.Client.csproj framework/AbpAdmin.HttpApi.Client/
COPY framework/AbpAdmin.Webhooks/AbpAdmin.Webhooks.csproj framework/AbpAdmin.Webhooks/
COPY modules/AbpAdmin.Biz.Template/AbpAdmin.Biz.Template.csproj modules/AbpAdmin.Biz.Template/
COPY modules/AbpAdmin.Collection.Contracts/AbpAdmin.Collection.Contracts.csproj modules/AbpAdmin.Collection.Contracts/
COPY modules/AbpAdmin.Collection/AbpAdmin.Collection.csproj modules/AbpAdmin.Collection/
COPY modules/AbpAdmin.Ordering/AbpAdmin.Ordering.csproj modules/AbpAdmin.Ordering/
COPY modules/AbpAdmin.Ledger.Integration.Contracts/AbpAdmin.Ledger.Integration.Contracts.csproj modules/AbpAdmin.Ledger.Integration.Contracts/
COPY modules/AbpAdmin.Ledger/AbpAdmin.Ledger.csproj modules/AbpAdmin.Ledger/
COPY framework/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj framework/AbpAdmin.DbMigrator/
RUN dotnet restore framework/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj

COPY framework/ framework/
COPY modules/ modules/
RUN dotnet publish framework/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj \
    -c Release -o /app/publish --no-restore

# ---- 运行时 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 种子的"单一事实来源"：迁移器 Program.cs 按 ContentRootPath/../AbpAdmin.HttpApi.Host
# 去读 Host 的 appsettings.json（OpenIddict:Applications 客户端清单等，见其 Program.cs
# 的分层加载说明）。容器里没有那个兄弟目录，这里把源文件摆到它要找的绝对路径上；
# 数据库连接串等敏感项不进镜像，由 compose 的环境变量（最高优先级）注入。
# appsettings.Development.json 同步带上：本地体验栈的 migrator 以 DOTNET_ENVIRONMENT=
# Development 运行，靠它种入开发专属客户端/回调（AbpAdmin_TestCli、demo 子域名）；
# 生产部署的迁移器不设该环境变量，这份文件在镜像里也不会被加载。

# Host Configuration/*.json = SelfUrl/Cors/OpenIddict/AuthServer source (Program.cs reads here first).
# Skip Database/Security secrets (.example only on disk); compose injects connection via env.
# Without these files AbpAdmin_App:ClientId is empty and the seed block is skipped entirely.
COPY framework/AbpAdmin.HttpApi.Host/Configuration/App.json /AbpAdmin.HttpApi.Host/Configuration/App.json
COPY framework/AbpAdmin.HttpApi.Host/Configuration/Cache.json /AbpAdmin.HttpApi.Host/Configuration/Cache.json
COPY framework/AbpAdmin.HttpApi.Host/Configuration/Cors.json /AbpAdmin.HttpApi.Host/Configuration/Cors.json
COPY framework/AbpAdmin.HttpApi.Host/Configuration/OpenIddict.json /AbpAdmin.HttpApi.Host/Configuration/OpenIddict.json
COPY framework/AbpAdmin.HttpApi.Host/Configuration/Quartz.json /AbpAdmin.HttpApi.Host/Configuration/Quartz.json

COPY framework/AbpAdmin.HttpApi.Host/appsettings.json /AbpAdmin.HttpApi.Host/appsettings.json
COPY framework/AbpAdmin.HttpApi.Host/appsettings.Development.json /AbpAdmin.HttpApi.Host/appsettings.Development.json

# Serilog 文件 sink 写相对路径 Logs/logs.txt；非 root（$APP_UID）建不了目录——预建并归属
# 运行用户，否则文件日志静默丢失（迁移是一次性进程，控制台日志才是主通道，兜底而已）。
RUN mkdir -p /app/Logs && chown $APP_UID /app/Logs

USER $APP_UID
ENTRYPOINT ["dotnet", "AbpAdmin.DbMigrator.dll"]
