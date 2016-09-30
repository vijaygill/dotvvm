using System;
using System.Threading.Tasks;
using DotVVM.Framework.Hosting;

namespace DotVVM.Framework.Runtime.Filters
{
    /// <summary>
    /// Allows to modify the response when an exception occurs.
    /// </summary>
    public abstract class ExceptionFilterAttribute : ActionFilterAttribute
    {
        /// <inheritdoc />
        protected internal override Task OnCommandExecutedAsync(IDotvvmRequestContext context, ActionInfo actionInfo, Exception exception)
            => exception != null ? OnCommandExceptionAsync(context, actionInfo, exception) : Task.CompletedTask;

        /// <summary>
        /// Called when the exception occurs during the command invocation.
        /// </summary>
        protected virtual Task OnCommandExceptionAsync(IDotvvmRequestContext context, ActionInfo actionInfo, Exception ex)
            => Task.CompletedTask;
    }
}