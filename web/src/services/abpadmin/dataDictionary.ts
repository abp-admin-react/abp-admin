// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/data-dictionary/data-dictionary */
export async function getApiDataDictionaryDataDictionary(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiDataDictionaryDataDictionaryParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1DataDictionaryDto>(
    "/api/data-dictionary/data-dictionary",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/data-dictionary/data-dictionary */
export async function postApiDataDictionaryDataDictionary(
  body: API.DataDictionaryCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.DataDictionaryDto>(
    "/api/data-dictionary/data-dictionary",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/data-dictionary/data-dictionary/${param0} */
export async function getApiDataDictionaryDataDictionaryId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiDataDictionaryDataDictionaryIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.DataDictionaryDto>(
    `/api/data-dictionary/data-dictionary/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/data-dictionary/data-dictionary/${param0} */
export async function putApiDataDictionaryDataDictionaryId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiDataDictionaryDataDictionaryIdParams,
  body: API.DataDictionaryUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.DataDictionaryDto>(
    `/api/data-dictionary/data-dictionary/${param0}`,
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

/** 此处后端没有提供注释 DELETE /api/data-dictionary/data-dictionary/${param0} */
export async function deleteApiDataDictionaryDataDictionaryId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiDataDictionaryDataDictionaryIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/data-dictionary/data-dictionary/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/data-dictionary/data-dictionary/by-code/${param0} */
export async function getApiDataDictionaryDataDictionaryByCodeCode(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiDataDictionaryDataDictionaryByCodeCodeParams,
  options?: { [key: string]: any }
) {
  const { code: param0, ...queryParams } = params;
  return request<API.DataDictionaryDto>(
    `/api/data-dictionary/data-dictionary/by-code/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
