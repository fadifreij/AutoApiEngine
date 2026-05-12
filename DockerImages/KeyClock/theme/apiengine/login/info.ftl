<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>

    <#if section == "header">

    <#elseif section == "form">

        <#-- ===== AUTO-PROCEED: skip the manual "Proceed" click ===== -->
        <#if actionUri?has_content>
            <div class="card-pf" id="verifying-card" style="text-align:center;">
                <style>
                    @keyframes ae-spin { to { transform: rotate(360deg); } }
                    @keyframes ae-pulse { 0%,100% { opacity:.6; } 50% { opacity:1; } }
                </style>
                <div style="width:64px;height:64px;margin:0 auto 24px auto;position:relative;">
                    <svg viewBox="0 0 64 64" width="64" height="64" style="animation:ae-spin 1s linear infinite;">
                        <circle cx="32" cy="32" r="28" fill="none" stroke="#e5e7eb" stroke-width="4"/>
                        <circle cx="32" cy="32" r="28" fill="none" stroke="#4f46e5" stroke-width="4"
                                stroke-dasharray="90 180" stroke-linecap="round"/>
                    </svg>
                </div>
                <h1 style="font-size:22px;margin:0 0 8px 0;">Verifying your email&hellip;</h1>
                <p style="color:#6b7280;font-size:15px;margin:0;">
                    Please wait while we activate your account.
                </p>
            </div>
            <script>
                (function() {
                    window.location.href = "${actionUri?js_string}";
                })();
            </script>

        <#-- ===== SUCCESS / INFO: beautiful result page ===== -->
        <#else>
            <div class="card-pf" style="text-align:center;">
                <style>
                    @keyframes ae-pop {
                        0%   { transform: scale(0); opacity: 0; }
                        60%  { transform: scale(1.15); opacity: 1; }
                        100% { transform: scale(1); }
                    }
                    @keyframes ae-draw {
                        to { stroke-dashoffset: 0; }
                    }
                    @keyframes ae-fadeUp {
                        from { opacity: 0; transform: translateY(12px); }
                        to   { opacity: 1; transform: translateY(0); }
                    }
                    .ae-success-ring {
                        width: 80px; height: 80px;
                        border-radius: 50%;
                        display: flex; align-items: center; justify-content: center;
                        margin: 0 auto 24px auto;
                        animation: ae-pop .5s cubic-bezier(.34,1.56,.64,1) forwards;
                    }
                    .ae-success-ring.success {
                        background: linear-gradient(135deg, #dcfce7 0%, #bbf7d0 100%);
                    }
                    .ae-success-ring.info {
                        background: linear-gradient(135deg, #ede9fe 0%, #ddd6fe 100%);
                    }
                    .ae-check {
                        stroke-dasharray: 36;
                        stroke-dashoffset: 36;
                        animation: ae-draw .4s .35s ease forwards;
                    }
                    .ae-fade { animation: ae-fadeUp .4s .5s ease both; }
                    .ae-fade-2 { animation: ae-fadeUp .4s .65s ease both; }
                    .ae-fade-3 { animation: ae-fadeUp .4s .8s ease both; }
                </style>

                <#assign isSuccess = (message?? && message.type == "success") || !(message??)>

                <div class="ae-success-ring <#if isSuccess>success<#else>info</#if>">
                    <#if isSuccess>
                        <svg width="40" height="40" viewBox="0 0 40 40" fill="none">
                            <path class="ae-check" d="M11 21l6 6L29 15"
                                  stroke="#16a34a" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
                        </svg>
                    <#else>
                        <svg width="36" height="36" viewBox="0 0 36 36" fill="none">
                            <circle cx="18" cy="18" r="14" stroke="#4f46e5" stroke-width="2.5" fill="none"/>
                            <line x1="18" y1="10" x2="18" y2="20" stroke="#4f46e5" stroke-width="2.5" stroke-linecap="round"/>
                            <circle cx="18" cy="25" r="1.5" fill="#4f46e5"/>
                        </svg>
                    </#if>
                </div>

                <h1 class="ae-fade" style="font-size:24px;margin:0 0 8px 0;">
                    <#if isSuccess>
                        Account Verified
                    <#else>
                        ${message.summary!''}
                    </#if>
                </h1>

                <p class="ae-fade-2" style="color:#6b7280;font-size:15px;margin:0 0 8px 0;line-height:1.6;">
                    <#if isSuccess>
                        Your email has been confirmed and your account is now active.<br>
                        You're all set to start using APIEngine.
                    <#else>
                        ${message.summary!''}
                    </#if>
                </p>

                <#if isSuccess>
                    <div class="ae-fade-3" style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:10px;padding:16px 20px;margin:20px 0 0 0;text-align:left;">
                        <p style="margin:0 0 10px 0;font-size:14px;font-weight:600;color:#166534;">What's next?</p>
                        <div style="display:flex;flex-direction:column;gap:6px;">
                            <div style="font-size:14px;color:#4b5563;">
                                <span style="color:#22c55e;font-weight:700;margin-right:6px;">&#10003;</span>Sign in to your account
                            </div>
                            <div style="font-size:14px;color:#4b5563;">
                                <span style="color:#22c55e;font-weight:700;margin-right:6px;">&#10003;</span>Create your first workspace
                            </div>
                            <div style="font-size:14px;color:#4b5563;">
                                <span style="color:#22c55e;font-weight:700;margin-right:6px;">&#10003;</span>Connect a database & auto-generate APIs
                            </div>
                        </div>
                    </div>
                </#if>

                <div class="ae-fade-3" style="margin-top:28px;">
                    <#if skipLink??>
                    <#else>
                        <#if pageRedirectUri?has_content>
                            <a href="${pageRedirectUri}" class="btn btn-primary btn-lg" style="width:100%;">Continue to App</a>
                        <#else>
                            <a id="ae-signin-btn" href="${url.loginUrl}" class="btn btn-primary btn-lg" style="width:100%;">
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" style="margin-right:6px;">
                                    <path d="M15 3h4a2 2 0 012 2v14a2 2 0 01-2 2h-4M10 17l5-5-5-5M15 12H3"
                                          stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                                </svg>
                                Sign In
                            </a>
                        </#if>
                    </#if>
                </div>

                <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0 16px 0;">

                <p style="font-size:13px;color:#9ca3af;margin:0;">
                    <a id="ae-back-link" href="${url.loginUrl}" style="color:#6b7280;text-decoration:none;">&laquo; Back to sign in</a>
                </p>
            </div>

            <script>
                // After action-token verification the auth session is destroyed,
                // so url.loginUrl may contain a stale session_code. Build a clean
                // login URL from the current page origin + realm path.
                (function() {
                    var loc  = window.location;
                    var match = loc.pathname.match(/^(\/realms\/[^\/]+)/);
                    if (match) {
                        var clean = loc.origin + match[1] + '/account';
                        var btn  = document.getElementById('ae-signin-btn');
                        var link = document.getElementById('ae-back-link');
                        if (btn)  btn.href  = clean;
                        if (link) link.href = clean;
                    }
                })();
            </script>
        </#if>
    </#if>

</@layout.registrationLayout>
