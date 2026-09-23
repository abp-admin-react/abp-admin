// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/my-menu */
export async function getApiAppMyMenu(options?: { [key: string]: any }) {
  return request<API.ListResultDto1MyMenuItemDto>("/api/app/my-menu", {
    method: "GET",
    ...(options || {}),
  });
}
