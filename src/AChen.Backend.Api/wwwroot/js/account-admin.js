(() => {
    const dialog = document.querySelector("#delete-account-dialog");
    const idInput = dialog?.querySelector("[data-delete-account-id]");
    const nameText = dialog?.querySelector("[data-delete-account-name]");

    document.querySelectorAll("[data-delete-account]").forEach(button => {
        button.addEventListener("click", () => {
            if (!dialog || !idInput || !nameText) return;
            idInput.value = button.dataset.accountId ?? "";
            nameText.textContent = button.dataset.accountName ?? "";
            dialog.showModal();
        });
    });

    dialog?.querySelector("[data-cancel-delete]")?.addEventListener("click", () => {
        dialog.close();
    });

    dialog?.addEventListener("click", event => {
        if (event.target === dialog) dialog.close();
    });
})();
