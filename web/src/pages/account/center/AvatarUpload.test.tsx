import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as imaging from '@/abp/imaging';

// useModel('@@initialState') 的可变替身：验证上传成功后 initialState 里的头像 URL 被刷新
let mockAvatarUrl: string | undefined;
const setInitialState = vi.fn();

vi.mock('@umijs/max', () => ({
  useModel: () => ({
    initialState: {
      currentUser: { userName: 'admin', avatar: mockAvatarUrl },
    },
    setInitialState,
  }),
}));

vi.mock('@/abp/imaging', () => ({
  getMyAvatarInfo: vi.fn(),
  uploadAvatar: vi.fn(),
}));

// ImgCrop 替身：直接渲染 children（裁剪交互不在单测范围）
vi.mock('antd-img-crop', () => ({
  default: ({ children }: any) => <div data-testid="img-crop">{children}</div>,
}));

vi.mock('antd', async () => {
  const actual = await vi.importActual('antd');
  return {
    ...actual,
    App: Object.assign(() => null, {
      useApp: () => ({
        message: {
          success: vi.fn(),
          error: vi.fn(),
        },
      }),
    }),
    Upload: Object.assign(
      ({ children, beforeUpload, customRequest }: any) => (
        <div data-testid="upload">
          {children}
          <button
            type="button"
            data-testid="upload-trigger"
            onClick={() => {
              const file = new File(['x'.repeat(1024)], 'avatar.png', {
                type: 'image/png',
              });
              if (beforeUpload?.(file) !== false) {
                customRequest?.({ file, onSuccess: vi.fn(), onError: vi.fn() });
              }
            }}
          >
            模拟上传
          </button>
          <button
            type="button"
            data-testid="upload-oversize-trigger"
            onClick={() => {
              const big = new File(['x'], 'big.png', { type: 'image/png' });
              Object.defineProperty(big, 'size', { value: 6 * 1024 * 1024 });
              const result = beforeUpload?.(big);
              // 与 antd Upload 行为一致：false 或 LIST_IGNORE('ignore') 都阻止上传
              if (result !== false && result !== 'ignore') {
                customRequest?.({
                  file: big,
                  onSuccess: vi.fn(),
                  onError: vi.fn(),
                });
              }
            }}
          >
            模拟超大上传
          </button>
        </div>
      ),
      { LIST_IGNORE: 'ignore' },
    ),
  };
});

import AvatarUpload from './AvatarUpload';

describe('AvatarUpload', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAvatarUrl = '/api/app/profile-avatar/user-1?v=old12345';
  });

  it('用 initialState 里的头像 URL 渲染头像', () => {
    render(<AvatarUpload />);

    const img = screen.getByRole('img');
    expect(img).toHaveAttribute('src', mockAvatarUrl);
  });

  it('上传成功后刷新 initialState 里的头像 URL（新版本号）', async () => {
    vi.mocked(imaging.uploadAvatar).mockResolvedValue(undefined);
    vi.mocked(imaging.getMyAvatarInfo).mockResolvedValue({
      avatarUrl: '/api/app/profile-avatar/user-1?v=new67890',
    });

    render(<AvatarUpload />);
    screen.getByTestId('upload-trigger').click();

    await waitFor(() => {
      expect(imaging.uploadAvatar).toHaveBeenCalledTimes(1);
      expect(imaging.getMyAvatarInfo).toHaveBeenCalledTimes(1);
    });

    // initialState 的头像被更新为带新版本参数的 URL（右上角立即变化）
    await waitFor(() => {
      expect(setInitialState).toHaveBeenCalled();
    });
    const updater = setInitialState.mock.calls[0][0] as (s: any) => any;
    const next = updater({
      currentUser: { userName: 'admin', avatar: mockAvatarUrl },
    });
    expect(next.currentUser.avatar).toBe(
      '/api/app/profile-avatar/user-1?v=new67890',
    );
  });

  it('超过 5 MB 的文件在 beforeUpload 被拦截，不调用上传', async () => {
    render(<AvatarUpload />);
    screen.getByTestId('upload-oversize-trigger').click();

    await new Promise((r) => setTimeout(r, 50));
    expect(imaging.uploadAvatar).not.toHaveBeenCalled();
  });
});
