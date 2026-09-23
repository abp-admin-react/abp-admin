// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/post */
export async function getApiAppPost(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppPostParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1PostDto>("/api/app/post", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/post */
export async function postApiAppPost(
  body: API.CreatePostDto,
  options?: { [key: string]: any }
) {
  return request<API.PostDto>("/api/app/post", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/post/${param0} */
export async function putApiAppPostId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppPostIdParams,
  body: API.UpdatePostDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PostDto>(`/api/app/post/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/post/${param0} */
export async function deleteApiAppPostId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppPostIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/post/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/post/${param0}/member/${param1} */
export async function deleteApiAppPostIdMemberUserId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppPostIdMemberUserIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, userId: param1, ...queryParams } = params;
  return request<any>(`/api/app/post/${param0}/member/${param1}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/post/${param0}/members */
export async function getApiAppPostIdMembers(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppPostIdMembersParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PagedResultDto1PostUserDto>(
    `/api/app/post/${param0}/members`,
    {
      method: "GET",
      params: {
        ...queryParams,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/post/${param0}/members */
export async function postApiAppPostIdMembers(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppPostIdMembersParams,
  body: string[],
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/post/${param0}/members`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/post/lookup */
export async function getApiAppPostLookup(options?: { [key: string]: any }) {
  return request<API.ListResultDto1PostDto>("/api/app/post/lookup", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/post/posts-by-user/${param0} */
export async function getApiAppPostPostsByUserUserId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppPostPostsByUserUserIdParams,
  options?: { [key: string]: any }
) {
  const { userId: param0, ...queryParams } = params;
  return request<API.ListResultDto1PostDto>(
    `/api/app/post/posts-by-user/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
