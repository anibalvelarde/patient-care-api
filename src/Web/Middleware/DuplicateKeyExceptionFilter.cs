using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Neurocorp.Api.Web.Middleware;

public class DuplicateKeyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateException dbEx) return;
        if (dbEx.InnerException is not MySqlException { Number: 1062 } mysqlEx) return;

        // Extract the constraint name to provide a targeted message
        var message = mysqlEx.Message;
        string userMessage;

        if (message.Contains("uq_systemuser_email", StringComparison.OrdinalIgnoreCase))
        {
            userMessage = "A user with this email address already exists.";
        }
        else
        {
            userMessage = "A record with this value already exists.";
        }

        context.Result = new ConflictObjectResult(new { error = userMessage });
        context.ExceptionHandled = true;
    }
}
