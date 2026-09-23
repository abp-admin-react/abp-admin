import {
  ModalForm,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { Form } from 'antd';
import React from 'react';
import type { JobTypeOption, ScheduledJob } from '../service';
import CronInput from './CronInput';

interface ScheduledJobFormProps {
  title: string;
  trigger: React.ReactElement;
  /** 作业类型下拉数据：由页面加载一次后下发。本页每行渲染一个表单实例，若各自拉取会放大成 N+1 请求。 */
  jobTypes: JobTypeOption[];
  initialValues?: Partial<ScheduledJob>;
  onFinish: (values: {
    name: string;
    jobType?: string;
    cronExpression: string;
    isEnabled?: boolean;
    description?: string;
    payload?: string;
  }) => Promise<boolean>;
}

/**
 * 新建/编辑定时作业的表单。
 * - 作业类型下拉数据（jobTypes）由页面加载一次后经 props 下发，本组件不自取；
 * - 编辑态（initialValues.id 存在）时 jobType 选择器禁用：handler 绑定是作业的标识，改绑等于换成另一个作业；
 * - onFinish 由页面注入，返回 true 时 ModalForm 自动关窗，本组件随后 resetFields。
 */
const ScheduledJobForm: React.FC<ScheduledJobFormProps> = ({
  title,
  trigger,
  jobTypes,
  initialValues,
  onFinish,
}) => {
  const [form] = Form.useForm();
  const isEdit = !!initialValues?.id;

  return (
    <ModalForm
      title={title}
      trigger={trigger}
      form={form}
      modalProps={{ destroyOnHidden: true }}
      initialValues={{ isEnabled: false, ...initialValues }}
      onFinish={async (values) => {
        const ok = await onFinish(values as never);
        if (ok) {
          form.resetFields();
        }
        return ok;
      }}
    >
      <ProFormText
        name="name"
        label="名称"
        rules={[{ required: true, message: '请输入名称' }]}
      />
      <ProFormSelect
        name="jobType"
        label="作业类型"
        disabled={isEdit}
        rules={[{ required: true, message: '请选择作业类型' }]}
        options={jobTypes.map((t) => ({
          label: t.displayName,
          value: t.jobType,
        }))}
      />
      <Form.Item
        name="cronExpression"
        label="cron 表达式"
        rules={[{ required: true, message: '请输入 cron 表达式' }]}
      >
        <CronInput />
      </Form.Item>
      <ProFormSwitch name="isEnabled" label="启用" />
      <ProFormTextArea name="description" label="描述" />
      <ProFormTextArea
        name="payload"
        label="参数（JSON，可选）"
        fieldProps={{ rows: 2 }}
      />
    </ModalForm>
  );
};

export default ScheduledJobForm;
