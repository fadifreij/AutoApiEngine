<#--
  APIEngine email layout macro
  Usage:
    <@layout.emailLayout title="..." preheader="..." userName=user.firstName>
        ... main HTML content ...
    </@layout.emailLayout>
-->
<#macro emailLayout title="APIEngine" preheader="" userName="" badgeIcon="✓">
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1.0">
<meta http-equiv="X-UA-Compatible" content="IE=edge">
<meta name="color-scheme" content="light">
<meta name="supported-color-schemes" content="light">
<title>${title}</title>
</head>
<body style="margin:0;padding:0;background:#f3f4f6;font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,Roboto,Helvetica,Arial,sans-serif;color:#111827;-webkit-font-smoothing:antialiased;">
<#if preheader?has_content>
<div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">${preheader}</div>
</#if>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f3f4f6;padding:40px 16px;">
  <tr>
    <td align="center">
      <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;width:100%;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 4px 16px rgba(0,0,0,0.08);">
        <tr>
          <td style="background:linear-gradient(135deg,#4f46e5 0%,#7c3aed 100%);padding:28px 40px;">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
              <tr>
                <td style="vertical-align:middle;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td style="vertical-align:middle;padding-right:10px;">
                        <div style="width:36px;height:36px;background:rgba(255,255,255,0.18);border-radius:8px;text-align:center;line-height:36px;color:#ffffff;font-weight:800;font-size:18px;">A</div>
                      </td>
                      <td style="vertical-align:middle;">
                        <span style="color:#ffffff;font-size:22px;font-weight:800;letter-spacing:-0.3px;">API<span style="color:#e0e7ff;font-weight:600;">Engine</span></span>
                      </td>
                    </tr>
                  </table>
                </td>
                <td align="right" style="vertical-align:middle;">
                  <span style="color:#e0e7ff;font-size:12px;font-weight:500;letter-spacing:0.5px;text-transform:uppercase;">Account Notification</span>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <tr>
          <td style="padding:0;">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="background:linear-gradient(180deg,#eef2ff 0%,#ffffff 100%);padding:36px 40px 8px 40px;">
              <tr>
                <td align="center">
                  <div style="width:64px;height:64px;background:#ffffff;border:1px solid #e0e7ff;border-radius:50%;line-height:64px;text-align:center;font-size:28px;color:#4f46e5;box-shadow:0 4px 14px rgba(79,70,229,0.15);">
                    ${badgeIcon}
                  </div>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <tr>
          <td style="padding:24px 40px 32px 40px;">
            <#nested>
          </td>
        </tr>

        <tr>
          <td style="background:#f9fafb;padding:28px 40px;border-top:1px solid #e5e7eb;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td align="center" style="padding-bottom:14px;">
                  <a href="#" style="display:inline-block;margin:0 8px;color:#6b7280;text-decoration:none;font-size:13px;">Documentation</a>
                  <span style="color:#d1d5db;">·</span>
                  <a href="#" style="display:inline-block;margin:0 8px;color:#6b7280;text-decoration:none;font-size:13px;">Status</a>
                  <span style="color:#d1d5db;">·</span>
                  <a href="#" style="display:inline-block;margin:0 8px;color:#6b7280;text-decoration:none;font-size:13px;">Support</a>
                </td>
              </tr>
              <tr>
                <td align="center" style="padding-bottom:6px;">
                  <p style="margin:0;font-size:13px;color:#6b7280;">
                    Need help? Email <a href="mailto:support@apiengine.com" style="color:#4f46e5;text-decoration:none;font-weight:600;">support@apiengine.com</a>
                  </p>
                </td>
              </tr>
              <tr>
                <td align="center">
                  <p style="margin:0;font-size:12px;color:#9ca3af;">&copy; 2026 APIEngine. All rights reserved.</p>
                </td>
              </tr>
            </table>
          </td>
        </tr>
      </table>

      <p style="margin:16px auto 0 auto;font-size:11px;color:#9ca3af;max-width:600px;text-align:center;line-height:1.5;">
        This is a transactional email regarding your APIEngine account. If you received this in error, please ignore it or contact support.
      </p>
    </td>
  </tr>
</table>
</body>
</html>
</#macro>

<#--
  Reusable styled CTA button macro.
  Usage: <@layout.button href="..." label="Verify Email" />
-->
<#macro button href label>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px auto 0 auto;">
  <tr>
    <td align="center" style="border-radius:10px;background:#4f46e5;">
      <a href="${href}" target="_blank"
         style="display:inline-block;padding:14px 36px;font-size:16px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:10px;background:#4f46e5;border:1px solid #4f46e5;">
        ${label}
      </a>
    </td>
  </tr>
</table>
</#macro>

<#--
  Info / warning callout box.
  type: info | warning | success | danger
-->
<#macro callout type="info">
<#assign bg = "#eff6ff" /><#assign bd = "#bfdbfe" /><#assign tc = "#1d4ed8" />
<#if type == "warning">
  <#assign bg = "#fffbeb" /><#assign bd = "#fde68a" /><#assign tc = "#92400e" />
<#elseif type == "success">
  <#assign bg = "#f0fdf4" /><#assign bd = "#bbf7d0" /><#assign tc = "#166534" />
<#elseif type == "danger">
  <#assign bg = "#fef2f2" /><#assign bd = "#fecaca" /><#assign tc = "#991b1b" />
</#if>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:${bg};border:1px solid ${bd};border-radius:10px;margin:0 0 20px 0;">
  <tr>
    <td style="padding:14px 18px;font-size:13px;line-height:1.5;color:${tc};">
      <#nested>
    </td>
  </tr>
</table>
</#macro>
