using System.Threading.Tasks;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.ViewModel.Validation;

namespace DotVVM.Framework.Runtime.Filters
{
    /// <summary>
    /// Runs the model validation and returns the errors if the viewModel is not valid.
    /// </summary>
    public class ModelValidationFilterAttribute : ActionFilterAttribute
    {
        /// <inheritdoc />
        protected internal override Task OnCommandExecutingAsync(IDotvvmRequestContext context, ActionInfo actionInfo)
        {
            if (!string.IsNullOrEmpty(context.ModelState.ValidationTargetPath))
            {
                var validator = context.Configuration.ServiceLocator.GetService<IViewModelValidator>();
                context.ModelState.Errors.AddRange(validator.ValidateViewModel(context.ModelState.ValidationTarget));
                context.FailOnInvalidModelState();
            }

            return Task.CompletedTask;
        }
    }
}