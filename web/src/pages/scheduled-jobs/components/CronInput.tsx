import { Input, Select, Typography } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import { previewCron } from '../service';

// Quartz 7 段语法预设（秒 分 时 日 月 周 [年]），不是 Unix 5 段
const CRON_PRESETS = [
  { label: '每分钟', value: '0 * * * * ?' },
  { label: '每 5 分钟', value: '0 */5 * * * ?' },
  { label: '每 30 分钟', value: '0 */30 * * * ?' },
  { label: '每小时', value: '0 0 * * * ?' },
  { label: '每天凌晨 2 点', value: '0 0 2 * * ?' },
  { label: '每天凌晨 3 点', value: '0 0 3 * * ?' },
  { label: '每天中午 12 点', value: '0 0 12 * * ?' },
  { label: '每周一凌晨 3 点', value: '0 0 3 ? * MON' },
  { label: '每周日凌晨 5 点', value: '0 0 5 ? * SUN' },
  { label: '每月 1 日凌晨 4 点', value: '0 0 4 1 * ?' },
];

interface CronInputProps {
  value?: string;
  onChange?: (value: string) => void;
}

/**
 * cron 输入：预设下拉 + 手输 + 下次执行预览。
 * 预览走后端（PreviewCronAsync），不在前端引 cron 解析库——
 * 前端库的语法方言（5/6/7 段）与 Quartz 不一定一致，
 * 两边算出不同结果比没有预览更糟。
 */
const CronInput: React.FC<CronInputProps> = ({ value, onChange }) => {
  const [preview, setPreview] = useState<string[]>([]);
  const [error, setError] = useState<string>();
  const timerRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => {
    clearTimeout(timerRef.current);
    if (!value) {
      setPreview([]);
      setError(undefined);
      return;
    }

    timerRef.current = setTimeout(async () => {
      try {
        const result = await previewCron(value);
        if (result.isValid) {
          setError(undefined);
          setPreview(
            (result.nextFireTimes ?? []).map((t) =>
              new Date(t).toLocaleString(),
            ),
          );
        } else {
          setPreview([]);
          setError(result.errorMessage ?? '无效的 cron 表达式');
        }
      } catch {
        setPreview([]);
        setError('预览失败');
      }
    }, 500);

    return () => clearTimeout(timerRef.current);
  }, [value]);

  return (
    <div>
      <Select
        placeholder="选择预设（也可直接手输）"
        style={{ marginBottom: 8, width: '100%' }}
        allowClear
        options={CRON_PRESETS}
        onChange={(v) => onChange?.(v)}
      />
      <Input
        value={value}
        onChange={(e) => onChange?.(e.target.value)}
        placeholder="Quartz 7 段：秒 分 时 日 月 周 [年]，如 0 0 3 * * ?"
        status={error ? 'error' : undefined}
      />
      <div style={{ minHeight: 22, marginTop: 4 }}>
        {error && <Typography.Text type="danger">{error}</Typography.Text>}
        {!error && preview.length > 0 && (
          <Typography.Text type="secondary">
            接下来执行：{preview.join('；')}
          </Typography.Text>
        )}
      </div>
    </div>
  );
};

export default CronInput;
