// API Engine Login Theme Script
document.addEventListener('DOMContentLoaded', function() {
    console.log('API Engine Login Theme Loaded');
});

function togglePassword() {
    var input = document.getElementById('password');
    var eyeIcon = document.getElementById('eye-icon');
    var eyeOffIcon = document.getElementById('eye-off-icon');

    if (input) {
        if (input.type === 'password') {
            input.type = 'text';
            if (eyeIcon) eyeIcon.style.display = 'none';
            if (eyeOffIcon) eyeOffIcon.style.display = 'block';
        } else {
            input.type = 'password';
            if (eyeIcon) eyeIcon.style.display = 'block';
            if (eyeOffIcon) eyeOffIcon.style.display = 'none';
        }
    }
}

function handleSubmit(event) {
    var btn = document.getElementById('kc-login');
    if (btn) {
        btn.classList.add('loading');
        btn.disabled = true;
    }
    return true;
}
