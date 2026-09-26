import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  App,
  Button,
  Card,
  Col,
  Descriptions,
  Progress,
  Row,
  Statistic,
  Tag,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { getServerMonitor, type ServerMonitorDto } from '@/abp/proModules';

const formatBytes = (bytes?: number): string => {
  if (bytes == null || Number.isNaN(bytes)) {
    return '-';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = -1;
  do {
    value /= 1024;
    unit += 1;
  } while (value >= 1024 && unit < units.length - 1);
  return `${value.toFixed(1)} ${units[unit]}`;
};

const formatDuration = (seconds: number): string => {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const parts: string[] = [];
  if (days > 0) parts.push(`${days} 天`);
  if (hours > 0) parts.push(`${hours} 小时`);
  parts.push(`${minutes} 分钟`);
  return parts.join(' ');
};

const ServerMonitorPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();
  const [info, setInfo] = useState<ServerMonitorDto>();
  const [loading, setLoading] = useState(false);

  // 权限守卫必须同时门禁请求：否则无权限用户进页面仍会打该接口，
  // 并吃到全局 403「没有权限」提示
  const canView = access.canViewServerMonitor;

  // 已知存量口径（勿模仿新代码）：本函数失败时全局 errorHandler（requestErrorConfig）
  // 与这里的 message.error 会对同一次失败各弹一次提示，尚未收敛成 getJobTypes 那样的
  // skipErrorHandler 单提示方案；失败时 info 保留上一次成功数据，各卡片继续展示旧值
  // （仅首次加载前 loading 骨架有效），刷新成功后自动覆盖。
  const load = useCallback(async () => {
    setLoading(true);
    try {
      setInfo(await getServerMonitor());
    } catch {
      message.error('加载服务器信息失败');
    } finally {
      setLoading(false);
    }
  }, [message]);

  useEffect(() => {
    if (canView) {
      load();
    }
  }, [canView, load]);

  if (!canView) {
    return (
      <PageContainer>
        <Card>无权限查看服务监控。</Card>
      </PageContainer>
    );
  }

  // undefined = 整个请求未成功（加载中/请求失败）；null = 请求成功但后端 300ms
  // 差分采样失败。两者都区别于 0% 空闲，但只有后者才配得上「采样失败」提示。
  const cpuPercent = info?.processCpuUsagePercent;
  const workingSet =
    info && info.gcTotalMemoryLimitBytes > 0
      ? (info.workingSetBytes / info.gcTotalMemoryLimitBytes) * 100
      : 0;

  return (
    <PageContainer>
      <Row gutter={[16, 16]}>
        <Col span={24}>
          <Card
            title="机器 / 运行时"
            loading={!info}
            /* 页头标题行已全局隐藏，刷新挪到首卡片 extra */
            extra={
              <Button
                key="refresh"
                type="primary"
                loading={loading}
                onClick={load}
              >
                刷新
              </Button>
            }
          >
            {info && (
              <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
                <Descriptions.Item label="机器名">
                  {info.machineName}
                </Descriptions.Item>
                <Descriptions.Item label="操作系统">
                  {info.osDescription}（{info.osArchitecture}）
                </Descriptions.Item>
                <Descriptions.Item label="进程架构">
                  {info.processArchitecture}
                </Descriptions.Item>
                <Descriptions.Item label="CPU 逻辑核心">
                  {info.processorCount}
                </Descriptions.Item>
                <Descriptions.Item label=".NET 版本">
                  {info.dotNetVersion}
                </Descriptions.Item>
                <Descriptions.Item label="GC 模式">
                  {info.isServerGc ? (
                    <Tag color="blue">Server GC</Tag>
                  ) : (
                    <Tag>Workstation GC</Tag>
                  )}
                </Descriptions.Item>
                <Descriptions.Item label="启动时间 (UTC)">
                  {new Date(info.processStartTimeUtc).toLocaleString()}
                </Descriptions.Item>
                <Descriptions.Item label="已运行">
                  {formatDuration(info.uptimeSeconds)}
                </Descriptions.Item>
              </Descriptions>
            )}
          </Card>
        </Col>

        <Col xs={24} md={12}>
          <Card title="进程 CPU" loading={!info}>
            {/* 后端采样失败时返回 null（与 0% 空闲有本质区别），渲染为「-」避免误读；
                后缀 % 一并隐藏，否则会渲染成费解的「-%」 */}
            <Statistic
              title="CPU 占用（全部核心归一）"
              value={cpuPercent ?? '-'}
              precision={cpuPercent == null ? undefined : 1}
              suffix={cpuPercent == null ? undefined : '%'}
            />
            {cpuPercent != null && (
              <Progress
                percent={Math.min(cpuPercent, 100)}
                status={cpuPercent > 80 ? 'exception' : 'normal'}
                style={{ marginTop: 8 }}
              />
            )}
            {/* 仅当请求成功而采样失败时提示；整卡加载失败已有全局错误提示，勿误归因 */}
            {info != null && cpuPercent == null && (
              <Typography.Text type="warning">
                CPU 采样失败（300ms 差分窗口采样出错），请刷新重试。
              </Typography.Text>
            )}
          </Card>
        </Col>

        <Col xs={24} md={12}>
          <Card title="内存 / GC" loading={!info}>
            {info && (
              <>
                <Row gutter={16}>
                  <Col span={8}>
                    <Statistic
                      title="工作集"
                      value={formatBytes(info.workingSetBytes)}
                    />
                  </Col>
                  <Col span={8}>
                    <Statistic
                      title="GC 堆"
                      value={formatBytes(info.gcHeapSizeBytes)}
                    />
                  </Col>
                  <Col span={8}>
                    <Statistic
                      title="可用上限"
                      value={formatBytes(info.gcTotalMemoryLimitBytes)}
                    />
                  </Col>
                </Row>
                <Progress
                  percent={Math.min(workingSet, 100)}
                  format={() =>
                    `${formatBytes(info.workingSetBytes)} / ${formatBytes(info.gcTotalMemoryLimitBytes)}`
                  }
                  style={{ marginTop: 16 }}
                />
                <Typography.Text type="secondary">
                  GC 次数：Gen0 {info.gen0Collections} / Gen1{' '}
                  {info.gen1Collections} / Gen2 {info.gen2Collections}
                  （容器内"可用上限"为配额，非整机物理内存）
                </Typography.Text>
              </>
            )}
          </Card>
        </Col>

        <Col xs={24} md={12}>
          <Card title="线程" loading={!info}>
            {info && (
              <Row gutter={16}>
                <Col span={8}>
                  <Statistic title="托管线程数" value={info.threadCount} />
                </Col>
                <Col span={8}>
                  <Statistic
                    title="线程池可用工作线程"
                    value={info.threadPoolAvailableWorkerThreads}
                  />
                </Col>
                <Col span={8}>
                  <Statistic
                    title="线程池最小线程"
                    value={info.threadPoolMinWorkerThreads}
                  />
                </Col>
              </Row>
            )}
          </Card>
        </Col>

        <Col xs={24} md={12}>
          <Card title="磁盘" loading={!info}>
            {info?.disks.map((disk) => {
              const usedPercent =
                disk.totalBytes > 0
                  ? ((disk.totalBytes - disk.freeBytes) / disk.totalBytes) * 100
                  : 0;
              return (
                <div key={disk.name} style={{ marginBottom: 12 }}>
                  <Typography.Text strong>{disk.name}</Typography.Text>
                  <Typography.Text type="secondary">
                    （{disk.driveFormat}）剩余 {formatBytes(disk.freeBytes)} /
                    共 {formatBytes(disk.totalBytes)}
                  </Typography.Text>
                  <Progress
                    percent={Math.round(usedPercent * 10) / 10}
                    status={usedPercent > 90 ? 'exception' : 'normal'}
                  />
                </div>
              );
            })}
          </Card>
        </Col>
      </Row>
    </PageContainer>
  );
};

export default ServerMonitorPage;
