window.atelier = window.atelier || {};

window.atelier.setTheme = function (name) {
    if (!["vellum", "noir", "petrol"].includes(name)) return;
    const html = document.documentElement;
    html.classList.remove("palette-vellum", "palette-noir", "palette-petrol");
    html.classList.add("palette-" + name);
    const secure = location.protocol === "https:" ? "; secure" : "";
    document.cookie = "Atelier.Theme=" + name + "; path=/; max-age=31536000; samesite=strict" + secure;
};
