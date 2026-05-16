<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false displayWide=false authUrl="">
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
    <meta name="robots" content="noindex, nofollow">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>APIEngine - Sign In</title>
    <link rel="icon" href="${url.resourcesPath}/img/favicon.svg" />
    <style>
        /* APIEngine Keycloak Login Theme - Modern Design */
        :root {
          --primary: #4f46e5;
          --primary-dark: #4338ca;
          --primary-light: #818cf8;
          --primary-bg: #ede9fe;
          --accent: #7c3aed;
          --accent-light: #a78bfa;
          --success: #22c55e;
          --warning: #f59e0b;
          --danger: #ef4444;
          --gray-50: #f9fafb;
          --gray-100: #f3f4f6;
          --gray-200: #e5e7eb;
          --gray-300: #d1d5db;
          --gray-400: #9ca3af;
          --gray-500: #6b7280;
          --gray-600: #4b5563;
          --gray-700: #374151;
          --gray-800: #1f2937;
          --gray-900: #111827;
          --font: 'Segoe UI', system-ui, -apple-system, sans-serif;
          --radius: 12px;
          --radius-lg: 20px;
          --shadow-sm: 0 1px 3px rgba(0,0,0,.06);
          --shadow-md: 0 4px 24px rgba(0,0,0,.07);
          --shadow-lg: 0 20px 60px rgba(79,70,229,.12);
          --shadow-card: 0 8px 32px rgba(0,0,0,.08), 0 2px 8px rgba(0,0,0,.04);
        }

        *, *::before, *::after {
          box-sizing: border-box;
        }

        body {
          margin: 0;
          padding: 0;
          font-family: var(--font);
          color: var(--gray-900);
          -webkit-font-smoothing: antialiased;
          -moz-osx-font-smoothing: grayscale;
        }

        .login-pf {
          min-height: 100vh;
          display: flex;
          flex-direction: column;
          background: linear-gradient(135deg, #f5f3ff 0%, #ede9fe 25%, #e0e7ff 50%, #f0f9ff 75%, #f5f3ff 100%);
          position: relative;
          overflow: hidden;
        }

        /* Animated background shapes */
        .login-pf::before {
          content: '';
          position: fixed;
          top: -30%;
          right: -20%;
          width: 600px;
          height: 600px;
          border-radius: 50%;
          background: radial-gradient(circle, rgba(79,70,229,.08) 0%, transparent 70%);
          animation: float 20s ease-in-out infinite;
          pointer-events: none;
        }

        .login-pf::after {
          content: '';
          position: fixed;
          bottom: -20%;
          left: -15%;
          width: 500px;
          height: 500px;
          border-radius: 50%;
          background: radial-gradient(circle, rgba(124,58,237,.06) 0%, transparent 70%);
          animation: float 25s ease-in-out infinite reverse;
          pointer-events: none;
        }

        @keyframes float {
          0%, 100% { transform: translate(0, 0) scale(1); }
          33% { transform: translate(30px, -30px) scale(1.05); }
          66% { transform: translate(-20px, 20px) scale(0.95); }
        }

        @keyframes fadeInUp {
          from {
            opacity: 0;
            transform: translateY(24px);
          }
          to {
            opacity: 1;
            transform: translateY(0);
          }
        }

        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }

        /* Header / Navbar */
        .login-pf-header {
          padding: 16px 32px;
          background: rgba(255,255,255,.7);
          backdrop-filter: blur(12px);
          -webkit-backdrop-filter: blur(12px);
          border-bottom: 1px solid rgba(255,255,255,.6);
          position: relative;
          z-index: 10;
          animation: fadeIn .6s ease;
        }

        .navbar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          max-width: 1200px;
          margin: 0 auto;
          width: 100%;
        }

        .navbar-brand {
          font-size: 22px;
          font-weight: 800;
          color: var(--primary);
          text-decoration: none;
          display: flex;
          align-items: center;
          gap: 10px;
          transition: opacity .2s;
        }

        .navbar-brand:hover {
          opacity: .85;
        }

        .navbar-brand span {
          color: var(--gray-900);
        }

        .navbar-actions {
          display: flex;
          align-items: center;
          gap: 12px;
        }

        .btn-back {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 8px 16px;
          border-radius: 8px;
          font-size: 14px;
          font-weight: 500;
          color: var(--gray-600);
          text-decoration: none;
          background: rgba(255,255,255,.6);
          border: 1px solid var(--gray-200);
          transition: all .2s;
          cursor: pointer;
          font-family: var(--font);
        }

        .btn-back:hover {
          color: var(--primary);
          border-color: var(--primary-light);
          background: rgba(255,255,255,.9);
          transform: translateX(-2px);
        }

        .btn-back svg {
          width: 16px;
          height: 16px;
          transition: transform .2s;
        }

        .btn-back:hover svg {
          transform: translateX(-2px);
        }

        /* Main body */
        .login-pf-body {
          flex: 1;
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 40px 20px;
          position: relative;
          z-index: 5;
        }

        .pf-login-container {
          width: 100%;
          max-width: 460px;
          margin: 0 auto;
          display: flex;
          flex-direction: column;
          gap: 16px;
          animation: fadeInUp .7s ease;
        }

        /* Card */
        .card-pf {
          background: rgba(255,255,255,.85);
          backdrop-filter: blur(20px);
          -webkit-backdrop-filter: blur(20px);
          border: 1px solid rgba(255,255,255,.7);
          border-radius: var(--radius-lg);
          padding: 40px;
          width: 100%;
          box-shadow: var(--shadow-card);
          transition: box-shadow .3s;
        }

        .card-pf:hover {
          box-shadow: var(--shadow-lg);
        }

        .card-pf h1 {
          font-size: 26px;
          font-weight: 700;
          margin: 0 0 4px 0;
          color: var(--gray-900);
          letter-spacing: -0.5px;
        }

        .subtitle {
          color: var(--gray-500);
          font-size: 15px;
          margin: 0 0 28px 0;
          line-height: 1.5;
        }

        .subtitle a {
          color: var(--primary);
          font-weight: 600;
          text-decoration: none;
          transition: color .15s;
        }

        .subtitle a:hover {
          color: var(--accent);
          text-decoration: underline;
        }

        /* Form */
        .form-group {
          margin-bottom: 20px;
        }

        .form-group label {
          display: block;
          font-size: 13px;
          font-weight: 600;
          color: var(--gray-700);
          margin-bottom: 6px;
          letter-spacing: .02em;
          text-transform: uppercase;
        }

        .form-control {
          width: 100%;
          padding: 12px 16px;
          border: 1.5px solid var(--gray-200);
          border-radius: 10px;
          font-size: 15px;
          font-family: var(--font);
          background: rgba(255,255,255,.8);
          transition: all .2s;
          box-sizing: border-box;
          color: var(--gray-900);
        }

        .form-control::placeholder {
          color: var(--gray-400);
        }

        .form-control:focus {
          outline: none;
          border-color: var(--primary);
          background: #fff;
          box-shadow: 0 0 0 4px rgba(79,70,229,.08);
        }

        .form-control:hover:not(:focus) {
          border-color: var(--gray-300);
        }

        /* Actions row */
        .login-pf-actions {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin: 20px 0 24px;
        }

        .login-pf-actions label {
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 13px;
          color: var(--gray-600);
          cursor: pointer;
          user-select: none;
        }

        .login-pf-actions label input[type="checkbox"] {
          width: 16px;
          height: 16px;
          accent-color: var(--primary);
          cursor: pointer;
        }

        .login-pf-actions a {
          font-size: 13px;
          color: var(--primary);
          font-weight: 600;
          text-decoration: none;
          transition: color .15s;
        }

        .login-pf-actions a:hover {
          color: var(--accent);
          text-decoration: underline;
        }

        /* Password input group */
        .input-group {
          position: relative;
          display: flex;
          align-items: center;
        }

        .input-group .form-control {
          padding-right: 48px;
        }

        .input-group-text {
          position: absolute;
          right: 4px;
          top: 50%;
          transform: translateY(-50%);
          cursor: pointer;
          color: var(--gray-400);
          background: none;
          border: none;
          padding: 8px;
          border-radius: 6px;
          display: flex;
          align-items: center;
          justify-content: center;
          transition: all .2s;
        }

        .input-group-text:hover {
          color: var(--primary);
          background: var(--primary-bg);
        }

        .input-group-text svg {
          width: 20px;
          height: 20px;
        }

        /* Buttons */
        .btn {
          display: inline-flex;
          align-items: center;
          justify-content: center;
          gap: 8px;
          padding: 12px 24px;
          border-radius: 10px;
          font-size: 15px;
          font-weight: 600;
          border: none;
          transition: all .25s;
          cursor: pointer;
          text-decoration: none;
          font-family: var(--font);
          box-sizing: border-box;
          position: relative;
          overflow: hidden;
        }

        .btn-primary {
          background: linear-gradient(135deg, var(--primary) 0%, var(--accent) 100%);
          color: #fff;
          width: 100%;
        }

        .btn-primary:hover {
          box-shadow: 0 6px 20px rgba(79,70,229,.35);
          transform: translateY(-1px);
        }

        .btn-primary:active {
          transform: translateY(0);
          box-shadow: 0 2px 8px rgba(79,70,229,.25);
        }

        .btn-lg {
          padding: 14px 32px;
          font-size: 16px;
          border-radius: 12px;
        }

        .btn-primary.loading {
          pointer-events: none;
          opacity: .85;
        }

        .btn-primary.loading .btn-text {
          visibility: hidden;
        }

        .btn-primary.loading::after {
          content: '';
          position: absolute;
          width: 22px;
          height: 22px;
          border: 2.5px solid rgba(255,255,255,.3);
          border-top-color: #fff;
          border-radius: 50%;
          animation: spin .6s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        .btn-outline {
          border: 1.5px solid var(--gray-200);
          color: var(--gray-700);
          background: rgba(255,255,255,.7);
          padding: 8px 18px;
          font-size: 14px;
        }

        .btn-outline:hover {
          border-color: var(--primary);
          color: var(--primary);
          background: rgba(255,255,255,.9);
        }

        /* Alerts */
        .alert {
          border-radius: 10px;
          padding: 14px 18px;
          margin-bottom: 20px;
          font-size: 14px;
          display: flex;
          align-items: flex-start;
          gap: 10px;
          line-height: 1.5;
        }

        .alert-error {
          background: #fef2f2;
          border: 1px solid #fecaca;
          color: #dc2626;
        }

        .alert-warning {
          background: #fffbeb;
          border: 1px solid #fde68a;
          color: #b45309;
        }

        .alert-info {
          background: #eff6ff;
          border: 1px solid #bfdbfe;
          color: #1d4ed8;
        }

        .alert-success {
          background: #f0fdf4;
          border: 1px solid #bbf7d0;
          color: #16a34a;
        }

        /* Footer */
        .login-pf-footer {
          text-align: center;
          padding: 20px;
          color: var(--gray-400);
          font-size: 13px;
          position: relative;
          z-index: 5;
          animation: fadeIn 1s ease;
        }

        .login-pf-footer a {
          color: var(--primary);
          text-decoration: none;
        }

        .login-pf-footer a:hover {
          text-decoration: underline;
        }

        /* Form check */
        .form-check {
          display: flex;
          align-items: center;
          gap: 6px;
        }

        .form-check-input {
          width: 16px;
          height: 16px;
          accent-color: var(--primary);
        }

        /* Responsive */
        @media (max-width: 640px) {
          .login-pf-header {
            padding: 12px 16px;
          }

          .card-pf {
            padding: 28px 24px;
            border-radius: 16px;
          }

          .pf-login-container {
            max-width: 100%;
          }

          .login-pf-body {
            padding: 24px 16px;
          }

          .navbar-brand {
            font-size: 18px;
          }

          .btn-back span.btn-back-label {
            display: none;
          }
        }
    </style>
</head>

<body class="apiengine-login">
    <div class="login-pf">
        <header class="login-pf-header">
            <nav class="navbar">
                <a href="http://localhost:4200" class="navbar-brand">
                    <svg viewBox="0 0 32 32" fill="none" width="28" height="28">
                        <rect x="2" y="6" width="28" height="20" rx="4" stroke="#4f46e5" stroke-width="2.5"/>
                        <path d="M9 16h6m-3-3v6" stroke="#4f46e5" stroke-width="2" stroke-linecap="round"/>
                        <circle cx="22" cy="16" r="3" stroke="#7c3aed" stroke-width="2"/>
                    </svg>
                    API<span>Engine</span>
                </a>
                <div class="navbar-actions">
                    <a href="http://localhost:4200" class="btn-back">
                        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M10.5 19.5 3 12m0 0 7.5-7.5M3 12h18" />
                        </svg>
                        <span class="btn-back-label">Go back</span>
                    </a>
                    <#if realm.registrationAllowed && !registrationDisabled??>
                        <a href="${url.registrationUrl}" class="btn btn-outline">Create Account</a>
                    </#if>
                </div>
            </nav>
        </header>

        <main class="login-pf-body">
            <div class="pf-login-container">
                <#nested "header">
                <#if displayMessage && message??>
                    <div class="alert <#if message.type == 'error'>alert-error<#elseif message.type == 'warning'>alert-warning<#elseif message.type == 'success'>alert-success<#else>alert-info</#if>" style="margin:0;">
                        ${kcSanitize((message.summary)!'')?no_esc}
                    </div>
                </#if>
                <#nested "form">
            </div>
        </main>

        <footer class="login-pf-footer">
            <p>&copy; 2026 APIEngine. All rights reserved.</p>
        </footer>
    </div>

    <#if properties.scripts?has_content>
        <#list properties.scripts?split(' ') as script>
            <script src="${url.resourcesPath}/${script}"></script>
        </#list>
    </#if>
</body>
</html>
</#macro>
