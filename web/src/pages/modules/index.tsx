import { PageContainer } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useAccess, useIntl, useModel } from '@umijs/max';
import { Alert, App, Button, Modal, Space, Table, Tag } from 'antd';
import { useRef, useState } from 'react';
import {
  getModuleManagement,
  type ManagedModule,
  type ModuleChange,
  type ModuleChangePreview,
  type ModuleId,
  previewModuleChange,
  updateModuleManagement,
} from '@/abp/moduleManagement';
import english from '@/locales/en-US/modules';
import { isSafeMenuPath } from '@/utils/redirect';

const managementKey = ['module-management'] as const;

const builtInIds: readonly ModuleId[] = [
  'Orders',
  'Collection',
  'Chain',
  'Finance',
];

type PreviewDialog = {
  module: ManagedModule;
  change: ModuleChange;
  preview: ModuleChangePreview;
};

export default function ModulesPage() {
  const access = useAccess();
  const intl = useIntl();
  const { setInitialState } = useModel('@@initialState');
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const moduleQuery = useQuery({
    queryKey: managementKey,
    queryFn: getModuleManagement,
    enabled: access.canViewModuleManagement,
  });
  const [dialog, setDialog] = useState<PreviewDialog>();
  const [pending, setPending] = useState(false);
  const inFlight = useRef(false);
  const state = moduleQuery.data;
  const modules = state?.modules ?? [];
  const t = (id: keyof typeof english, values?: Record<string, string>) =>
    intl.formatMessage({ id, defaultMessage: english[id] }, values);
  const moduleName = (id: string) => {
    const builtInId = builtInIds.find((knownId) => knownId === id);
    return builtInId
      ? t(`pages.modules.name.${builtInId}`)
      : (modules.find((module) => module.id === id)?.displayName ?? id);
  };
  const moduleList = (ids: string[]) =>
    new Intl.ListFormat(intl.locale, { type: 'unit' }).format(
      ids.map(moduleName),
    );
  const actionLabel = (id: string, enabled: boolean) =>
    t(enabled ? 'pages.modules.enableTitle' : 'pages.modules.disableTitle', {
      module: moduleName(id),
    });
  const suspendedModule = builtInIds.find(
    (id) => id === dialog?.module.id && !dialog?.change.isEnabled,
  );
  const pageTitle = t('pages.modules.title');

  const canConfigure = (id: string) => {
    switch (id) {
      case 'Orders':
      case 'Collection':
      case 'Finance':
        return access.canManagePaymentSettings;
      case 'Chain':
        return access.canManageWeb3Configuration;
      default:
        return false;
    }
  };
  const canViewHistory = (id: string) => {
    switch (id) {
      case 'Orders':
        return access.canViewOrders;
      case 'Collection':
        return access.canViewCollectionPayments;
      case 'Chain':
        return access.canViewWeb3Operations;
      case 'Finance':
        return access.canOpenFinanceVouchersWorkspace;
      default:
        return false;
    }
  };
  const configurationLink = (module: ManagedModule) => {
    const path = module.configurationPath;
    if (!path || !isSafeMenuPath(path) || !canConfigure(module.id))
      return undefined;
    if (
      module.id === 'Chain' &&
      (path === '/web3/operations?settings=deposit' || path === '/web3/operations?settings=network') &&
      !access.canConfigureCollectionNetwork &&
      access.canAdminWithdrawals &&
      access.canManageCollectionWallets
    ) {
      return '/collection/withdrawals?settings=withdraw';
    }
    return path;
  };
  const historyLink = (module: ManagedModule) => {
    if (!module.historyPath || !isSafeMenuPath(module.historyPath) || !canViewHistory(module.id))
      return undefined;
    if (module.id === 'Chain' && module.historyPath === '/web3/operations') {
      if (access.canViewDepositRecords) return '/operations/payments?view=deposits';
      if (access.canAdminWithdrawals) return '/collection/withdrawals';
    }
    return module.historyPath;
  };

  async function showPreview(module: ManagedModule) {
    if (
      !state?.initialized ||
      !access.canUpdateModuleManagement ||
      inFlight.current
    ) {
      return;
    }
    const change = {
      moduleId: module.id,
      isEnabled: !module.isEnabled,
      expectedVersion: state.version,
    };
    inFlight.current = true;
    setPending(true);
    try {
      const preview = await previewModuleChange(change);
      setDialog({ module, change, preview });
    } catch {
      message.error(t('pages.modules.previewError'));
      await moduleQuery.refetch();
    } finally {
      inFlight.current = false;
      setPending(false);
    }
  }

  async function applyChange() {
    if (
      !dialog?.preview.canApply ||
      !access.canUpdateModuleManagement ||
      inFlight.current
    ) {
      return;
    }
    inFlight.current = true;
    setPending(true);
    try {
      const updated = await updateModuleManagement(dialog.change);
      queryClient.setQueryData(managementKey, updated);
      setInitialState((current) => ({
        ...current,
        moduleAvailability: {
          version: updated.version,
          initialized: updated.initialized,
          states: Object.fromEntries(
            updated.modules.map(({ id, isEnabled }) => [id, isEnabled]),
          ),
        },
      }));
      setDialog(undefined);
      void queryClient.invalidateQueries({ queryKey: ['workbench'] });
      message.success(t('pages.modules.updateSuccess'));
    } catch {
      setDialog(undefined);
      message.error(t('pages.modules.updateError'));
      await moduleQuery.refetch();
    } finally {
      inFlight.current = false;
      setPending(false);
    }
  }

  return (
    <PageContainer
      title={pageTitle}
      breadcrumb={{
        items: [{ title: t('pages.modules.platform') }, { title: pageTitle }],
      }}
    >
      {moduleQuery.isError && (
        <Alert
          type="error"
          showIcon
          title={t('pages.modules.fetchError')}
          action={
            <Button onClick={() => void moduleQuery.refetch()}>
              {t('pages.modules.retry')}
            </Button>
          }
          style={{ marginBottom: 12 }}
        />
      )}
      {state && !state.initialized && (
        <Alert
          type="warning"
          showIcon
          title={t('pages.modules.uninitialized')}
          style={{ marginBottom: 12 }}
        />
      )}
      <Table<ManagedModule>
        rowKey="id"
        loading={moduleQuery.isLoading}
        dataSource={modules}
        pagination={false}
        scroll={{ x: 570 }}
        columns={[
          {
            title: t('pages.modules.column.module'),
            render: (_, module) => moduleName(module.id),
          },
          {
            title: t('pages.modules.column.status'),
            render: (_, module) =>
              !state?.initialized ? (
                <Tag>{t('pages.modules.status.pending')}</Tag>
              ) : module.isEnabled ? (
                <Tag color="success">{t('pages.modules.status.enabled')}</Tag>
              ) : (
                <Tag>{t('pages.modules.status.disabled')}</Tag>
              ),
          },
          {
            title: t('pages.modules.column.dependencies'),
            render: (_, module) => moduleList(module.dependencies) || '—',
          },
          {
            title: t('pages.modules.column.actions'),
            render: (_, module) => {
              const configurationPath = configurationLink(module);
              const historyPath = historyLink(module);
              return (
                <Space wrap>
                  {configurationPath && (
                    <Link
                      to={configurationPath}
                      aria-label={t('pages.modules.configureLabel', {
                        module: moduleName(module.id),
                      })}
                    >
                      {t('pages.modules.configure')}
                    </Link>
                  )}
                  {historyPath && (
                      <Link
                        to={historyPath}
                        aria-label={t('pages.modules.historyLabel', {
                          module: moduleName(module.id),
                        })}
                      >
                        {t('pages.modules.history')}
                      </Link>
                    )}
                  {access.canUpdateModuleManagement && (
                    <Button
                      disabled={!state?.initialized || pending}
                      danger={module.isEnabled}
                      aria-label={actionLabel(module.id, !module.isEnabled)}
                      onClick={() => void showPreview(module)}
                    >
                      {t(
                        module.isEnabled
                          ? 'pages.modules.disable'
                          : 'pages.modules.enable',
                      )}
                    </Button>
                  )}
                </Space>
              );
            },
          },
        ]}
      />
      <Modal
        open={!!dialog}
        title={dialog && actionLabel(dialog.module.id, dialog.change.isEnabled)}
        onCancel={() => {
          if (!inFlight.current) setDialog(undefined);
        }}
        footer={
          dialog?.preview.canApply ? undefined : (
            <Button onClick={() => setDialog(undefined)}>
              {t('pages.modules.close')}
            </Button>
          )
        }
        confirmLoading={pending}
        onOk={() => void applyChange()}
        okText={t('pages.modules.confirm')}
        okButtonProps={{ danger: dialog?.change.isEnabled === false }}
        cancelText={t('pages.modules.cancel')}
        mask={{ closable: false }}
      >
        {dialog &&
          (dialog.preview.canApply ? (
            <>
              {suspendedModule && (
                <p>{t(`pages.modules.suspension.${suspendedModule}`)}</p>
              )}
              <ul>
                {dialog.preview.changes.map((change) => (
                  <li key={change.moduleId}>
                    {moduleName(change.moduleId)} ·{' '}
                    {t(
                      change.isEnabled
                        ? 'pages.modules.enable'
                        : 'pages.modules.disable',
                    )}
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <Alert
              type="warning"
              showIcon
              title={t('pages.modules.blocked')}
              description={t('pages.modules.blockedBy', {
                modules: moduleList(dialog.preview.blockingModuleIds),
              })}
            />
          ))}
      </Modal>
    </PageContainer>
  );
}
