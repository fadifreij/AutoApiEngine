<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>

    <#if section == "header">

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
            .ae-v-fade   { animation: ae-fadeUp .4s .35s ease both; }
            .ae-v-fade-2 { animation: ae-fadeUp .4s .55s ease both; }
            .ae-v-fade-3 { animation: ae-fadeUp .4s .75s ease both; }
            .ae-v-fade-4 { animation: ae-fadeUp .4s .95s ease both; }
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
        </style>

        <div class="card-pf" style="text-align:center;">
            <div style="width:80px;height:80px;border-radius:50%;background:linear-gradient(135deg,#ede9fe 0%,#ddd6fe 100%);display:flex;align-items:center;justify-content:center;margin:0 auto 24px auto;animation:ae-envelope-pop .5s cubic-bezier(.34,1.56,.64,1) forwards, ae-envelope-float 3s 1s ease-in-out infinite;">
                <svg width="38" height="38" viewBox="0 0 24 24" fill="none">
                    <rect x="2" y="5" width="20" height="14" rx="2" stroke="#4f46e5" stroke-width="1.8" fill="none"/>
                    <path d="M2 5l10 8 10-8" stroke="#7c3aed" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
                </svg>
            </div>

            <h1 class="ae-v-fade" style="font-size:24px;margin:0 0 8px 0;">Check your inbox</h1>

            <p class="ae-v-fade-2" style="color:#6b7280;font-size:15px;margin:0 0 4px 0;line-height:1.6;">
                We sent a verification link to
            </p>
            <p class="ae-v-fade-2" style="font-weight:700;color:#111827;font-size:16px;margin:0 0 6px 0;">
                ${user.email}
            </p>
            <p class="ae-v-fade-2" style="color:#9ca3af;font-size:13px;margin:0 0 24px 0;">
                Waiting for verification <span class="ae-dots"><span></span><span></span><span></span></span>
            </p>

            <div class="ae-v-fade-3" style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:10px;padding:16px 20px;text-align:left;">
                <p style="margin:0 0 10px 0;font-size:14px;font-weight:600;color:#1e40af;">Quick steps</p>
                <div style="display:flex;flex-direction:column;gap:6px;">
                    <div style="font-size:14px;color:#4b5563;">
                        <span style="color:#4f46e5;font-weight:700;margin-right:6px;">1</span>Open your email inbox
                    </div>
                    <div style="font-size:14px;color:#4b5563;">
                        <span style="color:#4f46e5;font-weight:700;margin-right:6px;">2</span>Click <strong>Verify Email Address</strong>
                    </div>
                    <div style="font-size:14px;color:#4b5563;">
                        <span style="color:#4f46e5;font-weight:700;margin-right:6px;">3</span>You're in &mdash; start building APIs!
                    </div>
                </div>
            </div>

            <div class="ae-v-fade-4" style="margin-top:24px;">
                <p style="font-size:14px;color:#6b7280;margin:0 0 12px 0;">
                    Didn't get the email?
                </p>
                <a href="${url.loginAction}" class="btn btn-primary" style="width:100%;padding:12px 24px;">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" style="margin-right:6px;">
                        <path d="M1 4v6h6M23 20v-6h-6" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                        <path d="M20.49 9A9 9 0 005.64 5.64L1 10m22 4l-4.64 4.36A9 9 0 013.51 15" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                    </svg>
                    Resend Verification Email
                </a>
            </div>

            <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0 16px 0;">

            <p class="ae-v-fade-4" style="font-size:13px;color:#9ca3af;margin:0;">
                Wrong account?
                <a href="${url.loginRestartFlowUrl}" style="color:#4f46e5;font-weight:600;text-decoration:none;">Sign in with a different account</a>
            </p>
        </div>
    </#if>

</@layout.registrationLayout>
