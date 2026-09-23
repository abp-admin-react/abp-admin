// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 语言文本列表：覆盖行 + 静态基线合并视图（对标 Pro 的静态文本外部存储），
把从未覆盖过的静态 key 一并纳入，翻译人员能发现漏译。
资源可选：不传 = 跨全部注册资源列出（Pro 同款，表格以 ResourceName 列区分来源）。
两个口径：Value=生效值（本层覆盖 > host 覆盖 > 静态基线；静态基线沿目标文化父链，
刻意不含框架 DefaultCulture 回退——链上缺失的 key 显示空串并被 OnlyEmpty 计为未翻译）；
IsOverridden=当前上下文是否有覆盖行（不含 host 的行）——「恢复默认」只删当前层，
前端以它决定按钮是否可点。OnlyEmpty 按生效值过滤，即真正的"未翻译过滤"。 GET /api/app/language-text */
export async function getApiAppLanguageText(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppLanguageTextParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1LanguageTextDto>("/api/app/language-text", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/language-text */
export async function putApiAppLanguageText(
  body: API.UpdateLanguageTextDto,
  options?: { [key: string]: any }
) {
  return request<API.LanguageTextDto>("/api/app/language-text", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/language-text/resource-names */
export async function getApiAppLanguageTextResourceNames(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1String>(
    "/api/app/language-text/resource-names",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/language-text/restore-to-default */
export async function postApiAppLanguageTextRestoreToDefault(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppLanguageTextRestoreToDefaultParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/language-text/restore-to-default", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
