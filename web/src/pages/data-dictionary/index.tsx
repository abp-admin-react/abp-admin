import { PageContainer, ProCard } from '@ant-design/pro-components';
import { Empty } from 'antd';
import { useState } from 'react';
import DictionaryItemEditor from './components/DictionaryItemEditor';
import DictionaryList from './components/DictionaryList';
import type { DataDictionaryListItem } from './service';

/**
 * 数据字典管理页（T3.4 第 7 步）。左右分栏：左侧字典列表，右侧选中字典的项编辑。
 * 字典项编辑走自建原子端点 saveDataDictionaryItems（非静态字典对 Items 是全量替换语义、
 * 静态字典有服务端结构锁），不分页、不做增量 diff。
 */
export default function DataDictionaryPage() {
  const [selected, setSelected] = useState<DataDictionaryListItem | undefined>();
  const [listVersion, setListVersion] = useState(0);

  return (
    <PageContainer>
      <ProCard split="vertical">
        <ProCard colSpan="380px" ghost>
          <DictionaryList
            selectedId={selected?.id}
            version={listVersion}
            onSelect={setSelected}
          />
        </ProCard>
        <ProCard ghost>
          {selected ? (
            <DictionaryItemEditor
              key={selected.id}
              dictionary={selected}
              onSaved={() => setListVersion((v) => v + 1)}
            />
          ) : (
            <Empty description="请先在左侧选择一个字典" />
          )}
        </ProCard>
      </ProCard>
    </PageContainer>
  );
}
