<#import "template.ftl" as layout>
<@layout.registrationLayout 
    displayMessage=!messagesPerField.existsError('username','password') 
    displayInfo=realm.password && realm.registrationAllowed
    ; section>

    <#if section == "header">

    <#elseif section == "form">
        <div class="card-pf">
            <h1>Welcome back</h1>

            <#if realm.registrationAllowed>
                <p class="subtitle">
                    Don't have an account? 
                    <a href="${url.registrationUrl}">Start free trial</a>
                </p>
            </#if>

            <#if messagesPerField.existsError('username','password')>
                <div class="alert alert-error">
                    ${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}
                </div>
            </#if>

            <#if realm.password>
                <form id="kc-form-login" 
                      onsubmit="login.disabled = true; return true;" 
                      action="${url.loginAction}" 
                      method="post">

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

                        <input tabindex="1"
                               id="username"
                               class="form-control"
                               name="username"
                               value="${(login.username!'')}"
                               type="text"
                               autofocus
                               autocomplete="username"
                               aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"/>
                    </div>

                    <div class="form-group">
                        <label for="password">${msg("password")}</label>
                        <div class="input-group">
                            <input tabindex="2"
                                   id="password"
                                   class="form-control"
                                   name="password"
                                   type="password"
                                   autocomplete="current-password"
                                   aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"/>

                            <button type="button"
                                    class="input-group-text"
                                    tabindex="3"
                                    onclick="togglePassword()">
                                👁
                            </button>
                        </div>
                    </div>

                    <div class="login-pf-actions">
                        <#if realm.rememberMe && !usernameEditDisabled??>
                            <label>
                                <input tabindex="3"
                                       id="rememberMe"
                                       type="checkbox"
                                       name="rememberMe"
                                       <#if login.rememberMe??>checked</#if>>
                                Remember me
                            </label>
                        </#if>

                        <#if realm.resetPasswordAllowed>
                            <a tabindex="5" href="${url.loginResetCredentialsUrl}">
                                Forgot password?
                            </a>
                        </#if>
                    </div>

                    <input type="hidden"
                           name="credentialId"
                           <#if auth.selectedCredential?has_content>
                               value="${auth.selectedCredential}"
                           </#if>/>

                    <button tabindex="4"
                            class="btn btn-primary btn-lg"
                            name="login"
                            id="kc-login"
                            type="submit">
                        Sign in
                    </button>
                </form>
            </#if>
        </div>
    </#if>
</@layout.registrationLayout>

<script>
function togglePassword() {
    var input = document.getElementById('password');
    input.type = input.type === 'password' ? 'text' : 'password';
}
</script>