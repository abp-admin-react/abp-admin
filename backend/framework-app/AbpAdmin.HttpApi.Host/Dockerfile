# HttpApi.Host 镜像：自包含多阶段构建（源码进、镜像出，无需先在宿主机 dotnet publish）。
# 构建上下文必须是 backend/（组合编排见仓库根 docker-compose.yml；手工构建：
#   cd backend && docker build -f framework-app/AbpAdmin.HttpApi.Host/Dockerfile -t abp-admin-host . ）

# 使用 .NET 10 补丁标签。自动更新脚本先拉取基础镜像，再按 digest 判断是否重建。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CERT_PASSPHRASE=dev-only-change-me
WORKDIR /src

# restore 层复制公共 NuGet 配置和宿主项目的全部传递引用。
# 修改 ProjectReference 后运行 scripts/check-docker-restore.ps1 同步清单，供 publish --no-restore 使用。
COPY NuGet.Config common.props Directory.Packages.props ./
COPY framework-app/AbpAdmin.Domain.Shared/AbpAdmin.Domain.Shared.csproj framework-app/AbpAdmin.Domain.Shared/
COPY framework-app/AbpAdmin.Domain/AbpAdmin.Domain.csproj framework-app/AbpAdmin.Domain/
COPY framework/AbpAdmin.DynamicQueryable/AbpAdmin.DynamicQueryable.csproj framework/AbpAdmin.DynamicQueryable/
COPY framework/AbpAdmin.Elasticsearch/AbpAdmin.Elasticsearch.csproj framework/AbpAdmin.Elasticsearch/
COPY framework-app/AbpAdmin.EntityFrameworkCore/AbpAdmin.EntityFrameworkCore.csproj framework-app/AbpAdmin.EntityFrameworkCore/
COPY framework-app/AbpAdmin.Application.Contracts/AbpAdmin.Application.Contracts.csproj framework-app/AbpAdmin.Application.Contracts/
COPY framework-app/AbpAdmin.Application/AbpAdmin.Application.csproj framework-app/AbpAdmin.Application/
COPY framework-app/AbpAdmin.HttpApi/AbpAdmin.HttpApi.csproj framework-app/AbpAdmin.HttpApi/
COPY modules/platform/AbpAdmin.Webhooks/AbpAdmin.Webhooks.csproj modules/platform/AbpAdmin.Webhooks/
COPY modules/biz/AbpAdmin.Biz.Template/AbpAdmin.Biz.Template.csproj modules/biz/AbpAdmin.Biz.Template/
COPY modules/platform/AbpAdmin.Collection.Contracts/AbpAdmin.Collection.Contracts.csproj modules/platform/AbpAdmin.Collection.Contracts/
COPY modules/platform/AbpAdmin.Collection/AbpAdmin.Collection.csproj modules/platform/AbpAdmin.Collection/
COPY modules/platform/AbpAdmin.Ordering/AbpAdmin.Ordering.csproj modules/platform/AbpAdmin.Ordering/
COPY modules/platform/AbpAdmin.Ledger.Integration.Contracts/AbpAdmin.Ledger.Integration.Contracts.csproj modules/platform/AbpAdmin.Ledger.Integration.Contracts/
COPY modules/platform/AbpAdmin.Ledger/AbpAdmin.Ledger.csproj modules/platform/AbpAdmin.Ledger/
COPY modules/platform/AbpAdmin.TokenGateway/AbpAdmin.TokenGateway.csproj modules/platform/AbpAdmin.TokenGateway/
COPY framework-app/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj framework-app/AbpAdmin.HttpApi.Host/
RUN dotnet restore framework-app/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj

COPY framework/ framework/
COPY framework-app/ framework-app/
COPY modules/ modules/

# ABP CLI 安装并复制 Razor 登录页所需的静态库到 wwwroot/libs。
# ABP 10.7 使用 jQuery 4 / Migrate 4，Yarn 会提示旧依赖声明的版本范围不匹配。
COPY --from=node:22-bookworm-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:22-bookworm-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
COPY --from=node:22-bookworm-slim /opt/yarn-v1.22.22 /opt/yarn-v1.22.22
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
    && ln -s /opt/yarn-v1.22.22/bin/yarn /usr/local/bin/yarn
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global Volo.Abp.Studio.Cli
RUN abp install-libs -wd framework-app/AbpAdmin.HttpApi.Host

RUN dotnet publish framework-app/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj \
    -c Release -o /app/publish --no-restore

# ---- 证书：容器内无宿主受信证书，构建期自签一张两用（OpenIddict 签名/加密 + Kestrel
# HTTPS 内网直连；对外入口是 web nginx）。⚠ 口令变更必须重建镜像：pfx 构建期烘焙，只改
# 运行时口令解不开旧证书、启动即失败。自签直连有不受信告警，导入受信根或挂真证书可消除。 ----
RUN dotnet dev-certs https -ep /app/openiddict.pfx -p "$CERT_PASSPHRASE" \
    && chmod 644 /app/openiddict.pfx

# 运行时仅携带发布产物、证书和 IP 归属地库。
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
    ASPNETCORE_Kestrel__Certificates__Default__Path=/app/openiddict.pfx
# Compose 在运行时传入 Kestrel 和 OpenIddict 的证书口令，均与构建时生成证书的口令一致。

EXPOSE 44395 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AbpAdmin.HttpApi.Host.dll"]
