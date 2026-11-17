export default {
  'pages.modules.title': 'Module management',
  'pages.modules.platform': 'Platform administration',
  'pages.modules.name.Orders': 'Orders',
  'pages.modules.name.Collection': 'Payments',
  'pages.modules.name.Chain': 'On-chain deposits and withdrawals',
  'pages.modules.name.Finance': 'Finance workspace',
  'pages.modules.column.module': 'Module',
  'pages.modules.column.status': 'Status',
  'pages.modules.column.dependencies': 'Dependencies',
  'pages.modules.column.actions': 'Actions',
  'pages.modules.status.pending': 'Awaiting initialization',
  'pages.modules.status.enabled': 'Enabled',
  'pages.modules.status.disabled': 'Not accepting new work',
  'pages.modules.configure': 'Configure',
  'pages.modules.configureLabel': 'Configure {module}',
  'pages.modules.history': 'History',
  'pages.modules.historyLabel': 'View history for {module}',
  'pages.modules.enable': 'Enable',
  'pages.modules.disable': 'Disable',
  'pages.modules.enableTitle': 'Enable {module}',
  'pages.modules.disableTitle': 'Disable {module}',
  'pages.modules.confirm': 'Confirm',
  'pages.modules.cancel': 'Cancel',
  'pages.modules.close': 'Close',
  'pages.modules.retry': 'Retry',
  'pages.modules.fetchError': 'Could not load module status',
  'pages.modules.uninitialized': 'Deployment modules are not initialized',
  'pages.modules.previewError':
    'Could not preview the change. Module status has been reloaded.',
  'pages.modules.updateSuccess': 'Module status updated',
  'pages.modules.updateError':
    'Could not update module status. Check the latest status and try again.',
  'pages.modules.blocked': 'Cannot disable this module',
  'pages.modules.blockedBy':
    'First disable the modules that still depend on it: {modules}',
  'pages.modules.suspension.Orders':
    'New orders, quotes, and payment starts are paused. Payments and orders already in progress continue.',
  'pages.modules.suspension.Collection':
    'New payments and first-time balance debits are paused. Existing payments can still be checked, confirmed by callbacks, or refunded.',
  'pages.modules.suspension.Chain':
    'New deposit addresses and withdrawal requests are paused. Deposits to existing addresses, existing withdrawals, and sweeps continue.',
  'pages.modules.suspension.Finance':
    'Creating or submitting new manual ledger adjustments is paused. Previously submitted work and integrated posting continue under existing rules.',
};
