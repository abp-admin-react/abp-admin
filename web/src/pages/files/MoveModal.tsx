import { ModalForm, ProFormText } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import React from 'react';

/**
 * 「移动」弹窗：目标目录 Id 手填（目录树节点 Id），留空表示移动到根目录；
 * NewFileName 是后端 [Required]，不改名也必须传当前文件名。
 */
const MoveModal: React.FC<{
  target?: API.FileInfoDto;
  onClose: () => void;
  onMove: (
    file: API.FileInfoDto,
    newParentId: string | undefined,
    newFileName: string,
  ) => Promise<void>;
  onMoved: () => void;
}> = ({ target, onClose, onMove, onMoved }) => {
  const intl = useIntl();

  return (
    <ModalForm
      title={intl.formatMessage(
        { id: 'pages.files.move.title' },
        { name: target?.fileName || '' },
      )}
      open={!!target}
      modalProps={{ destroyOnHidden: true }}
      width={480}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      initialValues={{ newFileName: target?.fileName }}
      onFinish={async (values: {
        newParentId?: string;
        newFileName?: string;
      }) => {
        if (!target?.id) return false;
        await onMove(
          target,
          values.newParentId || undefined,
          values.newFileName || target.fileName || '',
        );
        onClose();
        onMoved();
        return true;
      }}
    >
      <ProFormText
        name="newFileName"
        label={intl.formatMessage({ id: 'pages.files.move.fileName' })}
        rules={[
          {
            required: true,
            message: intl.formatMessage({
              id: 'pages.files.move.fileNameRequired',
            }),
          },
        ]}
      />
      <ProFormText
        name="newParentId"
        label={intl.formatMessage({ id: 'pages.files.move.targetDirId' })}
        tooltip={intl.formatMessage({
          id: 'pages.files.move.targetDirIdTooltip',
        })}
      />
    </ModalForm>
  );
};

export default MoveModal;
