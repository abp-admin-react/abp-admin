import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  Alert,
  App,
  Button,
  Card,
  Drawer,
  Input,
  Popconfirm,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import {
  type CacheKeyDto,
  type CacheMonitorInfoDto,
  deleteCacheKey,
  getCacheKeys,
  getCacheMonitorInfo,
  getCacheValue,
} from '@/abp/proModules';

const formatBytes = (bytes?: number | null): string => {
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

const formatTtl = (ttlSeconds?: number | null): string => {
  if (ttlSeconds == null) {
    return '永不过期';
  }
  if (ttlSeconds < 60) {
    return `${ttlSeconds} 秒`;
  }
  if (ttlSeconds < 3600) {
    return `${Math.floor(ttlSeconds / 60)} 分钟`;
  }
  if (ttlSeconds < 86400) {
    return `${Math.floor(ttlSeconds / 3600)} 小时`;
  }
  return `${Math.floor(ttlSeconds / 86400)} 天`;
};

const CacheMonitorPage: React.FC = () => {
  const access = useAccess();
  const { message } = App.useApp();
  const [info, setInfo] = useState<CacheMonitorInfoDto>();
  const [keys, setKeys] = useState<CacheKeyDto[]>([]);
  const [cursor, setCursor] = useState(0);
  const [loading, setLoading] = useState(false);
  const [prefixInput, setPrefixInput] = useState<string>();
  const [activePrefix, setActivePrefix] = useState<string>();
  const [valueTarget, setValueTarget] = useState<CacheKeyDto>();
  const [valueContent, setValueContent] = useState<string>();
  const [valueLoading, setValueLoading] = useState(false);

  const loadInfo = useCallback(async () => {
    try {
      const result = await getCacheMonitorInfo();
      setInfo(result);
      setPrefixInput((current) => current ?? (result.keyPrefix || 'c:'));
      return result;
    } catch {
      message.error('加载缓存信息失败');
      return undefined;
    }
  }, [message]);

  useEffect(() => {
    loadInfo();
  }, [loadInfo]);

  // prefixOverride：onSearch/扫描按钮先 setState 再触发扫描，render 闭包里的 activePrefix
  // 还是旧值，必须显式传入新前缀，否则首次扫描用的是上一次的过滤条件。
  const scan = async (
    nextCursor: number,
    merge: boolean,
    prefixOverride?: string,
  ) => {
    if (info?.backend !== 'redis') {
      return;
    }
    setLoading(true);
    try {
      const result = await getCacheKeys({
        prefix: prefixOverride ?? activePrefix,
        cursor: nextCursor,
      });
      setKeys((current) =>
        merge ? [...current, ...result.keys] : result.keys,
      );
      setCursor(result.nextCursor);
    } catch {
      message.error('扫描键失败');
    } finally {
      setLoading(false);
    }
  };

  const openValue = async (record: CacheKeyDto) => {
    setValueTarget(record);
    setValueLoading(true);
    setValueContent(undefined);
    try {
      const result = await getCacheValue(record.key);
      setValueContent(
        `${result.content}${result.truncated ? '\n…（内容已截断）' : ''}`,
      );
    } catch (e) {
      setValueContent(
        `读取失败：${e instanceof Error ? e.message : '未知错误'}（仅允许 ABP 缓存键：c: / t: 前缀${
          info?.keyPrefix ? `或配置的 ${info.keyPrefix} 前缀` : ''
        }）`,
      );
    } finally {
      setValueLoading(false);
    }
  };

  const redisDisabled = info && info.backend !== 'redis';

  return (
    <PageContainer>
      <Card
        title="缓存概览"
        extra={
          <Space>
            <Button
              onClick={() => {
                loadInfo();
                setKeys([]);
                setCursor(0);
              }}
            >
              刷新
            </Button>
          </Space>
        }
      >
        {info ? (
          <Space size="large" wrap>
            <span>
              后端：
              {info.backend === 'redis' ? (
                <Tag color="green">Redis {info.redisVersion ?? ''}</Tag>
              ) : (
                <Tag>Memory</Tag>
              )}
            </span>
            {info.backend === 'redis' && (
              <>
                <span>总键数：{info.totalKeys ?? '-'}</span>
                <span>
                  已用内存：{formatBytes(info.usedMemoryBytes)}（上限{' '}
                  {info.maxMemoryBytes === 0
                    ? '不限制'
                    : formatBytes(info.maxMemoryBytes)}
                  ）
                </span>
              </>
            )}
            <span>
              ABP 键前缀：
              <Typography.Text code>
                {info.keyPrefix || 'c: / t:（默认结构前缀）'}
              </Typography.Text>
            </span>
          </Space>
        ) : (
          '加载中…'
        )}
        {info?.connectionError && (
          <Alert
            type="error"
            showIcon
            style={{ marginTop: 12 }}
            title="Redis 连接失败"
            description={info.connectionError}
          />
        )}
        {redisDisabled && (
          <Alert
            type="info"
            showIcon
            style={{ marginTop: 12 }}
            title="当前缓存后端是 Memory（开发默认），无法枚举键。启用 Redis（Redis:IsEnabled + Redis:Configuration）后可浏览、查看与删除缓存键。"
          />
        )}
      </Card>

      <Card
        title="键浏览"
        style={{ marginTop: 16 }}
        extra={
          <Space>
            <Input.Search
              placeholder="键名包含匹配（后端自动按 *关键词* 模糊扫描）"
              allowClear
              style={{ width: 320 }}
              value={prefixInput}
              onChange={(e) => setPrefixInput(e.target.value)}
              onSearch={(value) => {
                setActivePrefix(value || undefined);
                setKeys([]);
                setCursor(0);
                setTimeout(() => scan(0, false, value || undefined), 0);
              }}
            />
            <Button
              type="primary"
              disabled={!!redisDisabled}
              onClick={() => {
                setActivePrefix(prefixInput || undefined);
                setKeys([]);
                setCursor(0);
                setTimeout(() => scan(0, false, prefixInput || undefined), 0);
              }}
            >
              扫描
            </Button>
          </Space>
        }
      >
        <Table<CacheKeyDto>
          rowKey="key"
          size="small"
          loading={loading}
          dataSource={keys}
          pagination={false}
          columns={[
            {
              title: '键',
              dataIndex: 'key',
              ellipsis: true,
              render: (key: string) => (
                <Typography.Text code copyable={{ text: key }}>
                  {key}
                </Typography.Text>
              ),
            },
            {
              title: '类型',
              dataIndex: 'type',
              width: 90,
              render: (type: string) => <Tag>{type}</Tag>,
            },
            {
              title: '大小',
              dataIndex: 'sizeBytes',
              width: 110,
              render: (size: number | null) => formatBytes(size),
            },
            {
              title: '过期',
              dataIndex: 'ttlSeconds',
              width: 110,
              render: (ttl: number | null) => formatTtl(ttl),
            },
            {
              title: '操作',
              key: 'actions',
              width: 140,
              render: (_, record) => [
                <a key="view" onClick={() => openValue(record)}>
                  查看
                </a>,
                access.canManageCache ? (
                  <Popconfirm
                    key="delete"
                    title="确认删除该缓存键？"
                    description="删除后相关缓存将按需重建，期间可能出现瞬时回源压力。"
                    onConfirm={async () => {
                      try {
                        await deleteCacheKey(record.key);
                        message.success('已删除');
                        setKeys((current) =>
                          current.filter((k) => k.key !== record.key),
                        );
                      } catch {
                        message.error(
                          '删除失败（仅允许 ABP 缓存键：c: / t: 前缀或配置的 KeyPrefix 前缀）',
                        );
                      }
                    }}
                  >
                    <a>删除</a>
                  </Popconfirm>
                ) : null,
              ],
            },
          ]}
        />
        {cursor !== 0 && (
          <Button
            block
            style={{ marginTop: 12 }}
            loading={loading}
            onClick={() => scan(cursor, true)}
          >
            继续扫描（SCAN 游标 {cursor}）
          </Button>
        )}
      </Card>

      <Drawer
        title={`缓存值 - ${valueTarget?.key ?? ''}`}
        size={640}
        open={!!valueTarget}
        onClose={() => setValueTarget(undefined)}
        destroyOnHidden
      >
        {valueLoading ? (
          '读取中…'
        ) : (
          <Typography.Paragraph>
            <pre style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>
              {valueContent}
            </pre>
          </Typography.Paragraph>
        )}
      </Drawer>
    </PageContainer>
  );
};

export default CacheMonitorPage;
