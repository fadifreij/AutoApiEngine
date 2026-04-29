<#import "template.ftl" as layout>
<@layout.emailLayout title="${msg(\"eventLoginErrorSubject\")} - APIEngine"
                      preheader="A security event was detected on your APIEngine account."
                      badgeIcon="🛡">
  <h1 style="margin:0 0 16px 0;font-size:26px;line-height:1.3;font-weight:700;color:#111827;text-align:center;">
    Security alert
  </h1>
  <p style="margin:0 0 16px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    Hi <strong style="color:#111827;">${user.firstName!user.username!"there"}</strong>,
  </p>
  <p style="margin:0 0 24px 0;font-size:16px;line-height:1.6;color:#4b5563;text-align:center;">
    We detected an event on your <strong style="color:#111827;">APIEngine</strong> account that you should know about.
  </p>

  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;margin:0 0 24px 0;">
    <tr>
      <td style="padding:18px 20px;font-size:14px;color:#374151;line-height:1.6;">
        <strong style="color:#111827;">Event:</strong> ${event.type}<br>
        <#if event.ipAddress??>
          <strong style="color:#111827;">IP address:</strong> ${event.ipAddress}<br>
        </#if>
        <#if event.date??>
          <strong style="color:#111827;">Time:</strong> ${event.date?datetime}<br>
        </#if>
      </td>
    </tr>
  </table>

  <@layout.callout type="danger">
    🛡 <strong>Was this you?</strong> If yes, you can ignore this message. If not, please <a href="mailto:support@apiengine.com" style="color:#991b1b;text-decoration:underline;font-weight:600;">contact support</a> immediately and reset your password.
  </@layout.callout>
</@layout.emailLayout>
