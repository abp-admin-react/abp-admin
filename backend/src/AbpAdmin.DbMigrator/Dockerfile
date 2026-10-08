# DbMigrator 镜像：一次性迁移/种子进程（compose 里先于 Host 跑，跑完退出）。
# 构建上下文必须是 backend/（同 src/AbpAdmin.HttpApi.Host/Dockerfile 的约定；
# 手工构建：cd backend && docker build -f src/AbpAdmin.DbMigrator/Dockerfile -t abp-admin-migrator . ）。

# ---- 构建：与 Host Dockerfile 同款分层，restore 挂在 csproj 层上 ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config common.props Directory.Packages.props ./
COPY src/AbpAdmin.Domain.Shared/AbpAdmin.Domain.Shared.csproj src/AbpAdmin.Domain.Shared/
COPY src/AbpAdmin.Domain/AbpAdmin.Domain.csproj src/AbpAdmin.Domain/
COPY src/AbpAdmin.DynamicQueryable/AbpAdmin.DynamicQueryable.csproj src/AbpAdmin.DynamicQueryable/
COPY src/AbpAdmin.Elasticsearch/AbpAdmin.Elasticsearch.csproj src/AbpAdmin.Elasticsearch/
COPY src/AbpAdmin.EntityFrameworkCore/AbpAdmin.EntityFrameworkCore.csproj src/AbpAdmin.EntityFrameworkCore/
COPY src/AbpAdmin.Application.Contracts/AbpAdmin.Application.Contracts.csproj src/AbpAdmin.Application.Contracts/
COPY src/AbpAdmin.Application/AbpAdmin.Application.csproj src/AbpAdmin.Application/
COPY src/AbpAdmin.HttpApi/AbpAdmin.HttpApi.csproj src/AbpAdmin.HttpApi/
COPY src/AbpAdmin.HttpApi.Client/AbpAdmin.HttpApi.Client.csproj src/AbpAdmin.HttpApi.Client/
COPY src/AbpAdmin.Webhooks/AbpAdmin.Webhooks.csproj src/AbpAdmin.Webhooks/
COPY src/AbpAdmin.Biz.Template/AbpAdmin.Biz.Template.csproj src/AbpAdmin.Biz.Template/
COPY src/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj src/AbpAdmin.DbMigrator/
RUN dotnet restore src/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj

COPY src/ src/
RUN dotnet publish src/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj \
    -c Release -o /app/publish --no-restore

# ---- 运行时 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 种子的"单一事实来源"：迁移器 Program.cs 按 ContentRootPath/../AbpAdmin.HttpApi.Host
# 去读 Host 的 appsettings.json（OpenIddict:Applications 客户端清单等，见其 Program.cs
# 的分层加载说明）。容器里没有那个兄弟目录，这里把源文件摆到它要找的绝对路径上；
# 数据库连接串等敏感项不进镜像，由 compose 的环境变量（最高优先级）注入。
COPY src/AbpAdmin.HttpApi.Host/appsettings.json /AbpAdmin.HttpApi.Host/appsettings.json

# Serilog 文件 sink 写相对路径 Logs/logs.txt；非 root（$APP_UID）建不了目录——预建并归属
# 运行用户，否则文件日志静默丢失（迁移是一次性进程，控制台日志才是主通道，兜底而已）。
RUN mkdir -p /app/Logs && chown $APP_UID /app/Logs

USER $APP_UID
ENTRYPOINT ["dotnet", "AbpAdmin.DbMigrator.dll"]
