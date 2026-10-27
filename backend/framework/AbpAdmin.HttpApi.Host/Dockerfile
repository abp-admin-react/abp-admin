# HttpApi.Host 镜像：自包含多阶段构建（源码进、镜像出，无需先在宿主机 dotnet publish）。
# 构建上下文必须是 backend/（组合编排见仓库根 docker-compose.yml；手工构建：
#   cd backend && docker build -f framework/AbpAdmin.HttpApi.Host/Dockerfile -t abp-admin-host . ）

# ---- 构建：restore 单独成层（csproj 不变则命中缓存，不重下包） ----
# 基础镜像走浮动 tag（10.0 只在 .NET 10 LTS 补丁线内浮动，不用 latest 防跨大版本）。
# 自动跟进最新补丁的前提是构建必须带 --pull（docker compose build --pull）：docker build
# 默认复用本地已有 tag 的缓存镜像，不 pull 就永远停在旧补丁——历史上"aspnet:10.0 长期
# 停在带 2 CRITICAL + 11 HIGH 的 10.0.11"实为这个假象，当年因此改成 digest 钉版 +
# 手动刷新；现改为每周 scripts/auto-update-stack.ps1 计划任务自动 --pull 重建。
# 两道 trivy 门禁职责不互替：自动更新流程内置 trivy image 单镜像扫（CRITICAL/HIGH 阈值，
# 未装 trivy 自动跳过）；scripts/security-scan.sh 仍是发布前的三路全面门禁
# （.NET 依赖 / 前端生产树 / 镜像层）。
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

# ---- 前端库还原：官方约定 wwwroot/libs 不进库（.gitignore "Use abp install-libs
# to restore"，ABP 模板同款），容器构建必须自带还原步骤。Razor 页（/Account/Login、
# /Error）的 ABP Bundling 需要 /libs/abp 等物理文件；SPA 登录走 code flow 会跳转到
# Razor 登录页——缺 libs 时渲染即 500→302 死循环，等于 docker 部署上主登录流不可用
# （lab 实测）。node 二进制从官方 node 镜像取（sdk 基座不带 node）；abp CLI 为
# dotnet 全局工具，install-libs 只做 npm 层搬运、与 ABP 版本解耦。 ----
COPY --from=node:22-bookworm-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:22-bookworm-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global Volo.Abp.Studio.Cli
RUN abp install-libs -wd framework/AbpAdmin.HttpApi.Host
# （历史补丁已移除）10.7.0 曾需手动补 jquery-migrate 与覆盖 jquery 4——根因是仓库
# package.json 残留 10.6 模板的 resolutions.jquery=3.7.1。官方口径（abp.io/support/
# questions/10921，maliming）：10.7 唯一受支持组合是 jQuery 4 + Migrate 4，修法是把
# resolutions.jquery 钉 4.0.0（= 10.7 官方模板形态），install-libs 即自动产出正确
# 的一对（实测：jquery.js=4.0.0 且 jquery-migrate 三件自动拷入 wwwroot/libs）。
# 上游插件旧 range（<4.0 族）属预期——Migrate 4 负责补回被移除的 API；10.8 将替换
# timeago 等非维护插件后 Migrate 可去（issue #25973）。

RUN dotnet publish framework/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj \
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
# 浮动 tag 策略同顶部 sdk 段说明：补丁自动跟进靠构建 --pull，不靠手改 digest。
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
