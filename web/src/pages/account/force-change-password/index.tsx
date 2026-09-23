import { PageContainer } from '@ant-design/pro-components';
import { history, useModel } from '@umijs/max';
import { Button, Card, Form, Input, message } from 'antd';
import React, { useEffect, useState } from 'react';
import { changeMyPassword } from '@/abp/account';
import { getCurrentAccountStatus } from '@/abp/identity';

/**
 * 强制改密页面。
 * 当用户被要求下次登录改密或密码已过期时，登录后自动重定向到此页面。
 * 改密成功后跳转回首页。
 */
const ForceChangePasswordPage: React.FC = () => {
  const [form] = Form.useForm();
  const { initialState, setInitialState } = useModel('@@initialState');
  const [reason, setReason] = useState<string>();
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    // 检查当前账户状态，获取改密原因
    getCurrentAccountStatus()
      .then((status) => {
        if (!status.shouldChangePassword) {
          // 不需要改密，直接跳回首页
          history.replace('/');
          return;
        }
        setReason(status.reason);
      })
      .catch(() => {
        // 接口异常时也允许改密
      });
  }, []);

  const handleSubmit = async (values: {
    currentPassword: string;
    newPassword: string;
    confirmPassword: string;
  }) => {
    if (values.newPassword !== values.confirmPassword) {
      message.error('两次输入的新密码不一致');
      return;
    }

    setLoading(true);
    try {
      await changeMyPassword({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      });
      message.success('密码修改成功，即将跳转到首页');
      // 刷新用户信息
      if (initialState?.fetchUserInfo) {
        const currentUser = await initialState.fetchUserInfo();
        setInitialState((s) => ({ ...s, currentUser }));
      }
      setTimeout(() => {
        history.replace('/');
      }, 1000);
    } catch (error: any) {
      const errorMessage =
        error?.response?.data?.error?.message || '密码修改失败';
      message.error(errorMessage);
    } finally {
      setLoading(false);
    }
  };

  return (
    <PageContainer title="修改密码">
      <Card style={{ maxWidth: 480, margin: '0 auto' }}>
        {reason && (
          <p style={{ marginBottom: 24, color: '#faad14' }}>{reason}</p>
        )}
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          <Form.Item
            name="currentPassword"
            label="当前密码"
            rules={[{ required: true, message: '请输入当前密码' }]}
          >
            <Input.Password placeholder="请输入当前密码" />
          </Form.Item>
          <Form.Item
            name="newPassword"
            label="新密码"
            rules={[
              { required: true, message: '请输入新密码' },
              { min: 6, message: '密码长度至少 6 位' },
            ]}
          >
            <Input.Password placeholder="请输入新密码" />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label="确认新密码"
            rules={[
              { required: true, message: '请再次输入新密码' },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('newPassword') === value) {
                    return Promise.resolve();
                  }
                  return Promise.reject(new Error('两次输入的密码不一致'));
                },
              }),
            ]}
          >
            <Input.Password placeholder="请再次输入新密码" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={loading} block>
              确认修改
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </PageContainer>
  );
};

export default ForceChangePasswordPage;
