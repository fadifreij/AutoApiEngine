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
                    <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="flex-shrink:0;margin-top:1px;">
                        <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
                    </svg>
                    <span>${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}</span>
                </div>
            </#if>

            <#if realm.password>
                <form id="kc-form-login" 
                      action="${url.loginAction}" 
                      method="post"
                      onsubmit="return handleSubmit(event);">

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
                               placeholder="Enter your email or username"
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
                                   placeholder="Enter your password"
                                   aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"/>

                            <button type="button"
                                    class="input-group-text"
                                    tabindex="3"
                                    onclick="togglePassword()"
                                    aria-label="Toggle password visibility">
                                <svg id="eye-icon" xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                                    <circle cx="12" cy="12" r="3"/>
                                </svg>
                                <svg id="eye-off-icon" xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="display:none;">
                                    <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/>
                                    <line x1="1" y1="1" x2="23" y2="23"/>
                                </svg>
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
                        <span class="btn-text">Sign in</span>
                    </button>
                </form>
            </#if>
        </div>
    </#if>

    <script>
        function togglePassword() {
            const passwordInput = document.getElementById('password');
            const eyeIcon = document.getElementById('eye-icon');
            const eyeOffIcon = document.getElementById('eye-off-icon');

            if (passwordInput.type === 'password') {
                passwordInput.type = 'text';
                eyeIcon.style.display = 'none';
                eyeOffIcon.style.display = '';
            } else {
                passwordInput.type = 'password';
                eyeIcon.style.display = '';
                eyeOffIcon.style.display = 'none';
            }
        }
    </script>
</@layout.registrationLayout>
