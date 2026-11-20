export const DEFAULT_LANDING_PATH = '/welcome';

/**
 * 服务端下发菜单的路径须为站内相对路径。先解析再比较 origin，覆盖浏览器对反斜杠
 * 和换行符等字符的归一化。
 */
export function isSafeMenuPath(path?: string | null): path is string {
  if (!path?.startsWith('/')) return false;
  try {
    const { origin } = window.location;
    return new URL(path, origin).origin === origin;
  } catch {
    return false;
  }
}
