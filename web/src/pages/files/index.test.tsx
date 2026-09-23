import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as imaging from '@/abp/imaging';
import * as fileManagement from '@/services/fileManagement';

// 权限可变的 mock：默认全量权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = {};

// 文案已全部走 locales（useIntl）：mock 直接回显 key，断言用 key 值保持稳定
vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
  useIntl: () => ({
    formatMessage: ({ id }: { id: string }) => id,
  }),
}));

// Mock 缩略图 API（T3.1：文件列表改走后端「列表 + 缩略图 URL」端点）
vi.mock('@/abp/imaging', async () => {
  const actual =
    await vi.importActual<typeof import('@/abp/imaging')>('@/abp/imaging');
  return {
    ...actual,
    getFileListWithThumbnails: vi.fn(),
  };
});

// Mock fileManagement 服务层（页面不直接依赖生成客户端，所以 mock 这一层即可）
vi.mock('@/services/fileManagement', async () => {
  const actual = await vi.importActual<
    typeof import('@/services/fileManagement')
  >('@/services/fileManagement');
  return {
    ...actual,
    getFileList: vi.fn(),
    getFileConfiguration: vi.fn(),
    uploadFile: vi.fn(),
    createDirectory: vi.fn(),
    getDownloadInfo: vi.fn(),
    renameFile: vi.fn(),
    moveFile: vi.fn(),
    deleteFile: vi.fn(),
  };
});

// Mock ProComponents：保留语义化 testid，便于断言
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProCard: ({ children, title }: any) => (
    <div data-testid="pro-card" data-title={title}>
      {children}
    </div>
  ),
  ProTable: ({ columns, toolBarRender, request, params }: any) => {
    // 模拟 ProTable 挂载即请求数据
    request?.({ current: 1, pageSize: 10, ...params }, {}, {});
    return (
      <div data-testid="pro-table">
        <div data-testid="table-columns">
          {columns?.map((col: any) => (
            <div key={col.dataIndex} data-testid={`column-${col.dataIndex}`}>
              {col.title}
            </div>
          ))}
        </div>
        {toolBarRender && <div data-testid="toolbar">{toolBarRender()}</div>}
      </div>
    );
  },
  ModalForm: ({ children, title, trigger }: any) => (
    <div data-testid="modal-form" data-title={title}>
      {trigger}
      {children}
    </div>
  ),
  ProFormText: ({ label }: any) => <input aria-label={label} />,
}));

// Mock antd：Tree/Upload/Popconfirm 用轻量替身
vi.mock('antd', async () => {
  const actual = await vi.importActual('antd');
  return {
    ...actual,
    // 页面在全局 <App> 包裹下使用 App.useApp()，测试环境没有该 Provider，
    // 直接替身化，避免 message 为 undefined
    App: Object.assign(() => null, {
      useApp: () => ({
        message: {
          success: vi.fn(),
          error: vi.fn(),
          info: vi.fn(),
        },
        notification: { success: vi.fn(), error: vi.fn() },
        modal: {},
      }),
    }),
    Tree: ({ treeData }: any) => (
      <div data-testid="dir-tree">
        {treeData?.map((n: any) => (
          <div key={n.key} data-testid={`tree-node-${n.key}`}>
            {n.title}
          </div>
        ))}
      </div>
    ),
    Upload: ({ children, beforeUpload, customRequest }: any) => (
      <div data-testid="upload">
        {children}
        <button
          type="button"
          data-testid="upload-trigger"
          onClick={() => {
            const file = new File(['hello'], 'hello.txt', {
              type: 'text/plain',
            });
            if (beforeUpload?.(file) !== false) {
              customRequest?.({ file, onSuccess: vi.fn(), onError: vi.fn() });
            }
          }}
        >
          模拟上传
        </button>
      </div>
    ),
    Popconfirm: ({ children }: any) => (
      <div data-testid="popconfirm">{children}</div>
    ),
  };
});

vi.mock('@ant-design/icons', () => ({
  FileOutlined: () => <span className="anticon" />,
  FolderOpenOutlined: () => <span />,
  FolderOutlined: () => <span />,
  UploadOutlined: () => <span />,
}));

// App.useApp 需要 message
vi.mock('antd/es/app', () => ({
  default: {
    useApp: () => ({ message: { success: vi.fn(), error: vi.fn() } }),
  },
}));

import FileThumbnailCell from './FileThumbnailCell';
import FilesPage from './index';

const allPermissions = {
  canCreateFile: true,
  canUpdateFile: true,
  canDeleteFile: true,
  canMoveFile: true,
  canGetFileDownloadInfo: true,
};

describe('FilesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAccess = { ...allPermissions };

    vi.mocked(fileManagement.getFileConfiguration).mockResolvedValue({
      maxByteSizeForEachFile: 50 * 1024 * 1024,
      maxByteSizeForEachUpload: 50 * 1024 * 1024,
      maxFileQuantityForEachUpload: 1,
      allowOnlyConfiguredFileExtensions: false,
      fileExtensionsConfiguration: {},
    } as API.PublicFileContainerConfiguration);

    vi.mocked(fileManagement.getFileList).mockResolvedValue({
      items: [
        {
          id: 'dir-1',
          fileName: '文档',
          fileType: 1,
          hasSubdirectories: false,
          subFilesQuantity: 0,
        },
      ],
      totalCount: 1,
    } as API.PagedResultDto1FileInfoDto);

    // 文件表格走「列表 + 缩略图 URL」端点：返回一条有缩略图的图片 + 一条非图片
    vi.mocked(imaging.getFileListWithThumbnails).mockResolvedValue({
      items: [
        { id: 'img-1', fileName: 'photo.png', fileType: 2, byteSize: 100 },
        { id: 'txt-1', fileName: 'note.txt', fileType: 2, byteSize: 10 },
      ],
      totalCount: 2,
      thumbnailUrls: { 'img-1': '/api/app/file-thumbnail/img-1' },
    });
  });

  it('渲染目录树与文件表格', async () => {
    render(<FilesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('dir-tree')).toBeInTheDocument();
      expect(screen.getByTestId('pro-table')).toBeInTheDocument();
    });

    // 目录树加载了根目录
    await waitFor(() => {
      expect(screen.getByTestId('tree-node-dir-1')).toHaveTextContent('文档');
    });

    // 表格列包含预览/名称/大小（文案走 intl，mock 回显 key）
    expect(screen.getByTestId('column-thumbnailUrl')).toHaveTextContent(
      'pages.files.columns.preview',
    );
    expect(screen.getByTestId('column-fileName')).toHaveTextContent(
      'pages.files.columns.name',
    );
    expect(screen.getByTestId('column-byteSize')).toHaveTextContent(
      'pages.files.columns.size',
    );

    // 文件表格的数据请求走「列表 + 缩略图 URL」端点
    await waitFor(() => {
      expect(imaging.getFileListWithThumbnails).toHaveBeenCalled();
    });
  });

  it('上传调用 uploadFile 并携带当前目录 parentId（multipart 由生成函数组装）', async () => {
    vi.mocked(fileManagement.uploadFile).mockResolvedValue(
      {} as API.FileInfoDto,
    );

    render(<FilesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('upload-trigger')).toBeInTheDocument();
    });

    screen.getByTestId('upload-trigger').click();

    await waitFor(() => {
      expect(fileManagement.uploadFile).toHaveBeenCalledTimes(1);
    });

    const [file, parentId] = vi.mocked(fileManagement.uploadFile).mock.calls[0];
    expect(file).toBeInstanceOf(File);
    expect((file as File).name).toBe('hello.txt');
    // 根目录时 parentId 为 undefined
    expect(parentId).toBeUndefined();
  });

  it('超出大小限制时 beforeUpload 拦截，不调用 uploadFile', async () => {
    vi.mocked(fileManagement.getFileConfiguration).mockResolvedValue({
      maxByteSizeForEachFile: 1, // 1 字节，必然超限
      allowOnlyConfiguredFileExtensions: false,
      fileExtensionsConfiguration: {},
    } as API.PublicFileContainerConfiguration);

    render(<FilesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('upload-trigger')).toBeInTheDocument();
    });
    // 等 config 加载完成
    await waitFor(() => {
      expect(fileManagement.getFileConfiguration).toHaveBeenCalled();
    });

    screen.getByTestId('upload-trigger').click();

    // 给事件循环一个 tick，确认 uploadFile 没有被调用
    await new Promise((r) => setTimeout(r, 50));
    expect(fileManagement.uploadFile).not.toHaveBeenCalled();
  });

  it('无 Create 权限时不渲染上传与新建目录按钮', async () => {
    mockAccess = { ...allPermissions, canCreateFile: false };

    render(<FilesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('pro-table')).toBeInTheDocument();
    });

    expect(screen.queryByTestId('upload')).not.toBeInTheDocument();
    expect(
      screen.queryByText('pages.files.toolbar.newDirectory'),
    ).not.toBeInTheDocument();
  });

  it('无 Delete 权限时不渲染删除按钮', async () => {
    mockAccess = { ...allPermissions, canDeleteFile: false };

    // 让表格 request 返回一条文件记录，操作列才会渲染删除
    vi.mocked(imaging.getFileListWithThumbnails).mockResolvedValue({
      items: [
        {
          id: 'file-1',
          fileName: 'a.txt',
          fileType: 2,
          byteSize: 3,
        },
      ],
      totalCount: 1,
      thumbnailUrls: {},
    });

    render(<FilesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('pro-table')).toBeInTheDocument();
    });

    expect(
      screen.queryByText('pages.files.actions.delete'),
    ).not.toBeInTheDocument();
  });
});

describe('FileThumbnailCell', () => {
  it('有缩略图 URL 时渲染 40×40 图片', () => {
    render(
      <FileThumbnailCell
        thumbnailUrl="/api/app/file-thumbnail/img-1"
        fileName="photo.png"
      />,
    );

    const img = screen.getByRole('img', { name: 'photo.png' });
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute('src', '/api/app/file-thumbnail/img-1');
  });

  it('无缩略图 URL 时渲染通用文件图标', () => {
    const { container } = render(<FileThumbnailCell fileName="note.txt" />);

    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    // FileOutlined 渲染为带 anticon 类名的 span
    expect(container.querySelector('.anticon')).not.toBeNull();
  });
});
