// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/personal-data */
export async function getApiAppPersonalData(options?: { [key: string]: any }) {
  return request<API.PersonalDataDto>("/api/app/personal-data", {
    method: "GET",
    ...(options || {}),
  });
}
