import {
  ModalForm,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { App } from 'antd';
import type React from 'react';
import { queryClient } from '@/queryClient';
import type { DataDictionaryListItem } from '../service';
import {
  createDataDictionary,
  getDataDictionaryByCode,
  saveDataDictionaryItems,
} from '../service';

interface DictionaryFormProps {
  title: string;
  trigger: React.ReactElement;
  /** 编辑时传入现有字典；新建时不传 */
  initialValues?: DataDictionaryListItem;
  onSuccess: () => void;
}

/**
 * 新建/编辑字典本体（T3.4 第 7 步）。
 * 编辑改显示名与描述，走原子保存端点（同一工作单元提交 Items+元数据，带静态结构锁）：
 * 模块 UpdateAsync 是全量替换语义且无静态守卫，重放旧快照会把部署后种子新增的项静默删掉。
 * 提交时现取合并视图（含 tagType/order），避免表单打开期间的快照过期。
 * 静态字典的编码由代码定义，不允许改（编码字段只在新建时可编辑）。
 */
const DictionaryForm: React.FC<DictionaryFormProps> = ({
  title,
  trigger,
  initialValues,
  onSuccess,
}) => {
  const { message } = App.useApp();
  const isEdit = !!initialValues?.id;

  return (
    <ModalForm
      title={title}
      trigger={trigger}
      initialValues={{
        code: initialValues?.code,
        displayText: initialValues?.displayText,
        description: initialValues?.description,
      }}
      onFinish={async (values) => {
        try {
          if (isEdit && initialValues?.code) {
            // 以服务端当前数据为准组装 items（含 tagType），防止旧快照漏项被全量替换删除
            const view = await getDataDictionaryByCode(initialValues.code);
            if (!view) {
              // 字典在表单打开期间被并发删除：显式报错而不是 view.items 抛 TypeError
              throw new Error('字典已被删除，请刷新列表');
            }
            await saveDataDictionaryItems(initialValues.code, {
              displayText: values.displayText,
              description: values.description,
              items: (view.items ?? []).map((it) => ({
                code: it.code ?? '',
                displayText: it.displayText ?? '',
                description: it.description,
                tagType: it.tagType,
              })),
            });
            // 与字典项编辑器同一份缓存契约：显示名/描述变了要让全站字典消费方失效
            await queryClient.invalidateQueries({
              queryKey: ['data-dictionary'],
            });
            message.success('已保存');
          } else {
            // 创建一律是非静态字典（IsStatic 不开放给 API，静态字典只能由代码定义）
            await createDataDictionary({
              code: values.code,
              displayText: values.displayText,
              description: values.description,
            });
            message.success('已创建');
          }
          onSuccess();
          return true;
        } catch (e) {
          // 服务端校验失败（静态结构锁/编码重复/超长等）必须可见：
          // 吞掉异常时弹窗只是不关闭，用户不知道保存没发生。
          // return false = 阻止 ModalForm 自动关闭，表单值留在输入框里可改后重提
          message.error(
            e instanceof Error && e.message
              ? e.message
              : '保存失败，请检查输入后重试',
          );
          return false;
        }
      }}
    >
      <ProFormText
        name="code"
        label="编码"
        disabled={isEdit}
        rules={[{ required: true, message: '请输入编码' }]}
        tooltip="编码是代码与前端消费字典的键，创建后不可改"
      />
      <ProFormText
        name="displayText"
        label="显示名"
        rules={[{ required: true, message: '请输入显示名' }]}
      />
      <ProFormTextArea name="description" label="描述" />
    </ModalForm>
  );
};

export default DictionaryForm;
