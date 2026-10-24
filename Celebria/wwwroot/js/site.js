
// Форми с data-confirm питат за потвърждение преди изпращане (напр. изтриване на профил в админ панела).
document.addEventListener('submit', function (e) {
    var message = e.target.getAttribute && e.target.getAttribute('data-confirm');
    if (message && !window.confirm(message)) {
        e.preventDefault();
    }
});
