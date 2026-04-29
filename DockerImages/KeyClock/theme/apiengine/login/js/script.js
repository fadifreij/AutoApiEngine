// API Engine Login Theme Script
document.addEventListener('DOMContentLoaded', function() {
    // Initialize any necessary functionality
    console.log('API Engine Login Theme Loaded');
});

function togglePassword() {
    const input = document.getElementById('password');
    if (input) {
        input.type = input.type === 'password' ? 'text' : 'password';
    }
}
