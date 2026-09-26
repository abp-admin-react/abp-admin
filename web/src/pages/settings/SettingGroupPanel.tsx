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
import { App, Button, Popconfirm, Space, Tooltip } from 'antd';
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
  group: API.SettingGroup;
  /** 保存/重置成功后回调（用于刷新外层数据） */
  onChanged?: () => void;
};

/**
 * SettingUi 通用渲染面板（T1.4）
 * 按 Group2 分块（ProCard），按 Type 选控件。
 * 提交 PUT /api/setting-ui/set-setting-values，重置 PUT /api/setting-ui/reset-setting-values。
 */
const SettingGroupPanel: React.FC<Props> = ({ group, onChanged }) => {
  const { message } = App.useApp();
  const access = useAccess();
  const [form] = ProForm.useForm();
  const [saving, setSaving] = React.useState(false);
  const [resetting, setResetting] = React.useState(false);

  // 读写分离（模块五收口）：无 AbpAdmin.SettingUi.Update 权限时只读，
  // 后端方法级 Authorize 是真防线，这里只做按钮态与提示
  const canUpdate = access.canUpdateSettings;

  const settingInfos = useMemo(() => group.settingInfos || [], [group]);

  // 按 Group2 分块：{ "": [...], "General": [...], "Appearance": [...] }
  const blocks = useMemo(() => {
    const map = new Map<string, API.SettingInfo[]>();
    for (const info of settingInfos) {
      const g2 = getSettingGroup2(info);
      if (!map.has(g2)) map.set(g2, []);
      map.get(g2)!.push(info);
    }
    return Array.from(map.entries());
  }, [settingInfos]);

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

  // antd 的 initialValues 只在表单 mount 时生效；保存/重置后 onChanged 触发外层重载，
  // group 换了新数据但 form 实例不变——不回灌的话界面停在旧值，再点保存会把旧值写回去。
  // 回灌语义 = 以服务端真值为准：本分组的数据重载由本分组保存/重置触发；此外挂载和
  // 权限态变化也会触发外层整体重载（同样回灌服务端真值）。方向始终朝服务端收敛，不会丢服务端数据。
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
      // 只读账号（缺 AbpAdmin.SettingUi.Update）字段一并禁用——
      // 只藏提交按钮不够：表单仍可编辑、Enter 提交仍会发 PUT 然后吃 403
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
        // T3.5：加密设置项永不下发值（后端脱敏），留空保存 = 不修改
        return (
          <ProFormText.Password
            key={info.name}
            {...common}
            placeholder="留空表示不修改"
          />
        );
      case SettingUiComponentTypes.Text:
      default:
        return <ProFormText key={info.name} {...common} />;
    }
  };

  // 按组提交：payload 只含本分组的 settingInfos（表单值经 toSettingString 转回服务端字符串），
  // 其它分组不受影响；无 name 的条目跳过，不进 payload
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
              // 只读态：render 完全替换提交区，只留一条只读提示（与后端 403 语义对齐）。
              // 不再配 searchConfig/submitButtonProps 等——custom render 下它们是死配置。
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
        {blocks.map(([group2, infos]) => (
          <ProCard
            key={group2 || '_default'}
            title={
              group2 || group.groupDisplayName || group.groupName || '通用'
            }
            size="small"
            headerBordered
            colSpan={24}
          >
            {infos.map(renderField)}
          </ProCard>
        ))}
      </ProCard>
    </ProForm>
  );
};

export default SettingGroupPanel;
