using Microsoft.Playwright;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>The contrast check of text in a real browser, shared by the tests of the pages.</summary>
internal static class PageContrast
{
    /// <summary>
    /// Fails when a visible text below <paramref name="selector"/> has less contrast against its background than WCAG asks for
    /// (4.5:1, large text 3:1). Hidden, decorative and disabled elements are left out; background images are not seen.
    /// </summary>
    public static async Task AssertContrastAsync(this IPage page, string name, string selector)
    {
        var failures = await page.EvaluateAsync<string[]>(
            """
            selector => {
                const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
                const ctx = canvas.getContext('2d', { willReadFrequently: true });
                const rgba = c => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = 'rgba(0,0,0,0)'; ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1);
                    const d = ctx.getImageData(0, 0, 1, 1).data; return [d[0], d[1], d[2], d[3] / 255]; };
                const over = (top, under) => { const a = top[3]; return [0, 1, 2].map(i => top[i] * a + under[i] * (1 - a)).concat(1); };
                const background = el => {
                    const layers = [];
                    for (let n = el; n; n = n.parentElement) {
                        const c = rgba(getComputedStyle(n).backgroundColor);
                        if (c[3] > 0) { layers.push(c); if (c[3] >= 1) break; }
                    }
                    let result = [255, 255, 255, 1];
                    for (let i = layers.length - 1; i >= 0; i--) result = over(layers[i], result);
                    return result;
                };
                const lum = c => { const v = c.slice(0, 3).map(x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); });
                    return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]; };
                const failures = [];
                const scope = document.querySelector(selector);
                const walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT);
                const seen = new Set();
                for (let t = walker.nextNode(); t; t = walker.nextNode()) {
                    const el = t.parentElement;
                    if (!t.textContent.trim() || seen.has(el)) continue;
                    seen.add(el);
                    if (el.closest('#film, svg, [aria-hidden="true"], .visually-hidden, [disabled], script, style')) continue;
                    if (!el.checkVisibility({ opacityProperty: true, visibilityProperty: true })) continue;
                    const r = el.getBoundingClientRect();
                    if (r.width === 0 || r.height === 0) continue;
                    const style = getComputedStyle(el);
                    const bg = background(el);
                    let opacity = 1;
                    for (let n = el; n; n = n.parentElement) opacity *= parseFloat(getComputedStyle(n).opacity);
                    const ink = rgba(style.color); ink[3] *= opacity;
                    const fg = over(ink, bg);
                    const l1 = lum(fg), l2 = lum(bg);
                    const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
                    const size = parseFloat(style.fontSize), bold = parseInt(style.fontWeight, 10) >= 700;
                    const need = size >= 24 || (bold && size >= 18.66) ? 3 : 4.5;
                    if (ratio < need) failures.push(`${ratio.toFixed(2)} < ${need}: "${t.textContent.trim().slice(0, 40)}" (${el.tagName.toLowerCase()}.${el.className})`);
                }
                return failures;
            }
            """, selector);
        Assert.True(failures.Length == 0, $"Text below the contrast minimum ({name}):\n" + string.Join("\n", failures));
    }
}
