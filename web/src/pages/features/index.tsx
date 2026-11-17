import { PageContainer, ProCard } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import { Button } from 'antd';
import React, { useState } from 'react';
import FeatureModal from '@/components/FeatureModal';

const FeaturesPage: React.FC = () => {
  const [open, setOpen] = useState(true);
  const intl = useIntl();
  const title = intl.formatMessage({
    id: 'menu.navigation.features',
    defaultMessage: '宿主功能配置',
  });

  return (
    <PageContainer title={title}>
      <ProCard>
        <Button type="primary" onClick={() => setOpen(true)}>
          编辑功能配置
        </Button>
      </ProCard>
      <FeatureModal
        open={open}
        title={title}
        providerName="T"
        onClose={() => setOpen(false)}
      />
    </PageContainer>
  );
};

export default FeaturesPage;
