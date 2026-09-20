DynamicApiServices.cs 
in backend 
in the current file there is only mysql and sqlserver and it is coded against solid principle 
now  I am focusing on mysql and sqlserver but later I will add sqllite and postgressSQL and that means I have to go to this DynamicApiServies and change and that not what I need 
create a folder call it DynamicApiSerivce and inside it create abstraction for each database type for for dynamic API Serivce and if there is any thing common create a base service for this 

do now mysql , sqlserver and leave the other to later 


solving sp_verb_problem go to sp_verb_problem.md and understand the problem and the stuggested solution is regarding to view and Functions always get verb but regarding to stored procedure we have to read the contnet and 
1-if the end of the conetent there is select that means GET Verb 
2-if at the end there is execute and inside execute there is select that means it is GET

3-otherwise it is POST 

that means it is either POST or GET 

and in case of GET whatever the input of the stored procudure will be parameter for the GET and if it is POST the input of the stored procedure will be in the body of POST
