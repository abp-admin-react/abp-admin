import { UserOutlined } from '@ant-design/icons';
import { useModel } from '@umijs/max';
import { App, Avatar, Space, Typography, Upload } from 'antd';
import ImgCrop from 'antd-img-crop';
import type React from 'react';
import { getMyAvatarInfo, uploadAvatar } from '@/abp/imaging';

/** 头像大小上限（与后端 Imaging:AvatarMaxByteSize 一致的 UX 预校验，不是安全边界） */
const MAX_AVATAR_BYTES = 5 * 1024 * 1024;

/**
 * 当前用户头像上传（T3.1）。
 * ImgCrop 让用户自己决定裁切区域（后端做的是居中 Crop，半身照效果差）；
 * 上传成功后刷新 initialState 里的头像 URL（带新版本号），右上角头像立即变化。
 */
const AvatarUpload: React.FC = () => {
  const { message } = App.useApp();
  const { initialState, setInitialState } = useModel('@@initialState');
  const avatarUrl = initialState?.currentUser?.avatar;

  return (
    <Space orientation="vertical" size="small">
      <ImgCrop rotationSlider aspect={1} quality={0.9}>
        <Upload
          accept=".jpg,.jpeg,.png"
          maxCount={1}
          showUploadList={false}
          beforeUpload={(file) => {
            // 仅 UX 预校验；服务端的大小/内容校验一道都不能省
            if (file.size > MAX_AVATAR_BYTES) {
              message.error('图片不能超过 5 MB');
              return Upload.LIST_IGNORE;
            }
            return true;
          }}
          customRequest={async ({ file, onSuccess, onError }) => {
            try {
              await uploadAvatar(file as File);
              const info = await getMyAvatarInfo();
              setInitialState((state) =>
                state?.currentUser
                  ? {
                      ...state,
                      currentUser: {
                        ...state.currentUser,
                        avatar: info.avatarUrl || undefined,
                      },
                    }
                  : state,
              );
              message.success('头像已更新');
              onSuccess?.({});
            } catch (error) {
              onError?.(error as Error);
            }
          }}
        >
          <Avatar
            size={96}
            src={avatarUrl}
            icon={<UserOutlined />}
            style={{ cursor: 'pointer' }}
          />
        </Upload>
      </ImgCrop>
      <Typography.Text type="secondary">
        点击头像更换（支持 jpg/png，最大 5 MB）
      </Typography.Text>
    </Space>
  );
};

export default AvatarUpload;
