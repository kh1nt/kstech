using kstech.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace kstech.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireOwnerScopeForSuperAdminAttribute : TypeFilterAttribute
    {
        public RequireOwnerScopeForSuperAdminAttribute()
            : base(typeof(RequireOwnerScopeForSuperAdminFilter))
        {
        }
    }

    public sealed class RequireOwnerScopeForSuperAdminFilter : IAsyncActionFilter
    {
        private readonly ITenantContext _tenantContext;
        private readonly ITempDataDictionaryFactory _tempDataDictionaryFactory;

        public RequireOwnerScopeForSuperAdminFilter(
            ITenantContext tenantContext,
            ITempDataDictionaryFactory tempDataDictionaryFactory)
        {
            _tenantContext = tenantContext;
            _tempDataDictionaryFactory = tempDataDictionaryFactory;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (_tenantContext.IsSuperAdmin && !_tenantContext.HasOwnerScope)
            {
                var tempData = _tempDataDictionaryFactory.GetTempData(context.HttpContext);
                tempData["OwnerScopeError"] = "Select an owner workspace first before opening operational modules.";
                context.Result = new RedirectToActionResult("Index", "Owner", null);
                return;
            }

            await next();
        }
    }
}
