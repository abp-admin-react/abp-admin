import {
  ModalForm,
  ProCard,
  ProFormSwitch,
  ProFormText,
} from '@ant-design/pro-components';
import { App, Button, Popconfirm } from 'antd';
import React, { useState } from 'react';
import {
  deleteFeatures,
  type FeatureDto,
  type FeatureGroup,
  getFeatures,
  updateFeatures,
} from '@/abp/features';

type FeatureModalProps = {
  open: boolean;
  title: string;
  providerName: string;
  providerKey?: string;
  onClose: () => void;
};

/**
 * 按开关渲染的判定：值类型名含 Toggle，或当前值本身就是 'true'/'false' 字符串。
 * 后者兜底：provider 未声明 valueType 的纯布尔功能也能正确渲染成 Switch。
 */
function isToggle(feature: FeatureDto) {
  const typeName = feature.valueType?.name || '';
  return (
    typeName.includes('Toggle') ||
    feature.value === 'true' ||
    feature.value === 'false'
  );
}

const FeatureModal: React.FC<FeatureModalProps> = ({
  open,
  title,
  providerName,
  providerKey,
  onClose,
}) => {
  const { message } = App.useApp();
  const [groups, setGroups] = useState<FeatureGroup[]>([]);

  // 恢复默认 = 调 DELETE 清掉该 provider（providerName+providerKey）名下全部已保存功能值，
  // 后端逐项失效功能值缓存；providerKey 为 undefined（Host 功能）时整体省略参数——
  // 见 abp/features.ts providerKeyParams 的 null/'' 语义说明。关闭弹窗后由 destroyOnHidden
  // 销毁表单，下次打开 request 重新拉取即显示默认值。
  const handleReset = async () => {
    await deleteFeatures(providerName, providerKey);
    message.success('已恢复默认');
    onClose();
  };

  return (
    <ModalForm
      title={title}
      open={open}
      width={640}
      modalProps={{ destroyOnHidden: true, onCancel: onClose }}
      request={async () => {
        const result = await getFeatures(providerName, providerKey);
        const nextGroups = result.groups || [];
        setGroups(nextGroups);
        const values: Record<string, string | boolean> = {};
        nextGroups.forEach((group) => {
          (group.features || []).forEach((item) => {
            values[item.name] = isToggle(item)
              ? item.value === 'true'
              : (item.value ?? '');
          });
        });
        return values;
      }}
      onFinish={async (values) => {
        // 功能值端点只收字符串：Switch 的布尔值序列化回 'true'/'false'，其余 String() 兜底
        const features = Object.entries(values).map(([name, value]) => ({
          name,
          value:
            typeof value === 'boolean' ? String(value) : String(value ?? ''),
        }));
        await updateFeatures(providerName, providerKey, features);
        message.success('功能已保存');
        onClose();
        return true;
      }}
    >
      <div style={{ textAlign: 'right', marginBottom: 8 }}>
        <Popconfirm
          title="确认恢复默认？"
          description="将清除该层级已保存的全部功能值（含此前保存的所有项）。"
          onConfirm={handleReset}
        >
          <Button size="small">恢复默认</Button>
        </Popconfirm>
      </div>
      <ProCard ghost gutter={[0, 12]} direction="column">
        {groups.map((group) => (
          <ProCard
            key={group.name}
            title={group.displayName}
            size="small"
            headerBordered
            colSpan={24}
          >
            {(group.features || []).map((item) =>
              isToggle(item) ? (
                <ProFormSwitch
                  key={item.name}
                  name={item.name}
                  label={item.displayName || item.name}
                  extra={item.description}
                />
              ) : (
                <ProFormText
                  key={item.name}
                  name={item.name}
                  label={item.displayName || item.name}
                  extra={item.description}
                />
              ),
            )}
          </ProCard>
        ))}
      </ProCard>
    </ModalForm>
  );
};

export default FeatureModal;
