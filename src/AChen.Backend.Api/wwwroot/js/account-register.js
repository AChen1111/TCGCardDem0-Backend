(() => {
    const form = document.querySelector("[data-register-form]");
    if (!(form instanceof HTMLFormElement)) {
        return;
    }

    const password = form.querySelector("#Input_Password");
    const confirmation = form.querySelector("#Input_ConfirmPassword");
    const toggle = form.querySelector("[data-password-toggle]");
    const hint = form.querySelector("[data-password-hint]");
    const segments = Array.from(form.querySelectorAll("[data-password-segment]"));
    const submit = form.querySelector("[data-submit-button]");
    const submitLabel = form.querySelector("[data-submit-label]");

    const calculateStrength = value => {
        let score = value.length >= 8 ? 1 : 0;
        score += value.length >= 12 ? 1 : 0;
        score += /[a-z]/.test(value) && /[A-Z]/.test(value) ? 1 : 0;
        score += /\d/.test(value) && /[^A-Za-z0-9]/.test(value) ? 1 : 0;
        return score;
    };

    const updateStrength = () => {
        if (!(password instanceof HTMLInputElement)) {
            return;
        }

        const score = calculateStrength(password.value);
        segments.forEach((segment, index) => {
            segment.classList.toggle("is-active", index < score);
            segment.dataset.level = String(score);
        });

        if (hint instanceof HTMLElement) {
            hint.textContent = password.value.length === 0
                ? "建议组合大小写字母、数字和符号"
                : ["密码还不够长", "基础强度", "中等强度", "较强密码", "高强度密码"][score];
        }
    };

    const validateConfirmation = () => {
        if (!(password instanceof HTMLInputElement) || !(confirmation instanceof HTMLInputElement)) {
            return;
        }

        confirmation.setCustomValidity(
            confirmation.value.length > 0 && confirmation.value !== password.value
                ? "两次输入的密码不一致。"
                : "");
    };

    password?.addEventListener("input", () => {
        updateStrength();
        validateConfirmation();
    });
    confirmation?.addEventListener("input", validateConfirmation);

    toggle?.addEventListener("click", () => {
        if (!(password instanceof HTMLInputElement) || !(confirmation instanceof HTMLInputElement)) {
            return;
        }

        const reveal = password.type === "password";
        password.type = reveal ? "text" : "password";
        confirmation.type = reveal ? "text" : "password";
        toggle.textContent = reveal ? "隐藏" : "显示";
        toggle.setAttribute("aria-pressed", String(reveal));
        password.focus({ preventScroll: true });
    });

    form.addEventListener("submit", event => {
        validateConfirmation();
        if (!form.checkValidity()) {
            event.preventDefault();
            form.reportValidity();
            return;
        }

        if (submit instanceof HTMLButtonElement) {
            submit.disabled = true;
            submit.setAttribute("aria-busy", "true");
            submit.classList.add("is-loading");
        }
        if (submitLabel instanceof HTMLElement) {
            submitLabel.textContent = "正在创建账号";
        }
    });

    updateStrength();
})();
