<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('firstName','lastName','email','username'); section>

    <#if section == "header">

    <#elseif section == "form">
        <div class="card-pf">
            <h1>Update your profile</h1>
            <p class="subtitle">
                Please review and complete your account information to continue.
            </p>

            <form id="kc-update-profile-form" action="${url.loginAction}" method="post">

                <#if user.editUsernameAllowed>
                    <div class="form-group">
                        <label for="username">Username</label>
                        <input type="text"
                               id="username"
                               class="form-control"
                               name="username"
                               value="${(user.username!'')}"
                               autocomplete="username"
                               aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"/>
                        <#if messagesPerField.existsError('username')>
                            <span style="color:#dc2626;font-size:13px;display:block;margin-top:4px;">
                                ${kcSanitize(messagesPerField.get('username'))?no_esc}
                            </span>
                        </#if>
                    </div>
                </#if>

                <#if user.editEmailAllowed!true>
                    <div class="form-group">
                        <label for="email">Email</label>
                        <input type="email"
                               id="email"
                               class="form-control"
                               name="email"
                               value="${(user.email!'')}"
                               autocomplete="email"
                               aria-invalid="<#if messagesPerField.existsError('email')>true</#if>"/>
                        <#if messagesPerField.existsError('email')>
                            <span style="color:#dc2626;font-size:13px;display:block;margin-top:4px;">
                                ${kcSanitize(messagesPerField.get('email'))?no_esc}
                            </span>
                        </#if>
                    </div>
                </#if>

                <div class="form-group">
                    <label for="firstName">First name</label>
                    <input type="text"
                           id="firstName"
                           class="form-control"
                           name="firstName"
                           value="${(user.firstName!'')}"
                           autocomplete="given-name"
                           aria-invalid="<#if messagesPerField.existsError('firstName')>true</#if>"/>
                    <#if messagesPerField.existsError('firstName')>
                        <span style="color:#dc2626;font-size:13px;display:block;margin-top:4px;">
                            ${kcSanitize(messagesPerField.get('firstName'))?no_esc}
                        </span>
                    </#if>
                </div>

                <div class="form-group">
                    <label for="lastName">Last name</label>
                    <input type="text"
                           id="lastName"
                           class="form-control"
                           name="lastName"
                           value="${(user.lastName!'')}"
                           autocomplete="family-name"
                           aria-invalid="<#if messagesPerField.existsError('lastName')>true</#if>"/>
                    <#if messagesPerField.existsError('lastName')>
                        <span style="color:#dc2626;font-size:13px;display:block;margin-top:4px;">
                            ${kcSanitize(messagesPerField.get('lastName'))?no_esc}
                        </span>
                    </#if>
                </div>

                <button type="submit" class="btn btn-primary btn-lg" style="margin-top:8px;">
                    Save and continue
                </button>

                <#if isAppInitiatedAction??>
                    <button type="submit" class="btn btn-outline btn-lg" name="cancel-aia" value="true" style="margin-top:10px;width:100%;">
                        Cancel
                    </button>
                </#if>
            </form>
        </div>
    </#if>

</@layout.registrationLayout>
