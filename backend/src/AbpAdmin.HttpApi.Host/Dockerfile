# HttpApi.Host 镜像：自包含多阶段构建（源码进、镜像出，无需先在宿主机 dotnet publish）。
# 构建上下文必须是 backend/（组合编排见仓库根 docker-compose.yml；手工构建：
#   cd backend && docker build -f src/AbpAdmin.HttpApi.Host/Dockerfile -t abp-admin-host . ）

# ---- 构建：restore 单独成层（csproj 不变则命中缓存，不重下包） ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CERT_PASSPHRASE=dev-only-change-me
WORKDIR /src

# restore 输入：中央包管理（Directory.Packages.props）、公共属性（common.props）、
# NuGet 源声明（NuGet.Config，单源 nuget.org）+ 全部 csproj
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
COPY src/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj src/AbpAdmin.HttpApi.Host/
RUN dotnet restore src/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj

COPY src/ src/
RUN dotnet publish src/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj \
    -c Release -o /app/publish --no-restore

# ---- 证书：容器里没有宿主机受信的开发证书，构建期自签一张 ----
# 一张证书两用：OpenIddict 签名/加密（非 Development 启动强制要求 openiddict.pfx，
# AuthServer:CertificatePassPhrase 提供口令）+ Kestrel HTTPS（仅容器内挂载/直连场景，
# 对外入口是 web nginx，见仓库根 docker-compose.yml）。
# ⚠ 口令变更必须连同镜像一起重建（docker compose build host）：pfx 在构建期烘焙加密，
# 只改运行时环境变量会让容器拿新口令解旧证书，启动即失败。
# 浏览器直连场景首访会有"证书不受信任"告警；要消除：导出容器内 /app/openiddict.pfx
# 导入本机受信根，或用真实证书挂载覆盖 + 换掉口令配置。
RUN dotnet dev-certs https -ep /app/openiddict.pfx -p "$CERT_PASSPHRASE" \
    && chmod 644 /app/openiddict.pfx

# ---- 运行时：仅携带 publish 产物 + 证书 + IP 归属地库 ----
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
# 上面两条 ENV 的口令默认值与 build ARG 默认值同源；改口令时 compose 侧用同一变量
# 同时喂 build.args.CERT_PASSPHRASE 和运行时环境变量（见 docker-compose.yml），
# 且必须 --build 重建镜像（见上方证书段说明）。

EXPOSE 44395 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AbpAdmin.HttpApi.Host.dll"]
