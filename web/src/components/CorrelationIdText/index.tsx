import { useNavigate } from '@umijs/max';
import { Typography } from 'antd';
import React from 'react';

/**
 * 关联 ID 展示 + 跨日志串联跳转（操作日志 ⇄ 审计日志共用）。
 * value 可源自外部 X-Correlation-Id 头透传，跳转 URL 做编码防查询串注入。
 */
const CorrelationIdText: React.FC<{
  value?: string | null;
  linkTo: string;
  linkText: string;
}> = ({ value, linkTo, linkText }) => {
  const navigate = useNavigate();

  if (!value) {
    return <span>-</span>;
  }

  return (
    <span>
      <Typography.Text copyable style={{ marginRight: 8 }}>
        {value}
      </Typography.Text>
      <a
        onClick={() =>
          navigate(`${linkTo}?correlationId=${encodeURIComponent(value)}`)
        }
      >
        {linkText}
      </a>
    </span>
  );
};

export default CorrelationIdText;
