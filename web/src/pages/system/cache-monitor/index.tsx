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
import React, { useCallback, useEffect, useRef, useState } from 'react';
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

  // 刷新编排（挂载自动扫与刷新按钮共用，唯一的"清表 → 拉概览 → 扫第一页"实现）：
  // 扫描结果只存在组件 state 里，不自动扫的话"空表"与"还没扫过"无法区分，看起来像
  // 数据丢了。seed 是本次的过滤词来源——undefined（挂载/未播种）时回落
  // keyPrefix || 'c:'：keyPrefix 是 string（可空性镜像合同），?? 会放过空串、
  // 把默认口径漂移成 [ct]:** 全键空间扫；用户显式清空输入框（''）则尊重其"无过滤词"
  const rescan = (seed?: string) => {
    setKeys([]);
    setCursor(0);
    void loadInfo().then((loaded) => {
      if (loaded) {
        scanFirstPage(
          seed ?? (loaded.keyPrefix || 'c:'),
          loaded.backend,
          loaded.connectionError,
        );
      }
    });
  };

  // 挂载期一次性编排（ref 只防 StrictMode 双挂载；刷新按钮走 rescan(prefixInput)）
  const mountedScanRef = useRef(false);
  useEffect(() => {
    if (mountedScanRef.current) {
      return;
    }
    mountedScanRef.current = true;
    rescan();
    // rescan 依赖 loadInfo（useCallback([message])，message 来自 App.useApp() 稳定引用）——
    // 挂载期一次性编排，无需跟踪其依赖
  }, []);

  // prefixOverride：onSearch/扫描按钮先 setState 再触发扫描，render 闭包里的 activePrefix
  // 还是旧值，必须显式传入新前缀，否则首次扫描用的是上一次的过滤条件。
  const scan = async (
    nextCursor: number,
    merge: boolean,
    prefixOverride?: string,
  ) => {
    // info 为 undefined 时放行：挂载期编排的自动首扫发生在 loadInfo resolve 之后、
    // setInfo 提交渲染之前，此 render 闭包里的 info 还是 undefined；backend 判定
    // 已由调用方（scanFirstPage）用加载结果做过。info 已载的正常路径（onSearch、
    // 继续扫描）照常拦截 memory 后端。
    if (info && info.backend !== 'redis') {
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

  // 首页扫描的统一入口（挂载自动扫 + 刷新重扫共用）：与 onSearch/扫描按钮一致地同步
  // activePrefix——"继续扫描"不带过滤词，靠它沿用首页口径；漏同步会让首页 c:** 的
  // 续扫退化成 [ct]:**（口径漂移：合成游标失配静默截断、真实游标混入租户键）。
  // 连接已失败（connectionError 非空）时不扫：红色告警已说明状况，不白发注定失败的请求。
  const scanFirstPage = (
    prefix: string,
    backend?: string,
    connectionError?: string | null,
  ) => {
    if (backend !== 'redis' || connectionError) {
      return;
    }
    setActivePrefix(prefix || undefined);
    void scan(0, false, prefix || undefined);
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
        `读取失败：${e instanceof Error ? e.message : '未知错误'}（仅允许 ABP 缓存键：c: / t: 结构前缀开头${
          info?.keyPrefix ? `；本应用键含 ${info.keyPrefix} 隔离前缀` : ''
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
                // 按当前输入框的词重扫（连接失败则只刷新概览，红色告警自会说明）
                rescan(prefixInput);
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
              应用隔离前缀：
              <Typography.Text code>
                {info.keyPrefix || '（未配置：键仅带 c: / t: 结构前缀）'}
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
        {info?.infoError && (
          <Alert
            type="warning"
            showIcon
            style={{ marginTop: 12 }}
            title="服务器信息不可用（连接正常，键浏览与操作不受影响）"
            description={info.infoError}
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
              placeholder="键名包含匹配；输入 c: / t: 开头按宿主/租户过滤；应用前缀可直接粘贴"
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
              // memory 后端或连接已失败都不可扫：后者的红色告警已说明状况，不白发注定
              // 失败的请求——与 scanFirstPage 的闸门同一口径（此前按钮只看 redisDisabled）
              disabled={!!redisDisabled || !!info?.connectionError}
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
                          `删除失败（仅允许 ABP 缓存键：c: / t: 结构前缀开头${
                            info?.keyPrefix
                              ? `；本应用键含 ${info.keyPrefix} 隔离前缀`
                              : ''
                          }）`,
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
