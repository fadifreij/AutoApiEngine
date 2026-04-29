<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=true; section>

    <#if section == "header">
        <h1>Sorry, there was an error</h1>
    <#elseif section == "form">
        <div class="card-pf">
            <div class="alert alert-error">
                <#if message??>
                    ${(message.summary)!''}
                </#if>
            </div>
        </div>
    </#if>

</@layout.registrationLayout>
