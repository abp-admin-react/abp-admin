import { history } from '@umijs/max';
import { Alert, App, Button, Empty, Modal, Space, Spin, Tag } from 'antd';
import { createStyles } from 'antd-style';
import React, {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import type { IdentityRoleDto } from '@/abp/identity';
import {
  getRolePermissions,
  type PermissionGroup,
  type PermissionRequestContext,
  updateRolePermissions,
} from '@/abp/permissions';
import { getTenantId } from '@/abp/tenant';
import {
  isNotFound,
  RequestSessionContextChangedError,
} from '@/requestErrorConfig';
import RolePermissionEditorBody from './RolePermissionEditorBody';

type Props = {
  role: IdentityRoleDto;
  userId: string;
  tenantId?: string;
  tenantName?: string;
  onClose: () => void;
  onSaved: () => void;
  onStaleRole?: () => void;
};

type Phase =
  | 'loading'
  | 'loadFailed'
  | 'ready'
  | 'saving'
  | 'submitted'
  | 'unknown'
  | 'stale';
type GrantChange = { name: string; isGranted: boolean };

// 弹层固定 85dvh：勾选、展开、换组只在内部滚动。antd 6 语义槽是 container，不是 content。
const useStyles = createStyles(({ css }) => ({
  container: css`
    display: flex;
    flex-direction: column;
    block-size: 85dvh;
    max-block-size: 85dvh;
    overflow: hidden;
  `,
  body: css`
    display: flex;
    flex-direction: column;
    flex: 1 1 auto;
    min-block-size: 0;
    overflow: hidden;
  `,
  shell: css`
    display: flex;
    flex-direction: column;
    flex: 1 1 auto;
    gap: 12px;
    min-block-size: 0;
    overflow: hidden;
    block-size: 100%;
  `,
}));

function indexPermissions(groups: PermissionGroup[]) {
  return new Map(
    groups.flatMap((group) =>
      group.permissions.map((item) => [item.name, item] as const),
    ),
  );
}

function rebase(
  groups: PermissionGroup[],
  desired: ReadonlyMap<string, boolean>,
) {
  const permissions = indexPermissions(groups);
  const pending = new Map<string, boolean>();
  const unavailable: string[] = [];
  for (const [name, grant] of desired) {
    const item = permissions.get(name);
    if (item?.isGranted === grant) continue;
    if (!item || item.isEditable === false) {
      unavailable.push(
        `${grant ? '授予' : '撤销'} ${item?.displayName ?? name}（${name}）`,
      );
    } else {
      pending.set(name, grant);
    }
  }
  return { pending, unavailable };
}

function responseOf(error: unknown) {
  return (
    error as {
      response?: { status?: number; data?: { error?: { code?: string } } };
    } | null
  )?.response;
}

// 角色、租户或账号变化后由列表重新挂载，草稿只属于打开时的目标。
const RolePermissionEditor: React.FC<Props> = ({
  role,
  userId,
  tenantId,
  tenantName,
  onClose,
  onSaved,
  onStaleRole,
}) => {
  const { styles } = useStyles();
  const { modal, message } = App.useApp();
  const [groups, setGroups] = useState<PermissionGroup[]>([]);
  const [changes, setChanges] = useState(new Map<string, boolean>());
  const [phase, setPhase] = useState<Phase>('loading');
  const [notice, setNotice] = useState('');
  const [loadAttempt, setLoadAttempt] = useState(0);
  const alive = useRef(false);
  const [requestContext] = useState<PermissionRequestContext>(() => ({
    userId,
    tenantId: getTenantId(),
  }));
  const permissions = useMemo(() => indexPermissions(groups), [groups]);
  const busy = phase === 'saving';
  const awaitingVerification = phase === 'submitted' || phase === 'unknown';

  useLayoutEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  function sameTenant() {
    const id = getTenantId();
    return id === requestContext.tenantId && (!id || id === tenantId);
  }

  useEffect(() => {
    let cancelled = false;
    setPhase('loading');
    readCurrentGroups()
      .then((result) => {
        if (cancelled || !result) return;
        setGroups(result);
        setPhase('ready');
      })
      .catch((error) => {
        if (!cancelled && !handleTargetError(error)) setPhase('loadFailed');
      });
    return () => {
      cancelled = true;
    };
  }, [role.id, role.name, loadAttempt, tenantId, requestContext]);

  useEffect(() => {
    if (!changes.size) return;
    const unblock = history?.block?.((transition) => {
      if (
        transition.location.pathname !== '/user/login' &&
        (busy ||
          !window.confirm(
            awaitingVerification
              ? '结束本次核对并离开？'
              : '放弃当前角色未保存的权限变更并离开？',
          ))
      )
        return;
      unblock?.();
      onClose();
      transition.retry();
    });
    return () => unblock?.();
  }, [changes.size, busy, awaitingVerification, onClose]);

  function edit(updates: GrantChange[]) {
    if (phase !== 'ready') return;
    setChanges((previous) => {
      const next = new Map(previous);
      for (const { name, isGranted } of updates) {
        const original = permissions.get(name);
        if (!original || original.isEditable === false) continue;
        if (original.isGranted === isGranted) next.delete(name);
        else next.set(name, isGranted);
      }
      return next;
    });
  }

  function stale(text: string) {
    setPhase('stale');
    setNotice(text);
    onStaleRole?.();
  }

  function checkContext() {
    if (!alive.current) return false;
    if (sameTenant()) return true;
    stale('租户已切换，请关闭并重新打开角色。');
    return false;
  }

  function handleTargetError(error: unknown, prefix = '') {
    if (!checkContext()) return true;
    const response = responseOf(error);
    const code = response?.data?.error?.code;
    let reason: string;
    if (error instanceof RequestSessionContextChangedError) {
      reason = error.message;
    } else if (isNotFound(error)) {
      reason = '角色已删除或不可见，请关闭并重新选择角色。';
    } else if (
      response?.status === 409 &&
      code === 'AbpAdmin:RolePermissionTargetChanged'
    ) {
      reason = '角色名称已变更，请关闭并重新选择角色。';
    } else if (
      response?.status === 409 &&
      code === 'AbpAdmin:RolePermissionNameAmbiguous'
    ) {
      reason =
        '当前租户存在同名角色，无法确认权限归属。请联系管理员核查角色记录。';
    } else {
      return false;
    }
    stale(`${prefix}${reason}`);
    return true;
  }

  async function readCurrentGroups() {
    if (!checkContext()) return;
    const result = await getRolePermissions(role, requestContext);
    if (!checkContext()) return;
    return result.groups.filter((group) => group.permissions.length);
  }

  function finish() {
    setChanges(new Map());
    message.success('角色权限已核对');
    onClose();
  }

  async function verify(
    desired: ReadonlyMap<string, boolean>,
    acknowledged: boolean,
  ) {
    // 成功回执已触发会话刷新；响应丢失时，只在重新读到服务端状态后刷新，核对本身不重发 PUT。
    setPhase('saving');
    try {
      const fresh = await readCurrentGroups();
      if (!fresh) return;
      const { pending, unavailable } = rebase(fresh, desired);
      setGroups(fresh);
      setChanges(pending);
      if (!acknowledged) onSaved();
      if (!pending.size && !unavailable.length) {
        finish();
        return;
      }
      setPhase('ready');
      const unavailableText = unavailable.length
        ? ` 当前不可编辑或已移除：${unavailable.join('、')}。`
        : '';
      setNotice(
        `核对发现 ${pending.size + unavailable.length} 项未生效。${pending.size ? '可编辑的改动已保留，请复核后再保存。' : ''}${unavailableText}`,
      );
    } catch (error) {
      if (
        handleTargetError(
          error,
          acknowledged ? '已提交，无法核对。' : '提交结果待核实。',
        )
      )
        return;
      setPhase(acknowledged ? 'submitted' : 'unknown');
      setNotice(
        acknowledged
          ? '已提交，无法核对角色权限。恢复读取后可重新核对。'
          : '提交结果待核实。请先重新核对，再决定是否保存。',
      );
    }
  }

  async function save() {
    if (phase !== 'ready' || !changes.size) return;
    setPhase('saving');
    setNotice('');
    let desired: Map<string, boolean>;
    try {
      const fresh = await readCurrentGroups();
      if (!fresh) return;
      const { pending, unavailable } = rebase(fresh, changes);
      desired = pending;
      setGroups(fresh);
      setChanges(pending);
      if (unavailable.length) {
        setPhase('ready');
        setNotice(
          `权限范围已变化，本次尚未提交。以下改动已无法操作：${unavailable.join('、')}。请复核剩余改动。`,
        );
        return;
      }
      if (!desired.size) {
        onSaved();
        finish();
        return;
      }
    } catch (error) {
      if (!handleTargetError(error)) {
        setPhase('ready');
        setNotice('未能读取角色的最新权限，本次尚未提交。');
      }
      return;
    }
    try {
      await updateRolePermissions(
        role,
        Array.from(desired, ([name, isGranted]) => ({ name, isGranted })),
        requestContext,
      );
    } catch (error) {
      if (handleTargetError(error)) return;
      const status = responseOf(error)?.status;
      if (status && status >= 400 && status < 500 && status !== 408) {
        setPhase('ready');
        setNotice('服务端拒绝了本次提交，草稿已保留。请核对角色与权限后重试。');
      } else {
        setPhase('unknown');
        setNotice('提交结果待核实。请先重新核对，再决定是否保存。');
      }
      return;
    }
    if (!checkContext()) return;
    onSaved();
    await verify(desired, true);
  }

  function close() {
    if (busy) return;
    if (!changes.size || phase === 'stale') {
      onClose();
      return;
    }
    modal.confirm({
      title: awaitingVerification ? '结束本次核对？' : '放弃未保存的变更？',
      okText: awaitingVerification ? '结束核对' : '放弃变更',
      cancelText: awaitingVerification ? '继续核对' : '继续编辑',
      focusable: { autoFocusButton: 'cancel' },
      onOk: onClose,
    });
  }

  return (
    <Modal
      open
      centered
      width="min(1120px, 100vw)"
      style={{ maxWidth: '100vw', marginInline: 'auto' }}
      classNames={{ container: styles.container, body: styles.body }}
      title={
        <Space wrap>
          <span>功能权限 · {role.name}</span>
          <Tag>{tenantName || tenantId || '宿主'}</Tag>
          {role.isDefault && <Tag>默认角色</Tag>}
        </Space>
      }
      onCancel={close}
      closable={!busy}
      mask={{ closable: !busy }}
      keyboard={!busy}
      footer={
        <Space>
          <Button onClick={close} disabled={busy}>
            关闭
          </Button>
          {awaitingVerification ? (
            <Button
              type="primary"
              onClick={() => void verify(changes, phase === 'submitted')}
            >
              重新核对
            </Button>
          ) : (
            <Button
              type="primary"
              onClick={save}
              loading={busy}
              disabled={phase !== 'ready' || !changes.size}
            >
              保存变更
            </Button>
          )}
        </Space>
      }
    >
      <div className={styles.shell}>
        {notice && <Alert type="warning" showIcon title={notice} />}
        {phase === 'loading' ? (
          <Spin description="加载角色权限">
            <div style={{ minBlockSize: 200 }} />
          </Spin>
        ) : phase === 'loadFailed' ? (
          <Alert
            type="error"
            title="加载角色权限失败"
            showIcon
            action={
              <Button onClick={() => setLoadAttempt((attempt) => attempt + 1)}>
                重新加载
              </Button>
            }
          />
        ) : groups.length ? (
          <RolePermissionEditorBody
            groups={groups}
            changes={changes}
            disabled={phase !== 'ready'}
            onChange={edit}
          />
        ) : (
          phase !== 'stale' && <Empty description="没有可配置的权限" />
        )}
      </div>
    </Modal>
  );
};

export default RolePermissionEditor;
