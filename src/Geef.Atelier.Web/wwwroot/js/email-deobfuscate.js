(function () {
    const at = String.fromCharCode(64);

    // Reassemble a single .obf-email span into a real mailto link.
    function deobfuscate(element) {
        if (!element || element.dataset.obfDone) return;
        const u = element.getAttribute('data-u');
        const d = element.getAttribute('data-d');
        if (!u || !d) return;
        const addr = u + at + d;
        const link = document.createElement('a');
        link.href = 'mailto:' + addr;
        link.className = 'email-link';
        link.textContent = addr;
        element.innerHTML = '';
        element.appendChild(link);
        element.dataset.obfDone = '1';
    }

    function scan() {
        document.querySelectorAll('.obf-email').forEach(deobfuscate);
        addHoneypot();
    }

    function addHoneypot() {
        if (document.querySelector('.email-hidden')) return;
        const trap = document.createElement('a');
        trap.href = 'mailto:trap' + at + 'example.invalid';
        trap.className = 'email-hidden';
        trap.setAttribute('aria-hidden', 'true');
        trap.style.display = 'none';
        document.body.appendChild(trap);
    }

    // Exposed for interactive Blazor components (OnAfterRenderAsync).
    window.deobfuscateEmail = deobfuscate;

    // Auto-scan for static SSR pages (no Blazor circuit).
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scan);
    } else {
        scan();
    }

    // Re-scan after Blazor enhanced navigation swaps the document body. The 'enhancedload' event
    // fires on the Blazor object — NOT on document — so document.addEventListener never sees it
    // and the freshly swapped-in spans stay empty. Blazor may not be defined yet when this
    // deferred script runs, so poll briefly until it is, then register on the correct target.
    function registerEnhancedLoad() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', scan);
        } else {
            setTimeout(registerEnhancedLoad, 50);
        }
    }
    registerEnhancedLoad();
})();
