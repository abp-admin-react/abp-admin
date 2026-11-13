/**
 * 顶栏 Host 标签左侧：后端版本、前端版本、服务器时间、本机时间。
 * 登录页 layout: false，不经过这组顶栏，看不到它。
 *
 * 服务器时间按拉取瞬间 + 本机经过毫秒走，每分钟对一次；失败不改用浏览器时间。
 * 本机时间按浏览器 IANA 时区单独一行，和服务器时间上下对齐。
 */
import { createStyles } from 'antd-style';
import React, { useEffect, useRef, useState } from 'react';
import {
  formatServerZoneLabel,
  getRuntimeInfo,
  type RuntimeInfoDto,
  readWebVersion,
} from '@/abp/runtimeInfo';
import { formatBrowserWallClock, formatServerWallClock } from '@/utils/time';

const CLOCK_INTERVAL_MS = 1000;
const RESYNC_INTERVAL_MS = 60_000;

const useStyles = createStyles(({ token, css }) => ({
  bar: css`
    display: inline-flex;
    align-items: center;
    gap: 8px;
    white-space: nowrap;
    color: ${token.colorTextSecondary};
    font-size: ${token.fontSizeSM}px;
    line-height: 1.3;
    cursor: default;
  `,
  times: css`
    display: grid;
    grid-template-columns: auto auto;
    column-gap: 8px;
    row-gap: 2px;
    align-items: baseline;
  `,
}));

const RuntimeStatus: React.FC = () => {
  const { styles } = useStyles();
  const webVersion = readWebVersion();
  const [info, setInfo] = useState<RuntimeInfoDto | null>(null);
  const [failed, setFailed] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const anchorRef = useRef<{ serverMs: number; clientMs: number } | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = () => {
      getRuntimeInfo()
        .then((dto) => {
          if (cancelled) return;
          anchorRef.current = {
            serverMs: dto.serverUnixTimeMilliseconds,
            clientMs: Date.now(),
          };
          setInfo(dto);
          setFailed(false);
          setNow(Date.now());
        })
        .catch(() => {
          if (!cancelled) setFailed(true);
        });
    };
    load();
    const resync = window.setInterval(load, RESYNC_INTERVAL_MS);
    const tick = window.setInterval(
      () => setNow(Date.now()),
      CLOCK_INTERVAL_MS,
    );
    return () => {
      cancelled = true;
      window.clearInterval(resync);
      window.clearInterval(tick);
    };
  }, []);

  const anchor = anchorRef.current;
  const epoch =
    anchor == null ? null : anchor.serverMs + (now - anchor.clientMs);
  const clock =
    epoch != null && info ? formatServerWallClock(epoch, info.utcOffset) : null;
  const zone = info ? formatServerZoneLabel(info) : null;
  const apiVersion = info?.apiVersion ?? (failed ? '不可用' : '获取中');
  const timeText =
    clock && zone ? `${clock}（${zone.text}）` : failed ? '不可用' : '获取中';
  const browserTime = formatBrowserWallClock(now) ?? '不可用';

  return (
    <div
      className={styles.bar}
      data-testid="runtime-status"
      title={zone?.title}
    >
      <span>后端 {apiVersion}</span>
      <span aria-hidden="true">·</span>
      <span>前端 {webVersion}</span>
      <span className={styles.times}>
        <span>服务器时间</span>
        <span>{timeText}</span>
        <span>本机时间</span>
        <span>{browserTime}</span>
      </span>
    </div>
  );
};

export default RuntimeStatus;
