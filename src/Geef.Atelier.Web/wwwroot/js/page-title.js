export function set(title) {
    if (typeof title === 'string' && title.length > 0) document.title = title;
}
