<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('password','password-confirm'); section>

    <#if section == "header">

    <#elseif section == "form">
        <div class="card-pf">
            <h1>Set a new password</h1>
            <p class="subtitle">
                Choose a strong password you don't use anywhere else.
            </p>

            <#if messagesPerField.existsError('password','password-confirm')>
                <div class="alert alert-error">
                    ${kcSanitize(messagesPerField.getFirstError('password','password-confirm'))?no_esc}
                </div>
            </#if>

            <form id="kc-passwd-update-form" action="${url.loginAction}" method="post">
                <input type="text" id="username" name="username" value="${(auth.attemptedUsername!'')}" autocomplete="username" readonly style="display:none;"/>
                <input type="password" id="password" name="password" autocomplete="current-password" style="display:none;"/>

                <div class="form-group">
                    <label for="password-new">New password</label>
                    <div class="input-group">
                        <input type="password"
                               id="password-new"
                               class="form-control"
                               name="password-new"
                               autofocus
                               autocomplete="new-password"
                               aria-invalid="<#if messagesPerField.existsError('password')>true</#if>"/>
                        <button type="button" class="input-group-text" tabindex="-1" onclick="togglePwd('password-new')">👁</button>
                    </div>
                </div>

                <div class="form-group">
                    <label for="password-confirm">Confirm new password</label>
                    <div class="input-group">
                        <input type="password"
                               id="password-confirm"
                               class="form-control"
                               name="password-confirm"
                               autocomplete="new-password"
                               aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>"/>
                        <button type="button" class="input-group-text" tabindex="-1" onclick="togglePwd('password-confirm')">👁</button>
                    </div>
                </div>

                <#if isAppInitiatedAction??>
                    <div class="form-group" style="display:flex;align-items:center;gap:8px;margin:8px 0 16px 0;">
                        <input type="checkbox" id="logout-sessions" name="logout-sessions" value="on" checked>
                        <label for="logout-sessions" style="font-size:13px;color:#4b5563;margin:0;cursor:pointer;">Sign out of other devices</label>
                    </div>
                </#if>

                <button type="submit" class="btn btn-primary btn-lg">Update password</button>

                <#if isAppInitiatedAction??>
                    <button type="submit" class="btn btn-outline btn-lg" style="margin-top:10px;width:100%;" name="cancel-aia" value="true">Cancel</button>
                </#if>
            </form>
        </div>

        <script>
            function togglePwd(id) {
                var el = document.getElementById(id);
                if (el) el.type = el.type === 'password' ? 'text' : 'password';
            }
        </script>
    </#if>

</@layout.registrationLayout>
