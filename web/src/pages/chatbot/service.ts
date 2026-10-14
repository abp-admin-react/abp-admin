// src/pages/chatbot/service.ts
import { OpenAIChatProvider, XRequest } from '@ant-design/x-sdk';

/**
 * CHAT_API_URL：聊天演示页的模型接口。构建期 process.env 读取——未在 config.ts define 中注入，
 * 实际始终用下方默认演示地址；要覆盖须同时在 define 里加该键。与财务/ABP 无关，勿放密钥。
 */
export const CHAT_API_URL =
  process.env.CHAT_API_URL ??
  'https://api.x.ant.design/api/big_model_glm-4.5-flash';

/**
 * Factory — call once per component mount (wrap in useMemo).
 * OpenAIChatProvider handles SSE parsing and history accumulation internally.
 */
export const createChatProvider = () =>
  new OpenAIChatProvider({
    request: XRequest(CHAT_API_URL, {
      manual: true,
      params: { model: 'glm-4.5-flash', stream: true },
    }),
  });
