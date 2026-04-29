<#import "template.ftl" as layout>
<@layout.emailLayout title="Reset your password - APIEngine"
                      preheader="A password reset was requested for your APIEngine account."
                      badgeIcon="🔐">
  <h1 style="margin:0 0 16px 0;font-size:26px;line-height:1.3;font-weight:700;color:#111827;text-align:center;">
    Reset your password
  </h1>
  <p style="margin:0 0 16px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Hi <strong style="color:#111827;">${user.firstName!user.username!"there"}</strong>,
  </p>
  <p style="margin:0 0 28px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    We received a request to reset the password for your <strong style="color:#111827;">APIEngine</strong> account. Click the button below to choose a new password.
  </p>

  <@layout.button href=link label="Reset Password" />

  <p style="margin:28px 0 8px 0;font-size:13px;line-height:1.6;color:#6b7280;text-align:center;">
    Or copy and paste this link into your browser:
  </p>
  <p style="margin:0 0 24px 0;font-size:12px;line-height:1.5;word-break:break-all;text-align:center;">
    <a href="${link}" target="_blank" style="color:#4f46e5;text-decoration:none;">${link}</a>
  </p>

  <@layout.callout type="warning">
    ⏱ <strong>This link expires in ${linkExpirationFormatter(linkExpiration)}.</strong> For your security, password reset links can only be used once.
  </@layout.callout>

  <@layout.callout type="danger">
    🛡 <strong>Didn't request this?</strong> You can safely ignore this email — your password will not change. If you suspect unusual activity on your account, please contact <a href="mailto:support@apiengine.com" style="color:#991b1b;text-decoration:underline;font-weight:600;">support@apiengine.com</a>.
  </@layout.callout>

  <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;">

  <h3 style="margin:0 0 14px 0;font-size:15px;font-weight:700;color:#111827;">Tips for a strong password</h3>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Use at least 12 characters
    </td></tr>
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Mix upper &amp; lower case, numbers, and symbols
    </td></tr>
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Avoid reusing passwords from other sites
    </td></tr>
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Consider enabling two-factor authentication
    </td></tr>
  </table>
</@layout.emailLayout>
