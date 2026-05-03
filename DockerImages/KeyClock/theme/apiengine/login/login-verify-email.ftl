<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>

    <#if section == "header">

    <#elseif section == "form">
        <div class="card-pf" style="text-align:center;">
            <div style="width:64px;height:64px;background:#ede9fe;border-radius:50%;display:flex;align-items:center;justify-content:center;margin:0 auto 20px auto;font-size:30px;color:#4f46e5;">
                ✉
            </div>

            <h1>Verify your email</h1>
            <p class="subtitle">
                We've sent a verification link to <strong style="color:#111827;">${user.email}</strong>.<br>
                Click the link in the email to activate your account.
            </p>

            <div class="alert alert-info" style="text-align:left;margin-bottom:20px;">
                <strong>Didn't receive the email?</strong><br>
                <span style="font-size:13px;">Check your spam folder, or click below to resend.</span>
            </div>

            <p style="font-size:14px;color:#6b7280;margin:20px 0 8px 0;">
                Wrong email or didn't get it?
                <a href="${url.loginAction}" style="color:#4f46e5;font-weight:600;text-decoration:none;">Click here</a> to resend.
            </p>

            <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;">

            <p style="font-size:13px;color:#9ca3af;margin:0;">
                <a href="${url.loginRestartFlowUrl}" style="color:#6b7280;text-decoration:none;">&laquo; Back to sign in</a>
            </p>
        </div>
    </#if>

</@layout.registrationLayout>
