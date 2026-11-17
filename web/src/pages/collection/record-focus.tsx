import { Link } from '@umijs/max';
import { Alert, Button, Space } from 'antd';
import type { ReactNode } from 'react';

export function isValidRecordId(value: string) {
  return /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(value);
}

export function RecordFocus({
  kind,
  listPath,
  invalid,
  failed,
  loading,
  summary,
  reload,
}: {
  kind: '充值' | '提现' | '归集任务';
  listPath: string;
  invalid: boolean;
  failed: boolean;
  loading: boolean;
  summary?: ReactNode;
  reload: () => void;
}) {
  const recordName = kind === '归集任务' ? kind : `${kind}单`;
  const title = invalid
    ? `${recordName}链接无效`
    : failed
      ? `加载${recordName}失败`
      : loading && !summary
        ? `正在定位${recordName}`
        : summary
          ? `已定位${recordName}`
          : `未找到该${recordName}`;

  return (
    <Alert
      showIcon
      type={
        failed
          ? 'error'
          : invalid || (!loading && !summary)
            ? 'warning'
            : 'info'
      }
      title={title}
      description={summary}
      style={{ marginBottom: 16 }}
      action={
        <Space wrap>
          {failed && (
            <Button size="small" onClick={reload}>
              重试
            </Button>
          )}
          <Link to={listPath}>查看全部{recordName}</Link>
        </Space>
      }
    />
  );
}
