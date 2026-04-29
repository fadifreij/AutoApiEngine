<#import "template.ftl" as layout>
<@layout.emailLayout title="Verify your email - APIEngine"
                      preheader="Confirm your email address to activate your APIEngine account."
                      badgeIcon="✉">
  <h1 style="margin:0 0 16px 0;font-size:26px;line-height:1.3;font-weight:700;color:#111827;text-align:center;">
    Verify your email address
  </h1>
  <p style="margin:0 0 16px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Hi <strong style="color:#111827;">${user.firstName!user.username!"there"}</strong>,
  </p>
  <p style="margin:0 0 28px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Welcome to <strong style="color:#111827;">APIEngine</strong> — the platform to design, generate and manage APIs in minutes. To activate your account and start your free trial, please confirm your email address.
  </p>

  <@layout.button href=link label="Verify Email Address" />

  <p style="margin:28px 0 8px 0;font-size:13px;line-height:1.6;color:#6b7280;text-align:center;">
    Or copy and paste this link into your browser:
  </p>
  <p style="margin:0 0 24px 0;font-size:12px;line-height:1.5;word-break:break-all;text-align:center;">
    <a href="${link}" target="_blank" style="color:#4f46e5;text-decoration:none;">${link}</a>
  </p>

  <@layout.callout type="warning">
    ⏱ <strong>This link expires in ${linkExpirationFormatter(linkExpiration)}.</strong> If it expires, you can request a new verification email from the sign-in page.
  </@layout.callout>

  <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;">

  <h3 style="margin:0 0 14px 0;font-size:15px;font-weight:700;color:#111827;">What's next?</h3>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Create your first workspace
    </td></tr>
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Connect a database and auto-generate APIs
    </td></tr>
    <tr><td style="padding:6px 0;font-size:14px;color:#4b5563;">
      <span style="color:#22c55e;font-weight:700;">✓</span> &nbsp;Invite your team to collaborate
    </td></tr>
  </table>

  <p style="margin:28px 0 0 0;font-size:13px;line-height:1.6;color:#9ca3af;text-align:center;">
    If you didn't create an APIEngine account, you can safely ignore this email — no account will be created.
  </p>
</@layout.emailLayout>
