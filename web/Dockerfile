# web 前端镜像：pnpm 构建静态产物 → nginx 承载（SPA + 后端反代，见 nginx.conf）。
# 构建上下文必须是 web/（仓库根 docker-compose.yml 已按此配置；手工构建：
#   cd web && docker build -t abp-admin-web . ）。
FROM node:22-alpine AS build
# umi max build 对内存要求高，默认堆上限会 OOM
ENV NODE_OPTIONS=--max-old-space-size=4096
WORKDIR /app

# corepack 按 package.json 的 packageManager 钉死 pnpm 版本（10.x）
RUN corepack enable

# 先拷锁文件与 .npmrc 单独装依赖：源码改动不触发全量重装。
# .npmrc 必须随行：shamefully-hoist=true 决定依赖提升布局，源码里 dayjs 等
# 传递依赖靠它提升到 node_modules 根才可解析（漏拷 = Can't resolve 'dayjs'）
COPY package.json pnpm-lock.yaml .npmrc ./
RUN pnpm install --frozen-lockfile

COPY . .
RUN pnpm build

FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-proxy-params.conf /etc/nginx/proxy_params_backend.conf
# SPA 侧安全响应头（CSP 等，被 nginx.conf 的静态/回退 location include）
COPY security-headers.conf /etc/nginx/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
