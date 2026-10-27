# HttpApi.Host 镜像：自包含多阶段构建（源码进、镜像出，无需先在宿主机 dotnet publish）。
# 构建上下文必须是 backend/（组合编排见仓库根 docker-compose.yml；手工构建：
#   cd backend && docker build -f framework/AbpAdmin.HttpApi.Host/Dockerfile -t abp-admin-host . ）

# ---- 构建：restore 单独成层（csproj 不变则命中缓存，不重下包） ----
# 基础镜像浮动 tag（10.0 = .NET 10 LTS 补丁线内浮动，不用 latest 防跨大版本）。底座新鲜度
# 由 scripts/auto-update-stack.ps1 保证：显式 docker pull（宿主已配 1ms.run 镜像源加速）
# → digest 快照对比 → 有变化才构建；构建不带 --pull、吃刷好的本地 tag，单底座源故障不拖挂
# 整个构建。docker build 默认吃本地 tag 缓存——历史"aspnet:10.0 长期停在 10.0.11
# （2 CRITICAL + 11 HIGH）"即此假象，当年的 digest 钉版 + 手动刷新已废弃。
# trivy 门禁两道不互替：自动更新内置单镜像扫（CRITICAL/HIGH，未装 trivy 自动跳过）；
# scripts/security-scan.sh 仍是发布前三路全量门禁（.NET 依赖/前端生产树/镜像层）。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CERT_PASSPHRASE=dev-only-change-me
WORKDIR /src

# restore 输入：中央包管理（Directory.Packages.props）、公共属性（common.props）、
# NuGet 源声明（NuGet.Config，单源 nuget.org）+ 全部 csproj。
# ⚠ restore 层 COPY 链必须覆盖引用闭包的全部 csproj——framework/ 与 modules/ 两区都要 COPY，
#   漏拷一条 dotnet restore 在容器内直接断（工程挪区/新增业务模块时同步补链）。
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
COPY modules/AbpAdmin.TokenGateway/AbpAdmin.TokenGateway.csproj modules/AbpAdmin.TokenGateway/
COPY framework/AbpAdmin.DbMigrator/AbpAdmin.DbMigrator.csproj framework/AbpAdmin.DbMigrator/
COPY framework/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj framework/AbpAdmin.HttpApi.Host/
RUN dotnet restore framework/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj

COPY framework/ framework/
COPY modules/ modules/

# ---- 前端库还原：wwwroot/libs 不进库（ABP 模板约定），Razor 登录页的 ABP Bundling 需要
# /libs 物理文件，缺了渲染即 500→302 死循环 = docker 部署登录流不可用。node 取自官方镜像
# （sdk 基座不带）；abp CLI 走 dotnet 全局工具，install-libs 只做 npm 层搬运。jquery 按
# 10.7 官方口径钉 resolutions.jquery=4.0.0（jQuery 4 + Migrate 4 唯一受支持组合，
# abp.io/support/questions/10921），install-libs 自动产出正确一对。 ----
COPY --from=node:22-bookworm-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:22-bookworm-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global Volo.Abp.Studio.Cli
RUN abp install-libs -wd framework/AbpAdmin.HttpApi.Host

RUN dotnet publish framework/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj \
    -c Release -o /app/publish --no-restore

# ---- 证书：容器内无宿主受信证书，构建期自签一张两用（OpenIddict 签名/加密 + Kestrel
# HTTPS 内网直连；对外入口是 web nginx）。⚠ 口令变更必须重建镜像：pfx 构建期烘焙，只改
# 运行时口令解不开旧证书、启动即失败。自签直连有不受信告警，导入受信根或挂真证书可消除。 ----
RUN dotnet dev-certs https -ep /app/openiddict.pfx -p "$CERT_PASSPHRASE" \
    && chmod 644 /app/openiddict.pfx

# ---- 运行时：仅携带 publish 产物 + 证书 + IP 归属地库 ----
# 浮动 tag 策略同顶部说明：底座新鲜度靠 auto-update-stack.ps1 显式 pull，不靠手改 digest。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/openiddict.pfx .
# IP 归属地库（IP2Region xdb）：本地开发经 ../../etc 相对路径解析到 backend/etc；
# 容器 ContentRoot 是 /app，必须把库随镜像带上，compose 用 IpRegion__DbPath 指到镜像内路径。
# 缺了不崩（IpRegionSearcher 降级为功能关闭 + Warning），但 docker 栈会静默丢失归属地能力。
COPY etc/ip2region/ etc/ip2region/

# Serilog 文件 sink 写相对路径 Logs/logs.txt；非 root（$APP_UID）在 root 属主的 /app 下
# 建不了目录——预建并归属运行用户，否则文件日志静默丢失（控制台日志不受影响）。
RUN mkdir -p /app/Logs && chown $APP_UID /app/Logs

# 两个端口都只存在于 compose 内网：44395 HTTPS（自签，供直连/挂载场景），8080 HTTP
# （web nginx 反代上游 + 容器内健康检查）。对外唯一入口是 web 服务（见 docker-compose.yml）。
ENV ASPNETCORE_URLS=https://+:44395;http://+:8080 \
    ASPNETCORE_Kestrel__Certificates__Default__Path=/app/openiddict.pfx \
    ASPNETCORE_Kestrel__Certificates__Default__Password=dev-only-change-me
# 两条 ENV 口令与 build ARG 同源：改口令 = compose 同一变量喂 build.args 与运行时，
# 且必须 --build 重建（见上方证书段）。

EXPOSE 44395 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AbpAdmin.HttpApi.Host.dll"]
