const knownExtensions = ['.pdf', '.docx', '.txt', '.md', '.markdown'];

let nextId = 0;

function hasFiles(event) {
    const types = event.dataTransfer && event.dataTransfer.types;
    return !!types && Array.prototype.indexOf.call(types, 'Files') >= 0;
}

function typeHint(file) {
    const name = (file.name || '').toLowerCase();
    const dot = name.lastIndexOf('.');
    const extension = dot >= 0 ? name.substring(dot) : '';
    return knownExtensions.indexOf(extension) >= 0
        ? null
        : 'This does not look like a PDF, Word or text file. We will still try to read it.';
}

export function init(root, dotnet, options) {
    const handle = {
        root,
        dotnet,
        url: options.url,
        token: options.token,
        single: !!options.single,
        files: new Map(),
        requests: new Map(),
        queue: [],
        active: 0,
        dragDepth: 0,
        listeners: []
    };

    const listen = (target, type, fn) => {
        target.addEventListener(type, fn);
        handle.listeners.push(() => target.removeEventListener(type, fn));
    };

    const zone = root.querySelector('[data-upload-zone]') || root;
    const input = root.querySelector('input[type=file][data-upload-input]');

    const setDragging = on => zone.classList.toggle('is-dragging', on);

    listen(document, 'dragenter', e => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        handle.dragDepth++;
        setDragging(true);
    });
    listen(document, 'dragover', e => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
    });
    listen(document, 'dragleave', e => {
        if (!hasFiles(e)) return;
        handle.dragDepth = Math.max(0, handle.dragDepth - 1);
        if (handle.dragDepth === 0) setDragging(false);
    });
    listen(document, 'drop', e => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        handle.dragDepth = 0;
        setDragging(false);
        addFiles(handle, e.dataTransfer.files);
    });
    listen(root, 'paste', e => {
        const files = e.clipboardData && e.clipboardData.files;
        if (!files || files.length === 0) return;
        e.preventDefault();
        addFiles(handle, files);
    });
    if (input) {
        listen(input, 'change', () => {
            addFiles(handle, input.files);
            input.value = '';
        });
    }
    return handle;
}

async function addFiles(handle, fileList) {
    let files = Array.prototype.slice.call(fileList || []);
    if (handle.single && files.length > 1) {
        handle.dotnet.invokeMethodAsync('OnExtraFilesIgnored', files.length - 1).catch(() => { });
        files = files.slice(0, 1);
    }
    for (const file of files) {
        const localId = 'u' + (++nextId) + '-' + Date.now().toString(36);
        let accepted = false;
        try {
            accepted = await handle.dotnet.invokeMethodAsync('OnUploadStarted', localId, file.name, file.size, typeHint(file));
        } catch {
            accepted = false;
        }
        if (!accepted) continue;
        handle.files.set(localId, file);
        send(handle, localId);
    }
}

const maxParallelUploads = 2;

function send(handle, localId) {
    if (!handle.files.has(localId)) return;
    if (handle.active >= maxParallelUploads) {
        if (!handle.queue.includes(localId)) handle.queue.push(localId);
        return;
    }
    start(handle, localId);
}

function next(handle) {
    while (handle.active < maxParallelUploads && handle.queue.length > 0) {
        const localId = handle.queue.shift();
        if (handle.files.has(localId)) start(handle, localId);
    }
}

function start(handle, localId) {
    const file = handle.files.get(localId);
    if (!file) return;
    handle.active++;
    let released = false;
    const release = () => {
        if (released) return;
        released = true;
        handle.active--;
        next(handle);
    };
    const form = new FormData();
    form.append('file', file, file.name);
    const xhr = new XMLHttpRequest();
    handle.requests.set(localId, xhr);
    let lastPercent = -1;
    xhr.upload.onprogress = e => {
        if (!e.lengthComputable) return;
        const percent = Math.floor((e.loaded / e.total) * 100);
        if (percent - lastPercent < 5 && percent < 100) return;
        lastPercent = percent;
        handle.dotnet.invokeMethodAsync('OnUploadProgress', localId, percent).catch(() => { });
    };
    const finish = (status, body) => {
        handle.requests.delete(localId);
        release();
        handle.dotnet.invokeMethodAsync('OnUploadCompleted', localId, status, body || '').catch(() => { });
    };
    xhr.onload = () => finish(xhr.status, xhr.responseText);
    xhr.onerror = () => finish(0, '');
    xhr.onabort = () => {
        handle.requests.delete(localId);
        release();
    };
    xhr.open('POST', handle.url);
    xhr.setRequestHeader('RequestVerificationToken', handle.token);
    xhr.send(form);
}

export function retry(handle, localId) {
    if (!handle.files.has(localId)) return false;
    send(handle, localId);
    return true;
}

export function forget(handle, localId) {
    handle.queue = handle.queue.filter(id => id !== localId);
    const xhr = handle.requests.get(localId);
    if (xhr) xhr.abort();
    handle.requests.delete(localId);
    handle.files.delete(localId);
}

export async function removeSource(handle, sourceId) {
    try {
        const response = await fetch(handle.url + '/' + encodeURIComponent(sourceId), {
            method: 'DELETE',
            credentials: 'same-origin',
            headers: { 'RequestVerificationToken': handle.token }
        });
        return response.status;
    } catch {
        return 0;
    }
}

export function openPicker(handle) {
    const input = handle.root.querySelector('input[type=file][data-upload-input]');
    if (input) input.click();
}

export function dispose(handle) {
    if (!handle) return;
    for (const off of handle.listeners) off();
    handle.listeners = [];
    handle.queue = [];
    for (const xhr of handle.requests.values()) xhr.abort();
    handle.requests.clear();
    handle.files.clear();
}
