<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username'); section>

    <#if section == "header">

    <#elseif section == "form">
        <div class="card-pf">
            <h1>Forgot your password?</h1>
            <p class="subtitle">
                Enter your username or email and we'll send you instructions to reset your password.
            </p>

            <#if messagesPerField.existsError('username')>
                <div class="alert alert-error">
                    ${kcSanitize(messagesPerField.getFirstError('username'))?no_esc}
                </div>
            </#if>

            <form id="kc-reset-password-form" action="${url.loginAction}" method="post">
                <div class="form-group">
                    <label for="username">
                        <#if !realm.loginWithEmailAllowed>
                            ${msg("username")}
                        <#elseif !realm.registrationEmailAsUsername>
                            ${msg("usernameOrEmail")}
                        <#else>
                            ${msg("email")}
                        </#if>
                    </label>
                    <input type="text"
                           id="username"
                           class="form-control"
                           name="username"
                           value="${(auth.attemptedUsername!'')}"
                           autofocus
                           autocomplete="username"
                           aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"/>
                </div>

                <div class="login-pf-actions">
                    <a href="${url.loginUrl}">Back to Sign in</a>
                </div>

                <button type="submit" class="btn btn-primary btn-lg">
                    Send reset instructions
                </button>
            </form>
        </div>
    </#if>

</@layout.registrationLayout>
