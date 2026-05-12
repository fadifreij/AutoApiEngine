<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('firstName','lastName','email','username','password','password-confirm'); section>
    <#if section == "header">
        Create your account
    <#elseif section == "form">
        <style>
            @keyframes ae-envelope-pop {
                0%   { transform: scale(0) rotate(-12deg); opacity: 0; }
                60%  { transform: scale(1.12) rotate(2deg); opacity: 1; }
                100% { transform: scale(1) rotate(0); }
            }
            @keyframes ae-envelope-float {
                0%, 100% { transform: translateY(0); }
                50%      { transform: translateY(-6px); }
            }
            @keyframes ae-fadeUp {
                from { opacity: 0; transform: translateY(14px); }
                to   { opacity: 1; transform: translateY(0); }
            }
            @keyframes ae-dot-pulse {
                0%, 80%, 100% { opacity: .3; transform: scale(.8); }
                40%           { opacity: 1;  transform: scale(1); }
            }
            @keyframes ae-spinner { to { transform: rotate(360deg); } }

            /* Success overlay */
            .ae-reg-success-overlay {
                display: none;
                position: absolute;
                inset: 0;
                background: #fff;
                border-radius: var(--radius-lg);
                z-index: 10;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                padding: 40px;
                text-align: center;
            }
            .ae-reg-success-overlay.visible {
                display: flex;
            }
            .ae-envelope-icon {
                width: 80px; height: 80px;
                border-radius: 50%;
                background: linear-gradient(135deg, #ede9fe 0%, #ddd6fe 100%);
                display: flex; align-items: center; justify-content: center;
                margin-bottom: 24px;
                animation: ae-envelope-pop .5s cubic-bezier(.34,1.56,.64,1) forwards,
                           ae-envelope-float 3s 1s ease-in-out infinite;
            }
            .ae-s-fade   { animation: ae-fadeUp .4s .35s ease both; }
            .ae-s-fade-2 { animation: ae-fadeUp .4s .55s ease both; }
            .ae-s-fade-3 { animation: ae-fadeUp .4s .75s ease both; }
            .ae-s-fade-4 { animation: ae-fadeUp .4s .95s ease both; }
            .ae-dots span {
                display: inline-block;
                width: 6px; height: 6px;
                border-radius: 50%;
                background: #4f46e5;
                margin: 0 3px;
                animation: ae-dot-pulse 1.4s infinite ease-in-out;
            }
            .ae-dots span:nth-child(2) { animation-delay: .2s; }
            .ae-dots span:nth-child(3) { animation-delay: .4s; }

            /* Button loading state */
            .btn-loading {
                pointer-events: none;
                opacity: .85;
                position: relative;
            }
            .btn-loading .btn-text { visibility: hidden; }
            .btn-loading::after {
                content: '';
                position: absolute;
                width: 20px; height: 20px;
                border: 2.5px solid rgba(255,255,255,.3);
                border-top-color: #fff;
                border-radius: 50%;
                animation: ae-spinner .6s linear infinite;
            }

            /* Name row */
            .ae-name-row {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 12px;
            }
            @media (max-width: 480px) {
                .ae-name-row { grid-template-columns: 1fr; }
            }

            /* Password strength */
            .ae-pw-bar {
                height: 3px;
                border-radius: 2px;
                background: var(--gray-200);
                margin-top: 6px;
                overflow: hidden;
            }
            .ae-pw-fill {
                height: 100%;
                width: 0;
                border-radius: 2px;
                transition: width .3s, background .3s;
            }
        </style>

        <div class="card-pf" style="position:relative;overflow:hidden;">

            <#-- ===== SUCCESS OVERLAY (hidden until JS shows it) ===== -->
            <div class="ae-reg-success-overlay" id="ae-reg-success">
                <div class="ae-envelope-icon">
                    <svg width="38" height="38" viewBox="0 0 24 24" fill="none">
                        <rect x="2" y="5" width="20" height="14" rx="2" stroke="#4f46e5" stroke-width="1.8" fill="none"/>
                        <path d="M2 5l10 8 10-8" stroke="#7c3aed" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
                    </svg>
                </div>
                <h1 class="ae-s-fade" style="font-size:24px;margin:0 0 8px 0;">Check your email</h1>
                <p class="ae-s-fade-2" style="color:#6b7280;font-size:15px;margin:0 0 4px 0;line-height:1.6;">
                    We've sent a verification link to
                </p>
                <p class="ae-s-fade-2" id="ae-reg-email-display" style="font-weight:700;color:#111827;font-size:16px;margin:0 0 20px 0;"></p>

                <div class="ae-s-fade-3" style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:10px;padding:16px 20px;text-align:left;width:100%;box-sizing:border-box;">
                    <p style="margin:0 0 8px 0;font-size:14px;font-weight:600;color:#1e40af;">What to do next</p>
                    <div style="display:flex;flex-direction:column;gap:6px;">
                        <div style="font-size:14px;color:#4b5563;">
                            <span style="margin-right:6px;">1.</span>Open your email inbox
                        </div>
                        <div style="font-size:14px;color:#4b5563;">
                            <span style="margin-right:6px;">2.</span>Click the verification link
                        </div>
                        <div style="font-size:14px;color:#4b5563;">
                            <span style="margin-right:6px;">3.</span>Start building APIs!
                        </div>
                    </div>
                </div>

                <p class="ae-s-fade-4" style="color:#9ca3af;font-size:13px;margin:24px 0 0 0;">
                    Didn't get it? Check your spam folder or <a href="${url.registrationUrl}" style="color:#4f46e5;font-weight:600;text-decoration:none;">try again</a>.
                </p>
            </div>

            <#-- ===== REGISTRATION FORM ===== -->
            <div id="ae-reg-form-wrap">
                <h1>Create your account</h1>
                <p class="subtitle">Start your 14-day free trial &middot; <a href="${url.loginUrl}">Sign in instead</a></p>

                <#if message?? && message.type == "error">
                    <div class="alert alert-error">
                        ${kcSanitize(message.summary)?no_esc}
                    </div>
                </#if>

                <form id="kc-register-form" action="${url.registrationAction}" method="post" novalidate>
                    <div class="ae-name-row">
                        <div class="form-group">
                            <label for="firstName">First name</label>
                            <input type="text" id="firstName" class="form-control" name="firstName"
                                   placeholder="John" value="${(register.formData.firstName!'')}" autocomplete="given-name"/>
                        </div>
                        <div class="form-group">
                            <label for="lastName">Last name</label>
                            <input type="text" id="lastName" class="form-control" name="lastName"
                                   placeholder="Doe" value="${(register.formData.lastName!'')}" autocomplete="family-name"/>
                        </div>
                    </div>

                    <div class="form-group">
                        <label for="email">Email address</label>
                        <input type="email" id="email" class="form-control" name="email"
                               placeholder="you@company.com" value="${(register.formData.email!'')}" autocomplete="email"/>
                    </div>

                    <#if !realm.registrationEmailAsUsername>
                        <div class="form-group">
                            <label for="username">Username</label>
                            <input type="text" id="username" class="form-control" name="username"
                                   placeholder="johndoe" value="${(register.formData.username!'')}" autocomplete="username"/>
                        </div>
                    </#if>

                    <div class="form-group">
                        <label for="password">Password</label>
                        <input type="password" id="password" class="form-control" name="password"
                               placeholder="At least 8 characters" autocomplete="new-password"/>
                        <div class="ae-pw-bar"><div class="ae-pw-fill" id="ae-pw-fill"></div></div>
                    </div>

                    <div class="form-group">
                        <label for="password-confirm">Confirm password</label>
                        <input type="password" id="password-confirm" class="form-control" name="password-confirm"
                               placeholder="Re-enter your password" autocomplete="new-password"/>
                    </div>

                    <#if recaptchaRequired??>
                        <div class="form-group">
                            <div class="g-recaptcha" data-size="compact" data-sitekey="${recaptchaSiteKey}"></div>
                        </div>
                    </#if>

                    <button type="submit" id="ae-reg-btn" class="btn btn-primary btn-lg" style="margin-top:8px;">
                        <span class="btn-text">Start Free Trial</span>
                    </button>

                    <p style="margin:16px 0 0 0;font-size:12px;color:#9ca3af;text-align:center;line-height:1.5;">
                        By registering, you agree to our Terms of Service and Privacy Policy.
                    </p>
                </form>
            </div>
        </div>

        <script>
        (function() {
            // Password strength meter
            var pw   = document.getElementById('password');
            var fill = document.getElementById('ae-pw-fill');
            if (pw && fill) {
                pw.addEventListener('input', function() {
                    var v = pw.value, s = 0;
                    if (v.length >= 4) s++;
                    if (v.length >= 8) s++;
                    if (/[A-Z]/.test(v) && /[a-z]/.test(v)) s++;
                    if (/\d/.test(v)) s++;
                    if (/[^A-Za-z0-9]/.test(v)) s++;
                    var pct  = Math.min(s, 4) * 25;
                    var cols = ['#ef4444','#f59e0b','#eab308','#22c55e'];
                    fill.style.width = pct + '%';
                    fill.style.background = cols[Math.min(s, 4) - 1] || '#e5e7eb';
                });
            }

            // Submit: show button spinner
            var form = document.getElementById('kc-register-form');
            var btn  = document.getElementById('ae-reg-btn');
            if (form && btn) {
                form.addEventListener('submit', function() {
                    btn.classList.add('btn-loading');
                });
            }
        })();
        </script>
    </#if>
</@layout.registrationLayout>
