import { Alert, App, Button, Card, Space, Typography } from 'antd';
import { useMemo, useRef, useState } from 'react';
import { createBrowserTransport } from '@/abp/customer/browserAdapter';
import {
  type CustomerError,
  type CustomerLocalMessages,
  isCustomerError,
  normalizeCustomerError,
  publicErrorCatalog,
} from '@/abp/customer/errors';
import {
  getClientBizProject,
  getClientBizProjectList,
} from '@/abp/customer/example';
import type { CustomerAuthAdapter } from '@/abp/customer/request';
import { createCustomerRequest } from '@/abp/customer/request';
import { getAccessToken } from '@/abp/oidc';

const localMessages: CustomerLocalMessages = {
  authentication: '请先登录后再试。',
  forbidden: '没有权限执行此操作。',
  validation: '输入校验未通过。',
  notFound: '请求的资源不存在。',
  rateLimited: '请求过于频繁，请稍后再试。',
  conflict: '数据已变化，请刷新后重试。',
  business: '操作失败，请稍后重试。',
  server: '系统繁忙，请稍后重试。',
  network: '网络不可用，请稍后重试。',
  timeout: '请求超时，请稍后重试。',
  cancelled: '请求已取消。',
  outcomeUnknown: '结果暂未确认，请查看记录。',
};

function describeError(error: unknown): string {
  if (!isCustomerError(error)) {
    return '未知失败';
  }
  const parts = [`kind=${error.kind}`, `httpStatus=${error.httpStatus ?? '-'}`];
  if (error.code) {
    parts.push(`code=${error.code}`);
  }
  parts.push(error.message);
  return parts.join(' | ');
}

export default function CustomerErrorsDemoPage() {
  const { message } = App.useApp();
  const [last, setLast] = useState<string>('尚未调用');
  const tokenRef = useRef<string | undefined>(undefined);
  const demoAuth = useMemo<CustomerAuthAdapter>(
    () => ({
      getSnapshot: () => ({ sessionGeneration: 1, tenantId: null }),
      applyHeaders: (headers) => {
        const token = tokenRef.current;
        if (token) {
          headers.Authorization = `Bearer ${token}`;
        }
      },
      recoverFromUnauthorized: async () => false,
    }),
    [],
  );
  const request = useMemo(
    () =>
      createCustomerRequest({
        transport: createBrowserTransport(),
        auth: demoAuth,
        messages: localMessages,
        notify: {
          global: (error: CustomerError) => {
            message.error(error.message);
          },
          form: (error: CustomerError) => {
            message.warning(error.message);
          },
        },
      }),
    [demoAuth, message],
  );

  const run = async (label: string, action: () => Promise<unknown>) => {
    tokenRef.current = await getAccessToken();
    if (!tokenRef.current) {
      const text = `${label} 失败：kind=authentication | 当前页没有可用令牌，请先登录管理端。`;
      setLast(text);
      message.error('请先登录管理端');
      return;
    }
    try {
      const result = await action();
      const text = `${label} 成功：${JSON.stringify(result)}`;
      setLast(text);
      message.success(label);
    } catch (error) {
      const text = `${label} 失败：${describeError(error)}`;
      setLast(text);
    }
  };

  const showMock = (label: string, error: CustomerError) => {
    setLast(`${label}：${describeError(error)}`);
    message.info(error.message);
  };

  return (
    <Card title="客户错误协议演示（开发）">
      <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
        <Alert
          type="warning"
          showIcon
          title="仅用于开发联调客户错误协议，不进入生产菜单。真实列表/详情会带上当前管理端令牌；Development 下已打开样例入口。"
        />
        <Typography.Paragraph>
          列表与详情走
          `/api/client/biz-template/projects`；其余按钮只展示归一化结果，不改管理端请求配置。
        </Typography.Paragraph>
        <Space wrap>
          <Button
            type="primary"
            onClick={() =>
              run('拉取项目列表', () => getClientBizProjectList(request))
            }
          >
            拉取项目列表
          </Button>
          <Button
            onClick={() =>
              run('拉取单个项目', () =>
                getClientBizProject(
                  request,
                  '00000000-0000-0000-0000-000000000001',
                ),
              )
            }
          >
            拉取单个项目
          </Button>
          <Button
            onClick={() =>
              showMock(
                '模拟 401',
                normalizeCustomerError({
                  status: 401,
                  body: {
                    error: {
                      code: 'Client:AuthenticationRequired',
                      message: '请先登录后再试。',
                      details: 'INTERNAL',
                      data: { correlationId: 'demo-401' },
                    },
                  },
                  catalog: publicErrorCatalog,
                  localMessages,
                }),
              )
            }
          >
            模拟 401
          </Button>
          <Button
            onClick={() =>
              showMock(
                '模拟业务 403',
                normalizeCustomerError({
                  status: 403,
                  body: {
                    error: {
                      code: 'BizTemplate:Public:ProjectUnavailable',
                      message: '该项目当前不可用，请刷新后查看。',
                      data: { correlationId: 'demo-403' },
                    },
                  },
                  catalog: publicErrorCatalog,
                  localMessages,
                }),
              )
            }
          >
            模拟业务 403
          </Button>
          <Button
            onClick={() =>
              showMock(
                '模拟校验失败',
                normalizeCustomerError({
                  status: 400,
                  body: {
                    error: {
                      code: 'Client:ValidationFailed',
                      message: '输入校验未通过。',
                      data: { correlationId: 'demo-400' },
                      validationErrors: [
                        { message: '名称不能为空', members: ['name'] },
                      ],
                    },
                  },
                  catalog: publicErrorCatalog,
                  localMessages,
                }),
              )
            }
          >
            模拟校验失败
          </Button>
          <Button
            onClick={() =>
              showMock(
                '模拟写超时',
                normalizeCustomerError({
                  method: 'POST',
                  timeout: true,
                  catalog: publicErrorCatalog,
                  localMessages,
                }),
              )
            }
          >
            模拟写超时
          </Button>
        </Space>
        <Typography.Text>{last}</Typography.Text>
      </Space>
    </Card>
  );
}
