<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>

    <#if section == "header">

    <#elseif section == "form">
        <div class="card-pf" style="text-align:center;">
            <div style="width:64px;height:64px;background:#dcfce7;border-radius:50%;display:flex;align-items:center;justify-content:center;margin:0 auto 20px auto;font-size:30px;color:#16a34a;">
                &#10003;
            </div>

            <h1>${message.summary!''}</h1>

            <#if skipLink??>
            <#else>
                <#if pageRedirectUri?has_content>
                    <p style="margin-top:24px;">
                        <a href="${pageRedirectUri}" class="btn btn-primary btn-lg">Continue</a>
                    </p>
                <#elseif actionUri?has_content>
                    <p style="margin-top:24px;">
                        <a href="${actionUri}" class="btn btn-primary btn-lg">Proceed</a>
                    </p>
                <#elseif (client.baseUrl)?has_content>
                    <p style="margin-top:24px;">
                        <a href="${client.baseUrl}" class="btn btn-primary btn-lg">Back to Application</a>
                    </p>
                </#if>
            </#if>

            <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;">

            <p style="font-size:13px;color:#9ca3af;margin:0;">
                <a href="${url.loginUrl}" style="color:#6b7280;text-decoration:none;">&laquo; Back to sign in</a>
            </p>
        </div>
    </#if>

</@layout.registrationLayout>
