// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 DELETE /api/app/profile-avatar */
export async function deleteApiAppProfileAvatar(options?: {
  [key: string]: any;
}) {
  return request<any>("/api/app/profile-avatar", {
    method: "DELETE",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/profile-avatar/${param0} */
export async function getApiAppProfileAvatarId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppProfileAvatarIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<string>(`/api/app/profile-avatar/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/profile-avatar/my-avatar-info */
export async function getApiAppProfileAvatarMyAvatarInfo(options?: {
  [key: string]: any;
}) {
  return request<API.AvatarInfoDto>("/api/app/profile-avatar/my-avatar-info", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/profile-avatar/upload */
export async function postApiAppProfileAvatarUpload(
  body: {},
  File?: File,
  options?: { [key: string]: any }
) {
  const formData = new FormData();

  if (File) {
    formData.append("File", File);
  }

  Object.keys(body).forEach((ele) => {
    const item = (body as any)[ele];

    if (item !== undefined && item !== null) {
      // 注意：此处用 Object.prototype.toString 判别 File 而非 item instanceof File。
        // 本文件由 openapi 生成器产出（npm run openapi 会覆盖手工改动，需同步生成模板）；
        // @types/node 的 File 声明会把 DOM File 遮蔽成纯类型，instanceof 直接编译报
        // TS2359（pnpm tsc 存量失败的根因，见 docs/TEST_EVIDENCE_2026-09-21.md D6）。
        if (typeof item === "object" && Object.prototype.toString.call(item) !== "[object File]") {
        if (item instanceof Array) {
          item.forEach((f) => formData.append(ele, f || ""));
        } else {
          formData.append(
            ele,
            new Blob([JSON.stringify(item)], { type: "application/json" })
          );
        }
      } else {
        formData.append(ele, item);
      }
    }
  });

  return request<any>("/api/app/profile-avatar/upload", {
    method: "POST",
    data: formData,
    requestType: "form",
    ...(options || {}),
  });
}
