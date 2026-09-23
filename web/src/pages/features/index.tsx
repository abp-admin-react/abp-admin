import { PageContainer, ProCard } from '@ant-design/pro-components';
import { Button } from 'antd';
import React, { useState } from 'react';
import FeatureModal from '@/components/FeatureModal';

const FeaturesPage: React.FC = () => {
  const [open, setOpen] = useState(true);

  return (
    <PageContainer>
      <ProCard>
        <Button type="primary" onClick={() => setOpen(true)}>
          配置 Host 功能
        </Button>
      </ProCard>
      <FeatureModal
        open={open}
        title="Host 功能"
        providerName="T"
        onClose={() => setOpen(false)}
      />
    </PageContainer>
  );
};

export default FeaturesPage;
