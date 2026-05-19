window.atelier = window.atelier || {};

window.atelier.setTheme = function (name) {
    if (!["vellum", "noir", "petrol"].includes(name)) return;
    const html = document.documentElement;
    html.classList.remove("palette-vellum", "palette-noir", "palette-petrol");
    html.classList.add("palette-" + name);
    const secure = location.protocol === "https:" ? "; secure" : "";
    document.cookie = "Atelier.Theme=" + name + "; path=/; max-age=31536000; samesite=strict" + secure;
};

window.atelier.setDashboardScope = function (scope) {
    if (!["my", "all"].includes(scope)) return;
    const secure = location.protocol === "https:" ? "; secure" : "";
    document.cookie = "Atelier.DashboardScope=" + scope + "; path=/; max-age=86400; samesite=strict" + secure;
};

window.atelier.clearDashboardScope = function () {
    document.cookie = "Atelier.DashboardScope=; path=/; max-age=0; samesite=strict";
};
