import { PageContainer, ProCard } from '@ant-design/pro-components';
import { Button } from 'antd';
import React, { useState } from 'react';
import FeatureModal from '@/components/FeatureModal';

const FeaturesPage: React.FC = () => {
  const [open, setOpen] = useState(true);

  return (
    <PageContainer title="平台功能开关">
      <ProCard>
        <Button type="primary" onClick={() => setOpen(true)}>
          编辑功能开关
        </Button>
      </ProCard>
      <FeatureModal
        open={open}
        title="平台功能开关"
        providerName="T"
        onClose={() => setOpen(false)}
      />
    </PageContainer>
  );
};

export default FeaturesPage;
