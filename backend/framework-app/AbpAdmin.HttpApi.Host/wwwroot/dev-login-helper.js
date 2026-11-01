/* Dev Login Helper
 * 仅 Development 打包引用（AbpAdminHttpApiHostModule.ConfigureBundles），但 wwwroot 文件本身
 * 在任何环境都匿名可取（MapAbpStaticAssets 公开静态资源）——所以这里不能出现明文口令：
 * 匿名访客 GET /dev-login-helper.js 就能读走"这套部署在用哪个默认口令"。
 * 口令改从 localStorage 读（dev 自己在控制台执行一次 localStorage.setItem 即可）：
 *   localStorage.setItem("devLoginHelper:password", "你的本地口令")
 */
(() => {
  const defaultAdminUsername = "admin";
  const defaultAdminPassword =
    window.localStorage.getItem("devLoginHelper:password") || "";

  const run = () => {
    if (!/\/Account\/Login\/?$/i.test(window.location.pathname)) {
      return;
    }

    const userInput = document.querySelector(
      'input[name$="UserNameOrEmailAddress"], input[id$="UserNameOrEmailAddress"], input[name$="UserName"], input[id$="UserName"]'
    );
    const passwordInput = document.querySelector(
      'input[type="password"][name$="Password"], input[type="password"][id$="Password"]'
    );

    const addHint = (input, text) => {
      if (!input || input.dataset.defaultHintAdded === "true") {
        return;
      }

      const hint = document.createElement("span");
      hint.className = "text-muted small";
      hint.textContent = text;

      const floatingContainer = input.closest(".form-floating.mb-2");
      if (floatingContainer) {
        floatingContainer.appendChild(hint);
        input.dataset.defaultHintAdded = "true";
        return;
      }

      const container =
        input.closest(".input-group") ||
        input.closest(".form-group") ||
        input.closest(".mb-3") ||
        input.closest(".form-floating") ||
        input;

      if (container === input || container.classList.contains("input-group")) {
        container.insertAdjacentElement("afterend", hint);
      } else {
        container.appendChild(hint);
      }
      input.dataset.defaultHintAdded = "true";
    };

    const autoFillDefaults = () => {
      if (userInput && !userInput.value) {
        userInput.value = defaultAdminUsername;
      }
      if (defaultAdminPassword && passwordInput && !passwordInput.value) {
        passwordInput.value = defaultAdminPassword;
      }
    };

    addHint(userInput, `Default username: ${defaultAdminUsername}`);
    addHint(
      passwordInput,
      defaultAdminPassword
        ? "Password: from localStorage(devLoginHelper:password)"
        : "Autofill off: set localStorage devLoginHelper:password",
    );

    setTimeout(autoFillDefaults, 150);
    if (userInput) {
      userInput.addEventListener("focus", autoFillDefaults, { once: true });
    }
    if (passwordInput) {
      passwordInput.addEventListener("focus", autoFillDefaults, { once: true });
    }
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", run);
  } else {
    run();
  }
})();
