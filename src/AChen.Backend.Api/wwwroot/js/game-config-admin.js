(() => {
    "use strict";

    const normalize = value => (value || "").trim().toLocaleLowerCase("zh-CN");

    document.querySelectorAll("[data-config-section]").forEach(section => {
        const key = section.dataset.configSection;
        const search = document.querySelector(`[data-config-search="${key}"]`);
        const filter = document.querySelector(`[data-config-filter="${key}"]`);
        const result = document.querySelector(`[data-config-result="${key}"]`);
        const rows = Array.from(section.querySelectorAll("[data-config-item]"));
        const empty = section.querySelector("[data-filter-empty]");

        const applyFilter = () => {
            const query = normalize(search?.value);
            const state = filter?.value || "all";
            let visible = 0;
            rows.forEach(row => {
                const matchesText = !query || normalize(row.dataset.searchText).includes(query);
                const enabled = row.dataset.enabled === "true";
                const matchesState = state === "all" ||
                    state === "enabled" && enabled ||
                    state === "disabled" && !enabled;
                row.hidden = !(matchesText && matchesState);
                if (!row.hidden) visible += 1;
            });

            if (result) result.textContent = `显示 ${visible} / ${rows.length}`;
            if (empty) empty.hidden = visible !== 0 || rows.length === 0;
        };

        search?.addEventListener("input", applyFilter);
        filter?.addEventListener("change", applyFilter);
        section.querySelectorAll('.row-toggle input[type="checkbox"]').forEach(input => {
            input.addEventListener("change", () => {
                const row = input.closest("[data-config-item]");
                const label = input.closest(".row-toggle")?.querySelector("span:last-child");
                if (row) row.dataset.enabled = input.checked ? "true" : "false";
                if (label) label.textContent = input.checked ? "启用" : "停用";
                applyFilter();
            });
        });
        applyFilter();
    });

    const publishForm = document.querySelector("[data-publish-form]");
    const publishDialog = document.querySelector("#publish-dialog");
    publishForm?.addEventListener("submit", event => {
        if (publishForm.dataset.confirmed === "true" || !publishDialog?.showModal) return;
        event.preventDefault();
        publishDialog.showModal();
    });
    document.querySelector("[data-cancel-publish]")?.addEventListener("click", () => publishDialog?.close());
    document.querySelector("[data-confirm-publish]")?.addEventListener("click", () => {
        publishDialog?.close();
        if (!publishForm) return;
        publishForm.dataset.confirmed = "true";
        publishForm.requestSubmit();
    });

    document.querySelectorAll("[data-config-form]").forEach(form => {
        form.addEventListener("submit", event => {
            const deleteTarget = form.dataset.deleteForm;
            const restoreTarget = form.dataset.restoreForm;
            const requiresImportConfirmation = form.hasAttribute("data-import-form");
            const requiresPullConfirmation = form.hasAttribute("data-pull-form");
            let question = null;
            if (deleteTarget) question = `确认删除${deleteTarget}？已经发布过的条目只能停用。`;
            if (restoreTarget) question = `确认把 Git 版本 ${restoreTarget} 载入当前草稿？线上版本不会立即改变。`;
            if (requiresImportConfirmation) question = "确认用所选 CSV 替换当前草稿？导入前会完整校验。";
            if (requiresPullConfirmation) question = "确认从 Git 远端拉取历史？当前草稿不会被自动覆盖。";
            if (question && !window.confirm(question)) {
                event.preventDefault();
                return;
            }

            if (!form.checkValidity()) return;
            const submitter = event.submitter;
            if (!(submitter instanceof HTMLButtonElement)) return;
            const label = submitter.dataset.submitLabel || submitter.textContent.trim();
            window.setTimeout(() => {
                submitter.disabled = true;
                submitter.classList.add("is-submitting");
                submitter.textContent = `${label}中…`;
            }, 0);
        });
    });

    document.querySelectorAll('.file-drop input[type="file"]').forEach(input => {
        input.addEventListener("change", () => {
            const title = input.closest(".file-drop")?.querySelector("span");
            if (title) title.textContent = input.files?.[0]?.name || "选择 CSV 文件";
        });
    });

    const navigationItems = Array.from(document.querySelectorAll(".config-nav-item"));
    navigationItems.forEach(item => item.addEventListener("click", () => {
        navigationItems.forEach(value => value.classList.remove("active"));
        item.classList.add("active");
    }));
})();
