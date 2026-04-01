using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Services.Commands;
using NetCord.Services;
using NetCord.Services.Commands;

namespace GWBot;

// So we get rid of "Command not found." messages
public class CustomCommandResultHandler<TContext>(MessageFlags? messageFlags = null) : ICommandResultHandler<TContext>
    where TContext : ICommandContext
{
    public ValueTask HandleResultAsync(IExecutionResult result, TContext context, GatewayClient client, ILogger logger, IServiceProvider services)
    {
        if (result is not IFailResult failResult)
            return default;

        var resultMessage = failResult.Message;

        var message = context.Message;

        if (failResult is IExceptionResult exceptionResult)
            logger.LogError(exceptionResult.Exception, "Execution of a command with content '{Content}' failed with an exception", message.Content);
        else
            logger.LogDebug("Execution of a command with content '{Content}' failed with '{Message}'", message.Content, resultMessage);

        if (resultMessage == "Command not found.")
            return default;

        return new(message.ReplyAsync(new()
        {
            Content = resultMessage,
            FailIfNotExists = false,
            Flags = messageFlags,
        }));
    }
}

