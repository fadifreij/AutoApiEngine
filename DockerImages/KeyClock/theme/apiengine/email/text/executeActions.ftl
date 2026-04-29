Hi ${user.firstName!user.username!"there"},

Your APIEngine administrator has requested the following actions on your account:

<#list requiredActions as reqAction>
  ${reqAction_index + 1}. ${msg("requiredAction.${reqAction}")}
</#list>

Complete them by visiting:

${link}

This link expires in ${linkExpirationFormatter(linkExpiration)}.

— The APIEngine Team
© 2026 APIEngine. All rights reserved.
