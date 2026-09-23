import { ModalForm, ProFormText } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import React from 'react';

/**
 * 「重命名」操作：ModalForm 包装，提交语义（renameFile + 提示 + 刷新）与
 * useFileActions.handleRename 保持一致。
 */
const RenameAction: React.FC<{
  file: API.FileInfoDto;
  onRenamed: () => void;
  onRename: (file: API.FileInfoDto, fileName: string) => Promise<void>;
}> = ({ file, onRenamed, onRename }) => {
  const intl = useIntl();

  return (
    <ModalForm
      key="rename"
      title={intl.formatMessage(
        { id: 'pages.files.rename.title' },
        { name: file.fileName },
      )}
      trigger={
        <a>{intl.formatMessage({ id: 'pages.files.actions.rename' })}</a>
      }
      modalProps={{ destroyOnHidden: true }}
      width={420}
      initialValues={{ fileName: file.fileName }}
      onFinish={async (values: { fileName?: string }) => {
        if (!file.id || !values.fileName) return false;
        await onRename(file, values.fileName);
        onRenamed();
        return true;
      }}
    >
      <ProFormText
        name="fileName"
        label={intl.formatMessage({ id: 'pages.files.rename.label' })}
        rules={[
          {
            required: true,
            message: intl.formatMessage({ id: 'pages.files.rename.required' }),
          },
        ]}
      />
    </ModalForm>
  );
};

export default RenameAction;
