<#import "template.ftl" as layout>
<@layout.emailLayout title="Confirm your new email - APIEngine"
                      preheader="Verify your new email address to update your APIEngine account."
                      badgeIcon="✉">
  <h1 style="margin:0 0 16px 0;font-size:26px;line-height:1.3;font-weight:700;color:#111827;text-align:center;">
    Confirm your new email
  </h1>
  <p style="margin:0 0 16px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Hi <strong style="color:#111827;">${user.firstName!user.username!"there"}</strong>,
  </p>
  <p style="margin:0 0 28px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    We received a request to update the email address on your <strong style="color:#111827;">APIEngine</strong> account to this address. Click the button below to confirm.
  </p>

  <@layout.button href=link label="Confirm New Email" />

  <p style="margin:28px 0 8px 0;font-size:13px;line-height:1.6;color:#6b7280;text-align:center;">
    Or copy and paste this link into your browser:
  </p>
  <p style="margin:0 0 24px 0;font-size:12px;line-height:1.5;word-break:break-all;text-align:center;">
    <a href="${link}" target="_blank" style="color:#4f46e5;text-decoration:none;">${link}</a>
  </p>

  <@layout.callout type="warning">
    ⏱ <strong>This link expires in ${linkExpirationFormatter(linkExpiration)}.</strong>
  </@layout.callout>

  <@layout.callout type="danger">
    🛡 <strong>Didn't request this change?</strong> Please ignore this email and contact <a href="mailto:support@apiengine.com" style="color:#991b1b;text-decoration:underline;font-weight:600;">support@apiengine.com</a> immediately if you suspect unauthorized activity.
  </@layout.callout>
</@layout.emailLayout>
