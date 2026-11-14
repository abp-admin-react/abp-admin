import {
  ProCard,
  ProForm,
  ProFormCheckbox,
  ProFormDatePicker,
  ProFormDateTimePicker,
  ProFormDigit,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Col, Popconfirm, Row, Space, Tooltip } from 'antd';
import React, { useEffect, useMemo } from 'react';
import {
  fromSettingString,
  getSettingGroup2,
  getSettingOptions,
  getSettingType,
  resetSettingValues,
  SettingUiComponentTypes,
  setSettingValues,
  toSettingString,
} from '@/abp/settingUi';

type Props = {
  group: API.SettingGroup | API.SettingGroup[];
  columns?: 1 | 2;
  /** 保存/重置成功后回调（用于刷新外层数据） */
  onChanged?: () => void;
};

const web3BlockTitles: Record<string, string> = {
  Chain: '链上收款与归集',
  Withdraw: '提现与热钱包',
};

/** 根据设置元数据渲染表单，提交和重置只作用于传入字段。 */
const SettingGroupPanel: React.FC<Props> = ({ group, columns = 1, onChanged }) => {
  const { message } = App.useApp();
  const access = useAccess();
  const [form] = ProForm.useForm();
  const [saving, setSaving] = React.useState(false);
  const [resetting, setResetting] = React.useState(false);

  const canUpdate = access.canUpdateSettings;

  const groups = useMemo(
    () => (Array.isArray(group) ? group : [group]),
    [group],
  );
  const settingInfos = useMemo(
    () => groups.flatMap((item) => item.settingInfos || []),
    [groups],
  );

  // 每个来源组内按 Group2 分块，跨组表单的提交范围仍以传入字段为准。
  const blocks = useMemo(() => {
    return groups.flatMap((item) => {
      const sections = new Map<string, API.SettingInfo[]>();
      for (const info of item.settingInfos || []) {
        const section = getSettingGroup2(info);
        const fields = sections.get(section);
        if (fields) fields.push(info);
        else sections.set(section, [info]);
      }
      return Array.from(sections, ([group2, infos]) => ({
        key: `${item.groupName ?? '_default'}:${group2}:${infos[0]?.name ?? ''}`,
        title:
          (item.groupName === 'Web3' && web3BlockTitles[group2]) ||
          (group2 === 'Others' ? undefined : group2) ||
          item.groupDisplayName ||
          item.groupName ||
          '通用',
        infos,
      }));
    });
  }, [groups]);

  // 初始值：把后端 string 按控件类型转回表单值
  const initialValues = useMemo(() => {
    const values: Record<string, unknown> = {};
    for (const info of settingInfos) {
      if (!info.name) continue;
      const type = getSettingType(info);
      values[info.name] = fromSettingString(info.value, type);
    }
    return values;
  }, [settingInfos]);

  // initialValues 只在挂载时生效，保存或重置后用新读到的设置回填。
  useEffect(() => {
    form.setFieldsValue(initialValues);
  }, [form, initialValues]);

  const renderField = (info: API.SettingInfo) => {
    if (!info.name) return null;
    const type = getSettingType(info);
    const label = info.displayName || info.name;
    const tooltip = info.description || undefined;
    const common = {
      name: info.name,
      label,
      tooltip,
      disabled: !canUpdate,
    };
    switch (type) {
      case SettingUiComponentTypes.Checkbox:
        return (
          <ProFormCheckbox key={info.name} {...common}>
            {label}
          </ProFormCheckbox>
        );
      case SettingUiComponentTypes.Number:
        return <ProFormDigit key={info.name} {...common} min={0} />;
      case SettingUiComponentTypes.Select:
        return (
          <ProFormSelect
            key={info.name}
            {...common}
            options={getSettingOptions(info)}
            allowClear={false}
          />
        );
      case SettingUiComponentTypes.Date:
        return <ProFormDatePicker key={info.name} {...common} width="md" />;
      case SettingUiComponentTypes.DateTime:
        return <ProFormDateTimePicker key={info.name} {...common} width="md" />;
      case SettingUiComponentTypes.Password:
        // 服务端不回传加密设置的原值，留空保留已存值。
        return (
          <ProFormText.Password
            key={info.name}
            {...common}
            placeholder="留空表示不修改"
          />
        );
      default:
        return <ProFormText key={info.name} {...common} />;
    }
  };

  const handleFinish = async (values: Record<string, unknown>) => {
    setSaving(true);
    try {
      const payload: Record<string, string> = {};
      for (const info of settingInfos) {
        if (!info.name) continue;
        payload[info.name] = toSettingString(values[info.name]);
      }
      await setSettingValues(payload);
      message.success('设置已保存');
      onChanged?.();
      return true;
    } catch (e: any) {
      message.error(e?.message || '保存失败');
      return false;
    } finally {
      setSaving(false);
    }
  };

  const handleReset = async () => {
    setResetting(true);
    try {
      const names = settingInfos.map((i) => i.name).filter(Boolean) as string[];
      await resetSettingValues(names);
      message.success('已重置为默认值');
      onChanged?.();
    } catch (e: any) {
      message.error(e?.message || '重置失败');
    } finally {
      setResetting(false);
    }
  };

  return (
    <ProForm
      form={form}
      initialValues={initialValues}
      onFinish={handleFinish}
      submitter={
        canUpdate
          ? {
              searchConfig: { submitText: '保存' },
              submitButtonProps: { loading: saving },
              resetButtonProps: { style: { display: 'none' } },
              render: (_, dom) => (
                <Space>
                  <Popconfirm
                    title="确定重置为默认值？"
                    okText="重置"
                    cancelText="取消"
                    onConfirm={handleReset}
                  >
                    <Button loading={resetting} danger>
                      重置为默认值
                    </Button>
                  </Popconfirm>
                  {dom}
                </Space>
              ),
            }
          : {
              render: () => (
                <Tooltip title="缺少修改设置值权限（AbpAdmin.SettingUi.Update），仅可查看">
                  <span style={{ color: 'rgba(0,0,0,0.45)', fontSize: 12 }}>
                    当前账号为只读：缺少设置修改权限
                  </span>
                </Tooltip>
              ),
            }
      }
    >
      <ProCard ghost gutter={[0, 12]} direction="column">
        {blocks.map(({ key, title, infos }) => (
          <ProCard
            key={key}
            title={title}
            size="small"
            headerBordered
            colSpan={24}
          >
            {columns === 2 ? (
              <Row gutter={24}>
                {infos.map((info) => (
                  <Col key={info.name} xs={24} md={12}>
                    {renderField(info)}
                  </Col>
                ))}
              </Row>
            ) : infos.map(renderField)}
          </ProCard>
        ))}
      </ProCard>
    </ProForm>
  );
};

export default SettingGroupPanel;
