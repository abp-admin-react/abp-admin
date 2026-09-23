// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/data-dictionary-view/${param0} */
export async function getApiAppDataDictionaryViewCode(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppDataDictionaryViewCodeParams,
  options?: { [key: string]: any }
) {
  const { code: param0, ...queryParams } = params;
  return request<API.DataDictionaryViewDto>(
    `/api/app/data-dictionary-view/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/data-dictionary-view/${param0}/items */
export async function putApiAppDataDictionaryViewDictionaryCodeItems(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppDataDictionaryViewDictionaryCodeItemsParams,
  body: API.SaveDataDictionaryItemsInput,
  options?: { [key: string]: any }
) {
  const { dictionaryCode: param0, ...queryParams } = params;
  return request<API.DataDictionaryViewDto>(
    `/api/app/data-dictionary-view/${param0}/items`,
    {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/data-dictionary-view/${param0}/items-meta */
export async function putApiAppDataDictionaryViewDictionaryCodeItemsMeta(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppDataDictionaryViewDictionaryCodeItemsMetaParams,
  body: API.BatchUpdateDataDictionaryItemsMetaInput,
  options?: { [key: string]: any }
) {
  const { dictionaryCode: param0, ...queryParams } = params;
  return request<any>(`/api/app/data-dictionary-view/${param0}/items-meta`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/data-dictionary-view/${param0}/items/${param1}/meta */
export async function putApiAppDataDictionaryViewDictionaryCodeItemsItemCodeMeta(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppDataDictionaryViewDictionaryCodeItemsItemCodeMetaParams,
  body: API.UpdateDataDictionaryItemMetaInput,
  options?: { [key: string]: any }
) {
  const { dictionaryCode: param0, itemCode: param1, ...queryParams } = params;
  return request<any>(
    `/api/app/data-dictionary-view/${param0}/items/${param1}/meta`,
    {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}
