import { request } from '@umijs/max';
import { Button, Space, Typography } from 'antd';
import React, { useEffect, useState } from 'react';

type CookieConsentConfig = {
  isEnabled?: boolean;
  cookiePolicyUrl?: string;
  privacyPolicyUrl?: string;
  expirationDays?: number;
};

/**
 * 接受状态 cookie 名。与后端唯一事实源
 * CookieConsentController.ConsentCookieName（.AbpAdmin.CookieConsent）保持一致，
 * 改名须两端同步，否则横幅会重新弹出。
 */
const CONSENT_COOKIE_NAME = '.AbpAdmin.CookieConsent';

/** 兜底有效期（天）。正常应取后端下发的 expirationDays，此值对齐 AbpAdminCookieConsentOptions.Expiration 默认 180 天。 */
const FALLBACK_EXPIRATION_DAYS = 180;

/**
 * Cookie Consent 横幅。
 *
 * 配置来源：后端 IApplicationConfigurationContributor 通过
 * /api/abp/application-configuration 下发的 extraProperties.cookieConsent 节点
 * （isEnabled / cookiePolicyUrl / privacyPolicyUrl / expirationDays）。
 *
 * 注意：本组件挂在 rootContainer（在 umi Provider 树之外），
 * 不能使用 useModel('@@initialState')，需自行拉取配置。
 *
 * 免责说明：本组件仅提供"接受 Cookie"的提示与记录能力，
 * 不构成法律合规意见。是否满足 GDPR/ePrivacy 要求取决于
 * 实际部署方对 Cookie 的使用方式与所在法域，请自行评估。
 */
const CookieConsent: React.FC = () => {
  const [config, setConfig] = useState<CookieConsentConfig>();
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    // 已接受过（cookie 仍在有效期内）则不再拉取配置
    if (document.cookie.includes(`${CONSENT_COOKIE_NAME}=`)) {
      return;
    }
    request<{ extraProperties?: { cookieConsent?: CookieConsentConfig } }>(
      '/api/abp/application-configuration',
      {
        method: 'GET',
        params: { includeLocalizationResources: false },
        skipErrorHandler: true,
      },
    )
      .then((res) => setConfig(res?.extraProperties?.cookieConsent))
      .catch(() => setConfig(undefined));
  }, []);

  if (!config?.isEnabled || dismissed) {
    return null;
  }

  const handleAccept = async () => {
    try {
      // 后端写入 .AbpAdmin.CookieConsent cookie（HttpOnly=false, SameSite=Lax）
      await request('/api/app/cookie-consent/accept', { method: 'POST' });
    } catch {
      // 即使接口失败也先隐藏横幅，cookie 由前端兜底写入
      const days = config.expirationDays ?? FALLBACK_EXPIRATION_DAYS;
      const expires = new Date(Date.now() + days * 86400000).toUTCString();
      document.cookie = `${CONSENT_COOKIE_NAME}=accepted; expires=${expires}; path=/; samesite=lax`;
    }
    setDismissed(true);
  };

  return (
    <div
      style={{
        position: 'fixed',
        bottom: 0,
        left: 0,
        right: 0,
        zIndex: 1000,
        background: '#fff',
        borderTop: '1px solid #f0f0f0',
        boxShadow: '0 -2px 8px rgba(0,0,0,0.08)',
        padding: '12px 24px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        flexWrap: 'wrap',
        gap: 12,
      }}
    >
      <Typography.Text>
        本站使用 Cookie 以提升浏览体验。
        {config.cookiePolicyUrl && (
          <Typography.Link
            href={config.cookiePolicyUrl}
            target="_blank"
            style={{ marginLeft: 8 }}
          >
            Cookie 政策
          </Typography.Link>
        )}
        {config.privacyPolicyUrl && (
          <Typography.Link
            href={config.privacyPolicyUrl}
            target="_blank"
            style={{ marginLeft: 8 }}
          >
            隐私政策
          </Typography.Link>
        )}
      </Typography.Text>
      <Space>
        <Button type="primary" onClick={handleAccept}>
          接受
        </Button>
      </Space>
    </div>
  );
};

export default CookieConsent;
