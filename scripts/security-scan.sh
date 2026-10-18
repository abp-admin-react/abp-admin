#!/usr/bin/env bash
# 依赖与镜像漏洞门禁（2026-10-18 安全审计面 H 落地）。
# 三条检查各自独立，任一条命中阈值即以非零码退出，可直接放进 CI。
#
# 为什么三条都要：本轮实测它们互不覆盖——
#   1) dotnet restore 的 NuGetAudit 告警即使全开也会漏报（同一张依赖图报 0 Warning），
#      所以 .NET 侧必须走 `dotnet list --vulnerable`；
#   2) NuGet 公告看不见 OS/镜像层的 OpenSSL、curl、libexpat；
#   3) trivy 看得见镜像里的 NuGet 包，但看不见没进镜像的测试项目依赖。
set -uo pipefail

SEV="${SEV:-CRITICAL,HIGH}"
LEVEL="${LEVEL:-low}"
IMG="${1:-}"

cd "$(dirname "$0")/.."
fail=0

echo "### 1) .NET 依赖（直接 + 传递）"
dotnet restore backend/AbpAdmin.slnx >/dev/null
out=$(dotnet list backend/AbpAdmin.slnx package --vulnerable --include-transitive 2>&1)
bad=$(printf '%s\n' "$out" | grep -E "(${SEV//,/|})" || true)
if [ -n "$bad" ]; then
  printf '%s\n' "$out" | grep -E '^\s*>' | sort -u
  echo "!! 存在 ${SEV} 级别的 .NET 依赖通告"
  [ -n "${STRICT:-}" ] && fail=1
else
  echo "OK：无 ${SEV} 级 .NET 依赖通告"
fi

echo "### 2) 前端依赖（只卡生产树）"
if [ -f web/pnpm-lock.yaml ]; then
  if (cd web && pnpm audit --prod --audit-level="$LEVEL"); then
    echo "OK：生产树无 ${LEVEL} 级以上通告"
  else
    echo "!! 生产树存在 ${LEVEL} 级以上通告（构建期 dev 树另计）"
    [ -n "${STRICT:-}" ] && fail=1
  fi
else
  echo "跳过：未找到 web/pnpm-lock.yaml"
fi

echo "### 3) 镜像层（需给镜像 tag，或设 IMG=...）"
if [ -z "$IMG" ]; then
  echo "跳过：用法 $0 <image:tag>，例如 abp-lab2-host:latest"
elif ! command -v trivy >/dev/null 2>&1; then
  echo "跳过：未安装 trivy（或不在 PATH）"
else
  # 国内网络下默认源 ghcr.io / mirror.gcr.io 不可达，走 public.ecr.aws
  export TRIVY_DB_REPOSITORY="${TRIVY_DB_REPOSITORY:-public.ecr.aws/aquasecurity/trivy-db:2}"
  if trivy image --severity "$SEV" --ignore-unfixed --exit-code 1 --quiet "$IMG"; then
    echo "OK：$IMG 无 ${SEV} 级（有修复版本的）通告"
  else
    echo "!! $IMG 命中 ${SEV} 级通告"
    fail=1
  fi
fi

# 基座 freshness 提醒：tag 是浮动的，digest 才不变。构建后应能对上号。
echo "### 提醒：基础镜像按 digest 钉版，刷新请 pull 新 digest 后复扫"
grep -rhoE "FROM [A-Za-z0-9._/:@-]+@sha256:[0-9a-f]{12}" backend web --include=Dockerfile* 2>/dev/null | sort -u | sed 's/^/  pinned: /'

exit $fail
