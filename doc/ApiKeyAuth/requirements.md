In this story we would like to add authentication layer using API Key to DynamicApiController

The Idea is once the user can assign the api to apikey from table APIkey or he can assign the api to use another authencation mechanisim way like using authentication identity provider using keyclock (  we will implement it later) or other external provider (this also later will implementing it with identity provider section)


we would like to add a table  in database using same pattern in entityframework this table decide the tablename , view, storedprocedure or function with the verb and the workspaceId  and database name so once the user select it from User interface it will saved here 

we would like to add custom filter in AutoApiEngine.Presentation project under folder called Filters 
the role of this filter is to check if the name of table and verb , workspaceId , database name  exist in this table and the api key in the header match the join table in apikey by key id as the relation between APIKey table to this table is 1 to many 1 apikey could have many permissions for objects with verbs inside workspaceId.


Scope: Backend + Frontend full story
Table design: Single ApiKeyPermission table 
Header name : X-Api-Key
Auth fallback : Api key if row eixsts else allow
FE UI locaiton : Extend api-generated page
Metadata endpoint: leave it JWT-only 