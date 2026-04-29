<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('firstName','lastName','email','username','password','password-confirm'); section>
    <#if section == "header">
        Create your account
    <#elseif section == "form">
        <div class="card-pf">
            <h1>Create your account</h1>
            <p class="subtitle">Start your 14-day free trial <a href="${url.loginUrl}">Sign in instead</a></p>

            <#if message??>
                <div class="alert alert-error">
                    ${message.summary}
                </div>
            </#if>

            <form id="kc-register-form" action="${url.registrationAction}" method="post">
                <div class="form-group">
                    <label for="firstName"></label>
                    <input type="text" id="firstName" class="form-control" name="firstName" value="" autocomplete="given-name"/>
                </div>

                <div class="form-group">
                    <label for="lastName"></label>
                    <input type="text" id="lastName" class="form-control" name="lastName" value="" autocomplete="family-name"/>
                </div>

                <div class="form-group">
                    <label for="email"></label>
                    <input type="email" id="email" class="form-control" name="email" value="" autocomplete="email"/>
                </div>

                <#if !realm.registrationEmailAsUsername>
                    <div class="form-group">
                        <label for="username"></label>
                        <input type="text" id="username" class="form-control" name="username" value="" autocomplete="username"/>
                    </div>
                </#if>

                <div class="form-group">
                    <label for="password"></label>
                    <input type="password" id="password" class="form-control" name="password" autocomplete="new-password"/>
                </div>

                <div class="form-group">
                    <label for="password-confirm"></label>
                    <input type="password" id="password-confirm" class="form-control" name="password-confirm" autocomplete="new-password"/>
                </div>

                <#if recaptchaRequired??>
                    <div class="form-group">
                        <div class="g-recaptcha" data-size="compact" data-sitekey=""></div>
                    </div>
                </#if>

                <button type="submit" class="btn btn-primary btn-lg">Start Free Trial</button>
            </form>
        </div>
    </#if>
</@layout.registrationLayout>
