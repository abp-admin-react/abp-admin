import { CheckOutlined, GlobalOutlined } from '@ant-design/icons';
import { getAllLocales, getLocale, setLocale } from '@umijs/max';
import type { MenuProps } from 'antd';
import { Button } from 'antd';
import { useMemo } from 'react';
import HeaderDropdown from '../HeaderDropdown';
import useHeaderActionStyles from './style';
import { syncCultureCookie } from '@/abp/culture';

const localeLabelMap: Record<string, { emoji: string; label: string }> = {
  'zh-CN': { emoji: '🇨🇳', label: '简体中文' },
  'zh-TW': { emoji: '🇭🇰', label: '繁體中文' },
  'en-US': { emoji: '🇺🇸', label: 'English' },
  'ja-JP': { emoji: '🇯🇵', label: '日本語' },
  'pt-BR': { emoji: '🇧🇷', label: 'Português' },
  'id-ID': { emoji: '🇮🇩', label: 'Bahasa Indonesia' },
  'fa-IR': { emoji: '🇮🇷', label: 'فارسی' },
  'bn-BD': { emoji: '🇧🇩', label: 'বাংলা' },
};

const onLangClick: MenuProps['onClick'] = ({ key }) => {
  if (key.startsWith('lang-')) {
    const locale = key.replace('lang-', '');
    setLocale(locale, false);
    // 同步 ABP culture cookie：后端 Razor 页（登录页）与 API 的 cookie provider
    // 都读它，不写的话后端永远停在旧语言（cookie 优先级高于 Accept-Language 头）。
    syncCultureCookie(locale);
  }
};

export const LangDropdown: React.FC = () => {
  const { styles } = useHeaderActionStyles();
  const allLocales = useMemo(() => getAllLocales(), []);
  const currentLocale = getLocale();
  const supportLocales = allLocales.filter((l) => l in localeLabelMap);

  if (supportLocales.length <= 1) {
    return null;
  }

  const langItems: MenuProps['items'] = supportLocales.map((locale) => ({
    key: `lang-${locale}`,
    icon:
      locale === currentLocale ? (
        <CheckOutlined style={{ color: '#52c41a' }} />
      ) : (
        <span style={{ display: 'inline-block', width: 14 }} />
      ),
    label: `${localeLabelMap[locale]?.emoji ?? ''} ${localeLabelMap[locale]?.label ?? locale}`,
  }));

  return (
    <HeaderDropdown
      placement="bottomRight"
      arrow
      menu={{
        selectedKeys: [`lang-${currentLocale}`],
        onClick: onLangClick,
        items: langItems,
        style: { minWidth: 180 },
      }}
    >
      <Button type="text" className={styles.action} aria-label="语言切换">
        <GlobalOutlined />
      </Button>
    </HeaderDropdown>
  );
};
