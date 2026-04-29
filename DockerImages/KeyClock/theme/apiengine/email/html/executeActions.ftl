<#import "template.ftl" as layout>
<@layout.emailLayout title="Action required - APIEngine"
                      preheader="An administrator has requested actions on your APIEngine account."
                      badgeIcon="⚡">
  <h1 style="margin:0 0 16px 0;font-size:26px;line-height:1.3;font-weight:700;color:#111827;text-align:center;">
    Action required on your account
  </h1>
  <p style="margin:0 0 16px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Hi <strong style="color:#111827;">${user.firstName!user.username!"there"}</strong>,
  </p>
  <p style="margin:0 0 24px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Your APIEngine administrator has requested the following ${requiredActions?size} action<#if requiredActions?size != 1>s</#if> to be completed on your account before you can continue.
  </p>

  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;margin:0 0 24px 0;">
    <tr>
      <td style="padding:18px 20px;">
        <#list requiredActions as reqAction>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
          <tr>
            <td style="width:32px;vertical-align:top;padding:6px 0;">
              <div style="width:22px;height:22px;background:#ede9fe;color:#7c3aed;border-radius:50%;text-align:center;line-height:22px;font-size:12px;font-weight:700;">${reqAction_index + 1}</div>
            </td>
            <td style="padding:6px 0;font-size:14px;color:#374151;line-height:1.5;">
              ${msg("requiredAction.${reqAction}")}
            </td>
          </tr>
        </table>
        </#list>
      </td>
    </tr>
  </table>

  <@layout.button href=link label="Complete Required Actions" />

  <p style="margin:28px 0 8px 0;font-size:13px;line-height:1.6;color:#6b7280;text-align:center;">
    Or copy and paste this link into your browser:
  </p>
  <p style="margin:0 0 24px 0;font-size:12px;line-height:1.5;word-break:break-all;text-align:center;">
    <a href="${link}" target="_blank" style="color:#4f46e5;text-decoration:none;">${link}</a>
  </p>

  <@layout.callout type="warning">
    ⏱ <strong>This link expires in ${linkExpirationFormatter(linkExpiration)}.</strong> After expiration you'll need to ask your administrator to send a new request.
  </@layout.callout>

  <p style="margin:24px 0 0 0;font-size:13px;line-height:1.6;color:#9ca3af;text-align:center;">
    If you didn't expect this email, please contact your administrator or <a href="mailto:support@apiengine.com" style="color:#4f46e5;text-decoration:none;">support@apiengine.com</a>.
  </p>
</@layout.emailLayout>
