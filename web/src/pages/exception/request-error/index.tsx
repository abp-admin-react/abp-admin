import { Button, Result } from 'antd';

export default function RequestError() {
  return (
    <Result
      status="error"
      title="请求未能完成"
      subTitle="如果你正在登录，请返回登录页重试。"
      extra={
        <Button type="primary" href="/user/login">
          返回登录
        </Button>
      }
    />
  );
}
